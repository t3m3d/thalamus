# Thalamus

Thalamus is the standalone Windows window, monitor, tiling, layout, and overview component of the Cerebrum desktop environment. It is a .NET 8 WPF x64 application and runs alongside Explorer. It does not replace the Windows shell or compositor.

## Current release

This repository contains the integrated 0.1.0 Windows release:

- Mission Control-style full-desktop overview with keyboard navigation and an explicit 256-card resource bound.
- Live DWM previews with safe application-card fallbacks.
- Debounced WinEvent-based top-level window tracking while the overview is open, plus a low-frequency reconciliation pass without background rescans while it is closed.
- Application grouping without hiding individual windows.
- Activate, minimize, asynchronous close, drag-to-monitor, and snap actions; minimized windows remain visible and reopenable.
- Halves, thirds, quarters, maximize, and exact placement restore, with safe refitting if the original monitor disappears and documented [workspace-to-screen conversion](https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-windowplacement) to prevent AppBar/taskbar drift.
- Named layout save/restore with semantic validation, changed-monitor mapping, and backup recovery.
- Bounded, parallel icon fallback so hung applications cannot serialize the overview scan.
- Per-monitor metadata, production DPI-scaled monitor moves, and negative desktop coordinates.
- Configurable global hotkey, theme preset, accent, transparency, high contrast, and reduced-motion-safe behavior.
- Single-instance command forwarding over current-user and Windows-session-qualified named objects.
- Documented capability fallback for Windows virtual desktops.
- Crash-safe settings and layout JSON.

## Safety contract

Thalamus is deliberately an ordinary desktop application:

- Explorer remains running and visible.
- No Winlogon or shell registry values are changed.
- No compositor is replaced.
- No undocumented shell hooks or low-level keyboard hooks are installed.
- A window is moved, hidden, closed, or tiled only after an explicit command.
- Icon requests use a 50 ms hung-window deadline; close is posted asynchronously.
- UI-thread work excludes enumeration, icon retrieval, persistence, and named-pipe I/O.
- Hotkeys, WinEvent hooks, DWM thumbnails, pipe resources, and the single-instance mutex are released during shutdown.
- Diagnostic records contain event codes, not window titles, file names, or user content.

Run at the user's normal integrity level. Do not run Thalamus elevated for everyday use.

## Architecture

| Area | Location | Responsibility |
| --- | --- | --- |
| Core models and contracts | `src/Thalamus.Core/Models`, `Services` | Platform-neutral snapshots, interfaces, capabilities, and data contracts |
| Command surface | `src/Thalamus.Core/Commands` | CLI validation, canonical argument construction, and IPC envelopes |
| Layout algorithms | `src/Thalamus.Core/Layout` | Snap geometry, DPI math, monitor mapping, and profile restore planning |
| Persistence | `src/Thalamus.Core/Persistence` | Atomic JSON writes, backups, validation, and layout profiles |
| Native integration | `src/Thalamus/Interop`, `Services` | Win32 enumeration/actions, WinEvent hooks, DWM, monitors, hotkey, and IPC |
| Presentation | `src/Thalamus/ViewModels`, `Views` | Overview state, commands, keyboard access, drag behavior, and WPF UI |
| Tests | `tests/Thalamus.Tests`, `tests/desktop-smoke.ps1` | Deterministic unit/integration coverage plus isolated real-desktop behavior |

`Thalamus.Core` has no WPF or native dependency. Native services implement its interfaces, keeping layout logic and fallback behavior directly testable.

## Requirements

- Windows 10 or Windows 11, x64.
- .NET 8 SDK to build.
- .NET 8 Desktop Runtime to run a framework-dependent build.
- No preinstalled .NET runtime is needed for the self-contained deployment documented below.

No application runtime package is used. MSTest packages are test-only dependencies.

The optional self-contained publish bundles the official .NET and WPF runtime files; it adds no third-party runtime dependency.


## Build, test, and publish

From the repository root:

