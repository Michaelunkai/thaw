<div align="center">

<img src="dev-icons/normal-128.png" width="104" alt="Thaw icon">

# Thaw

**Instant, keyboard-first unfreezing for a slow, stuttering, or unresponsive Windows PC.**

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](#build-from-source)
[![Version](https://img.shields.io/badge/version-1.4.20-2ea44f)](#changelog)
[![Download](https://img.shields.io/badge/download-latest%20release-181717?logo=github)](#download)

</div>

Thaw lives in your system tray and turns one key chord into a bounded, measured recovery pass.
When the desktop stops responding, frames stop presenting, or a background process eats the
machine, you press **Alt+F4** (or a configurable chord) and Thaw does the work in under a
second — no Task Manager, no hunting for the culprit, no reboot.

It is deliberately **not** a magic "fix everything" button. It cannot repair a kernel
deadlock, a failing disk, or a dead GPU driver. What it does do is cover every
*slow / stuttering / temporarily stuck* state with actions that are measured before they run,
journaled while they run, and rolled back exactly when they finish.

> **Important:** in the default `always` mode, **Alt+F4 recovers instead of closing the focused
> window**. Use the window's **X**, switch the mode to `stressed`/`off`, or press the panic chord.

---

## Table of contents

- [Why Thaw](#why-thaw)
- [Features](#features)
- [Requirements](#requirements)
- [Download](#download)
- [Install](#install)
- [Quick start](#quick-start)
- [Using Thaw](#using-thaw)
  - [Tray icon states](#tray-icon-states)
  - [Tray menu](#tray-menu)
  - [Hotkeys](#hotkeys)
- [What a recovery actually does](#what-a-recovery-actually-does)
  - [Recovery profiles](#recovery-profiles)
  - [The Alt+F4 relief tier](#the-altf4-relief-tier)
  - [Automatic rescue](#automatic-rescue)
  - [Rollback and receipts](#rollback-and-receipts)
- [Configuration](#configuration)
- [Command line](#command-line)
- [Logs, history, and support bundles](#logs-history-and-support-bundles)
- [Safety model](#safety-model)
- [Honest limits](#honest-limits)
- [Build from source](#build-from-source)
- [Project layout](#project-layout)
- [Troubleshooting](#troubleshooting)
- [Full method reference](#full-method-reference)
- [Changelog](#changelog)

---

## Why Thaw

Most "PC optimizer" tools guess. Thaw measures:

- **The machine is sampled every second** — scheduling delay, CPU, RAM, disk latency, DPC/ISR
  load, network errors, GPU and DWM frame evidence, foreground responsiveness.
- **The offender is identified by instantaneous rate**, not lifetime CPU time, so the process
  freezing you *right now* is the one that gets attention.
- **Every mutation has a restore path.** Priority, affinity, working set, power plan, and
  keep-awake state are recorded in a rollback journal and restored when the bounded run ends.
- **Nothing is killed** except `explorer.exe`, and only when you explicitly ask for it.

A recovery is a bounded run (10 s default budget) with per-action deadlines, not an open-ended
script. The tray reports what was attempted and whether the result was verified.

## Features

| | |
|---|---|
| **Keyboard-first** | Global `WH_KEYBOARD_LL` hooks plus an independent `RegisterHotKey` fallback and a Thaw-only rescue helper, so a chord still lands when the UI thread is busy. |
| **Tray-only** | No window, no console, no background service. Closing/Exit from the tray stops it immediately. |
| **Three deliberate actions** | Normal recovery, confirmed Explorer-only shell recovery, and emergency/panic recovery. They are not the same button with different labels. |
| **Force-all chord** | Alt+F4 runs display, foreground, memory, shell, network, diagnostics, and the relief tier concurrently. |
| **Automatic relief** | Since 1.4.20, sustained stress and DWM composition stalls trigger bounded recovery on their own. |
| **Measured, not guessed** | Every pass records stall, CPU, RAM, commit, disk, DPC/ISR, GPU, DWM frame, and foreground evidence. |
| **Exactly reversible** | Priority/affinity/working-set/power/keep-awake changes are rolled back from the journal. |
| **Self-contained** | Single `win-x64` executable. No .NET runtime install required. |
| **Transparent** | `%LOCALAPPDATA%\Thaw\log.txt`, incident receipts, and a one-click support bundle. |

## Requirements

- **Windows 10 or Windows 11, 64-bit.**
- No runtime required: the released executable is self-contained.
- **Administrator is optional.** It unlocks system-wide memory-list/cache operations. Display
  reset, DWM rescue, priority boosts, and diagnostics work without elevation; unavailable
  system-level steps are skipped and logged.
- Building from source requires the **.NET 10 SDK** and, for the installer,
  **Inno Setup 6**.

## Download

Grab the latest build from the [**Releases page**](https://github.com/Michaelunkai/thaw/releases/latest):

| File | Use |
|---|---|
| [`Thaw-Setup.exe`](https://github.com/Michaelunkai/thaw/releases/latest/download/Thaw-Setup.exe) | Recommended. Per-user installer with Start-menu entry and uninstaller. |
| [`Thaw-Portable.exe`](https://github.com/Michaelunkai/thaw/releases/latest/download/Thaw-Portable.exe) | No installation. Run it from anywhere. |
| [`SHA256SUMS.txt`](https://github.com/Michaelunkai/thaw/releases/latest/download/SHA256SUMS.txt) | Hashes for verifying either download. |

Verify a download in PowerShell:

```powershell
Get-FileHash .\Thaw-Setup.exe -Algorithm SHA256
# compare against SHA256SUMS.txt
```

The binaries are **unsigned**, so Windows SmartScreen may show a "More info → Run anyway"
prompt the first time.

## Install

### Option A — installer (recommended)

1. Run `Thaw-Setup.exe`.
2. Keep the optional desktop shortcut if you want one. **Start with Windows is not enabled
   automatically.**
3. Launch Thaw from the Start menu. A tray icon appears.
4. Uninstall later from **Settings → Apps → Installed apps → Thaw**.

The installer is per-user and does not need Administrator by itself. To let Thaw perform
system-level actions, use **tray menu → Restart as Administrator**.

### Option B — portable

1. Put `Thaw-Portable.exe` wherever you like (for example `%LOCALAPPDATA%\Programs\Thaw`).
2. Run it. No files are written outside `%APPDATA%\Thaw` and `%LOCALAPPDATA%\Thaw`.

### First run

1. Right-click the tray icon and open **Hotkeys & safety help**.
2. Decide whether **Alt+F4 behaviour** should be `always`, `stressed`, or `off`.
3. Enable **Start with Windows** only if you want Thaw always available.
4. Run **Normal recovery** once and read the balloon plus the log, so you know what the
   configured profile does on your machine.

## Quick start

```text
1. Run Thaw.                                        → tray icon appears
2. The desktop is slow / stuttering / stuck.
3. Press Alt+F4 (or Ctrl+Alt+U for emergency).       → recovery starts immediately
4. Read the balloon: verified / requested / unverified + what changed.
5. Still stuck? Tray menu → Strong recovery (restarts Explorer only).
```

To close a window while Alt+F4 is in `always` mode, click its **X**. To restore normal
Alt+F4 behaviour, choose **tray menu → Alt+F4 behaviour → Disabled**.

## Using Thaw

### Tray icon states

| Icon | Meaning |
|---|---|
| Blue ice shield + golden bolt | Monitoring; machine healthy |
| Red ring + red bolt | Watchdog sees a stall or heavy load |
| Tooltip / status line | CPU %, RAM %, cause, cooldown, privilege level, recovery state |

After each pass a balloon reports the evidence, for example:

```text
Thaw — Unfrozen: recovery verified in 879 ms · trimmed 2/187 processes ·
boosted 6 apps · +563 MB RAM
```

A captured-chord overlay/balloon only acknowledges that the chord was received. It is not
proof that Windows accepted a GPU reset or cache request; the result line and the log are.

### Tray menu

| Item | What it does |
|---|---|
| **Status / Recovery** | Live health, privilege, cooldown, in-progress state |
| **Normal recovery — configured sequence** | Slowdown/system profile: diagnostics, temporary Above Normal boost, pressure-gated memory/cache work |
| **Strong recovery — restart Explorer shell** | Confirmation first. Restarts `explorer.exe` only |
| **Emergency recovery — panic sequence** | Same as Ctrl+Alt+U; bypasses the duplicate-request cooldown but cannot overlap an active pass |
| **Alt+F4 behaviour** | `always` / `stressed` / `off` |
| **Start with Windows** | Per-user registry autostart toggle |
| **Open Config File / Reload Config** | Edit `%APPDATA%\Thaw\config.json` and apply it live |
| **Incident history** | Bounded aggregate receipts of past passes |
| **Export support bundle** | ZIP of log, config, receipts, and read-only diagnostics |
| **Restart as Administrator** | Optional UAC restart |
| **Hotkeys & safety help** | In-app explanation of every chord and action |
| **About Thaw / Exit Thaw** | Version info / quit immediately (no helper left behind when the broker is off) |

### Hotkeys

| Chord | Default mode | What it does |
|---|---|---|
| **Alt+F4** | `always` | Full force-all recovery: display, foreground, memory, shell, network, diagnostics, relief tier |
| **Ctrl+Alt+U** | always intercepted | Emergency/panic recovery |
| **Ctrl+Alt+S** | only while stressed | Slowdown/system recovery |
| **Ctrl+Alt+G** | always | Frame-drop/display recovery, including the graphics-reset request |

All four chords are configurable. Recovery chords are **debounced** (750 ms by default) and
the emergency chord bypasses the short UI cooldown but never overlaps a running pass.

> **Alt+F4 also releases a wedged input state.** If the foreground window is measured as not
> pumping messages, the relief tier sends `WM_CANCELMODE` and one Escape tap to it. A healthy
> window is never touched.

## What a recovery actually does

### Recovery profiles

| Profile | Used by | Surface |
|---|---|---|
| **System / slowdown** | Tray normal recovery, Ctrl+Alt+S | Diagnostics, priority boost, pressure-gated memory/cache work. No display reset, no power-plan change |
| **Display / frame-drop** | Ctrl+Alt+G, automatic DWM-stall rescue | DWM composition evidence, graphics-reset request, DWM progress comparison |
| **Force-all** | Alt+F4, emergency recovery | Everything above plus the relief tier, shell/power options, and diagnostics |
| **Relief** | Alt+F4 and automatic stress rescue | Offender-attributed CPU/affinity/IO relief, priority boosts, wedged-input reset, write-back flush |
| **Shell** | Tray strong recovery | Restarts `explorer.exe` after confirmation. This is the only intentional process restart |

A bounded pass is dispatched across a pre-warmed worker pool (32 slots). Actions can run
concurrently, each with its own deadline; a slow or failing action cannot stall the rest of the
pass. The default budget is **10 seconds**.

### The Alt+F4 relief tier

`AltF4ReliefActions` (default **true**) adds seven bounded actions that provide immediate
scheduling and storage relief, plus a run-length keep-awake guard. They are used by the Alt+F4
force-all chord and by the automatic sustained-stress trigger; no other trigger inherits them,
and the automatic trigger never gets the force-all surface.

| Action | What it does | Reversible |
|---|---|---|
| `foreground-priority-boost` | Boost the foreground process, its busiest threads, and the window's own thread | Yes, exactly |
| `shell-priority-boost` | Same across Explorer, DWM, Start, search, and the text-input host | Yes, exactly |
| `cpu-affinity-relief` | Move the busiest threads of measured CPU hogs off logical CPU 0 (DPC/ISR/timer heavy) | Yes, exact prior mask |
| `cpu-pressure-relief` | Demote measured CPU thieves — including single-core hogs a % -of-all-cores metric would miss — to Idle | Yes, exact prior class |
| `io-pressure-relief` | Demote measured storage offenders to Idle so the device yields to the session | Yes, exact prior class |
| `input-queue-reset` | Only on a *measured-hung* foreground window: `WM_CANCELMODE` + one Escape tap | No (input only) |
| `write-back-flush` | `FlushFileBuffers` on the Windows volume and `RegFlushKey` on SYSTEM/SOFTWARE/SAM | No (flush only) |

Offenders are identified by **instantaneous** CPU and I/O rates over a short bounded window.
The offender-driven actions share one measurement so they act on a consistent set, and each
pass re-checks the process *name* so a recycled PID cannot be mutated instead of the process
that was actually measured.

Nothing is terminated, suspended, or paused. Priority, affinity, and priority-class changes are
restored from the rollback journal at the end of the run.

### Automatic rescue

Two unattended triggers run on the 2-second tray tick. Both call the same
`RequestUnfreeze` entry point as a keypress, so overlap rejection, cooldown, receipts, and
rollback apply unchanged.

| Trigger | Evidence | What runs | Defaults |
|---|---|---|---|
| `AutoRelief` | Watchdog already reports `Stressed` with a non-healthy cause | Reversible relief tier only: probes, priority boosts, affinity/CPU/IO relief, input-queue reset, write-back flush, working-set trim. Never display reset, shell restart, power plan, or force-all | `AutoReliefOnStress=true`, sustained 4 s, cooldown 30 s |
| `FrameDrop` | DWM submitted frames stay pending while `CFramesDisplayed` stops advancing | Frame-drop/display profile, including DWM progress comparison | `AutoDisplayRescueOnDwmStall=true`, stall 3 s, cooldown 60 s |

Automatic relief requires a **fresh sustained window** each time, so a machine that stays
stressed gets one bounded pass per cooldown rather than a retry storm. The DWM check compares
only the displayed-frame counter between ticks: an idle desktop legitimately presents nothing,
so a stall is claimed only while frames are also pending. Both triggers log their measured
evidence and can be disabled from `config.json`.

The older `AutoUnfreezeOnStall` option is different: it fires on a **hard stall**
(`HardStallMs`, default 6000 ms) and runs the explicitly configured sequence. It is off by
default and deliberately not the same as automatic relief.

### Rollback and receipts

Each recovery owns a `RollbackJournal`. When the bounded run ends (success, timeout, or
partial failure), every recorded mutation is restored and the receipt records whether the
restore succeeded. Failed native restores are surfaced as `RollbackCompleted` receipts with
`Unverified` outcomes rather than being hidden behind a success message.

If Thaw is killed mid-run, rollback cannot execute — a changed priority or affinity on another
process can outlive Thaw. That is the honest limit of "reversible".

## Configuration

Config file: **`%APPDATA%\Thaw\config.json`**

Open it from **tray menu → Open Config File**, edit, then **tray menu → Reload Config**.
Missing values fall back to the defaults below; invalid hotkeys are replaced or disabled and
reported in the log. Start conservative and enable one disruptive surface at a time.

```jsonc
{
  "AltF4Mode": "always",                 // "always" | "stressed" | "off"
  "PanicHotkey": "Ctrl+Alt+U",
  "SlownessHotkey": "Ctrl+Alt+S",
  "SlownessHotkeyMode": "stressed",
  "FrameDropHotkey": "Ctrl+Alt+G",
  "FrameDropHotkeyMode": "always",
  "HotkeyDebounceMs": 750,

  "AutoUnfreezeOnStall": false,          // hard-stall auto recovery (explicit opt-in)
  "AutoReliefOnStress": true,
  "AutoReliefSustainedSeconds": 4,
  "AutoReliefCooldownSeconds": 30,
  "AutoDisplayRescueOnDwmStall": true,
  "AutoDisplayStallSeconds": 3,
  "AutoDisplayRescueCooldownSeconds": 60,

  "ShowBalloons": true,
  "DebugLog": false,
  "ShowCaptureOverlay": true,
  "ShowImmediateCaptureFeedback": true,
  "PlayRecoverySound": false,
  "CaptureOverlayDurationMs": 1200,

  "KeepIncidentHistory": true,
  "IncidentHistoryLimit": 100,
  "EnableSupportBundleExport": true,
  "EnableDetailedActionReporting": true,
  "EnableWindowsApplicationRestart": true,

  "ThawWatchdogMode": "off",             // "off" | "relaunch"
  "RescueBrokerMode": "relaunch",        // "off" | "relaunch"
  "WatchdogHeartbeatSeconds": 15,
  "WatchdogRelaunchDelaySeconds": 3,

  "StallThresholdMs": 2500,
  "HardStallMs": 6000,
  "CpuStressPercent": 90,
  "MemStressPercent": 92,

  "PowerPlanBoost": false,
  "PowerPlanRestoreAfterSeconds": 120,
  "RestartExplorerOnUnfreeze": false,
  "ResetGpuDriver": true,
  "RestartDwmOnFrozenScreen": false,
  "AltF4ReliefActions": true
}
```

| Key | Default | Notes |
|---|---|---|
| `AltF4Mode` | `"always"` | `always` disables Alt+F4 window closing; `stressed` only intercepts while the watchdog is alert; `off` restores normal closing |
| `PanicHotkey` | `Ctrl+Alt+U` | Always intercepted |
| `SlownessHotkey` / `SlownessHotkeyMode` | `Ctrl+Alt+S` / `stressed` | Slowdown/system profile |
| `FrameDropHotkey` / `FrameDropHotkeyMode` | `Ctrl+Alt+G` / `always` | Display/frame-drop profile |
| `HotkeyDebounceMs` | `750` | Minimum gap between accepted chords |
| `AutoUnfreezeOnStall` | `false` | Hard-stall auto recovery; runs the configured sequence |
| `AutoReliefOnStress` | `true` | Sustained stress → reversible relief |
| `AutoReliefSustainedSeconds` | `4` | Stress must persist this long |
| `AutoReliefCooldownSeconds` | `30` | Minimum gap between automatic relief passes |
| `AutoDisplayRescueOnDwmStall` | `true` | Frames pending while presents stop advancing |
| `AutoDisplayStallSeconds` | `3` | Stall must persist this long |
| `AutoDisplayRescueCooldownSeconds` | `60` | Minimum gap between automatic display passes |
| `ShowBalloons` | `true` | Completion balloons |
| `DebugLog` | `false` | Verbose hook/DWM/key logging |
| `ShowCaptureOverlay` | `true` | Immediate non-activating acknowledgement |
| `ShowImmediateCaptureFeedback` | `true` | Immediate balloon (when balloons are on) |
| `PlayRecoverySound` | `false` | Optional Windows notification sound |
| `CaptureOverlayDurationMs` | `1200` | Bounded 250–5000 ms |
| `KeepIncidentHistory` | `true` | Aggregate receipts in `%LOCALAPPDATA%\Thaw` |
| `IncidentHistoryLimit` | `100` | Bounded 10–500 |
| `EnableSupportBundleExport` | `true` | One-click ZIP export |
| `EnableDetailedActionReporting` | `true` | Per-action phase/outcome receipts |
| `EnableWindowsApplicationRestart` | `true` | Windows may relaunch Thaw after a crash |
| `ThawWatchdogMode` | `"off"` | Optional Thaw.exe-only relaunch helper |
| `RescueBrokerMode` | `"relaunch"` | Optional Thaw.exe-only crash/rescue helper |
| `WatchdogHeartbeatSeconds` | `15` | Helper heartbeat cadence (bounded) |
| `WatchdogRelaunchDelaySeconds` | `3` | Delay before an eligible relaunch |
| `StallThresholdMs` | `2500` | Scheduling delay that marks the system stressed |
| `HardStallMs` | `6000` | Stall that counts as hard |
| `CpuStressPercent` | `90` | CPU % that counts as fully stressed |
| `MemStressPercent` | `92` | RAM % that counts as fully stressed |
| `PowerPlanBoost` | `false` | Alt+F4-only, temporary High Performance, exact restore at run end |
| `PowerPlanRestoreAfterSeconds` | `120` | Compatibility field; current engine restores at run end |
| `RestartExplorerOnUnfreeze` | `false` | Permit an Explorer restart as part of a recovery |
| `ResetGpuDriver` | `true` | Used by explicit frame-drop/panic profiles |
| `RestartDwmOnFrozenScreen` | `false` | Detection-gated DWM restart (panic profile) |
| `AltF4ReliefActions` | `true` | Relief tier for Alt+F4 and automatic stress rescue |

## Command line

Thaw is a `WinExe` with no console UI, but it exposes read-only diagnostics and helper modes.
Run these from PowerShell in the folder that contains `Thaw.exe`.

| Command | Purpose |
|---|---|
| `Thaw.exe` | Normal tray start |
| `Thaw.exe --diagnose` | Read-only environment, privilege, config, hotkey, and RAM sample |
| `Thaw.exe --self-test` | Icon rendering, DWM timing, rescue channel, and complete action graph checks; no recovery actions |
| `Thaw.exe --smoke` | Start tray/hook/watchdog, then exit after a few seconds |
| `Thaw.exe --test-hook altf4` | Drive the **real** hook callback — **recovery actions run** |
| `Thaw.exe --test-hook panic` | Same, panic chord |
| `Thaw.exe --help` | Usage summary |
| `Thaw.exe --emit-icon <path>` | Write the app icon and exit |
| `Thaw.exe --emit-png <dir>` | Write PNG previews of the icon and exit |
| `Thaw.exe --watchdog --parent-pid <pid>` | Internal Thaw.exe-only relaunch helper |
| `Thaw.exe --rescue-broker --parent-pid <pid>` | Internal Thaw.exe-only rescue broker |
| `Thaw.exe --rescue-fallback` | Internal one-shot headless force-all recovery |

`--diagnose` and `--self-test` never install the hook, reset the GPU, restart DWM/Explorer,
purge a cache, or change the power plan. `--test-hook` is **not** read-only.

## Logs, history, and support bundles

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\Thaw\log.txt` | Rotating 1 MB log of every trigger, action, receipt, and error |
| `%LOCALAPPDATA%\Thaw\incidents.jsonl` | Bounded aggregate receipts (when history is enabled) |
| `%APPDATA%\Thaw\config.json` | Your configuration |

A single trigger line does **not** prove the action succeeded. Look for the completion and
receipt lines:

```text
[INF] Unfreeze triggered (Hotkey)
[INF] Unfreeze done: Recovery verified in 2656 ms · trimmed 2/187 processes · boosted 1 apps · +3100 MB RAM
[INF] GPU reset input accepted (8/8 events)
[INF] Recovery action receipts: ... :RollbackCompleted/Unverified ...
```

**Export support bundle** writes a ZIP containing the log, config, receipts, and read-only
diagnostics. It does not capture application memory or process dumps.

## Safety model

- **No arbitrary process termination or suspension.** The only intentional process restart is
  Explorer, only from the confirmed strong-recovery action or an explicit config option.
- **No arbitrary commands.** Helpers only relaunch `Thaw.exe` and only after a non-zero parent
  exit when the matching mode is `relaunch`; they refuse any other executable path.
- **Structured rollback.** Mutations are applied through actions that record their prior state
  and restore it at the end of the run; failures are reported, not swallowed.
- **Bounded.** Fixed action budget, per-action deadlines, bounded worker pool, bounded history,
  bounded log, bounded overlay duration.
- **Tray-only.** There is no service, driver, scheduled task, or hidden console. Exiting from
  the tray stops Thaw immediately (unless you explicitly enabled a relaunch helper).
- **Fail closed.** Missing privileges or an unreadable prior state causes a step to be skipped
  with a receipt rather than guessed at.

## Honest limits

- **A hard lock cannot be fixed from user mode.** If the CPU never schedules another thread,
  nothing can run — no unfreezer can help.
- **GPU driver hangs, disk hardware failures, thermal shutdowns, and kernel deadlocks** are
  outside an application's reach. Thaw can request a graphics reset; it cannot repair a driver.
- **The display reset is a request, not a proof.** `SendInput` returning 8 means the keystrokes
  were queued. Thaw reports `requested-not-confirmed` unless a real display-change event is
  observed. DWM rescue is the display recovery that is both real and observable.
- **Memory/cache operations are best-effort.** Pages can fault back in, and system-wide cache
  work may need Administrator.
- **Priority cannot fix I/O, GPU, thermal, or hardware limits.** It only helps when scheduling
  is the bottleneck.
- **The relief tier mutates other processes temporarily.** If Thaw is killed mid-run, the
  rollback cannot run and a priority/affinity change may outlive it.
- **`write-back-flush` and the input reset are not undoable.** Flushing moves buffered data to
  disk earlier; the input reset drops a hung window's pending modal state. Both are bounded.
- **The DWM stall heuristic can be wrong.** A false positive costs a brief display hiccup; the
  cooldown prevents loops.
- **The binaries are unsigned**, so SmartScreen may warn.

## Build from source

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/) (the repository includes `global.json`)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — only for the installer
- PowerShell 7+ on Windows

Full release build (portable + installer + hashes):

```powershell
pwsh -File .\build-release.ps1
```

The script publishes a self-contained `win-x64` single file, compiles the installer with
Inno Setup, and writes:

```text
artifacts\release\Thaw-Portable.exe
artifacts\release\Thaw-Setup.exe
artifacts\release\SHA256SUMS.txt
```

Quick build without the installer:

```powershell
dotnet publish Thaw.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts\publish
```

If a machine-level `MSBuildSDKsPath` points at a removed SDK, clear it for the session before
building:

```powershell
$env:MSBuildSDKsPath = $null
```

Run the checks before shipping:

```powershell
.\artifacts\publish\Thaw.exe --self-test
.\artifacts\publish\Thaw.exe --diagnose
```

## Project layout

```text
Thaw/
├── Thaw.csproj              # WinForms app, WinExe (no console), .NET 10
├── Program.cs               # entry point, CLI flags, single-instance mutex, helpers
├── AppContext.cs            # tray icon, feedback, history/export, auto rescue tick
├── KeyboardHook.cs          # global chords, dual low-level hooks, dispatch ring
├── Watchdog.cs              # stress detector (stall/CPU/RAM) + auto-unfreeze
├── Unfreezer.cs             # recovery engine, profiles, offender sampling, relief tier
├── RecoveryCoordinator.cs   # concurrent workers, deadlines, receipts, rollback
├── Config.cs                # %APPDATA%\Thaw\config.json, hotkeys, bounded policy
├── Native.cs                # Win32/ntdll/powrprof P/Invoke
├── NativeDiagnostics.cs     # DWM timing, device/event/foreground evidence
├── Telemetry.cs             # bounded system/foreground/GPU/network evidence
├── RescueBroker.cs          # per-user rescue event + Thaw-only relaunch helper
├── RescueHotkeyMonitor.cs   # helper-side RegisterHotKey receiver
├── Icons.cs                 # GDI+ ice/bolts artwork, ICO/PNG writer
├── Autostart.cs             # "Start with Windows" registry toggle
├── Log.cs                   # rotating file log
├── build-release.ps1        # portable + installer release build
├── installer/Thaw.iss       # Inno Setup definition
├── app.manifest             # asInvoker, PerMonitorV2 DPI
├── Assets/Thaw.ico          # multi-size app icon
├── dev-icons/               # PNG icon previews used by this README
├── README.md                # this file
└── UNFREEZE_METHODS.md      # complete method reference and limits
```

## Troubleshooting

**The tray icon is missing.** Windows hides new tray icons. Open the tray overflow chevron,
or Settings → Personalization → Taskbar → Other system tray icons → enable Thaw. Running
`--smoke` and checking the log proves the icon was created.

**Alt+F4 closes my window instead of recovering.** `AltF4Mode` is `off` (or `stressed` while
healthy). Use Ctrl+Alt+U, or set the mode to `always` in the tray menu/config.

**Nothing happens on Alt+F4.** Check the log for `Recovery requested (...) - handoff=accepted`.
If the chord never appears, another application has it captured (some remote-desktop and
accessibility tools intercept Alt+F4 first). Use Ctrl+Alt+U.

**The display did not reset.** The Windows graphics-reset chord is a request; Windows may
filter synthetic input. Use the strong recovery or enable `RestartDwmOnFrozenScreen`. The log
reports `requested-not-confirmed` when no display-change event arrived.

**Some actions say "unverified" or are skipped.** Thaw did not have Administrator rights, the
prior state was unreadable, or the action timed out. Nothing is guessed: skipped steps are
listed with a reason.

**Recovery keeps firing automatically.** Turn off `AutoReliefOnStress` and/or
`AutoDisplayRescueOnDwmStall`, or raise the sustained/cooldown values, then Reload Config.

**I edited the config and nothing changed.** Use **tray menu → Reload Config**. Invalid JSON is
reported in the log and the previous values remain active.

**Thaw does not start at login.** Enable **Start with Windows** in the tray menu. The portable
build requires the executable to stay at the same path.

## Full method reference

`UNFREEZE_METHODS.md` documents every action, what is provable about it, the exact P/Invoke
surface, the watchdog math, hook hardening, and the techniques that were deliberately built,
measured, and removed because their prior state cannot be read reliably on current Windows.

## Changelog

### 1.4.20 — automatic rescue and honest hotkey fallback

- Add `TriggerReason.AutoRelief`: sustained stress (4 s) now triggers a reversible-relief pass,
  gated by a 30 s cooldown and never granted the force-all surface.
- Add automatic display rescue: a DWM composition stall (frames pending, `CFramesDisplayed`
  not advancing for 3 s) triggers the frame-drop profile with a 60 s cooldown.
- Both paths go through `RequestUnfreeze`, so they cannot overlap a running recovery and are
  journaled/rolled back like a manual pass.
- Stop the per-second hotkey re-registration churn: `RegisterHotKey` cannot claim
  `Alt+F4`, so it is now excluded from the registerable fallback set; Alt+F4 remains covered by
  both low-level hooks and in-process dispatch.
- Self-test now asserts the relief tier is reversible-only and the fallback set is registerable.

### 1.4.19

- Detect **single-core CPU thieves** that a % -of-all-cores metric misses, and demote them with
  the new `cpu-pressure-relief` action.

### 1.4.18

- Add the Alt+F4 relief tier: offender-attributed CPU/affinity/I/O relief, priority boosts,
  wedged-input reset, and write-back flush.

See the git history for the full change log.

---

<div align="center">
<sub>Built for the moment Windows stops answering. If Thaw helped, star the repo.</sub>
</div>
