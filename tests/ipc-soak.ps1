[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateRange(1, 5000)]
    [int]$Requests = 200,
    [ValidateRange(1, 64)]
    [int]$ConcurrentClients = 12,
    [string]$ExecutablePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$executable = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    Join-Path $repositoryRoot "src\Thalamus\bin\$Configuration\net8.0-windows\win-x64\Thalamus.exe"
} else {
    [IO.Path]::GetFullPath($ExecutablePath)
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Thalamus executable not found: $executable"
}
if (@(Get-Process -Name Thalamus -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'Close every existing Thalamus process before running the IPC soak harness.'
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$isolatedName = "Thalamus.IpcSoak-$([Guid]::NewGuid().ToString('N'))"
$isolatedRoot = [IO.Path]::GetFullPath((Join-Path -Path $tempRoot -ChildPath $isolatedName))
$isolatedLeaf = Split-Path -Leaf $isolatedRoot
if (-not [string]::Equals(
        (Split-Path -Parent $isolatedRoot),
        $tempRoot,
        [StringComparison]::OrdinalIgnoreCase) -or
    $isolatedLeaf -notmatch '^Thalamus\.IpcSoak-[0-9a-f]{32}$') {
    throw "Refusing unsafe isolated root: $isolatedRoot"
}

$previousRoot = [Environment]::GetEnvironmentVariable('THALAMUS_DATA_ROOT', 'Process')
$hadPreviousRoot = $null -ne $previousRoot
$primary = $null
$summary = $null

function Invoke-ThalamusClient {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $client = Start-Process -FilePath $executable -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    try {
        if (-not $client.WaitForExit(10000)) {
            Stop-Process -Id $client.Id -Force -ErrorAction SilentlyContinue
            throw "Client timed out: $($Arguments -join ' ')"
        }

        $client.Refresh()
        return $client.ExitCode
    }
    finally {
        $client.Dispose()
    }
}

try {
    [Environment]::SetEnvironmentVariable('THALAMUS_DATA_ROOT', $isolatedRoot, 'Process')
    $primary = Start-Process -FilePath $executable -ArgumentList @('--workspace', 'next') -PassThru -WindowStyle Hidden

    $startup = [Diagnostics.Stopwatch]::StartNew()
    $settingsPath = Join-Path $isolatedRoot 'settings.json'
    while (-not (Test-Path -LiteralPath $settingsPath) -and
        $startup.Elapsed -lt [TimeSpan]::FromSeconds(10) -and
        -not $primary.HasExited) {
        Start-Sleep -Milliseconds 50
        $primary.Refresh()
    }
    if ($primary.HasExited) {
        throw "Primary exited during startup with code $($primary.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        throw 'Primary did not finish isolated settings initialization.'
    }

    $warmupRequests = [Math]::Min(20, [Math]::Max(5, $Requests))
    for ($index = 0; $index -lt $warmupRequests; $index++) {
        $code = Invoke-ThalamusClient @('--workspace', 'next')
        if ($code -ne 0) {
            throw "Warmup request $index exited with code $code."
        }
    }

    $primary.Refresh()
    $baselineHandles = $primary.HandleCount
    $baselinePrivateBytes = $primary.PrivateMemorySize64
    $timer = [Diagnostics.Stopwatch]::StartNew()

    for ($index = 0; $index -lt $Requests; $index++) {
        $code = Invoke-ThalamusClient @('--workspace', 'next')
        if ($code -ne 0) {
            throw "Soak request $index exited with code $code."
        }

        if (($index + 1) % 50 -eq 0) {
            $primary.Refresh()
            if ($primary.HasExited) {
                throw "Primary exited after $($index + 1) soak requests."
            }
        }
    }

    $burstClients = [Collections.Generic.List[Diagnostics.Process]]::new()
    try {
        for ($index = 0; $index -lt $ConcurrentClients; $index++) {
            $burstClient = Start-Process -FilePath $executable -ArgumentList @('--workspace', 'next') -PassThru -WindowStyle Hidden
            $burstClients.Add($burstClient)
        }

        foreach ($client in $burstClients) {
            if (-not $client.WaitForExit(20000)) {
                throw "Concurrent client $($client.Id) timed out."
            }
            $client.Refresh()
            if ($client.ExitCode -ne 0) {
                throw "Concurrent client $($client.Id) exited with code $($client.ExitCode)."
            }
        }
    }
    finally {
        foreach ($client in $burstClients) {
            try {
                if (-not $client.HasExited) {
                    Stop-Process -Id $client.Id -Force -ErrorAction SilentlyContinue
                    [void]$client.WaitForExit(5000)
                }
            }
            finally {
                $client.Dispose()
            }
        }
    }

    $restoreCode = Invoke-ThalamusClient @('--restore-layout', 'missing-soak-profile')
    if ($restoreCode -ne 0) {
        throw "Missing-layout request exited with code $restoreCode."
    }

    $primary.Refresh()
    $afterHandles = $primary.HandleCount
    $afterPrivateBytes = $primary.PrivateMemorySize64
    $handleGrowth = $afterHandles - $baselineHandles
    if ($handleGrowth -gt 32) {
        throw "Primary handle count grew unexpectedly by $handleGrowth."
    }

    $live = @(Get-Process -Name Thalamus -ErrorAction SilentlyContinue)
    if ($live.Count -ne 1 -or $live[0].Id -ne $primary.Id) {
        throw "Expected exactly one primary after the soak; found $($live.Count)."
    }

    $exitCode = Invoke-ThalamusClient @('--exit')
    if ($exitCode -ne 0) {
        throw "Exit request returned code $exitCode."
    }
    if (-not $primary.WaitForExit(10000)) {
        throw 'Primary did not honor the acknowledged exit request.'
    }

    $summary = "THALAMUS_IPC_SOAK_OK configuration=$Configuration requests=$Requests " +
        "warmup=$warmupRequests concurrent=$ConcurrentClients " +
        "elapsed_ms=$([Math]::Round($timer.Elapsed.TotalMilliseconds)) " +
        "handles=$baselineHandles->$afterHandles private_bytes=$baselinePrivateBytes->$afterPrivateBytes"
}
finally {
    if ($null -ne $primary) {
        if (-not $primary.HasExited) {
            Stop-Process -Id $primary.Id -Force -ErrorAction SilentlyContinue
            [void]$primary.WaitForExit(5000)
        }
        $primary.Dispose()
    }

    if ($hadPreviousRoot) {
        [Environment]::SetEnvironmentVariable('THALAMUS_DATA_ROOT', $previousRoot, 'Process')
    } else {
        [Environment]::SetEnvironmentVariable('THALAMUS_DATA_ROOT', $null, 'Process')
    }

    if (Test-Path -LiteralPath $isolatedRoot) {
        $resolvedExisting = (Get-Item -LiteralPath $isolatedRoot -Force).FullName
        if (-not [string]::Equals(
                $resolvedExisting,
                $isolatedRoot,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals(
                (Split-Path -Parent $resolvedExisting),
                $tempRoot,
                [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path -Leaf $resolvedExisting) -notmatch '^Thalamus\.IpcSoak-[0-9a-f]{32}$') {
            throw "Refusing unsafe cleanup target: $resolvedExisting"
        }

        Remove-Item -LiteralPath $resolvedExisting -Recurse -Force
    }
    if (Test-Path -LiteralPath $isolatedRoot) {
        throw "IPC soak cleanup left its isolated root behind: $isolatedRoot"
    }
}

Write-Output $summary