```powershell
dotnet restore Thalamus.sln --configfile NuGet.Config
dotnet format whitespace Thalamus.sln --verify-no-changes --no-restore
dotnet build Thalamus.sln -c Debug --no-restore -p:TreatWarningsAsErrors=true
dotnet test tests/Thalamus.Tests/Thalamus.Tests.csproj -c Debug --no-build --no-restore
dotnet build Thalamus.sln -c Release --no-restore -p:TreatWarningsAsErrors=true
dotnet test tests/Thalamus.Tests/Thalamus.Tests.csproj -c Release --no-build --no-restore
```

The x64 executable is produced at:

```text
src/Thalamus/bin/Debug/net8.0-windows/win-x64/Thalamus.exe
src/Thalamus/bin/Release/net8.0-windows/win-x64/Thalamus.exe
```

Run the Debug build:

```powershell
.\src\Thalamus\bin\Debug\net8.0-windows\win-x64\Thalamus.exe --overview
```

On an unlocked, interactive Windows desktop, run the isolated behavior harness after building:

```powershell
pwsh -NoProfile -File .\tests\desktop-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File .\tests\desktop-smoke.ps1 -Configuration Release
```

The single-instance boundary can also be verified on a locked or unlocked session without opening the overview:

```powershell
pwsh -NoProfile -File .\tests\headless-ipc-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File .\tests\headless-ipc-smoke.ps1 -Configuration Release
```

The headless harness verifies the user-and-session-qualified current-user pipe, direct acknowledgment, secondary-process forwarding, sole-primary convergence, and clean shutdown.

The same harness also covers relative-root rejection, invalid CLI handling, isolated data, cross-process persistence-lock wait and recovery, an idle-client deadline, and locked-session layout-save containment.

For a longer locked-session lifecycle and resource check, run:

```powershell
pwsh -NoProfile -File .\tests\ipc-soak.ps1 -Configuration Release -Requests 200
```

The soak harness warms one resident primary, launches the requested number of sequential real secondary processes and a bounded concurrent-client burst, requires every command acknowledgment, enforces sole-primary convergence and bounded handle growth, verifies a missing-profile request, then checks acknowledged shutdown and exact isolated-root cleanup.

Both harnesses abort if Thalamus is already running. Each child process receives a unique process-scoped `THALAMUS_DATA_ROOT` beneath the Windows temp directory, so tests never read or modify the normal settings, layouts, or diagnostics. Cleanup restores the caller's prior environment, verifies the resolved root and exact unique leaf, and only then removes that isolated tree.

The desktop harness creates uniquely titled disposable WPF windows and only tiles, restores, minimizes, activates, or closes those targets. For layout restore, it narrows the isolated temporary profile to that exact disposable window before applying it. It also verifies overview bounds and UI Automation, a hung-window deadline, single-instance forwarding, hotkey ownership/release, and clean shutdown.

Create a framework-dependent deployment directory with:

```powershell
dotnet publish src/Thalamus/Thalamus.csproj -c Release --no-restore -r win-x64 --self-contained false -o artifacts/publish/win-x64
```

Create a larger, fully self-contained x64 deployment that does not require a preinstalled .NET runtime with:

```powershell
dotnet restore src/Thalamus/Thalamus.csproj -r win-x64 -p:SelfContained=true --configfile NuGet.Config
dotnet publish src/Thalamus/Thalamus.csproj -c Release --no-restore -r win-x64 --self-contained true -o artifacts/publish/self-contained-win-x64
```


Closing the overview dismisses input capture but leaves Thalamus resident for its hotkey and CLI. Shut it down cleanly with `thalamus.exe --exit`.

## CLI reference

| Command | Behavior |
| --- | --- |
| `thalamus.exe --overview` | Show and focus the overview |
| `thalamus.exe --tile-active left` | Tile the active window to the left half |
| `thalamus.exe --tile-active right` | Tile the active window to the right half |
| `thalamus.exe --tile-active top` | Tile the active window to the top half |
| `thalamus.exe --tile-active bottom` | Tile the active window to the bottom half |
| `thalamus.exe --tile-active maximize` | Maximize after remembering placement |
| `thalamus.exe --tile-active restore` | Restore the placement remembered before tiling |
| `thalamus.exe --workspace next\|previous` | Request adjacent Windows workspace switching |
| `thalamus.exe --move-active-workspace next\|previous` | Request moving the active window to an adjacent workspace |
| `thalamus.exe --save-layout NAME` | Atomically save current eligible window placements |
| `thalamus.exe --restore-layout NAME` | Validate and restore a named layout |
| `thalamus.exe --exit` | Shut down the primary instance and release resources |

