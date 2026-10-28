@echo off
chcp 65001 >nul
title Đóng gói Tool FB v14 Pro C# (.NET 8)
echo Đang bắt đầu quá trình đóng gói...
powershell -ExecutionPolicy Bypass -File "%~dp0build_and_publish.ps1"
pause
