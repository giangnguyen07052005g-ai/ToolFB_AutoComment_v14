# SCRIPT BUILD VA PUBLISH TOOL FB v14 PRO (.NET 8 C# WIN-X64)
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   BAT DAU DONG GOI BAN PHAT HANH TOOL FB v14 PRO C#      " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionPath = Join-Path $ScriptDir "ToolFB_v14_Pro.sln"
$AppProj = Join-Path $ScriptDir "ToolFB.App\ToolFB.App.csproj"
$UpdaterProj = Join-Path $ScriptDir "ToolFB.Updater\ToolFB.Updater.csproj"
$TestProj = Join-Path $ScriptDir "ToolFB.Tests\ToolFB.Tests.csproj"
$OutDir = Join-Path $ScriptDir "Publish_win-x64"

# 1. Dung tien trinh cu
Write-Host "`n[1/5] Kiem tra va dong tien trinh cu..." -ForegroundColor White
Stop-Process -Name "ToolFB.App" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "ToolFB.Updater" -Force -ErrorAction SilentlyContinue

# 2. Kiem thu tu dong
Write-Host "`n[2/5] Chay bo kiem thu tu dong (Unit & Integration Tests)..." -ForegroundColor White
dotnet run --project $TestProj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "[LOI] Kiem thu that bai! Huy dong goi." -ForegroundColor Red
    exit 1
}

# 3. Publish ToolFB.Updater
Write-Host "`n[3/5] Dong goi ToolFB.Updater.exe..." -ForegroundColor White
dotnet publish $UpdaterProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "[LOI] Dong goi Updater that bai!" -ForegroundColor Red
    exit 1
}

# 4. Publish ToolFB.App
Write-Host "`n[4/5] Dong goi ToolFB.App.exe..." -ForegroundColor White
dotnet publish $AppProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "[LOI] Dong goi App that bai!" -ForegroundColor Red
    exit 1
}

# 5. Sao chep cau hinh mau
Write-Host "`n[5/5] Sao chep file cau hinh va database mau..." -ForegroundColor White
if (Test-Path (Join-Path $ScriptDir "github_update.json")) {
    Copy-Item (Join-Path $ScriptDir "github_update.json") (Join-Path $OutDir "github_update.json") -Force
}
if (Test-Path (Join-Path $ScriptDir "..\devices_db.json")) {
    Copy-Item (Join-Path $ScriptDir "..\devices_db.json") (Join-Path $OutDir "devices_db.json") -Force
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   HOAN THANH DONG GOI THANH CONG!                       " -ForegroundColor Green
Write-Host "   Thu muc phat hanh: $OutDir" -ForegroundColor Green
Write-Host "   File chay chinh: ToolFB.App.exe (Khong can cai .NET)" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Green
