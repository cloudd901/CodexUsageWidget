@echo off
if not exist "%~dp0bin\CodexUsageWidget.exe" (
  echo Widget is not built. Run build-widget.ps1 first.
  pause
  exit /b 1
)
start "" "%~dp0bin\CodexUsageWidget.exe"
