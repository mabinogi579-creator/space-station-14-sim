@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File play.ps1
pause
