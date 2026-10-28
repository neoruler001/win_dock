@echo off
rem WinNavi Dock uninstaller launcher.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
