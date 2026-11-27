# =====================================================================
# GPU & System Optimization Script for Radeon Pro 560X (Boot Camp)
# =====================================================================
# Target Hardware: AMD Radeon Pro 560X + Intel Core i9-8950HK
# Purpose: Prevent thermal downclocking, disable ULPS stutter,
#          optimize GPU scheduling, and configure thermal headroom.
# =====================================================================

# Check for Administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "This script requires Administrator privileges to modify system registry and power schemes."
    Write-Host "Please re-run this script in an elevated PowerShell session (Run as Administrator)." -ForegroundColor Red
    exit 1
}

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "   Applying GPU Optimizations for Radeon Pro 560X       " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. Disable ULPS (Ultra Low Power State) across all AMD display class keys
Write-Host "`n[1/5] Disabling AMD ULPS (eliminates GPU power-state stutter)..." -ForegroundColor Yellow
$amdClassPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
$ulpsKeys = Get-ChildItem -Path $amdClassPath -Recurse -ErrorAction SilentlyContinue | Where-Object {
    (Get-ItemProperty -Path $_.PSPath -Name "EnableUlps" -ErrorAction SilentlyContinue) -ne $null
}

if ($ulpsKeys) {
    foreach ($k in $ulpsKeys) {
        Set-ItemProperty -Path $k.PSPath -Name "EnableUlps" -Value 0 -Type DWord -Force
        Set-ItemProperty -Path $k.PSPath -Name "EnableUlps_NA" -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue
        Write-Host "  -> Set EnableUlps=0 on $($k.PSChildName)" -ForegroundColor Green
    }
} else {
    Write-Host "  -> No AMD ULPS keys found or already configured." -ForegroundColor Gray
}

# 1b. Enable Radeon Image Sharpening (Driver-level Contrast Adaptive Sharpening)
Write-Host "`n[1b/5] Enabling driver-level Radeon Image Sharpening (CAS)..." -ForegroundColor Yellow
$driverKeys = Get-ChildItem -Path $amdClassPath -ErrorAction SilentlyContinue
foreach ($dk in $driverKeys) {
    if ($dk.PSChildName -match "^\d{4}$") {
        Set-ItemProperty -Path $dk.PSPath -Name "KMD_RadeonImageSharpening" -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue
        Set-ItemProperty -Path $dk.PSPath -Name "KMD_RadeonImageSharpeningStrength" -Value 80 -Type DWord -Force -ErrorAction SilentlyContinue
        Write-Host "  -> Enabled Radeon Image Sharpening on $($dk.PSChildName)" -ForegroundColor Green
    }
}

# 2. Configure Windows Graphics Driver Parameters (HAGS & TDR Delay)
Write-Host "`n[2/5] Configuring Graphics Driver & TDR parameters..." -ForegroundColor Yellow
$gfxPath = "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"
if (-not (Test-Path $gfxPath)) { New-Item -Path $gfxPath -Force | Out-Null }

# TdrDelay: Increases GPU recovery timeout from 2s to 8s (prevents crash on heavy shader compilation)
Set-ItemProperty -Path $gfxPath -Name "TdrDelay" -Value 8 -Type DWord -Force
Write-Host "  -> Set TdrDelay = 8 (prevents false GPU driver timeout crashes)" -ForegroundColor Green

# HwSchMode: Hardware-Accelerated GPU Scheduling (2 = Enabled)
Set-ItemProperty -Path $gfxPath -Name "HwSchMode" -Value 2 -Type DWord -Force
Write-Host "  -> Set HwSchMode = 2 (Hardware-Accelerated GPU Scheduling enabled)" -ForegroundColor Green

# Disable Windows MPO (OverlayTestMode = 5) to stop borderless & FSR flicker/stutter
$dwmPath = "HKLM:\SOFTWARE\Microsoft\Windows\Dwm"
if (-not (Test-Path $dwmPath)) { New-Item -Path $dwmPath -Force | Out-Null }
Set-ItemProperty -Path $dwmPath -Name "OverlayTestMode" -Value 5 -Type DWord -Force
Write-Host "  -> Set OverlayTestMode = 5 (Disabled Windows MPO overlay stutter)" -ForegroundColor Green

# 3. Multimedia & System Responsiveness for 3D/Gaming workloads
Write-Host "`n[3/5] Setting system responsiveness for high-priority 3D rendering..." -ForegroundColor Yellow
$sysProfilePath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
if (-not (Test-Path $sysProfilePath)) { New-Item -Path $sysProfilePath -Force | Out-Null }
Set-ItemProperty -Path $sysProfilePath -Name "SystemResponsiveness" -Value 0 -Type DWord -Force
Set-ItemProperty -Path $sysProfilePath -Name "NetworkThrottlingIndex" -Value 0xFFFFFFFF -Type DWord -Force
Write-Host "  -> Set SystemResponsiveness = 0 (100% foreground CPU priority)" -ForegroundColor Green
Write-Host "  -> Set NetworkThrottlingIndex = 0xFFFFFFFF (disabled network throttling during 3D loads)" -ForegroundColor Green

