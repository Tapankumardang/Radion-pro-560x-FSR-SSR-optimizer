# =====================================================================
# Rollback Script: Revert GPU & System Optimizations
# =====================================================================

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "This script requires Administrator privileges to restore registry and power settings."
    Write-Host "Please re-run this script in an elevated PowerShell session (Run as Administrator)." -ForegroundColor Red
    exit 1
}

Write-Host "========================================================" -ForegroundColor Yellow
Write-Host "   Reverting GPU Optimizations to Windows Defaults      " -ForegroundColor Yellow
Write-Host "========================================================" -ForegroundColor Yellow

# 1. Revert ULPS to 1 (Factory default)
$amdClassPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
$ulpsKeys = Get-ChildItem -Path $amdClassPath -Recurse -ErrorAction SilentlyContinue | Where-Object {
    (Get-ItemProperty -Path $_.PSPath -Name "EnableUlps" -ErrorAction SilentlyContinue) -ne $null
}

if ($ulpsKeys) {
    foreach ($k in $ulpsKeys) {
        Set-ItemProperty -Path $k.PSPath -Name "EnableUlps" -Value 1 -Type DWord -Force
        Set-ItemProperty -Path $k.PSPath -Name "EnableUlps_NA" -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue
        Write-Host "  -> Reverted EnableUlps=1 on $($k.PSChildName)" -ForegroundColor Green
    }
}

# 1b. Revert Radeon Image Sharpening
$driverKeys = Get-ChildItem -Path $amdClassPath -ErrorAction SilentlyContinue
foreach ($dk in $driverKeys) {
    if ($dk.PSChildName -match "^\d{4}$") {
        Remove-ItemProperty -Path $dk.PSPath -Name "KMD_RadeonImageSharpening" -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $dk.PSPath -Name "KMD_RadeonImageSharpeningStrength" -ErrorAction SilentlyContinue
    }
}

# 2. Revert Graphics Driver Parameters
$gfxPath = "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"
if (Test-Path $gfxPath) {
    Remove-ItemProperty -Path $gfxPath -Name "TdrDelay" -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $gfxPath -Name "HwSchMode" -ErrorAction SilentlyContinue
    Write-Host "  -> Restored GraphicsDrivers defaults (TdrDelay, HwSchMode)" -ForegroundColor Green
}

$dwmPath = "HKLM:\SOFTWARE\Microsoft\Windows\Dwm"
if (Test-Path $dwmPath) {
    Remove-ItemProperty -Path $dwmPath -Name "OverlayTestMode" -ErrorAction SilentlyContinue
    Write-Host "  -> Restored Windows MPO (removed OverlayTestMode)" -ForegroundColor Green
}

# 3. Revert Multimedia SystemProfile
$sysProfilePath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
if (Test-Path $sysProfilePath) {
    Set-ItemProperty -Path $sysProfilePath -Name "SystemResponsiveness" -Value 20 -Type DWord -Force
    Set-ItemProperty -Path $sysProfilePath -Name "NetworkThrottlingIndex" -Value 10 -Type DWord -Force
    Write-Host "  -> Restored SystemResponsiveness = 20 & NetworkThrottlingIndex = 10" -ForegroundColor Green
}

# 4. Revert GameDVR and Game Mode
$gameConfigPath = "HKCU:\System\GameConfigStore"
if (Test-Path $gameConfigPath) {
    Set-ItemProperty -Path $gameConfigPath -Name "GameDVR_Enabled" -Value 1 -Type DWord -Force
    Write-Host "  -> Restored GameDVR_Enabled = 1" -ForegroundColor Green
}

# 5. Restore Balanced Power Plan
$balancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e"
powercfg -setactive $balancedGuid
Write-Host "  -> Re-activated default Balanced Power Scheme ($balancedGuid)" -ForegroundColor Green

# Remove the custom power plan if present
$schemes = powercfg -list
foreach ($line in ($schemes -split "`n")) {
    if ($line -like "*Radeon Pro 560X - Optimized*") {
        $guid = [regex]::Match($line, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}").Value
        if ($guid -and $guid -ne $balancedGuid) {
            powercfg -delete $guid
            Write-Host "  -> Deleted custom power scheme ($guid)" -ForegroundColor Green
        }
    }
}

Write-Host "`n========================================================" -ForegroundColor Yellow
Write-Host "   Defaults Restored! Restart your PC to finalize.      " -ForegroundColor Yellow
Write-Host "========================================================" -ForegroundColor Yellow
