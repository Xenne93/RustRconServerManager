@echo off
REM Double-click entry point - runs the real launcher logic in start.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1"
pause
