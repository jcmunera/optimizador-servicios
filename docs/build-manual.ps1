# Genera dist\Manual-Optimizador-Servicios.pdf a partir de docs\manual\manual.html
# usando Microsoft Edge en modo sin interfaz (incluido en Windows 10).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$html = Join-Path $PSScriptRoot 'manual\manual.html'
$pdf  = Join-Path $root 'dist\Manual-Optimizador-Servicios.pdf'

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'No se encuentra Microsoft Edge.' }

New-Item -ItemType Directory -Force -Path (Split-Path $pdf) | Out-Null
$perfil = Join-Path $env:TEMP 'edge-manual-optimizador'
$url = ([Uri]$html).AbsoluteUri
$p = Start-Process $edge -PassThru -Wait -WindowStyle Hidden -ArgumentList @(
    '--headless', '--disable-gpu', "--user-data-dir=`"$perfil`"",
    '--no-pdf-header-footer', '--print-to-pdf-no-header',
    "--print-to-pdf=`"$pdf`"", $url)
if (-not (Test-Path $pdf)) { throw "Edge no generó el PDF (código $($p.ExitCode))." }
Write-Host "Generado: $pdf" -ForegroundColor Green
