[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $root "src\Thalamus\bin\$Configuration\net8.0-windows\win-x64\Thalamus.exe"
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
    throw "Build Thalamus $Configuration before running this smoke test."
}
if (Get-Process -Name Thalamus -ErrorAction SilentlyContinue) {
    throw 'A Thalamus process is already running. Stop it before the isolated smoke test.'
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ThalamusSmokeNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder value, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, StringBuilder className, int maximum);


    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rectangle);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);


    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int command);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static IntPtr FindWindowForProcess(int targetProcessId, string exactTitle)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId != targetProcessId)
                return true;

            int length = Math.Min(4095, Math.Max(1, GetWindowTextLengthW(hWnd) + 1));
            var title = new StringBuilder(length);
            GetWindowTextW(hWnd, title, title.Capacity);
            if (string.Equals(title.ToString(), exactTitle, StringComparison.Ordinal))
            {
                result = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

if ([ThalamusSmokeNative]::GetForegroundWindow() -eq [IntPtr]::Zero) {
    throw 'The interactive desktop is locked or disconnected. Unlock it before running the desktop smoke test.'
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

function Get-WindowRectangle {
    param([IntPtr]$Handle)
    $rectangle = [ThalamusSmokeNative+RECT]::new()
    Assert-True ([ThalamusSmokeNative]::GetWindowRect($Handle, [ref]$rectangle)) 'GetWindowRect failed.'
    return [pscustomobject]@{
        X = $rectangle.Left
        Y = $rectangle.Top
        Width = $rectangle.Right - $rectangle.Left
        Height = $rectangle.Bottom - $rectangle.Top
    }
}

function Get-WorkArea {
    param([IntPtr]$Handle)
    $monitor = [ThalamusSmokeNative]::MonitorFromWindow($Handle, 2)
    $info = [ThalamusSmokeNative+MONITORINFOEX]::new()
    $info.Size = [Runtime.InteropServices.Marshal]::SizeOf([type][ThalamusSmokeNative+MONITORINFOEX])
    Assert-True ([ThalamusSmokeNative]::GetMonitorInfoW($monitor, [ref]$info)) 'GetMonitorInfo failed.'
    return [pscustomobject]@{
        X = $info.Work.Left
        Y = $info.Work.Top
        Width = $info.Work.Right - $info.Work.Left
        Height = $info.Work.Bottom - $info.Work.Top
    }
}

function Test-Near {
    param([int]$Actual, [int]$Expected, [int]$Tolerance = 10)
    return [Math]::Abs($Actual - $Expected) -le $Tolerance
}

function Focus-OwnedWindow {
    param([IntPtr]$Handle)
    [void][ThalamusSmokeNative]::ShowWindowAsync($Handle, 9)
    $foreground = [ThalamusSmokeNative]::GetForegroundWindow()
    [uint32]$foregroundProcess = 0
    $foregroundThread = [ThalamusSmokeNative]::GetWindowThreadProcessId($foreground, [ref]$foregroundProcess)
    $currentThread = [ThalamusSmokeNative]::GetCurrentThreadId()
    $attached = $foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and
        [ThalamusSmokeNative]::AttachThreadInput($currentThread, $foregroundThread, $true)
    try {
        [void][ThalamusSmokeNative]::BringWindowToTop($Handle)
        [void][ThalamusSmokeNative]::SetForegroundWindow($Handle)
    }
    finally {
        if ($attached) {
            [void][ThalamusSmokeNative]::AttachThreadInput($currentThread, $foregroundThread, $false)
        }
    }
    return Wait-Until { [ThalamusSmokeNative]::GetForegroundWindow() -eq $Handle } 3000
}

function Start-DisposableWindow {
    param([string]$Title, [switch]$Hang)
    $hangCode = if ($Hang) {
        '$window.ContentRendered += { Start-Sleep -Seconds 30 }'
    }
    else {
        ''
    }
    $childScript = @'
Add-Type -AssemblyName PresentationFramework
$window = [System.Windows.Window]::new()
$window.Title = '__TITLE__'
$window.WindowStartupLocation = [System.Windows.WindowStartupLocation]::Manual
$window.Left = 180
$window.Top = 160
$window.Width = 860
$window.Height = 620
$window.Content = [System.Windows.Controls.TextBlock]@{
    Text = 'Disposable Thalamus integration target'
    FontSize = 24
    HorizontalAlignment = 'Center'
    VerticalAlignment = 'Center'
}
$window.Loaded += {
    [void]$window.Activate()
    $window.Topmost = $true
    $window.Topmost = $false
}
__HANG__
$app = [System.Windows.Application]::new()
[void]$app.Run($window)
'@
    $childScript = $childScript.Replace('__TITLE__', $Title).Replace('__HANG__', $hangCode)
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childScript))
    $process = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList '-NoProfile', '-STA', '-EncodedCommand', $encoded -WindowStyle Hidden -PassThru
    $ready = Wait-Until {
        [ThalamusSmokeNative]::FindWindowForProcess($process.Id, $Title) -ne [IntPtr]::Zero
    } 8000
    Assert-True $ready "Disposable window '$Title' did not start."
    $handle = [ThalamusSmokeNative]::FindWindowForProcess($process.Id, $Title)
    return [pscustomobject]@{ Process = $process; Handle = $handle; Title = $Title }
}

