[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ExecutablePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$executable = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    Join-Path $root "src\Thalamus\bin\$Configuration\net8.0-windows\win-x64\Thalamus.exe"
} else {
    [IO.Path]::GetFullPath($ExecutablePath)
}
$sessionId = (Get-Process -Id $PID).SessionId
$windowsIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $identityName = if ($null -ne $windowsIdentity.User) {
        $windowsIdentity.User.Value
    } else {
        $windowsIdentity.Name
    }
}
finally {
    $windowsIdentity.Dispose()
}
$userScope = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identityName))
).Substring(0, 16)
$pipeName = "Cerebrum.Thalamus.Commands.v1.user-$userScope.session-$sessionId"

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Thalamus executable not found: $executable"
}
if (Get-Process -Name Thalamus -ErrorAction SilentlyContinue) {
    throw 'A Thalamus process is already running. Stop it before the isolated smoke test.'
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Wait-Until {
    param([scriptblock]$Condition, [int]$TimeoutMilliseconds = 5000)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        if (& $Condition) {
            return $true
        }
        Start-Sleep -Milliseconds 50
    }
    return $false
}

function Wait-PipeReady {
    param([int]$TimeoutMilliseconds = 10000)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        $client = [IO.Pipes.NamedPipeClientStream]::new(
            '.',
            $pipeName,
            [IO.Pipes.PipeDirection]::InOut,
            [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
        try {
            $client.Connect(200)
            $writer = [IO.StreamWriter]::new(
                $client,
                [Text.UTF8Encoding]::new($false),
                1024,
                $true)
            $writer.AutoFlush = $true
            $reader = [IO.StreamReader]::new(
                $client,
                [Text.Encoding]::UTF8,
                $true,
                1024,
                $true)
            $writer.WriteLine('{"Version":1,"Command":{"Kind":2,"Argument":"next"}}')
            $response = $reader.ReadLineAsync()
            if ($response.Wait(1000)) {
                return $response.Result -eq 'ok'
            }
        }
        catch {
            Start-Sleep -Milliseconds 100
        }
        finally {
            $client.Dispose()
        }
    }
    return $false
}

function Invoke-Thalamus {
    param(
        [string[]]$Arguments,
        [int]$ExpectedExitCode = 0
    )
    $forwarder = Start-Process `
        -FilePath $executable `
        -ArgumentList $Arguments `
        -WorkingDirectory $root `
        -WindowStyle Hidden `
        -PassThru
    try {
        Assert-True ($forwarder.WaitForExit(20000)) "Forwarder timed out: $($Arguments -join ' ')"
        Assert-True ($forwarder.ExitCode -eq $ExpectedExitCode) "Process returned exit code $($forwarder.ExitCode), expected $ExpectedExitCode`: $($Arguments -join ' ')"
    }
    finally {
        if (-not $forwarder.HasExited) {
            Stop-Process -Id $forwarder.Id -Force -ErrorAction SilentlyContinue
            [void]$forwarder.WaitForExit(5000)
        }
        $forwarder.Dispose()
    }
}
$runId = [Guid]::NewGuid().ToString('N')
$dataLeaf = "Thalamus.Smoke-$runId"
$dataRoot = Join-Path ([IO.Path]::GetTempPath()) $dataLeaf
$previousDataRoot = [Environment]::GetEnvironmentVariable('THALAMUS_DATA_ROOT', 'Process')
$env:THALAMUS_DATA_ROOT = $dataRoot

$primary = $null
$exitSent = $false

