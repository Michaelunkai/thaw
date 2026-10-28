# Thaw — Complete Unfreeze Methods Reference

**Every single technique used to unfreeze a stuck/slow Windows PC, documented for future
sessions.** Each entry explains *what* it does, *why* it helps, the *exact API + signatures*,
*where it lives in the code*, *admin requirements*, and *caveats*.

Project layout: `Thaw/` — `Unfreezer.cs` (engine), `Watchdog.cs` (detection),
`KeyboardHook.cs` (Alt+F4 plus panic/slowdown/frame-drop chords), `RescueHotkeyMonitor.cs` (helper fallback), `Native.cs` (all P/Invoke), `AppContext.cs` (tray + wiring),
`Config.cs` (settings), `Icons.cs` (artwork).

---

## 0. Recovery surfaces and sequence

Normal tray recovery uses the slowdown/system profile. Alt+F4 force-runs every configured
display, DWM, system, memory/cache, Explorer, optional temporary power, desktop-refresh,
foreground-probe, and resource-diagnostic tier concurrently. The complete graph is queued
into a 32-worker pre-warmed pool, so neither the diagnostic tail nor the Alt+F4 relief
tier is silently dropped. The force-all graph can reach 27 actions when the DWM, power-plan and
Explorer options are all enabled, so the pool keeps deliberate headroom over it.
Ctrl+Alt+S selects system recovery directly;
Ctrl+Alt+G selects display recovery; the panic chord also combines both profiles.
An enabled automatic trigger uses a diagnostics-only safe profile.
Strong shell recovery is a separate, confirmation-gated call to `RestartExplorerNow()`.

When enabled, the normal/emergency sequence is:

| # | Method | File / function | Admin? |
|---|---|---|---|
| 1 | Own process/thread → bounded Above Normal/Highest recovery priority | `Unfreezer.Run` | no |
| 2 | Profile selection + watchdog diagnostics | `AppContext` / `Unfreezer.Run` | no |
| 3 | GPU driver reset for frame-drop or panic profile (Ctrl+Shift+Win+B) | `Unfreezer.ResetGpuDriver` | SendInput may fail |
| 4 | Panic-only optional DWM rescue after independent hung + stall evidence | `Unfreezer.IsScreenStillFrozen` / `RestartDwm` | opt-in; disruptive |
| 5 | Best-effort working-set tuning for eligible processes | `Unfreezer.TrimWorkingSet` | partial (per-process ACL) |
| 6 | Optional standby + modified page list purge | `Unfreezer.PurgeStandbyList` / `PurgeModifiedPageList` | admin; system-wide |
| 7 | Optional system file cache flush | `Unfreezer.FlushFileCache` | admin; system-wide |
| 8 | Normal-priority foreground/shell → Above Normal temporarily | `Unfreezer.BoostTemporarily` | partial; restored in `finally` |
| 9 | Optional temporary High Performance plan with exact end-of-run restore | `Unfreezer.TryBoostPowerPlanWithJournal` (Alt+F4 only) | admin; machine-wide; restore can be lost on crash |
| 10 | Optional Explorer restart (shell/taskbar) | `Unfreezer.RestartExplorer` | intentional process termination |
| 11 | Tray icon re-registration (after Explorer restart) | `AppContext.ReAddTrayIcon` | — |
| 12 | Bounded desktop/theme refresh broadcast | `Native.TryBroadcastDesktopRefresh` | no; hung recipients are timed out |
| 13 | Foreground `WM_NULL` responsiveness probe | `NativeDiagnostics.TryProbeWindow` | read-only |
| 14 | Resource-cause receipt (commit, pagefile, disk, queue, DPC/ISR, network, GPU, thermal, frames, I/O) | `Unfreezer.BuildResourceDiagnostic` | read-only |
| 15 | Recent event correlation and all present-device problem enumeration | `NativeDiagnostics.CorrelateRecentEvents` / `EnumerateDeviceProblems` | read-only; bounded |
| 16 | Foreground process + busiest-thread priority boost, UI thread to Highest (Alt+F4 relief tier) | `Unfreezer.BoostForegroundWithJournal` | reversible; other process |
| 17 | Shell-surface priority boost (Alt+F4 relief tier) | `Unfreezer.BoostShellWithJournal` | reversible; other process |
| 18 | CPU-affinity offload of measured hogs off processor 0 (Alt+F4 relief tier) | `Unfreezer.RelieveCpuAffinity` | reversible; needs ≥2 CPUs |
| 19 | Idle-priority demotion of measured storage offenders (Alt+F4 relief tier) | `Unfreezer.RelieveIoPressure` | reversible; other process |
| 20 | Execution-Speed EcoQoS clear on measured offenders (Alt+F4 relief tier) | `Unfreezer.ClearEcoQoSOnOffenders` | reversible; other process |
| 21 | Wedged-input reset: `WM_CANCELMODE` + one Escape tap, measured-hung only (Alt+F4 relief tier) | `Unfreezer.ResetStuckInputQueue` | not reversible; injects input |
| 22 | Volume `FlushFileBuffers` + `RegFlushKey` on SYSTEM/SOFTWARE/SAM (Alt+F4 relief tier) | `Unfreezer.FlushWriteBack` | flush only; blocks until accepted |
| 23 | Display/system held awake for the run duration (Alt+F4 relief tier) | `Unfreezer.ArmKeepAwake` | reversible |


The force profile dispatches independent actions concurrently; Explorer restart, when
enabled, is still restricted to the exact interactive shell process. A completion event and
the log are the evidence of an attempted sequence; a trigger line alone does not prove that
Windows accepted every operation. AppContext
also records a bounded aggregate incident receipt plus the engine's per-action phase/outcome/
elapsed/detail and rollback receipts, and shows immediate capture feedback before the worker
starts.

---

## 1. Bounded recovery priority

**Why:** when the PC is slammed, the recovery worker still needs CPU time. Thaw uses
Above Normal for its process only when it began at Normal and `ThreadPriority.Highest`
for the bounded worker; it never uses Real-Time or Time-Critical scheduling.

```csharp
Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
Thread.CurrentThread.Priority = ThreadPriority.Highest;
```

Restored to Normal afterwards in the `finally` block.

- File: `Unfreezer.cs` → `Run()` start / `finally`.
- Implementation uses managed `Process.PriorityClass` / `Thread.Priority` and restores both.

---

## 2. Timer resolution deliberately unchanged

Thaw does not call `timeBeginPeriod` during recovery. A finer periodic timer can increase
scheduler wakeups and power use and does not repair a frame-rate or capacity bottleneck.

---

## 3. GPU driver reset (Ctrl+Shift+Win+B) — requested, not provable

**Why:** Windows exposes **Ctrl+Shift+Win+B** as a graphics-driver reset shortcut. There is no
API for it; the chord is the only user-mode way to ask.