function Wait-PipeReady {
    param([int]$TimeoutMilliseconds = 10000)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        $client = [IO.Pipes.NamedPipeClientStream]::new(
            '.', $pipeName,
            [IO.Pipes.PipeDirection]::InOut,
            [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
        try {
            $client.Connect(200)
            $writer = [IO.StreamWriter]::new(
                $client, [Text.UTF8Encoding]::new($false), 1024, $true)
            $writer.AutoFlush = $true
            $reader = [IO.StreamReader]::new(
                $client, [Text.Encoding]::UTF8, $true, 1024, $true)
            $writer.WriteLine('{"Version":1,"Command":{"Kind":2,"Argument":"next"}}')
            return $reader.ReadLine() -eq 'ok'
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
    param([string[]]$Arguments)
    $forwarder = Start-Process -FilePath $executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    Assert-True ($forwarder.WaitForExit(10000)) "Forwarder timed out: $($Arguments -join ' ')"
    Assert-True ($forwarder.ExitCode -eq 0) "Forwarder failed with exit code $($forwarder.ExitCode): $($Arguments -join ' ')"
}

function Get-OverviewHandle {
    param([Diagnostics.Process]$Primary)
    return [ThalamusSmokeNative]::FindWindowForProcess($Primary.Id, 'Thalamus window overview')
}

function Find-WindowCardAction {
    param(
        [Windows.Automation.AutomationElement]$Root,
        [string]$WindowTitle,
        [string]$ActionName
    )
    $titleCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty, $WindowTitle)
    $buttonCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::Button)
    $actionCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty, $ActionName)
    $combined = [Windows.Automation.AndCondition]::new($buttonCondition, $actionCondition)
    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
    $titleElements = $Root.FindAll([Windows.Automation.TreeScope]::Descendants, $titleCondition)

    foreach ($titleElement in $titleElements) {
        $candidate = $titleElement
        for ($depth = 0; $depth -lt 8 -and $null -ne $candidate; $depth++) {
            $buttons = $candidate.FindAll([Windows.Automation.TreeScope]::Descendants, $combined)
            if ($buttons.Count -eq 1) {
                return $buttons.Item(0)
            }
            $candidate = $walker.GetParent($candidate)
        }
    }

    return $null
}

