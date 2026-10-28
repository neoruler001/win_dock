@echo off
rem WinNavi Dock installer launcher: double-click this file. Needs no administrator rights.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
