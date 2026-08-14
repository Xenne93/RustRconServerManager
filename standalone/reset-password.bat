@echo off
REM Double-click entry point - runs the real logic in reset-password.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0reset-password.ps1"
pause