function Invoke-WindowCardAction {
    param(
        [Windows.Automation.AutomationElement]$Root,
        [string]$WindowTitle,
        [string]$ActionName
    )
    $button = Find-WindowCardAction $Root $WindowTitle $ActionName
    Assert-True ($null -ne $button) "Could not find $ActionName for the disposable window."
    $invoke = [Windows.Automation.InvokePattern]$button.GetCurrentPattern(
        [Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
}


$runId = [Guid]::NewGuid().ToString('N')
$dataLeaf = "Thalamus.Smoke-$runId"
$dataRoot = Join-Path ([IO.Path]::GetTempPath()) $dataLeaf
$previousDataRoot = [Environment]::GetEnvironmentVariable('THALAMUS_DATA_ROOT', 'Process')
$primary = $null
$normal = $null
$hung = $null
$profileName = "codex-desktop-smoke-$runId"
$profileRoot = Join-Path $dataRoot 'layouts'
$profilePath = Join-Path $profileRoot "$profileName.json"
$diagnosticsPath = Join-Path $dataRoot 'diagnostics.log'
$diagnosticLineCount = if (Test-Path -LiteralPath $diagnosticsPath) {
    @(Get-Content -LiteralPath $diagnosticsPath).Count
}
else {
    0
}
$exitSent = $false
$env:THALAMUS_DATA_ROOT = $dataRoot

try {
    $primary = Start-Process -FilePath $executable -ArgumentList '--workspace', 'next' -WindowStyle Hidden -PassThru
    Assert-True (Wait-PipeReady) 'The primary command pipe did not become ready.'

    $normal = Start-DisposableWindow "Thalamus Disposable $([Guid]::NewGuid().ToString('N'))"
    Assert-True (Focus-OwnedWindow $normal.Handle) 'Could not focus the disposable target safely.'
    $original = Get-WindowRectangle $normal.Handle
    $work = Get-WorkArea $normal.Handle

    Invoke-Thalamus @('--tile-active', 'left')
    $expectedHalfWidth = [int][Math]::Floor($work.Width / 2)
    $tiled = Wait-Until {
        $current = Get-WindowRectangle $normal.Handle
        (Test-Near $current.X $work.X) -and
        (Test-Near $current.Y $work.Y) -and
        (Test-Near $current.Width $expectedHalfWidth) -and
        (Test-Near $current.Height $work.Height)
    }
    Assert-True $tiled 'Left-half placement did not reach the monitor work area.'

    Assert-True (Focus-OwnedWindow $normal.Handle) 'Could not refocus the tiled target.'
    Invoke-Thalamus @('--tile-active', 'restore')
    $restored = Wait-Until {
        $current = Get-WindowRectangle $normal.Handle
        (Test-Near $current.X $original.X) -and
        (Test-Near $current.Y $original.Y) -and
        (Test-Near $current.Width $original.Width) -and
        (Test-Near $current.Height $original.Height)
    }
    Assert-True $restored 'Placement restore did not return to the remembered rectangle.'

    Assert-True (Focus-OwnedWindow $normal.Handle) 'Could not refocus before maximize.'
    Invoke-Thalamus @('--tile-active', 'maximize')
    Assert-True (Wait-Until { [ThalamusSmokeNative]::IsZoomed($normal.Handle) }) 'Maximize was not applied.'

    Assert-True (Focus-OwnedWindow $normal.Handle) 'Could not refocus the maximized target.'
    Invoke-Thalamus @('--tile-active', 'restore')
    Assert-True (Wait-Until {
        (-not [ThalamusSmokeNative]::IsZoomed($normal.Handle)) -and
        (Test-Near (Get-WindowRectangle $normal.Handle).X $original.X)
    }) 'Restore after maximize did not recover the original state.'

    Assert-True (Focus-OwnedWindow $normal.Handle) 'Could not focus before external maximize.'
    [void][ThalamusSmokeNative]::ShowWindowAsync($normal.Handle, 3)
    Assert-True (Wait-Until { [ThalamusSmokeNative]::IsZoomed($normal.Handle) }) `
        'Disposable window did not enter the external maximized state.'
    Assert-True (Wait-Until { [ThalamusSmokeNative]::GetForegroundWindow() -eq $normal.Handle }) `
        'Externally maximized disposable window lost foreground ownership.'

    Invoke-Thalamus @('--tile-active', 'left')
    Assert-True (Wait-Until {
        $current = Get-WindowRectangle $normal.Handle
        (-not [ThalamusSmokeNative]::IsZoomed($normal.Handle)) -and
        (Test-Near $current.X $work.X) -and
        (Test-Near $current.Width $expectedHalfWidth)
    }) 'Tiling an already-maximized window did not complete.'

    Assert-True (Focus-OwnedWindow $normal.Handle) `
        'Could not refocus the tile created from a maximized window.'
    Invoke-Thalamus @('--tile-active', 'restore')
    Assert-True (Wait-Until { [ThalamusSmokeNative]::IsZoomed($normal.Handle) }) `
        'Restore did not recover the preexisting maximized state.'

    [void][ThalamusSmokeNative]::ShowWindowAsync($normal.Handle, 9)
    Assert-True (Wait-Until {
        $current = Get-WindowRectangle $normal.Handle
        (-not [ThalamusSmokeNative]::IsZoomed($normal.Handle)) -and
        (Test-Near $current.X $original.X) -and
        (Test-Near $current.Y $original.Y) -and
        (Test-Near $current.Width $original.Width) -and
        (Test-Near $current.Height $original.Height)
    }) 'The normal rectangle beneath the restored maximized state changed.'



    Invoke-Thalamus @('--save-layout', $profileName)
    Assert-True (Wait-Until { Test-Path -LiteralPath $profilePath }) 'Named layout file was not created.'
    $profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
    Assert-True ($profile.Version -eq 1) 'Named layout version was invalid.'
    Assert-True ($profile.Name -eq $profileName) 'Named layout name did not round-trip.'
    Assert-True (@($profile.Windows).Count -gt 0) 'Named layout did not contain any eligible windows.'

    $targetClassBuilder = [Text.StringBuilder]::new(256)
    [void][ThalamusSmokeNative]::GetClassNameW(
        $normal.Handle, $targetClassBuilder, $targetClassBuilder.Capacity)
    $targetClass = $targetClassBuilder.ToString()
    $targetPlacements = @($profile.Windows | Where-Object { $_.ClassName -eq $targetClass })
    Assert-True ($targetPlacements.Count -eq 1) 'Could not isolate the disposable saved placement.'
    $profile.Windows = $targetPlacements
    $profile | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $profilePath -Encoding utf8

    Assert-True (Focus-OwnedWindow $normal.Handle) 'Could not focus before profile restore.'
    Invoke-Thalamus @('--tile-active', 'right')
    $expectedRightX = $work.X + [int][Math]::Floor($work.Width / 2)
    $expectedRightWidth = $work.Width - [int][Math]::Floor($work.Width / 2)
    Assert-True (Wait-Until {
        $current = Get-WindowRectangle $normal.Handle
        (Test-Near $current.X $expectedRightX) -and
        (Test-Near $current.Width $expectedRightWidth)
    }) 'Right-half setup for profile restore did not complete.'

    Invoke-Thalamus @('--restore-layout', $profileName)
    Assert-True (Wait-Until {
        $current = Get-WindowRectangle $normal.Handle
        (Test-Near $current.X $original.X) -and
        (Test-Near $current.Y $original.Y) -and
        (Test-Near $current.Width $original.Width) -and
        (Test-Near $current.Height $original.Height)
    }) 'Named layout restore did not recover the saved disposable placement.'


    Invoke-Thalamus @('--overview')
    $overview = [IntPtr]::Zero
    Assert-True (Wait-Until {
        (Get-OverviewHandle $primary) -ne [IntPtr]::Zero
    }) 'Overview did not become a visible top-level window.'
    $overview = Get-OverviewHandle $primary

    $overviewRect = Get-WindowRectangle $overview
    Assert-True (Test-Near $overviewRect.X ([ThalamusSmokeNative]::GetSystemMetrics(76)) 2) 'Overview virtual-screen X was incorrect.'
    Assert-True (Test-Near $overviewRect.Y ([ThalamusSmokeNative]::GetSystemMetrics(77)) 2) 'Overview virtual-screen Y was incorrect.'
    Assert-True (Test-Near $overviewRect.Width ([ThalamusSmokeNative]::GetSystemMetrics(78)) 2) 'Overview virtual-screen width was incorrect.'
    Assert-True (Test-Near $overviewRect.Height ([ThalamusSmokeNative]::GetSystemMetrics(79)) 2) 'Overview virtual-screen height was incorrect.'

    $buttonCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::Button)
    $requiredButtonNames = @(
        'Activate window', 'Minimize window', 'Close window', 'Maximize selected window', 'Restore selected window placement')
    $automationReady = Wait-Until {
        try {
            $candidateRoot = [Windows.Automation.AutomationElement]::FromHandle($overview)
            $candidateNames = @($candidateRoot.FindAll(
                [Windows.Automation.TreeScope]::Descendants, $buttonCondition) |
                ForEach-Object { $_.Current.Name })
            $missing = @($requiredButtonNames | Where-Object { $candidateNames -notcontains $_ })
            return $missing.Count -eq 0
        }
        catch {
            return $false
        }
    }
    Assert-True $automationReady "Overview automation controls did not become ready."
    $automationRoot = [Windows.Automation.AutomationElement]::FromHandle($overview)


    Invoke-WindowCardAction $automationRoot $normal.Title 'Minimize window'
    Assert-True (Wait-Until { [ThalamusSmokeNative]::IsIconic($normal.Handle) }) 'Card minimize did not minimize the disposable window.'
    Start-Sleep -Milliseconds 350

    $automationRoot = [Windows.Automation.AutomationElement]::FromHandle($overview)
    Invoke-WindowCardAction $automationRoot $normal.Title 'Activate window'
    Assert-True (Wait-Until { -not [ThalamusSmokeNative]::IsIconic($normal.Handle) }) 'A minimized card did not reactivate its window.'
    Assert-True (Wait-Until { -not [ThalamusSmokeNative]::IsWindow($overview) }) 'Activation did not dismiss the overview.'
    Assert-True (Wait-Until { [ThalamusSmokeNative]::GetForegroundWindow() -eq $normal.Handle }) 'Activated disposable window did not become foreground.'

    Invoke-Thalamus @('--overview')
    Assert-True (Wait-Until { (Get-OverviewHandle $primary) -ne [IntPtr]::Zero }) 'Overview did not reopen for close verification.'
    $overview = Get-OverviewHandle $primary
    $automationRoot = [Windows.Automation.AutomationElement]::FromHandle($overview)
    Invoke-WindowCardAction $automationRoot $normal.Title 'Close window'
    Assert-True (Wait-Until { -not [ThalamusSmokeNative]::IsWindow($normal.Handle) }) 'Card close did not close the disposable window.'
    Assert-True (Wait-Until { $normal.Process.Refresh(); $normal.Process.HasExited }) 'Disposable window process remained after card close.'

    [void][ThalamusSmokeNative]::PostMessageW($overview, 0x0100, [IntPtr]0x1B, [IntPtr]::Zero)
    [void][ThalamusSmokeNative]::PostMessageW($overview, 0x0101, [IntPtr]0x1B, [IntPtr]::Zero)
    Assert-True (Wait-Until { -not [ThalamusSmokeNative]::IsWindow($overview) }) 'Escape did not dismiss the overview.'


    $hung = Start-DisposableWindow "Thalamus Hung $([Guid]::NewGuid().ToString('N'))" -Hang
    Start-Sleep -Milliseconds 700
    $overviewTimer = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Thalamus @('--overview')
    $hungOverview = [IntPtr]::Zero
    Assert-True (Wait-Until {
        (Get-OverviewHandle $primary) -ne [IntPtr]::Zero
    } 5000) 'Overview did not open while a target window was hung.'
    $hungOverview = Get-OverviewHandle $primary
    Assert-True ($overviewTimer.ElapsedMilliseconds -lt 5000) 'Hung-window fallback exceeded the responsiveness deadline.'

    Stop-Process -Id $hung.Process.Id -Force -ErrorAction SilentlyContinue
    $hung = $null
    [void][ThalamusSmokeNative]::PostMessageW($hungOverview, 0x0100, [IntPtr]0x1B, [IntPtr]::Zero)
    [void][ThalamusSmokeNative]::PostMessageW($hungOverview, 0x0101, [IntPtr]0x1B, [IntPtr]::Zero)
    Assert-True (Wait-Until { -not [ThalamusSmokeNative]::IsWindow($hungOverview) }) 'Second overview did not dismiss.'

    Assert-True (Wait-Until { @(Get-Process -Name Thalamus -ErrorAction SilentlyContinue).Count -eq 1 }) 'Single-instance forwarding left extra processes.'

    $settingsPath = Join-Path $dataRoot 'settings.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    [uint32]$modifiers = 0x4000
    if ($settings.OverviewHotkey.Control) { $modifiers = $modifiers -bor 0x0002 }
    if ($settings.OverviewHotkey.Alt) { $modifiers = $modifiers -bor 0x0001 }
    if ($settings.OverviewHotkey.Shift) { $modifiers = $modifiers -bor 0x0004 }
    if ($settings.OverviewHotkey.Windows) { $modifiers = $modifiers -bor 0x0008 }
    $probeId = 0x5449
    $availableWhileRunning = [ThalamusSmokeNative]::RegisterHotKey(
        [IntPtr]::Zero, $probeId, $modifiers, [uint32]$settings.OverviewHotkey.VirtualKey)
    if ($availableWhileRunning) {
        [void][ThalamusSmokeNative]::UnregisterHotKey([IntPtr]::Zero, $probeId)
    }

    Invoke-Thalamus @('--exit')
    $exitSent = $true
    Assert-True (Wait-Until { $primary.Refresh(); $primary.HasExited } 8000) 'Primary did not exit after acknowledged shutdown.'
    Assert-True (Wait-Until { @(Get-Process -Name Thalamus -ErrorAction SilentlyContinue).Count -eq 0 }) 'Thalamus resources remained after shutdown.'

    $staleLocks = @(Get-ChildItem -LiteralPath $dataRoot -Filter '*.lock' -File -Recurse -ErrorAction SilentlyContinue)
    Assert-True ($staleLocks.Count -eq 0) 'A persistence lock remained after clean shutdown.'

    $newDiagnostics = if (Test-Path -LiteralPath $diagnosticsPath) {
        @(Get-Content -LiteralPath $diagnosticsPath | Select-Object -Skip $diagnosticLineCount)
    }
    else {
        @()
    }
    $hotkeyUnavailable = @($newDiagnostics | Where-Object { $_ -match '\tTHA-HOTKEY-UNAVAILABLE(\t|$)' }).Count -gt 0
    if (-not $hotkeyUnavailable) {
        Assert-True (-not $availableWhileRunning) 'Configured hotkey was not owned by the running primary.'
        $availableAfterExit = [ThalamusSmokeNative]::RegisterHotKey(
            [IntPtr]::Zero, $probeId, $modifiers, [uint32]$settings.OverviewHotkey.VirtualKey)
        Assert-True $availableAfterExit 'Configured hotkey was not released during shutdown.'
        [void][ThalamusSmokeNative]::UnregisterHotKey([IntPtr]::Zero, $probeId)
    }

    Write-Output "THALAMUS_DESKTOP_SMOKE_OK configuration=$Configuration tests=tile,restore,maximize,maximized-source,profile-restore,overview,uia,minimize,activate,close,hung,single-instance,hotkey,persistence-lock,shutdown"
}
finally {
    if ($hung -and -not $hung.Process.HasExited) {
        Stop-Process -Id $hung.Process.Id -Force -ErrorAction SilentlyContinue
    }
    if ($normal -and -not $normal.Process.HasExited) {
        Stop-Process -Id $normal.Process.Id -Force -ErrorAction SilentlyContinue
    }
    if ($primary -and -not $primary.HasExited) {
        if (-not $exitSent) {
            try { Invoke-Thalamus @('--exit') } catch { }
        }
        if (-not $primary.WaitForExit(3000)) {
            Stop-Process -Id $primary.Id -Force -ErrorAction SilentlyContinue
        }
    }
    foreach ($path in @($profilePath, "$profilePath.bak", "$profilePath.tmp")) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    if (Test-Path -LiteralPath $profileRoot) {
        $temporaryProfiles = Get-ChildItem -LiteralPath $profileRoot -File `
            -Filter "$profileName.json.*.tmp" -ErrorAction SilentlyContinue
        foreach ($temporaryProfile in $temporaryProfiles) {
            Remove-Item -LiteralPath $temporaryProfile.FullName -Force
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
