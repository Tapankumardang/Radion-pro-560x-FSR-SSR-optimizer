---
name: radeon-bootcamp-frame-pacing
description: Diagnose and eliminate frame skips and thermal stutter on 2018 MacBook Pro Boot Camp (Radeon Pro 560X and Core i9-8950HK) using safe 99% CPU thermal caps, Magpie 30/40/60 FPS pacing, 5-tap pre-EASU shaders, and C# 5 optimizer builds.
metadata:
  aas-risk: safe
  aas-source: local-project
  aas-date-added: '2026-11-28'
---

# Radeon Boot Camp Frame Pacing

Eliminate frame skips, cadence judder, and thermal clock drops on a **15" MacBook Pro (2018)** running **Windows 10/11 Boot Camp** (**AMD Radeon Pro 560X 4 GB Polaris 21, `FP16: No`** + **Intel Core i9-8950HK** on a single shared copper heatpipe).

## Safety Boundaries (Mandatory)

- **Zero risky hardware hacks:** NO undervolting, NO GPU overclocking or VBIOS edits, NO undocumented driver UMD binary modifications.
- **Process and service boundaries:** Maximum process priority is `High` (NEVER `RealTime`). NEVER modify `SessionId == 0` system services, audio (`audiodg`), fan control (`MacsFanControl`), or anti-cheat processes/memory (`EasyAntiCheat`, `BattlEye`, `vgc`, `SGuard64`, `NeProtect`, `EOSOverlayRenderer-Win64-Shipping`).
- **Live game protection:** NEVER kill or crash a running game (such as `wwm.exe` or `GTA5.exe`), and NEVER modify encrypted `.bin` game files (e.g., `WhereWindsMeetUJJch\Engine\Config\*.bin`). Wait until the game is closed before writing `settings.xml` or `LocalData\win_config`.
- **Reversibility:** Every registry, `powercfg`, timer, RAM-trim, and Magpie change must remain reversible via `Revert-Optimizations.ps1` or GUI Restore Defaults.

## When to Use

- A game shows micro-stutter, uneven frame cadence, or frame skips on the 60 Hz Retina panel.
- CPU turbo spikes (`80W+`) push the shared heatpipe to 95-100 C and throttle the Radeon Pro 560X from `1004 MHz` down to `300 MHz`.
- Magpie fails to parse `config.json` (UTF-8 BOM error) or drops frames via duplicate-frame detection.
- Updating and rebuilding `RadeonProOptimizer.cs` into `RadeonOptimizer.exe` with C# 5 (`csc.exe`).

## Diagnostic Workflow (Read-Only First)

1. Read `e:\apps\Graphic card improve\PROJECT_MEMORY.md` to confirm hardware constraints and pitfalls.
2. Check active power plan and CPU thermal cap:
   ```powershell
   powercfg /getactivescheme
   powercfg /qh SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX
   powercfg /qh SCHEME_CURRENT SUB_PROCESSOR CPMINCORES
   ```
   Target: `"Radeon Pro 560X - Optimized"` active, AC `PROCTHROTTLEMAX = 0x63` (99%), `CPMINCORES = 0x64` (100% unparked).
3. Inspect running game state and Magpie logs/config without killing the game:
   ```powershell
   Get-CimInstance Win32_Process -Filter "Name='wwm.exe' OR Name='Magpie.exe'" | Select-Object Name, ProcessId, Priority, WorkingSetSize
   Get-Content "e:\apps\Graphic card improve\FSR-Engine\logs\magpie.log" -Tail 40
   ```
4. Verify `C:\Users\tapan\AppData\Local\Magpie\config\v5\config.json` starts with byte `0x7B` (no UTF-8 BOM `0xEF 0xBB 0xBF`).

## Frame-Pacing & Magpie Tuning

On a fixed **60 Hz** Retina display, non-divisor FPS caps (such as `45 FPS`) cause alternating 16.67 ms / 33.33 ms presentation intervals (cadence judder). Use exact divisors or clean thermal targets:

