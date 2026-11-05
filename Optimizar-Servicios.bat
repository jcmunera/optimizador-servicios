@echo off
rem Lanzador: ejecuta el script sin depender de la directiva de ejecucion de PowerShell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Optimizar-Servicios.ps1"