try {
    $relativeDataRoot = "Thalamus.Relative-$runId"
    $rebasedDataRoot = Join-Path $root $relativeDataRoot
    try {
        $env:THALAMUS_DATA_ROOT = $relativeDataRoot
        Invoke-Thalamus -Arguments @('--exit') -ExpectedExitCode 2
    }
    finally {
        $env:THALAMUS_DATA_ROOT = $dataRoot
    }
    Assert-True (-not (Test-Path -LiteralPath $rebasedDataRoot)) `
        'A relative isolated data root was rebased against the working directory.'

    Invoke-Thalamus -Arguments @('--save-layout', '..') -ExpectedExitCode 2
    Assert-True (Wait-Until {
        @(Get-Process -Name Thalamus -ErrorAction SilentlyContinue).Count -eq 0
    }) 'Invalid CLI syntax left a Thalamus process running.'

    [IO.Directory]::CreateDirectory($dataRoot) | Out-Null
    $settingsLockPath = Join-Path $dataRoot 'settings.json.lock'
    $settingsBlocker = [IO.FileStream]::new(
        $settingsLockPath,
        [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None,
        1,
        [IO.FileOptions]::DeleteOnClose)
    try {
        $primary = Start-Process `
            -FilePath $executable `
            -ArgumentList '--workspace', 'next' `
            -WorkingDirectory $root `
            -WindowStyle Hidden `
            -PassThru
        Start-Sleep -Milliseconds 300
        $primary.Refresh()
        Assert-True (-not $primary.HasExited) 'The primary exited while waiting for the persistence lock.'
        Assert-True (-not (Wait-PipeReady -TimeoutMilliseconds 400)) `
            'The command pipe became ready before startup acquired the persistence lock.'
    }
    finally {
        $settingsBlocker.Dispose()
    }

    Assert-True (Wait-PipeReady) 'The primary command pipe did not become ready.'
    $isolatedSettings = Join-Path $dataRoot 'settings.json'
    Assert-True (Wait-Until { Test-Path -LiteralPath $isolatedSettings }) `
        'The primary did not honor the isolated data root.'

    $idleClient = [IO.Pipes.NamedPipeClientStream]::new(
        '.',
        $pipeName,
        [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
    try {
        $idleClient.Connect(2000)
        Start-Sleep -Milliseconds 5500
    }
    finally {
        $idleClient.Dispose()
    }

    Invoke-Thalamus @('--save-layout', 'locked-session-smoke')

    Invoke-Thalamus @('--workspace', 'previous')
    Assert-True (Wait-Until {
        $running = @(Get-Process -Name Thalamus -ErrorAction SilentlyContinue)
        $running.Count -eq 1 -and $running[0].Id -eq $primary.Id
    }) 'Single-instance forwarding did not converge to exactly the primary process.'

    Invoke-Thalamus @('--exit')
    $exitSent = $true
    Assert-True (Wait-Until {
        $primary.Refresh()
        $primary.HasExited
    } 8000) 'Primary did not exit after acknowledged shutdown.'
    Assert-True (Wait-Until {
        @(Get-Process -Name Thalamus -ErrorAction SilentlyContinue).Count -eq 0
    }) 'Thalamus resources remained after shutdown.'

    $staleLocks = @(Get-ChildItem -LiteralPath $dataRoot -Filter '*.lock' -File -Recurse -ErrorAction SilentlyContinue)
    Assert-True ($staleLocks.Count -eq 0) 'A persistence lock remained after clean shutdown.'

    Write-Output "THALAMUS_HEADLESS_IPC_OK configuration=$Configuration primary=$($primary.Id) session=$sessionId tests=relative-data-root,invalid-cli,isolated-data,persistence-lock-wait,pipe,forward,acknowledgment,single-instance,idle-client,locked-layout-save,persistence-lock-cleanup,shutdown"
}
finally {
    if ($primary -and -not $primary.HasExited) {
        if (-not $exitSent) {
            try {
                Invoke-Thalamus @('--exit')
            }
            catch {
                # Forced cleanup below remains scoped to the process created by this test.
            }
        }
        if (-not $primary.WaitForExit(3000)) {
            Stop-Process -Id $primary.Id -Force -ErrorAction SilentlyContinue
        }
    }

    [Environment]::SetEnvironmentVariable(
        'THALAMUS_DATA_ROOT',
        $previousDataRoot,
        'Process')
    $resolvedDataRoot = [IO.Path]::GetFullPath($dataRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $tempPrefix.EndsWith([IO.Path]::DirectorySeparatorChar)) {
        $tempPrefix += [IO.Path]::DirectorySeparatorChar
    }
    if (-not $resolvedDataRoot.StartsWith(
            $tempPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolvedDataRoot) -ne $dataLeaf) {
        throw "Refusing to remove an unexpected smoke-test data root: $resolvedDataRoot"
    }
    if (Test-Path -LiteralPath $resolvedDataRoot) {
        Remove-Item -LiteralPath $resolvedDataRoot -Recurse -Force
    }
}