| Preset | `frameRateLimiterEnabled` | `maxFrameRate` | Use Case |
| :--- | :--- | :--- | :--- |
| **30 FPS Constant (Zero Frame-Skip / Coolest)** | `true` | `30.0` | Exact 2:1 divisor of 60 Hz; rock-solid 33.33 ms pacing and lowest heatpipe load |
| **40 FPS Smooth (Thermal Guard)** | `true` | `40.0` | Responsive middle step when 60 FPS cannot be sustained thermally |
| **45 FPS Cap (Balanced)** | `true` | `45.0` | Balanced thermal headroom |
| **60 FPS Cap (Smooth & Cool)** | `true` | `60.0` | Exact 1:1 60 Hz match for lighter titles |
| **Uncapped (Max FPS)** | `false` | `60.0` | Benchmark or low-load titles |

Required Magpie v0.13.0 schema keys in `C:\Users\tapan\AppData\Local\Magpie\config\v5\config.json`:
- Top-level: `"duplicateFrameDetectionMode": 0`, `"enableStatisticsForDynamicDetection": false` (disables dynamic frame skipping).
- `profiles[0]`: `"scalingMode": 7`, `"frameRateLimiterEnabled": true`, `"maxFrameRate": 30.0`, `"3DGameMode": true`, `"disableDirectFlip": false`. Do NOT add unsupported `allowTearing` or `vsync` keys.
- Stop `Magpie.exe` cleanly before writing `config.json` (prevents `config.json.new` overwrite on exit), then write **UTF-8 without BOM**:
  ```powershell
  [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
  ```
- **5-Tap Pre-EASU Shader Order:** Mode 7 runs `SSR_Cinematic_Enhancer.hlsl` (5 bilinear taps) *before* `FSR\FSR_EASU` and `FSR\FSR_RCAS`, cutting shader texture fetches by ~88% at source resolutions (`1280x800` / `1440x900` / `1680x1050`) vs post-EASU `2880x1800`.

## Live Anti-Stutter System Stack

- **Static Baseline:** 99% AC CPU Max State + 100% unparked cores, `EnableUlps = 0`, `TdrDelay = 8`, `HwSchMode = 2`, `OverlayTestMode = 5` (MPO disabled), `GameDVR_Enabled = 0`, `SystemResponsiveness = 0`.
- **Live Frame-Pacing Guard (in `RadeonOptimizer.exe`):**
  1. Enables in-memory `0.5ms` `NtSetTimerResolution(5000, true, out cur)` (auto-released on toggle-off, Restore Defaults, or exit).
  2. Locks Magpie to `30 FPS Constant` (or preserves `40 FPS Smooth`) and starts `Magpie.exe` if stopped.
  3. Runs a 10-second watch timer boosting only `knownGames` to `High` priority (never `RealTime`) and trimming idle Session 1 background working sets every 30 seconds (`EmptyWorkingSet`, skipping `SessionId == 0`, `knownGames`, and all anti-cheat/system processes).

## Rebuilding RadeonOptimizer.exe

`csc.exe` on `.NET Framework 4.x` supports **C# 5 syntax only** (no `$""`, `?.`, or `out var`). If `RadeonOptimizer.exe` is locked by a running instance, rename it to `RadeonOptimizer_old.exe` first:

```powershell
if (Test-Path RadeonOptimizer.exe) { Rename-Item RadeonOptimizer.exe RadeonOptimizer_old.exe -Force }
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:app.manifest /win32icon:app.ico /out:RadeonOptimizer.exe RadeonProOptimizer.cs
```

Always pass `/win32manifest:app.manifest` so UAC elevation (`requireAdministrator`) is embedded in the binary.

## Verification & Rollback

- **Verify build:** `csc.exe` exits `0` and `RadeonOptimizer.exe` starts with `0x4D 0x5A` (`MZ`).
- **Verify Magpie JSON:** First byte is `0x7B` and `ConvertFrom-Json` confirms `duplicateFrameDetectionMode == 0`, `scalingMode == 7`, `frameRateLimiterEnabled == True`, `maxFrameRate == 30`.
- **Rollback:** Click `RESTORE DEFAULTS` in `RadeonOptimizer.exe` or run `Revert-Optimizations.ps1` as Administrator to restore Windows Balanced power plan, default registry values, `15.6ms` timer, and `Normal` process priority.
