@echo off
title GIANG DEV - Auto Comment Facebook Pro [v14 C# .NET 8]
cd /d "%~dp0"

echo =====================================================================
echo    GIANG DEV - AUTO COMMENT FACEBOOK PRO 2026 [v14 C# .NET 8]
echo    HIEN THUC DIRECTX 60 FPS / DA LUONG THAT / AUTO UPDATE GITHUB
echo =====================================================================
echo.

if exist "ToolFB.App\bin\Release\net8.0-windows\ToolFB.App.exe" (
    start "" "ToolFB.App\bin\Release\net8.0-windows\ToolFB.App.exe"
    exit /b 0
)

if exist "ToolFB.App\bin\Debug\net8.0-windows\ToolFB.App.exe" (
    start "" "ToolFB.App\bin\Debug\net8.0-windows\ToolFB.App.exe"
    exit /b 0
)

echo [*] Dang bien dich va khoi dong ung dung...
dotnet run --project ToolFB.App\ToolFB.App.csproj -c Release