The snap engine also supports `left-third`, `center-third`, `right-third`, `top-left`, `top-right`, `bottom-left`, and `bottom-right`.

The first process owns a current-user-and-session-qualified local mutex and a matching current-user-only command pipe, so Run As users, console sessions, and RDP desktops cannot collide. Later processes allow 15 seconds for a cold-starting or busy primary, forward one validated command, wait for its acknowledgment, and exit. The primary accepts at most 4,096 characters and drops a client that does not complete its request within five seconds, so an idle connection cannot starve later commands. Syntax validation records `THA-COMMAND-INVALID` and exits immediately with code 2 without opening a modal dialog; forwarding or unexpected handler failure exits with code 3. Medulla can invoke valid commands through this boundary without a project or assembly reference.

## Overview controls

- Arrow keys: move selection.
- Enter or double-click: activate the selected window and dismiss the overview.
- Escape: reliably dismiss.
- Open, Minimize, Close: explicit per-card actions.
- Snap toolbar: apply halves, thirds, quarters, maximize, or restore.
- Drag a card and release over another monitor: move the source window while preserving proportional placement.
- Shift+Left / Shift+Right: move the selected window to the adjacent display without a pointer.
- Ctrl+1 / Ctrl+2: left/right half shortcuts.

The overlay is always visibly rendered while it can receive input. It never creates an invisible input-capturing surface.

Cross-monitor moves keep relative work-area position and scale window size by the target/source monitor DPI ratio.


## Settings and themes

Settings are created at:

```text
%LOCALAPPDATA%\Cerebrum\Thalamus\settings.json
```

Edit settings while Thalamus is stopped. Example:

```json
{
  "Version": 1,
  "ThemePreset": "Cerebrum",
  "AccentColor": "#7DD3FC",
  "SurfaceOpacity": 0.86,
  "ReducedMotion": false,
  "OverviewHotkey": {
    "Control": true,
    "Alt": true,
    "Shift": false,
    "Windows": false,
    "VirtualKey": 32
  }
}
```

Available presets are `Cerebrum`, `Krypton Glass`, `Graphite`, and `Frost`. Accent colors accept exactly `#RRGGBB` or `#AARRGGBB`. `VirtualKey` is a Windows virtual-key code; 32 is Space, so the default hotkey is Ctrl+Alt+Space. If registration conflicts with another application, Thalamus continues running and records `THA-HOTKEY-UNAVAILABLE`.

Unknown settings contract versions are preserved on disk rather than downgraded. Thalamus uses current safe defaults for that run and records `THA-SETTINGS-VERSION-UNSUPPORTED`; recognized settings are normalized and repaired atomically.

The overview uses one restrained 140 ms entrance transition. `ReducedMotion: true` and Windows high-contrast mode both bypass it. High contrast replaces surfaces, text, focus, selection, and hover colors with system brushes, including the system highlight-text color for hovered controls, and updates live if the Windows mode changes while the overview is open.

## Data and diagnostics

```text
%LOCALAPPDATA%\Cerebrum\Thalamus\settings.json
%LOCALAPPDATA%\Cerebrum\Thalamus\layouts\NAME.json
%LOCALAPPDATA%\Cerebrum\Thalamus\diagnostics.log
```

For controlled isolation, `THALAMUS_DATA_ROOT` can override this root but must be an absolute path. A relative or invalid override exits with code 2 and is never rebased against the working directory; that rejection writes only `THA-DATA-ROOT-INVALID` to `%TEMP%\Cerebrum\Thalamus\bootstrap-diagnostics.log`.

