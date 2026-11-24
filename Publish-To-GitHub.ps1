$Host.UI.RawUI.WindowTitle = "Radeon Pro Optimizer - GitHub Publisher"
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "   RADEON PRO OPTIMIZER - GITHUB REPOSITORY PUBLISHER" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

$repoDir = "e:\apps\Graphic card improve"
Set-Location $repoDir

# 1. Ensure Git in path
$gitCmd = if (Get-Command git -ErrorAction SilentlyContinue) { "git" } elseif (Test-Path "C:\Program Files\Git\cmd\git.exe") { "C:\Program Files\Git\cmd\git.exe" } else { $null }

if (!$gitCmd) {
    Write-Host "[!] Git not found. Please install Git from https://git-scm.com/" -ForegroundColor Red
    Write-Host "Press any key to exit..."
    [Console]::ReadKey($true) | Out-Null
    exit 1
}

$gitVer = & $gitCmd --version
Write-Host "[OK] Git ready: $gitVer" -ForegroundColor Green

# 2. Configure Git remote origin
$remoteUrl = "https://github.com/Tapankumardang/Radion-pro-560x-FSR-SSR-optimizer.git"
& $gitCmd remote remove origin 2>$null
& $gitCmd remote add origin $remoteUrl
Write-Host "[*] Remote configured: $remoteUrl" -ForegroundColor Yellow

# 3. Commit any pending files
Write-Host "[*] Staging project files..." -ForegroundColor Gray
& $gitCmd add .
& $gitCmd commit -m "feat: Initial release of Radeon Pro 560X & Core i9-8950HK Performance Center" 2>$null

# 4. Push to GitHub
Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "   PUSHING TO GITHUB: $remoteUrl" -ForegroundColor Cyan
Write-Host "   (If your browser opens, click 'Sign in with your browser')" -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

& $gitCmd push -u origin main

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "======================================================================" -ForegroundColor Green
    Write-Host "   [SUCCESS] REPOSITORY IS LIVE ON GITHUB!" -ForegroundColor Green
    Write-Host "   URL: https://github.com/Tapankumardang/Radion-pro-560x-FSR-SSR-optimizer" -ForegroundColor Green
    Write-Host "======================================================================" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[!] Push exited with code $LASTEXITCODE." -ForegroundColor Red
}

Write-Host ""
Write-Host "Press any key to exit..."
[Console]::ReadKey($true) | Out-Null
