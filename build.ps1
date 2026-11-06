# Compila dist\OptimizadorServicios.exe (interfaz WPF) con el compilador de C#
# incluido en .NET Framework 4 (viene con Windows 10, no hace falta instalar nada).
# -Captura: versión de desarrollo que genera capturas de pantalla (sin permisos de administrador).
param([string]$Salida, [switch]$Captura)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $fw 'csc.exe'))) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc  = Join-Path $fw 'csc.exe'

if (-not $Salida) {
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $Salida = Join-Path $dist 'OptimizadorServicios.exe'
}

$refs = 'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Management.dll', 'System.ServiceProcess.dll',
        'System.Web.Extensions.dll', 'Microsoft.CSharp.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'WPF\WindowsBase.dll' |
        ForEach-Object { "/reference:$(Join-Path $fw $_)" }
$fuentes = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }

$extra = if ($Captura) { '/define:CAPTURA' } else { "/win32manifest:$(Join-Path $root 'src\app.manifest')" }
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$Salida" $extra $refs $fuentes

if ($LASTEXITCODE -ne 0) { throw "La compilación falló (código $LASTEXITCODE)" }
Write-Host "Generado: $Salida" -ForegroundColor Green
