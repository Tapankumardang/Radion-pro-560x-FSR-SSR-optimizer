# AGENTS.md - Agent Instructions & Project Memory Protocol

> This file is loaded automatically into the Antigravity / Gemini agent context whenever working inside this workspace.

## 📌 CRITICAL DIRECTIVE: PROJECT MEMORY FIRST & ALWAYS UPDATED

### 1. Before Planning or Acting:
- **Always read [`PROJECT_MEMORY.md`](file:///e:/apps/Graphic%20card%20improve/PROJECT_MEMORY.md)**:
  - Understand the hardware constraints: **15" MacBook Pro (2018)**, **Radeon Pro 560X (4 GB)**, **Intel Core i9-8950HK (6C/12T)** on **Windows Boot Camp**.
  - Note the shared single heatpipe thermal dynamics: The **99% CPU maximum processor state** is essential to prevent CPU 80W turbo spikes that choke the GPU clock down to 300 MHz.
  - Respect established tweaks: ULPS disabled, TdrDelay = 8, HAGS enabled, dedicated custom power plan.
- **Review Past Mistakes & Pitfalls (Section 5 of `PROJECT_MEMORY.md`)**:
  - Never use slow PowerShell `Expand-Archive` for large zips — always use `tar.exe -xf`.
  - Never dump all 3,000+ skills into active context simultaneously — keep the library in `~/.agents/skills_library` and activate targeted bundles using `C:\Users\tapan\.agents\Activate-Skills.ps1`.
  - Never call `read_file`/`edit_file` on internal protected files (`config.json`).
  - Never run registry or powercfg modifications without checking for Administrator privileges.

### 2. MANDATORY PROTOCOL: Update Memory on Every Change:
- **EVERY SINGLE TIME any change, edit, fix, script modification, or new feature is made in this workspace, the agent MUST immediately update [`PROJECT_MEMORY.md`](file:///e:/apps/Graphic%20card%20improve/PROJECT_MEMORY.md).**
- Record:
  - What was added, modified, or removed, and the exact files involved.
  - The date and rationale for the change.
  - Any new pitfalls discovered or mistakes to avoid in the future.
  - Updated status in the file inventory and pending roadmap.

---

## 🛠️ Key Files in This Repository

- **GUI Application:** [`RadeonOptimizer.exe`](file:///e:/apps/Graphic%20card%20improve/RadeonOptimizer.exe) / [`RadeonProOptimizer.cs`](file:///e:/apps/Graphic%20card%20improve/RadeonProOptimizer.cs)
- **CLI Scripts:** [`Optimize-GPU.ps1`](file:///e:/apps/Graphic%20card%20improve/Optimize-GPU.ps1), [`Revert-Optimizations.ps1`](file:///e:/apps/Graphic%20card%20improve/Revert-Optimizations.ps1)
- **Batch Elevators:** [`Launch-UI.bat`](file:///e:/apps/Graphic%20card%20improve/Launch-UI.bat), [`Run-Optimize.bat`](file:///e:/apps/Graphic%20card%20improve/Run-Optimize.bat), [`Run-Revert.bat`](file:///e:/apps/Graphic%20card%20improve/Run-Revert.bat)
- **Documentation:** [`README.md`](file:///e:/apps/Graphic%20card%20improve/README.md)
- **Project Ledger:** [`PROJECT_MEMORY.md`](file:///e:/apps/Graphic%20card%20improve/PROJECT_MEMORY.md)
- **Skill Manager:** [`C:\Users\tapan\.agents\Activate-Skills.ps1`](file:///C:/Users/tapan/.agents/Activate-Skills.ps1)