JSON updates are serialized process-wide per canonical path, including callers using different generic payload types, and coordinated across processes by a short-lived adjacent lock file. Writes use unique same-directory temporary files, write-through flushed streams, atomic replacement, and validator-aware `.bak` recovery. Recovery repairs an unreadable or semantically invalid current file without sacrificing its last valid backup; settings normalization uses the same backup-preserving repair path. Settings JSON is limited to 1 MiB and each layout JSON to 32 MiB for both accepted reads and persisted writes. Layout profiles reject unsupported versions, duplicate identities, duplicate monitors, unsafe coordinates, invalid work areas, and references to unknown saved monitors; extreme proportional mappings fall back to a visible clamped placement. Reserved DOS names, control characters, outer whitespace, and trailing periods are rejected; transformed or marker-prefixed names receive a reserved `!` marker plus a 128-bit SHA-256 suffix, so distinct names converge only under an impractical hash collision.

Diagnostics are intentionally sparse: UTC timestamp, stable event code, and only non-personal safe details. Titles, paths, and document content are never logged. At 1 MiB the active log rotates to `diagnostics.log.1`, with at most one previous generation retained.

## Safe manual test procedure

1. Keep Explorer running. Do not change shell registry settings.
2. Start ordinary applications such as Notepad and Calculator on each attached monitor.
3. Run `Thalamus.exe --overview`.
4. Confirm the overlay is obvious, Escape dismisses it, and Ctrl+Alt+Space reopens it.
5. Activate a card, then reopen and test minimize and close on disposable windows.
6. Tile a disposable window left, right, in a third, and in a quarter; use Restore and confirm its previous placement returns.
7. Drag a disposable window card to each monitor, including a monitor with negative desktop coordinates if available.
8. Save a profile, move windows, then restore it.
9. Run a second `Thalamus.exe --overview` and confirm only one process remains.
10. Suspend/resume or attach/detach a display where practical, then reopen the overview.
11. Finish with `Thalamus.exe --exit` and verify no Thalamus process remains.

Do not use an unsaved editor or a production application as a close, move, or layout-restore test target.

## Virtual desktop capability policy

Microsoft's documented [IVirtualDesktopManager](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager) exposes membership queries, desktop IDs, and moving a window to a desktop ID already known by the caller. It does not document adjacent desktop enumeration, creation, or switching.

Thalamus activates only that documented COM class for capability detection. Adjacent `--workspace` and `--move-active-workspace` requests therefore report a clear unsupported result on current Windows builds. No private Explorer interfaces or version-fragile COM contracts are used. The overview, monitor moves, tiling, and named layouts remain fully available.

Live previews use the documented [DwmRegisterThumbnail](https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail) relationship and unregister every handle owned by the process.

## Medulla and Cortex boundaries

Thalamus has no project or assembly references to Medulla or Cortex.

- Medulla integration: launch the documented CLI commands and inspect only process exit behavior.
- Cortex integration: launch the documented CLI commands when a file-management action needs overview or placement behavior.
- Theme compatibility: exchange a separately versioned JSON contract if cross-application theme synchronization is added.
- Writable settings: each application owns its own files. Do not point Medulla or Cortex at Thalamus's settings file.

## Known limitations

- Adjacent Windows virtual desktop control is unavailable until Microsoft publishes a stable API that covers it.
- UIPI can prevent a normal-integrity Thalamus process from controlling an elevated window.
- Protected shell, cloaked, tool, and non-activating windows are intentionally excluded.
- Some applications deny DWM thumbnail relationships; their cards remain usable with title, icon, and actions.
- Layout restore matches current windows by application ID, class, and a title-sorted ordinal (with HWND only as a tie-breaker); it does not launch missing applications.
- Thalamus is a resident utility and currently has no notification-area icon; use `--exit` for a clean shutdown.
- Local publish artifacts are not code-signed; sign release binaries before distributing them outside a controlled development environment.
- Automated tests cover deterministic core behavior, a locked-session-compatible harness covers real-process IPC and shutdown, and an interactive real-window harness covers the ordinary Debug and Release desktop path. Human verification is still required for heterogeneous-DPI hardware combinations, elevated/protected applications, physical display attach/detach, suspend/resume, and vendor-specific DWM behavior.

## Roadmap

- Adopt a documented Microsoft API if adjacent virtual desktop control becomes public and stable.
- Add a notification-area status/configuration surface.
- Add an interactive hotkey and theme editor.
- Add optional layout rules for application launch.
- Expand UI automation across heterogeneous-DPI multi-monitor hardware.
- Publish a versioned, read-only theme exchange contract for Medulla and Cortex.