$gamesTaskPath = "$sysProfilePath\Tasks\Games"
if (-not (Test-Path $gamesTaskPath)) { New-Item -Path $gamesTaskPath -Force | Out-Null }
Set-ItemProperty -Path $gamesTaskPath -Name "GPU Priority" -Value 8 -Type DWord -Force
Set-ItemProperty -Path $gamesTaskPath -Name "Priority" -Value 6 -Type DWord -Force
Set-ItemProperty -Path $gamesTaskPath -Name "Scheduling Category" -Value "High" -Type String -Force
Set-ItemProperty -Path $gamesTaskPath -Name "SFIO Priority" -Value "High" -Type String -Force
Write-Host "  -> Set Games Task Priority: High / GPU Priority 8" -ForegroundColor Green

# 4. Disable Background GameDVR & Enable Windows Game Mode
Write-Host "`n[4/5] Disabling background capture overhead & enabling Game Mode..." -ForegroundColor Yellow
$gameConfigPath = "HKCU:\System\GameConfigStore"
if (-not (Test-Path $gameConfigPath)) { New-Item -Path $gameConfigPath -Force | Out-Null }
Set-ItemProperty -Path $gameConfigPath -Name "GameDVR_Enabled" -Value 0 -Type DWord -Force
Set-ItemProperty -Path $gameConfigPath -Name "GameDVR_FSEBehaviorMode" -Value 2 -Type DWord -Force -ErrorAction SilentlyContinue
Write-Host "  -> Disabled GameDVR background recording" -ForegroundColor Green

$gameBarPath = "HKCU:\Software\Microsoft\GameBar"
if (-not (Test-Path $gameBarPath)) { New-Item -Path $gameBarPath -Force | Out-Null }
Set-ItemProperty -Path $gameBarPath -Name "AutoGameModeEnabled" -Value 1 -Type DWord -Force
Write-Host "  -> Enabled Windows Game Mode" -ForegroundColor Green

# 5. Create & Configure Dedicated "Radeon GPU Optimized" Power Plan
Write-Host "`n[5/5] Configuring dedicated Power Plan for thermal headroom..." -ForegroundColor Yellow

# Reuse existing custom plan if already created so repeated runs don't duplicate schemes
$planGuid = $null
$existingSchemes = powercfg -list
foreach ($line in ($existingSchemes -split "`n")) {
    if ($line -like "*Radeon Pro 560X - Optimized*") {
        $m = [regex]::Match($line, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
        if ($m.Success) { $planGuid = $m.Value; break }
    }
}

if (-not $planGuid) {
    # Try duplicating Ultimate Performance first, fallback to High Performance
    $ultimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61"
    $highPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"

    $dupOutput = powercfg -duplicatescheme $ultimateGuid 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $dupOutput) {
        $dupOutput = powercfg -duplicatescheme $highPerfGuid 2>$null
    }

    $newGuidMatch = [regex]::Match($dupOutput, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
    if ($newGuidMatch.Success) {
        $planGuid = $newGuidMatch.Value
        powercfg -changename $planGuid "Radeon Pro 560X - Optimized" "Tuned for maximum sustained GPU boost clock and reduced thermal throttling."
    }
}

if ($planGuid) {
    powercfg -setactive $planGuid

    # Set PCIe Link State Power Management to OFF (No throttling of PCIe lanes)
    # SUB_PCIEXPRESS: 501a4d13-42af-4429-9e56-4784f8da68f4, EE12F906: ee12f906-d277-404b-b6da-e5fa1a576df5
    powercfg -setacvalueindex $planGuid 501a4d13-42af-4429-9e56-4784f8da68f4 ee12f906-d277-404b-b6da-e5fa1a576df5 0

    # Cap Maximum Processor State (AC) to 99% & unpark all 12 CPU threads (CPMINCORES = 100%)
    # SUB_PROCESSOR: 54533251-82be-4824-96c1-47b60b740d00
    powercfg -setacvalueindex $planGuid 54533251-82be-4824-96c1-47b60b740d00 bc5038f7-23e0-4960-96da-33abaf5935ec 99
    powercfg -setacvalueindex $planGuid 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 100

    # Re-apply active
    powercfg -setactive $planGuid
    Write-Host "  -> Activated power plan: 'Radeon Pro 560X - Optimized'" -ForegroundColor Green
    Write-Host "  -> Set PCIe Link State = OFF" -ForegroundColor Green
    Write-Host "  -> Set Max CPU State = 99% & Min Cores = 100% (Unparked all 12 threads)" -ForegroundColor Green
} else {
    Write-Host "  -> Could not create/locate custom scheme; applying settings to active scheme..." -ForegroundColor Yellow
    $activeGuid = (powercfg /getactivescheme).Split()[3]
    powercfg -setacvalueindex $activeGuid 501a4d13-42af-4429-9e56-4784f8da68f4 ee12f906-d277-404b-b6da-e5fa1a576df5 0
    powercfg -setacvalueindex $activeGuid 54533251-82be-4824-96c1-47b60b740d00 bc5038f7-23e0-4960-96da-33abaf5935ec 99
    powercfg -setacvalueindex $activeGuid 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 100
    powercfg -setactive $activeGuid
    Write-Host "  -> Applied PCIe, 99% CPU limit, and core unparking to active plan ($activeGuid)" -ForegroundColor Green
}

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "   Optimizations Complete!                              " -ForegroundColor Green
Write-Host "   A system restart is recommended for HAGS and ULPS.   " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