```csharp
// one SendInput call so a partial failure cannot leave a modifier logically held
Key(Ctrl, down); Key(Shift, down); Key(LWIN, down, extended); Key(B, down);
Key(B, up);       Key(LWIN, up, extended);  Key(Shift, up); Key(Ctrl, up);
```

- File: `Unfreezer.cs` → `TryEmergencyDisplayReset()` / `ResetGpuDriver()`.
- P/Invoke: `SendInput` (see `Native.INPUT/INPUTUNION/KEYBDINPUT`). `KEYEVENTF_EXTENDEDKEY` is
  set on the Windows key because it is an extended key and the scan code otherwise does not
  describe it.

### What is proven, and what is not

`SendInput` returning 8 proves the events were **queued**, not that the driver acted on them. So
Thaw arms a `WM_DISPLAYCHANGE` listener (`Native.DisplayChangeProbe`, a message-only window) before
injecting and polls it afterwards. Windows broadcasts `WM_DISPLAYCHANGE` when the display mode is
re-applied, which is the only user-mode evidence that a reset actually took effect.

Measured live on this machine:

```
GPU driver reset chord: accepted=8/8, display-change=not-confirmed
```

The chord is delivered and the broadcast never arrives — consistent with Windows filtering
synthetic input for this shell-level hotkey. Thaw therefore reports **"GPU reset requested"**, never
"GPU reset", and the receipt says `requested-not-confirmed`. The tray summary, the incident receipt
and the run summary were all corrected to stop claiming a reset that cannot be shown.

The proof wait is deliberately shorter than the action's own deadline so that waiting for evidence
can never make the action look like it timed out.

### Why this matters: the real display rescue is the compositor

