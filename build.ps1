# Compila dist\OptimizadorServicios.exe (interfaz gráfica) con el compilador de C#
# incluido en .NET Framework 4 (viene con Windows 10, no hace falta instalar nada).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $fw 'csc.exe'))) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc  = Join-Path $fw 'csc.exe'

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exe = Join-Path $dist 'OptimizadorServicios.exe'

$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
        'System.Management.dll', 'System.ServiceProcess.dll', 'System.Web.Extensions.dll' |
        ForEach-Object { "/reference:$(Join-Path $fw $_)" }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$exe" `
    "/win32manifest:$(Join-Path $root 'src\app.manifest')" `
    $refs `
    (Join-Path $root 'src\Optimizador.cs')

if ($LASTEXITCODE -ne 0) { throw "La compilación falló (código $LASTEXITCODE)" }
Write-Host "Generado: $exe" -ForegroundColor Green