Because the chord is unverifiable, **restarting the hung compositor is the display recovery that is
both real and observable** (see §4). Alt+F4 now runs it on every force-all pass — which is what
`Config.RestartDwmOnFrozenScreen` has always claimed ("Alt+F4 force-all restarts it
unconditionally") and what the code did not actually do. Verified live:

```
[WRN] User-requested DWM escalation: terminating exact dwm PID 2072
[INF] validated dwm.exe respawned after terminating PID 2072; replacement PID 12928
dwm-restart  ->  exact compositor respawn observed
```

- Caveat: the screen goes black for 1–2 seconds on every Alt+F4, and DWM restart does not fix a
  hardware or kernel-level failure.


---

## 4. DWM soft-freeze rescue (restart the compositor)

**Why:** if the desktop compositor (dwm.exe) appears hung, the screen can stay frozen after
a display reset. When explicitly configured, Thaw waits 1.2 s, checks the compositor, and
may terminate dwm.exe so Windows can respawn it. This is a disruptive, heuristic-gated
operation, not a guaranteed diagnosis or repair.

**Detection — two signals:**
1. `IsHungAppWindow(dwmWindow)` — the OS's own hung check. The window is found by
   class name `DwmNotificationWindow` (classic) or `Dwm` (Windows 11 24H2+), falling back to
   enumerating any top-level window owned by the validated current-session dwm PID via
   `EnumWindows` + `GetWindowThreadProcessId`.
2. Watchdog stall: `Watchdog.LastStallMs >= Config.StallThresholdMs` (latest 1 s sample still
   shows the scheduler frozen).

**Restart:**
```csharp
// Resolve exactly one current-session %WINDIR%\\System32\\dwm.exe first.
// Reject every name-only, wrong-session, wrong-path, or unreadable candidate.
if (TryGetValidatedDwmProcess(out Process? dwm) && dwm is not null)
{
    int oldPid = dwm.Id;
    dwm.Kill(entireProcessTree: false);
    dwm.WaitForExit(1500);
    // Poll only for a different validated dwm.exe before recording success.
}
```

- File: `Unfreezer.cs` → `IsScreenStillFrozen()` / `IsDwmHung()` / `RestartDwm()`.
- P/Invoke: `user32.dll FindWindow, EnumWindows, GetWindowThreadProcessId, IsHungAppWindow`.
- Caveat: display can go black ~1–2 s, and a heuristic can be wrong. Only fires when enabled
  by `RestartDwmOnFrozenScreen` and the configured check reports a possible freeze.

---

## 5. Working-set tuning for eligible processes

**Why:** during memory-pressure freezes, physical RAM can be under pressure and the system
may thrash the pagefile. Where the engine has permission, working-set tuning can give the
system temporary breathing room. It is not a guarantee of freed memory or a substitute for
finding the process or workload causing pressure. Nothing is closed by this step; pages can
fault back in on demand.

```csharp
IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, pid);
EmptyWorkingSet(h);
CloseHandle(h);
```

- File: `Unfreezer.cs` → `TrimWorkingSet()` (called for eligible process candidates).
- P/Invoke: `kernel32.dll OpenProcess`, `psapi.dll EmptyWorkingSet`, `kernel32.dll CloseHandle`.
- Skipped: own process, PID 0/4, and `DoNotTrim` set = {System, Idle, Registry,
  Secure System, Memory Compression}. Access-denied processes are silently skipped.
- Caveat: freed pages fault back in when used — this is the point.

---

## 6. Standby list purge

**Why:** Windows caches files/executables in the "standby" memory list. Under RAM pressure
this cache can compete with active work. An explicitly enabled purge requests that Windows
reclaim the list; it is system-wide and may not be available or beneficial.

```csharp
NtSetSystemInformation(SystemMemoryListInformation /*0x50*/, &MemoryPurgeStandbyList /*1*/, 4);
```

- File: `Unfreezer.cs` → `PurgeStandbyList()`.
- P/Invoke: `ntdll.dll NtSetSystemInformation(int, IntPtr, int)`.
- Requires: **admin** + enabling `SeProfileSingleProcessPrivilege`
  (`Native.EnablePrivilege` → `OpenProcessToken` + `LookupPrivilegeValue` +
  `AdjustTokenPrivileges`).
- Safety: keep disabled while diagnosing unless the user has explicitly opted into a
  machine-wide memory operation.
- Status: logged as `Standby list purge: NTSTATUS 0x00000000` on success.

---

## 7. Modified page list purge

**Why:** dirty pages waiting to be written to disk can contribute to memory pressure. An
explicitly enabled purge asks Windows to process that list; it can be disk-heavy and is not
guaranteed to reduce stalls.

```csharp
NtSetSystemInformation(SystemMemoryListInformation /*0x50*/, &MemoryPurgeModifiedPageList /*2*/, 4);
```

- File: `Unfreezer.cs` → `PurgeModifiedPageList()`.
- Same requirements as standby purge (admin + `SeProfileSingleProcessPrivilege`); keep it
  opt-in because it is system-wide.

---

## 8. System file cache flush

**Why:** the system file cache can hold substantial memory. An explicitly enabled flush asks
Windows to reduce it; applications may need to reload data and the result is workload-
dependent.

```c
SetSystemFileCacheSize((SIZE_T)-1, (SIZE_T)-1, 0);   // -1,-1,0 = flush to minimum
```

- File: `Unfreezer.cs` → `FlushFileCache()`.
- P/Invoke: `kernel32.dll SetSystemFileCacheSize(IntPtr, IntPtr, uint)`.
- Requires: **admin** + enabling `SeIncreaseQuotaPrivilege`.
- Safety: this is a machine-wide cache operation; keep it disabled unless explicitly
  authorized and verify the return value in the log.

---

## 9. Foreground app + shell priority boost

**Why:** priority is relative. A temporary boost to the foreground app plus selected shell
components may improve visible responsiveness, but it can also add scheduler pressure and
does not fix the underlying workload.

```csharp
SetPriorityClass(hProcess, 0x00008000);   // ABOVE_NORMAL_PRIORITY_CLASS
```

Boosted: `GetForegroundWindow()` → `GetWindowThreadProcessId` → that PID; plus `ShellApps` =
{explorer, dwm, ShellExperienceHost, StartMenuExperienceHost, SearchHost}.

- File: `Unfreezer.cs` → `BoostTemporarily()`; only Normal-priority targets are changed and every handle/class is restored.
- P/Invoke: `kernel32.dll OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_INFORMATION)`,
  `kernel32.dll SetPriorityClass`.
- Safety: this is best-effort and should be evaluated with the current workload; a priority
  boost is not evidence that the machine has more capacity.

---

## 10. Power plan safety boundary

The default profile leaves the active power plan untouched. When `PowerPlanBoost` is enabled,
the **Alt+F4 force profile only** may temporarily select Windows' High Performance scheme
(`SCHEME_MIN`) before the force-run. The previously active scheme is captured in the rollback
journal and restored at the end of the bounded pass. The operation is machine-wide, requires
the relevant Windows permission, and is not proof that a recovery action succeeded. A process
crash, power loss, killed helper, or `powercfg` failure can still prevent the restore; inspect
the log and current Windows power-plan state when this matters.
Other triggers record `safety-policy-no-persistent-changes` and do not change the plan.

---

## 11. Explorer restart (shell/taskbar)

**Why:** a hung shell can make the desktop, taskbar, and File Explorer appear frozen.
When explicitly enabled or selected through the confirmed strong action, restarting
Explorer asks Windows to recreate that shell surface. It is not a general process or
hardware recovery.

Equivalent to the reference command:
```powershell
Stop-Process -Name explorer -Force; Start-Sleep 2; Start-Process "$env:windir\explorer.exe"
```

```csharp
foreach (var p in Process.GetProcessesByName("explorer")) { p.Kill(); p.WaitForExit(3000); }
Thread.Sleep(2000);
// preferred: start under the interactive user's token (normal integrity)
WTSGetActiveConsoleSessionId() → WTSQueryUserToken(session, out token)
    → CreateProcessAsUser(token, "...\\explorer.exe", ..., STARTUPINFO{ lpDesktop = "winsta0\\default" })
// fallback: Process.Start(explorer.exe, UseShellExecute = true)  ← same as the PS reference
```

- File: `Unfreezer.cs` → `RestartExplorer()` / `TryStartExplorerAsInteractiveUser()`.
- P/Invoke: `kernel32.dll WTSGetActiveConsoleSessionId`, `wtsapi32.dll WTSQueryUserToken`,
  `advapi32.dll CreateProcessAsUser` (see `Native.STARTUPINFO/PROCESS_INFORMATION`).
- Note: `WTSQueryUserToken` needs SYSTEM (`SeTcbPrivilege`), so from a plain elevated Thaw it
  may fall back to `Process.Start`; verify the resulting Explorer integrity level if it
  matters.
- Config: `RestartExplorerOnUnfreeze` (recommended default off). Tray action: confirmed
  "Strong recovery — restart Explorer shell".
- Explorer is the **only** process intentionally terminated by this operation.

---

## 12. Tray icon re-registration after explorer restart

**Why:** the notification area belongs to explorer. After a shell restart the tray icon must
be re-added (`Shell_NotifyIcon NIM_ADD`). Thaw toggles `NotifyIcon.Visible` on the UI thread
via `SynchronizationContext.Post` right after explorer comes back.

- File: `AppContext.cs` → `ReAddTrayIcon()`; wired from `Unfreezer.ExplorerRestartedNow`.

---

# Alt+F4 relief tier (1.4.18)

The tiers above either restore the display, relieve memory, or diagnose a cause. The relief
tier added in 1.4.18 exists for the other case: **the machine is alive but contended**, and
the user wants the pressure taken off *now*. It runs on the Alt+F4 force-all chord and, since
1.4.20, on the automatic sustained-stress trigger (`AutoRelief`) with the relief subset only.
Both are gated by `AltF4ReliefActions` (default **true**); no other trigger inherits the tier.

Every mutation below is recorded in the run's `RollbackJournal` and restored when the bounded
run ends. Nothing is terminated, suspended, or paused.

## Why instantaneous rates, not cumulative CPU

The historical offender search ranked by `TotalProcessorTime`, which is CPU consumed *since
process start*. A long-running updater or indexer that was busy last week therefore outranks
whatever is saturating the machine right now. The relief tier measures instead:

```csharp
// two passes separated by a bounded delay, same PID set both times
cpuPercent = (cpuAfter - cpuBefore) / (elapsedSeconds * 1000 * ProcessorCount) * 100;
ioRate     = (ioBytesAfter - ioBytesBefore) / elapsedSeconds;   // GetProcessIoCounters
```

A second pass also re-checks the process **name**, because a PID can be recycled inside the
window and the tier mutates whatever it names — acting on a different process than the one
that was measured would be worse than doing nothing. The three offender-driven actions share
one measurement (`SharedReliefSample`, 900 ms cache) so they act on a consistent set and the
run pays for one sampling delay instead of three.

## R1. Foreground process and thread priority boost

**Why:** priority boost doubles a thread's scheduler quantum. It is the least invasive
"run this now" primitive in Win32 and has no kernel counterpart, so an exact restore is always
possible. The window's own thread is the single most targeted target available from user mode:
it must run for the window to paint and accept input.

```csharp
SetProcessPriorityBoost(hProcess, TRUE);
foreach (busiest threads, UI thread always included) SetThreadPriorityBoost(hThread, TRUE);
```

- File: `Unfreezer.cs` → `BoostForegroundWithJournal()`.
- P/Invoke: `SetProcessPriorityBoost`, `SetThreadPriorityBoost` (`kernel32.dll`), and
  `OpenThread` with both the limited and full `THREAD_*INFORMATION` rights because the two
  APIs disagree on which form they accept.
- The UI thread is inserted into the thread set explicitly. `ProcessThreadCollection` order
  is unspecified and the sweep is capped at the 48 busiest threads, so a busy application
  would otherwise routinely have its own window thread excluded.
- **The absolute thread-priority raise was deliberately removed.** Raising the UI thread to
  `THREAD_PRIORITY_HIGHEST` is the obvious next step and it was implemented first — but
  `GetThreadPriority` returns `FALSE` for *every* thread on current Windows 11 builds
  (verified on this machine against both a foreign process and our own threads), and
  `NtQueryInformationThread(ThreadBasePriority)` is refused with `STATUS_ACCESS_DENIED`.
  Without a readable prior value there is no way to restore exactly, and restoring a guessed
  value would silently overwrite an application's deliberate priority. This is the same rule
  `TryBoostProcessQoS` already follows: **no mutation without an exact prior state.** Priority
  boost is kept because it is exactly reversible and demonstrably works.
- Never `HIGH_PRIORITY_CLASS`, never `REALTIME_PRIORITY_CLASS`, never
  `THREAD_PRIORITY_TIME_CRITICAL`. Time-critical scheduling can starve the machine.

## R2. Shell-surface priority boost

**Why:** an unschedulable taskbar is what a user experiences as "Windows is frozen" even on a
machine with spare CPU. Explorer, dwm, Start menu, search, and the text-input host are all part
of that surface.

```csharp
foreach (process in ShellApps) SetProcessPriorityBoost(hProcess, TRUE);
```

- File: `Unfreezer.cs` → `BoostShellWithJournal()`.
- `ShellApps` was widened from five entries to the full desktop shell surface
  (`explorer`, `dwm`, `ShellExperienceHost`, `StartMenuExperienceHost`, `SearchHost`,
  `StartMenu`, `TextInputHost`, `sihost`, `ctfmon`, `ApplicationFrameHost`,
  `ShellNewItemsFolder`, `SystemSettings`, `SearchApp`).
- Bounded to eight processes; every handle is closed by its own rollback entry.

## R3. CPU-affinity offload

**Why:** on current Windows, per-thread affinity is the only supported way to move a busy
process off specific processors. Logical processor 0 carries most DPC/ISR and timer work, so a
thread pinned across every processor competes directly with the interrupt path and the shell.
Removing one processor from the mask gives the interrupt and UI paths room without
descheduling anything.

```csharp
UIntPtr previous = SetThreadAffinityMask(hThread, fullMask & ~(UIntPtr)1UL);
```

- File: `Unfreezer.cs` → `RelieveCpuAffinity()`.
- `SetThreadAffinityMask` returns the thread's **previous** mask, which is exactly what the
  bounded rollback needs — no separate query, no race.
- Skipped below two logical processors, because a one-processor relief mask would strand the
  offender on the only remaining CPU and make the stall worse.
- Targets the busiest threads (`BusiestThreads`), not the first N enumerated;
  `ProcessThreadCollection` order is unspecified, so an unordered prefix would routinely
  re-affine idle threads.
- Bounded to 48 threads per offender and 256 threads sampled per process.

## R4. Storage-pressure demotion

**Why:** a process saturating a queue makes synchronous callers across the whole session block
behind it. Idle priority yields the device to everything else while still letting the offender
make forward progress.

```csharp
SetPriorityClass(hProcess, IDLE_PRIORITY_CLASS);   // 0x40, exact prior class journaled
```

- File: `Unfreezer.cs` → `RelieveIoPressure()`.
- Real-time and high priority classes are treated as protected and never demoted.
- Distinct from `background-demotion` (Below Normal on the working-set/CPU ranking): this one
  is attributed specifically by measured bytes/sec.

## R5. EcoQoS clearing — implemented, then removed on purpose

**Why it was wanted:** Execution-Speed EcoQoS pins a process to a fraction of a processor *by
design*. When it is applied and never released — a real bug class — the application stays
permanently slow and looks exactly like a hang.

**Why it was removed:** the prior state cannot be read on current Windows, so it can never be
restored. Measured on this machine:

| Attempt | Result |
|---|---|
| `GetProcessInformation(ProcessPowerThrottling)` | `FALSE`, `ERROR_BAD_LENGTH` (87) for every access-right combination |
| `NtQueryInformationProcess(ProcessPowerThrottling = 13)` | `STATUS_INFO_LENGTH_MISMATCH` (0xC0000003) at every buffer size from 8 to 32 bytes |

Without the exact prior throttling there is no way to undo the clear, and restoring a guess would
silently change a process the application chose to throttle. The same rule already applied to
`TryBoostProcessQoS` ("without the exact prior state, do not make a one-way QoS change"), so the
action was deleted instead of left in the graph as something that can never fire.
`--self-test` asserts it does not come back.

`foreground-qos` still calls `TryBoostProcessQoS` opportunistically and then falls through to the
reversible Above Normal priority boost, which is what actually runs today.

## R6. Memory-priority boost — implemented, then removed on purpose

**Why it was wanted:** raising a process's memory priority makes the memory manager fault its
pages in more aggressively under RAM pressure, which is genuinely visible on a paging application.

**Why it was removed:** `GetProcessInformation(ProcessMemoryPriority)` returned **contradictory,
non-reproducible results** on this machine. Two byte-identical probes with identical rights and an
identical 4-byte struct alternated between `TRUE` (priority 5) and `FALSE` with
`ERROR_INVALID_PARAMETER`. A mutation whose preconditions cannot be reliably read cannot be
reliably undone, so it was removed rather than shipped. `--self-test` asserts it does not return.

## R7. Wedged-input release

**Why:** a window stuck in a modal loop or holding a mouse capture makes the whole desktop feel
dead while the machine is completely healthy. `WM_CANCELMODE` releases capture and nested modal
loops; one Escape tap clears the stuck key state that keeps a menu or drag alive.

```csharp
// measured first: IsHungAppWindow(hwnd) || IsForegroundHung(ownerPid)
SendMessageTimeout(hwnd, WM_CANCELMODE, 0, 0, SMTO_ABORTIFHUNG, 200, out _);
SendInput(ESC down, ESC up);
```

- File: `Unfreezer.cs` → `ResetStuckInputQueue()`.
- **A healthy foreground window is never touched.** The hang is measured inside the action,
  not taken from the pre-dispatch counter, because the Alt+F4 fast path deliberately skips
  that probe.
- This is the only place Thaw injects input besides the documented graphics reset.
- Caveat, and it is a real one: this is **not undoable**. A hung window's pending menu, drag,
  or in-progress text edit is discarded. The alternative is a window that never responds.

## R8. Write-back and registry-hive flush

**Why:** heavy deferred-write or hive-lock contention produces multi-second stalls where
applications look hung even though CPU and memory are fine. `SYSTEM` backs most Windows
configuration reads; `SOFTWARE` is what the shell queries on every icon and menu rebuild.

```csharp
CreateFile(@"C:\", GENERIC_WRITE, FILE_SHARE_READ|WRITE|DELETE, NULL, OPEN_EXISTING, 0, NULL);
FlushFileBuffers(hVolume);
RegFlushKey(hHive, 0);   // HKLM\SYSTEM, HKLM\SOFTWARE, HKLM\SAM
```

- File: `Unfreezer.cs` → `FlushWriteBack()`; `Native.TryFlushRegistryHive` opens, flushes, and
  closes under one `finally`.
- Volume list is the Windows volume first, then `C:` — at most two. `FlushFileBuffers` on a
  volume does not return until the device accepts the request, so a wider sweep would add
  blocking risk without adding much: a saturated system volume is where freeze-relevant I/O is.
- **The volume must be opened through the Win32 device namespace (`\\.\C:`).** A live run
  proved that `CreateFileW("C:\\", GENERIC_WRITE, …, OPEN_EXISTING, 0, …)` fails with
  `ERROR_PATH_NOT_FOUND` and the flush is silently skipped; `FILE_FLAG_BACKUP_SEMANTICS` on the
  drive-letter path or the `\\.\C:` device path both succeed.
- `CreateFile` failure is `INVALID_HANDLE_VALUE`, not `NULL`. Checking only for zero would pass
  the pseudo-handle into `FlushFileBuffers`.
- Volume open needs Administrator. `RegFlushKey` on `HKLM\SYSTEM` and `HKLM\SOFTWARE` is
  best-effort and can be refused while a hive is locked; each hive is reported independently,
  and a refusal is not treated as a failure of the whole action.
- These are flushes, not mutations: nothing needs undoing. The receipt reports only that the
  requests were accepted, never that a queue actually drained.

## R9. Display/system held awake

**Why:** a display or system power transition landing mid-stall produces a black screen on a
machine that is otherwise alive — indistinguishable from a hard freeze.

```csharp
SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
// ... bounded run ...
SetThreadExecutionState(ES_CONTINUOUS);
```

- File: `Unfreezer.cs` → `ArmKeepAwake()`. Armed synchronously during graph construction, before
  dispatch, so the display cannot drop out while the workers run. Journaled; the restore runs on
  the same recovery-owner thread that armed it.

---

## Automatic rescue triggers (1.4.20)

Before 1.4.20 the only unattended path was `AutoUnfreezeOnStall`, which requires a hard stall
(delay >= `HardStallMs`) and then runs the full configured sequence. That misses two cases: a
machine that is merely contended (no hard stall yet, but the desktop is unusable), and a DWM
composition stall where input still arrives but the screen stops presenting.

`AppContext.AutoRescueTick()` runs on the existing 2-second UI timer and drives two independent
gates. Both call the same `RequestUnfreeze` entry point as a keypress, so the engine overlap
rejection, UI cooldown, receipts, and rollback apply unchanged.

### A1. Sustained stress -> reversible relief (`TriggerReason.AutoRelief`)

- Evidence: `Watchdog.Stressed` is already true and `Watchdog.Cause != Healthy`; the watchdog
  hysteresis keeps the flag set while the machine settles.
- Gate: the condition must persist for `AutoReliefSustainedSeconds` (default 4 s), and the last
  automatic pass must be at least `AutoReliefCooldownSeconds` (default 30 s) ago. A fresh window
  is required for every pass, so continuous stress yields one bounded pass per cooldown.
- Profile: `GetRecoveryProfile(AutoRelief)` returns `(display: false, system: true, memory: true)`;
  it is never force-all. `RecoveryCoordinator.SelectCauseDirectedActions` returns
  `AutoReliefActions` (`resource-diagnostics`, `foreground-probe`, `foreground-priority-boost`,
  `shell-priority-boost`, `cpu-affinity-relief`, `cpu-pressure-relief`, `io-pressure-relief`,
  `input-queue-reset`, `write-back-flush`, `working-set-trim`). Display reset, DWM restart, shell
  restart, and power-plan changes are excluded by name and asserted by `--self-test`.
- Why this is not the full chord: an unattended process must not restart Explorer or change the
  display mode without the user asking. The relief subset is exactly reversible and helps a
  CPU/IO-contended machine; the disruptive surfaces stay behind the explicit chords.

### A2. DWM composition stall -> display rescue (`TriggerReason.FrameDrop`)

- Evidence: `NativeDiagnostics.TryGetDwmTiming` reports `Supported`, `CFramesPending > 0`, and
  `CFramesDisplayed` did not advance between two ticks. Because DWM presents only on damage, an
  idle desktop shows no new frames; requiring pending frames distinguishes idle from wedged.
- Gate: stall must persist `AutoDisplayStallSeconds` (default 3 s), with
  `AutoDisplayRescueCooldownSeconds` (default 60 s) between automatic display passes.
- Profile: the existing frame-drop/display profile, including its DWM progress comparison and the
  display surfaces already used by Ctrl+Alt+G. It does not take the panic-only DWM restart path
  unless `RestartDwmOnFrozenScreen` and the frame-drop profile already select it.

Both triggers log their measured evidence (`Auto relief: sustained stress (...)` and
`Auto display rescue: DWM composition stalled (...)`) so the log explains why a pass started.
Both are controlled from `%APPDATA%\Thaw\config.json` (`AutoReliefOnStress`,
`AutoReliefSustainedSeconds`, `AutoReliefCooldownSeconds`, `AutoDisplayRescueOnDwmStall`,
`AutoDisplayStallSeconds`, `AutoDisplayRescueCooldownSeconds`) and can be disabled without
downgrading.


## What the relief tier deliberately does NOT do

- It does **not** suspend, pause, or kill anything. `NtSuspendProcess` remains removed by request.
- It does **not** raise any thread's absolute priority, and never uses `THREAD_PRIORITY_TIME_CRITICAL`
  or a high/real-time priority class. `GetThreadPriority` is unusable on current Windows 11, so an
  absolute raise cannot be rolled back and is therefore not attempted.
- It does **not** restrict any process. A starved foreground window needs the boost even if it is
  a browser with 400 threads; the boost is reversible, so it is proportionate.
- It does **not** write registry values, change the power plan, or touch services.
- It does **not** trust cumulative counters to pick a victim.

**Reversibility limit, stated plainly.** Every mutation here is journaled and restored when the
run ends. If Thaw is killed mid-run, the rollback cannot execute. Priority boost on Thaw's own
handles disappears with the process, but an affinity or priority-class change applied to
*another* process outlives Thaw until that process exits or the setting is changed back. This is
recorded in the run receipts rather than described as cleanly reversible.

---

## Watchdog — how "stressed / frozen" is detected

`Watchdog.cs`, a Highest-priority background thread sampling every 1 s. A single
native/probe exception is rate-limited in the log and isolated to that sample so the
detector continues on the next interval:

1. **Scheduling delay (the freeze detector).** `Environment.TickCount64` before/after
   `Thread.Sleep(1000)`. If the machine froze, the thread wakes late:
   `delayMs = wallMs - 1000`. Any delay > 0 means the OS could not schedule this thread.
2. **CPU%** via `GetSystemTimes` deltas (idle/kernel/user).
3. **RAM%** via `GlobalMemoryStatusEx.dwMemoryLoad`.

**Stress score** (0..1): `0.55·stallF + 0.25·cpuF + 0.20·memF`, where stallF =
clamp(delay/8000), cpuF = clamp((cpu−60)/(CpuStressPercent−60)), memF =
clamp((mem−70)/(MemStressPercent−70)).

**Stressed** when score > 0.40 **or** delay ≥ `StallThresholdMs` (2500). Hysteresis: stays
stressed until 5 consecutive healthy samples with score < 0.25 and delay < 1200 ms.
**Hard stall** (delay ≥ `HardStallMs`, 6000) fires `HardStallDetected`; the tray requests
automatic recovery only when `AutoUnfreezeOnStall` is explicitly enabled (recommended
default off), then the watchdog takes a 2 s breather. `RecentStall` = stall within the last
30 s. A hard-stall signal is not a root-cause diagnosis.

- P/Invoke: `kernel32.dll GetSystemTimes`, `kernel32.dll GlobalMemoryStatusEx`
  (`Native.MEMORYSTATUSEX`).

---

## Keyboard hook — how Alt+F4 is captured

`KeyboardHook.cs` — a global `WH_KEYBOARD_LL` low-level hook installed with
`SetWindowsHookEx(13, proc, GetModuleHandle(null), 0)` on a dedicated highest-priority
native message-loop thread independent of `Application.Run`. A 250/500 ms heartbeat renews
the primary/emergency hooks and forces a bounded 3/5-second handle renewal when needed,
while preallocated dispatch workers and a per-user rescue event preserve the sub-second
activation path. Every key event updates a modifier-state tracker
(handles **both** generic and left/right VKs: VK_MENU 0x12 / VK_LMENU 0xA4 / VK_RMENU 0xA5,
VK_CONTROL 0x11 / VK_LCONTROL 0xA2 / VK_RCONTROL 0xA3, VK_SHIFT 0x10 / 0xA0 / 0xA1 — real
keyboards send the left/right codes; the generic-only bug was caught and fixed).
Right-Alt is also recognized as AltGr; Ctrl+Alt recovery matching is skipped while AltGr
text input is active.

**Alt+F4:** on F4 (0x73) keydown with Alt (`LLKHF_ALTDOWN` = flags bit 0x20, or tracked
state), `_shouldInterceptAltF4()` decides by `AltF4Mode`:
- `always` → always swallow (explicit compatibility mode; window closing via Alt+F4 disabled),
- `stressed` → swallow only while `Watchdog.Stressed || Watchdog.RecentStall`,
- `off` → never swallow.

Swallowing = return `(IntPtr)1` from the hook proc (the keystroke never reaches any window),
then request recovery. **Alt+F4** and **panic** (default Ctrl+Alt+U) request the combined profile;
**slowdown** (Ctrl+Alt+S) selects system recovery and **frame drop** (Ctrl+Alt+G) selects
display-only recovery. Held repeats, injected input, duplicate chords, and presses inside
the configurable debounce window are rejected. Config reload rebuilds the live binding table.

- P/Invoke: `user32.dll SetWindowsHookEx/UnhookWindowsHookEx/CallNextHookEx/GetAsyncKeyState`,
  `KBDLLHOOKSTRUCT` (vkCode, scanCode, flags, time, dwExtraInfo).
- Note: LL hooks receive input across integrity levels, so Alt+F4 is caught even when an
  elevated window is focused.

---

## Capture-path hardening (1.4.8–1.4.18)

The keybind path now has several independent boundaries so a busy UI thread or a damaged
single hook does not erase the user's emergency request:

1. **Two independent low-level hooks plus a supervisor** — the primary and emergency
   `WH_KEYBOARD_LL` hooks use separate native message-loop threads. Each has its own handle,
   heartbeat, and bounded reinstall path; an independent supervisor recreates a hook thread
   if its message loop exits, while the newer hook gets the first opportunity and the primary
   remains a fallback.
2. **Lock-free callback edge** — callbacks use per-hook key state, atomic configuration
   references, a concurrent debounce table, and no synchronous file logging or recovery work.
   Callback faults are counted and passed through to Windows.
3. **Two-tier pre-warmed dispatch** — a 32-slot dispatch ring is backed by one reserved
   slot for the case where cleanup briefly owns the normal queue, plus a separate worker,
   wait handle, and final slot for simultaneous ring/worker pressure. A capture is never
   allowed to run the recovery engine synchronously; only an extreme burst that fills all
   pre-warmed slots is surfaced as a dropped count.
4. **Persistent dispatch boundary** — both dispatch workers isolate failed delivery or
   queue waits, clear only the affected slot, and retry instead of terminating a worker or
   clearing the entire captured ring. Event subscribers are invoked from a snapshot one at
   a time, so one throwing consumer cannot prevent later consumers from running.
5. **Named signal plus acknowledgement** — the capture edge pulses a per-user `Local\\`
   event and the dispatch worker pulses a paired acknowledgement event only after it has
   delivered the request. The exact signal sequence travels with each pre-warmed dispatch
   slot, so an earlier queued request cannot acknowledge a later one. Sequence gating is
   committed only after the native pulse succeeds, so a transient event-handle failure remains
   retryable and duplicate acknowledgements do not leave stale fallback pulses.
6. **In-process and out-of-process registered-hotkey fallback** — the main hook thread and
   the Thaw-only rescue helper register always-on panic/frame-drop chords. Alt+F4 is excluded
   from RegisterHotKey registration in every mode (Windows rejects `MOD_ALT|VK_F4` with
   `ERROR_HOTKEY_ALREADY_REGISTERED`, which previously made the set look permanently
   incomplete and churned re-registration every second); Alt+F4 stays on the low-level hooks
   and the in-process dispatch fallback. If the main process does not acknowledge a request
   within the bounded window, the helper starts one headless, force-all recovery. Both
   registrations re-register after recoverable message-loop failures; the helper also refreshes
   them after a live config change. Rejected registrations are retried, healthy registrations
   are periodically renewed, and the parent monitor recreates the helper thread if it exits.
   RegisterHotKey is a recovery wake-up fallback, not a promise of Alt+F4 close suppression.
7. **Pre-warmed recovery handoff** - `Unfreezer` starts a high-priority request worker before
   any shortcut is pressed. A trigger only publishes a compact reason and signals the existing
   worker, so recovery does not depend on creating a managed thread under scheduler or memory
   pressure. If the worker exits unexpectedly, the next trigger attempts a bounded recreation;
   failed handoff is released so a later shortcut can retry.
8. **Force-all fast path** — Alt+F4 determines its force-all profile before foreground,
   DWM, Explorer, or foreground-name probes that may themselves be stalled. It queues the
   complete recovery graph first; synchronous engine and tray acceptance/diagnostic logging
   is also deferred until after that handoff. Optional sound/UI feedback is posted
   asynchronously after handoff, and each pre-dispatch native probe is failure-isolated, so a
   diagnostic, log-volume, audio, or UI delay cannot postpone or cancel the first recovery
   mutations.
9. **Probe-failure containment** — unavailable memory, foreground, DWM, Explorer, or
   telemetry probes fall back to bounded defaults/watchdog evidence and still dispatch the
   configured recovery graph. Probe-detail logging is deferred until workers are signalled.
10. **Dispatch-fence cleanup** — the coordinator closes every action batch in a `finally`
   path even if its bounded wait faults. If late workers remain, the active-run reservation is
   held until they drain; if a drain watcher cannot be started, the owner waits safely instead
   of allowing late native mutations to overlap a newer shortcut recovery.
11. **Capture telemetry** — support diagnostics expose primary/emergency installation,
   registered fallback count, available capture path, capture count, dispatch drops, callback
   faults, and the most recent captured chord.
12. **Transactional rescue publication** — a rescue sequence becomes acknowledgeable only
   after the native signal event accepts it. A failed pulse returns sequence zero, so a later
   delivery cannot accidentally acknowledge an older published capture.
13. **Bounded wake recovery** — normal and emergency dispatch workers poll their pre-warmed
   slots on a short interval in addition to the event wake. A transient event-handle or
   `SetEvent` failure therefore leaves the captured request available for delivery.
14. **Timer-independent hook health** — primary and emergency low-level-hook loops, plus the
   registered-hotkey monitor, use bounded `MsgWaitForMultipleObjectsEx` polling. Heartbeats,
   hook renewal, and fallback registration maintenance continue when `SetTimer` fails or
   silently stops producing `WM_TIMER` messages.
15. **Native callback containment** — the `CallNextHookEx` pass-through is guarded so an
   exceptional native chain call cannot escape into Windows' low-level hook dispatcher or
   terminate the capture thread.
16. **Terminal failure release** — engine failures publish an explicit unverified completion,
   trigger failures release the tray retry fence immediately, and optional UI posts are
   guarded. A closed dispatch also completes queued actions as skipped instead of starting a
   late native mutation.
17. **Worker-boundary completion** — the pre-warmed recovery handoff tracks the request it has
   claimed and publishes an explicit unverified completion if the worker exits before `Run()`
   can report one. Stranded pending requests are converted into the same terminal receipt, so
   the tray retry guard and rescue helper cannot wait for an impossible acknowledgement.
18. **Dispatch-worker supervision** — the independent hook supervisor and both hook heartbeats
   watch the normal and emergency delivery workers. If one exits, its queue/slot remains
   available for a bounded restart instead of leaving already-captured shortcuts undelivered.
19. **Partial prewarm tolerance** — recovery worker creation records individual startup failures
   and continues with the workers that did start. If resource pressure prevents every worker from
   starting, the trigger becomes a terminal unverified result instead of crashing or hanging the
   tray process.
20. **Crash-time rescue escalation** — after a missing acknowledgement, the Thaw-only helper
   checks the parent's exit code. A nonzero crash exit escalates the captured request directly to
   the bounded headless force-all fallback; a clean exit remains non-escalating to avoid duplicate
   work.
21. **Aborted-dispatch fencing** — if action setup fails after filling any preallocated slots, the
   coordinator closes the partial batch while holding the queue gate, wakes the workers, and keeps
   the active-run reservation until any already-running action drains. Queued actions therefore
   finish as skipped instead of becoming untracked recovery mutations.
22. **DWM scheduling assist** — display-oriented panic, frame-drop, and Alt+F4 recovery can
   temporarily enable DWM participation in MMCSS, then restore the documented default through
   the rollback journal. This gives the compositor a bounded scheduling boost without leaving a
   persistent policy change.
23. **Resource-starved rescue fallback** — if the separate rescue helper cannot construct or hand
   off the full recovery engine, it still submits the allocation-light Ctrl+Shift+Win+B graphics
   reset directly instead of exiting without any recovery attempt. A failed full handoff therefore
   produces an explicit bounded result and one last native display-recovery attempt.
24. **Emergency callback fast path** — the independent rescue hook tracks modifier state locally and
     handles Alt+F4 without additional asynchronous key-state probes, reducing callback work while
     Windows is waiting for the low-level hook decision. Custom chords retain the existing state
     fallback for missed modifier events.
25. **Resource-safe worker creation** — recovery-worker construction, priority setup, publication, and
    thread start share one guarded boundary, so an allocation or start failure becomes a bounded
    handoff failure instead of escaping before the shortcut can publish its terminal receipt.
26. **Independent key-state polling fallback** — the separate rescue helper observes the current state
    of always-on emergency chords every 50 ms and emits only rising-edge requests. It remains useful
    when both low-level hooks and `RegisterHotKey` are unavailable, never injects or suppresses input,
    and establishes a baseline at startup so held keys do not create a false trigger.
27. **Pre-sized hook key state** — both independent low-level hooks reserve pressed-key capacity at
    startup, removing first-use collection growth from the callback path while Windows is waiting for
    its hook decision.
28. **Rescue completion-timeout escape hatch** — if the out-of-process headless force-all fallback
    cannot publish a terminal receipt within its bounded wait, or reports measured degradation, it
    submits the allocation-light graphics reset before exiting instead of silently ending rescue with
    no final display attempt.
29. **Handoff-gated rescue acknowledgement** — the main process acknowledges a captured rescue
    signal only after the recovery owner confirms that the request entered its pre-warmed handoff.
    A subscriber failure or genuine worker-start/queue failure therefore leaves the helper free to
    execute its independent bounded fallback; deliberate overlap handling is described next.
30. **Explicit rescue disposition** — an already-running recovery or deliberate cooldown is
    reported as intentionally covered, so it cannot spawn a duplicate force-all process, while a
    genuine queue/handoff failure remains unacknowledged and still escalates through the helper.
31. **Pre-wait dispatch abort** — if the recovery owner fails after queuing a batch but before
    reaching its normal bounded wait, the coordinator closes that batch under the queue gate,
    wakes workers to skip queued actions, and preserves the drain fence until late actions leave.
32. **Graph-capacity headroom** — the pre-warmed pool is sized above the force-all graph, and
    `BuildActionSpecs` warns explicitly if a future action pushes the graph past the pool, so
    the tail of a run is never dropped behind a generic capacity message.
33. **Attributed, not cumulative, offender selection** — the relief tier measures instantaneous
    CPU and I/O across a bounded window and re-checks the process name on the second pass, so a
    recycled PID cannot be mutated in place of the process that was actually measured.
34. **Single-owner handles** — every relief mutation registers exactly one rollback entry per
    handle, and that entry performs both the restore and the `CloseHandle`, so no handle is
    closed twice and none is leaked when a restore is skipped.


These are still user-mode recovery paths. If Windows cannot schedule either process, the
input stack, kernel, power source, or hardware has failed, no executable can react.

---

## Privilege enabling (admin helpers)

`Native.EnablePrivilege(name)` — `OpenProcessToken(TOKEN_QUERY|TOKEN_ADJUST_PRIVILEGES)` →
`LookupPrivilegeValue` → `AdjustTokenPrivileges(SE_PRIVILEGE_ENABLED)`.

Used for: `SeProfileSingleProcessPrivilege` (standby/modified purge),
`SeIncreaseQuotaPrivilege` (file cache flush). `Native.IsElevated()` uses
`WindowsIdentity` + `WindowsPrincipal.IsInRole(Administrator)`; deep memory ops run only when
elevated, everything else degrades gracefully. Tray menu → "Restart as Administrator"
re-launches with `--elevated` (skips the single-instance mutex).

---

## Config reference (`%APPDATA%\Thaw\config.json`)

```jsonc
{
  "AltF4Mode": "always",                 // "always" | "stressed" | "off"
  "PanicHotkey": "Ctrl+Alt+U",           // e.g. "Alt+F4", "Ctrl+Shift+U", "F12"
  "SlownessHotkey": "Ctrl+Alt+S",
  "SlownessHotkeyMode": "stressed",
  "FrameDropHotkey": "Ctrl+Alt+G",
  "FrameDropHotkeyMode": "always",
  "HotkeyDebounceMs": 750,
  "AutoUnfreezeOnStall": false,          // opt in: auto recovery on hard stall
  "ShowBalloons": true,                  // unfreeze result balloons
  "DebugLog": false,                     // verbose key/hook/DWM logging
  "StallThresholdMs": 2500,              // delay that flags the system as stressed
  "HardStallMs": 6000,                   // stall that triggers auto-unfreeze
  "CpuStressPercent": 90,                // CPU% counting as fully stressed
  "MemStressPercent": 92,                // RAM% counting as fully stressed
  "PowerPlanBoost": false,               // Alt+F4 only: temporary High Performance selection
  "PowerPlanRestoreAfterSeconds": 120,   // compatibility setting; current bounded runs restore the exact prior plan at run end
  "ShowCaptureOverlay": true,            // non-activating immediate capture acknowledgement
  "ShowImmediateCaptureFeedback": true,  // immediate balloon before recovery work starts
  "PlayRecoverySound": false,            // optional system acknowledgement sound
  "CaptureOverlayDurationMs": 1200,      // bounded to 250..5000 ms
  "KeepIncidentHistory": true,           // bounded aggregate receipts in %LOCALAPPDATA%\Thaw
  "IncidentHistoryLimit": 100,            // bounded to 10..500 receipts
  "EnableSupportBundleExport": true,     // user-selected ZIP of diagnostics/log/config/receipts
  "EnableDetailedActionReporting": true,  // persist bounded per-action phase/outcome/detail receipts
  "EnableWindowsApplicationRestart": true,
  "ThawWatchdogMode": "off",            // "off" | "relaunch"; Thaw-only child monitor
   "RescueBrokerMode": "relaunch",       // default; Thaw-only capture/crash fallback monitor
  "WatchdogHeartbeatSeconds": 15,        // reserved helper heartbeat cadence (bounded)
  "WatchdogRelaunchDelaySeconds": 3,     // delay before an eligible nonzero-exit relaunch
  "RestartExplorerOnUnfreeze": false,    // permit restart only if Explorer is measured hung
  "ResetGpuDriver": true,                // explicit frame-drop/panic profiles
  "RestartDwmOnFrozenScreen": false,     // opt in: detection-gated DWM restart
  "AltF4ReliefActions": true,            // Alt+F4 + AutoRelief: offender-attributed relief tier
  "AutoReliefOnStress": true,            // sustained stress -> reversible relief automatically
  "AutoReliefSustainedSeconds": 4,       // stress must persist this long before the auto pass
  "AutoReliefCooldownSeconds": 30,       // minimum gap between automatic relief passes
  "AutoDisplayRescueOnDwmStall": true,   // frames pending while presents stop advancing
  "AutoDisplayStallSeconds": 3,          // composition stall must persist this long
  "AutoDisplayRescueCooldownSeconds": 60 // minimum gap between automatic display passes
```


Treat this as a conservative reference profile. Existing config values remain user-owned;
inspect and change them deliberately, then use **Reload Config**. The tray shows whether
system-level operations are available under the current token.

---

## What Thaw deliberately does NOT do (and why)

- **Never suspends/pauses any process** (removed — `NtSuspendProcess` was dropped by request).
- **Never kills arbitrary application/service processes.** Explorer can be restarted only
  by the confirmed shell action or when enabled and measured hung.
- **No dwm.exe kill except the explicit panic profile**, with configuration plus independent
  DWM-hung and scheduler-stall evidence; the heuristic is not infallible.
- **No automatic service, adapter, audio, storage, input, or PnP-device changes**; these
  surfaces are diagnosed and left for an explicit, informed decision.
- **No GPU overclocking / no registry writes** (except the "Start with Windows" Run key).
- **No timer-resolution change** (`timeBeginPeriod` is never called).
- **No high/real-time priority for another process**, and never `THREAD_PRIORITY_TIME_CRITICAL`.
  The relief tier raises at most the foreground window's own UI thread, to Highest.


## Hard limits (physics, not Thaw)

- A total kernel panic / hardware deadlock / power failure cannot be fixed by **any**
  user-mode application — nothing can run while the CPU itself is frozen.
- GPU *hardware* failure and dying disks are outside software's reach.
- Everything Thaw can safely attempt or classify: slow/stutter/unresponsive-but-recovering —
  screen (GPU reset + optional DWM rescue), shell (Explorer refresh/restart), RAM
  (pressure-gated trim/cache work), foreground/background scheduling, desktop refresh, and
  bounded evidence for disk, commit, DPC/ISR, network, GPU, thermal, device, and deadlock
  causes. Timer resolution is left unchanged. If explicitly enabled, only an Alt+F4 force
  capture may temporarily select High Performance; that machine-wide change can outlive a
  crash or power loss and is not a repair guarantee.

## Verification log (`%LOCALAPPDATA%\Thaw\log.txt`)

Every method logs its outcome — the fastest way to confirm a fix worked:

```
[INF] Unfreeze triggered (Hotkey)
[INF] GPU reset input accepted (8/8 events)
[DBG] DWM check: hwnd=0x10048 hung=False
[INF] Power-plan boost skipped by safety policy; active plan left unchanged
[INF] Standby list purge: NTSTATUS 0x00000000
[INF] System file cache flush: True
[INF] explorer killed: True
[INF] explorer started (fallback)
[INF] Unfreeze done: ... · trimmed 2/187 processes · boosted 1 apps · +864 MB RAM · GPU reset
[INF] Tray icon re-registered after explorer restart
```
