#Requires -Version 5.1
<#
    Optimizar-Servicios.ps1
    Desactiva de forma opcional los servicios de Windows 10 que más penalizan
    el rendimiento en equipos modestos (4 GB de RAM + disco duro mecánico).

    - Antes de tocar un servicio guarda su configuración original en
      %ProgramData%\OptimizadorServicios\backup.json
    - La opción [R] del menú restablece todos los servicios a su estado original.
#>

# --- Auto-elevación a administrador -----------------------------------------
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

$ErrorActionPreference = 'Continue'
$BackupDir  = Join-Path $env:ProgramData 'OptimizadorServicios'
$BackupFile = Join-Path $BackupDir 'backup.json'
$LogFile    = Join-Path $BackupDir 'registro.log'
if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir | Out-Null }

# --- Catálogo de servicios ---------------------------------------------------
# Recomendado = se marca al pulsar [T]. Los no recomendados pueden ser útiles
# según el uso del equipo (imprimir, Bluetooth, huella...).
$Servicios = @(
    [pscustomobject]@{ Nombre='SysMain';          Impacto='Alto';  Rec=$true;  Desc='Superfetch: precarga apps en RAM. Causa disco al 100% en HDD' }
    [pscustomobject]@{ Nombre='WSearch';          Impacto='Alto';  Rec=$true;  Desc='Indexador de Windows Search (la búsqueda seguirá funcionando, más lenta)' }
    [pscustomobject]@{ Nombre='DiagTrack';        Impacto='Alto';  Rec=$true;  Desc='Telemetría y experiencias del usuario conectado' }
    [pscustomobject]@{ Nombre='DoSvc';            Impacto='Medio'; Rec=$true;  Desc='Optimización de distribución (comparte updates P2P)' }
    [pscustomobject]@{ Nombre='dmwappushservice'; Impacto='Medio'; Rec=$true;  Desc='Enrutamiento de mensajes WAP push (telemetría)' }
    [pscustomobject]@{ Nombre='PcaSvc';           Impacto='Medio'; Rec=$true;  Desc='Asistente de compatibilidad de programas' }
    [pscustomobject]@{ Nombre='WerSvc';           Impacto='Bajo';  Rec=$true;  Desc='Informe de errores de Windows' }
    [pscustomobject]@{ Nombre='diagnosticshub.standardcollector.service'; Impacto='Bajo'; Rec=$true; Desc='Recopilador del concentrador de diagnósticos' }
    [pscustomobject]@{ Nombre='TrkWks';           Impacto='Bajo';  Rec=$true;  Desc='Cliente de seguimiento de vínculos distribuidos' }
    [pscustomobject]@{ Nombre='MapsBroker';       Impacto='Bajo';  Rec=$true;  Desc='Administrador de mapas descargados' }
    [pscustomobject]@{ Nombre='RetailDemo';       Impacto='Bajo';  Rec=$true;  Desc='Modo demostración para tiendas' }
    [pscustomobject]@{ Nombre='wisvc';            Impacto='Bajo';  Rec=$true;  Desc='Programa Windows Insider' }
    [pscustomobject]@{ Nombre='WMPNetworkSvc';    Impacto='Bajo';  Rec=$true;  Desc='Uso compartido de red de Windows Media Player' }
    [pscustomobject]@{ Nombre='RemoteRegistry';   Impacto='Bajo';  Rec=$true;  Desc='Registro remoto (también mejora la seguridad)' }
    [pscustomobject]@{ Nombre='Fax';              Impacto='Bajo';  Rec=$true;  Desc='Servicio de fax' }
    [pscustomobject]@{ Nombre='XblAuthManager';   Impacto='Bajo';  Rec=$true;  Desc='Xbox Live: autenticación (no usar si juegas con Xbox/Game Pass)' }
    [pscustomobject]@{ Nombre='XblGameSave';      Impacto='Bajo';  Rec=$true;  Desc='Xbox Live: partidas guardadas' }
    [pscustomobject]@{ Nombre='XboxNetApiSvc';    Impacto='Bajo';  Rec=$true;  Desc='Xbox Live: red' }
    [pscustomobject]@{ Nombre='XboxGipSvc';       Impacto='Bajo';  Rec=$true;  Desc='Xbox: accesorios' }
    [pscustomobject]@{ Nombre='lfsvc';            Impacto='Bajo';  Rec=$false; Desc='Geolocalización (lo usan Mapas, Tiempo, hora automática)' }
    [pscustomobject]@{ Nombre='Spooler';          Impacto='Medio'; Rec=$false; Desc='Cola de impresión (NECESARIO para imprimir o PDF virtual)' }
    [pscustomobject]@{ Nombre='bthserv';          Impacto='Bajo';  Rec=$false; Desc='Bluetooth (NECESARIO para ratones/auriculares BT)' }
    [pscustomobject]@{ Nombre='WbioSrvc';         Impacto='Bajo';  Rec=$false; Desc='Biometría (NECESARIO para huella / Windows Hello)' }
    [pscustomobject]@{ Nombre='TabletInputService'; Impacto='Bajo'; Rec=$false; Desc='Teclado táctil y escritura a mano (puede afectar a la entrada de texto)' }
) | Where-Object { Get-Service -Name $_.Nombre -ErrorAction SilentlyContinue }   # solo los que existen en este equipo

# --- Utilidades --------------------------------------------------------------
function Write-Log([string]$Texto) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $Texto" | Out-File -FilePath $LogFile -Append -Encoding utf8
}

function Get-RegPath([string]$Nombre) { "HKLM:\SYSTEM\CurrentControlSet\Services\$Nombre" }

function Get-StartValue([string]$Nombre) {
    (Get-ItemProperty -Path (Get-RegPath $Nombre) -Name Start -ErrorAction SilentlyContinue).Start
}

# Escribe el tipo de inicio y el estado con colores:
#   verde = deshabilitado y detenido, amarillo = deshabilitado pero aún en
#   ejecución (se detendrá al reiniciar), blanco/gris = activo.
function Write-Estado([string]$Nombre) {
    $start = Get-StartValue $Nombre
    $svc   = Get-Service -Name $Nombre -ErrorAction SilentlyContinue
    $run   = [bool]($svc -and $svc.Status -eq 'Running')
    if ($start -eq 4) {
        Write-Host 'DESHABILITADO' -ForegroundColor Green -NoNewline
        if ($run) { Write-Host ', aún en ejecución (hasta reiniciar)' -ForegroundColor Yellow }
        else      { Write-Host ', detenido' -ForegroundColor Green }
    } else {
        $tipo = switch ($start) { 2 {'Automático'} 3 {'Manual'} default {'?'} }
        Write-Host $tipo -ForegroundColor White -NoNewline
        if ($run) { Write-Host ', en ejecución' -ForegroundColor Cyan }
        else      { Write-Host ', detenido' -ForegroundColor DarkGray }
    }
}

function Read-Backup {
    $tabla = @{}
    if (Test-Path $BackupFile) {
        $obj = Get-Content -Path $BackupFile -Raw | ConvertFrom-Json
        foreach ($p in $obj.PSObject.Properties) { $tabla[$p.Name] = $p.Value }
    }
    $tabla
}

function Save-Backup([hashtable]$Tabla) {
    $Tabla | ConvertTo-Json | Out-File -FilePath $BackupFile -Encoding utf8
}

# Stop-Service espera indefinidamente a que el servicio se detenga, y algunos
# (p. ej. SysMain en un HDD) pueden tardar minutos o quedarse en "deteniéndose".
# Aquí se envía la orden de parada y se espera como máximo $Segundos.
function Stop-ServicioConTimeout([string]$Nombre, [int]$Segundos = 20) {
    $svc = Get-Service -Name $Nombre -ErrorAction SilentlyContinue
    if (-not $svc -or $svc.Status -eq 'Stopped') { return $true }
    try {
        if ($svc.Status -ne 'StopPending') { $svc.Stop() }
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds($Segundos))
        return $true
    } catch {
        return $false
    }
}

# Igual que la anterior para el arranque (Start-Service también puede bloquearse).
function Start-ServicioConTimeout([string]$Nombre, [int]$Segundos = 20) {
    $svc = Get-Service -Name $Nombre -ErrorAction SilentlyContinue
    if (-not $svc -or $svc.Status -eq 'Running') { return $true }
    try {
        if ($svc.Status -eq 'Stopped') { $svc.Start() }
        $svc.WaitForStatus('Running', [TimeSpan]::FromSeconds($Segundos))
        return $true
    } catch {
        return $false
    }
}

# --- Acciones ----------------------------------------------------------------
function Disable-Servicios([string[]]$Nombres) {
    $backup = Read-Backup
    foreach ($n in $Nombres) {
        # Solo se guarda el estado original la primera vez, para no sobrescribirlo
        # con "Deshabilitado" si se ejecuta el script varias veces.
        if (-not $backup.ContainsKey($n)) {
            $svc = Get-Service -Name $n -ErrorAction SilentlyContinue
            $backup[$n] = [pscustomobject]@{
                Start   = Get-StartValue $n
                Running = [bool]($svc -and $svc.Status -eq 'Running')
            }
            Save-Backup $backup
        }

        Write-Host "  Desactivando $n ... " -NoNewline
        try {
            # Se escribe en el registro porque algunos servicios (p. ej. DoSvc)
            # no permiten cambiar el tipo de inicio con Set-Service.
            Set-ItemProperty -Path (Get-RegPath $n) -Name Start -Value 4 -Type DWord -ErrorAction Stop
        } catch {
            Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
            Write-Log "Error al deshabilitar $n : $($_.Exception.Message)"
            continue
        }
        # Se deshabilita antes de detenerlo: si la parada no termina a tiempo,
        # el servicio ya no arrancará en el próximo reinicio.
        if (Stop-ServicioConTimeout $n) {
            Write-Host 'OK' -ForegroundColor Green
        } else {
            Write-Host 'OK (se detendrá al reiniciar)' -ForegroundColor Yellow
        }
        Write-Log "Deshabilitado: $n"
    }
}

function Restore-Servicios {
    $backup = Read-Backup
    if ($backup.Count -eq 0) {
        Write-Host "`n  No hay cambios que restablecer." -ForegroundColor Yellow
        return
    }
    Write-Host ''
    $pendientes = @{}
    foreach ($n in $backup.Keys) {
        $orig = $backup[$n]
        Write-Host "  Restableciendo $n ... " -NoNewline
        try {
            if ($null -ne $orig.Start) {
                Set-ItemProperty -Path (Get-RegPath $n) -Name Start -Value ([int]$orig.Start) -Type DWord -ErrorAction Stop
            }
            if ($orig.Running -and $orig.Start -ne 4) {
                Start-ServicioConTimeout $n | Out-Null
            }
            Write-Host 'OK' -ForegroundColor Green
            Write-Log "Restablecido: $n (Start=$($orig.Start))"
        } catch {
            Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
            Write-Log "Error al restablecer $n : $($_.Exception.Message)"
            $pendientes[$n] = $orig
        }
    }
    # Se conservan en la copia solo los que no se pudieron restablecer.
    if ($pendientes.Count -gt 0) { Save-Backup $pendientes }
    else { Remove-Item -Path $BackupFile -Force }
    Write-Host "`n  Reinicia el equipo para completar el restablecimiento." -ForegroundColor Cyan
}

function New-PuntoRestauracion {
    $r = Read-Host '  ¿Crear un punto de restauración del sistema antes? (S/N)'
    if ($r -match '^[sS]') {
        try {
            Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
            Checkpoint-Computer -Description 'Antes de Optimizar-Servicios' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
            Write-Host '  Punto de restauración creado.' -ForegroundColor Green
        } catch {
            Write-Host "  No se pudo crear (Windows solo permite uno cada 24 h): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

# Convierte "1,3,5-8" en una lista de índices
function ConvertTo-Indices([string]$Texto, [int]$Max) {
    $res = @()
    foreach ($parte in ($Texto -split '[,\s]+' | Where-Object { $_ })) {
        if ($parte -match '^(\d+)-(\d+)$') { $res += [int]$Matches[1]..[int]$Matches[2] }
        elseif ($parte -match '^\d+$')     { $res += [int]$parte }
    }
    $res | Where-Object { $_ -ge 1 -and $_ -le $Max } | Select-Object -Unique
}

# --- Menú ----------------------------------------------------------------------
$Seleccion = New-Object System.Collections.Generic.HashSet[int]

while ($true) {
    Clear-Host
    $hayBackup = (Read-Backup).Count -gt 0
    Write-Host '================================================================================' -ForegroundColor Cyan
    Write-Host '   OPTIMIZADOR DE SERVICIOS - Windows 10 (4 GB RAM / HDD)' -ForegroundColor Cyan
    Write-Host '================================================================================' -ForegroundColor Cyan
    Write-Host ''
    for ($i = 0; $i -lt $Servicios.Count; $i++) {
        $s     = $Servicios[$i]
        $marca = if ($Seleccion.Contains($i + 1)) { '[X]' } else { '[ ]' }
        $color = switch ($s.Impacto) { 'Alto' {'Red'} 'Medio' {'Yellow'} default {'Gray'} }
        $rec   = if ($s.Rec) { '' } else { ' (opcional)' }
        Write-Host ("  {0} {1,2}. " -f $marca, ($i + 1)) -NoNewline
        Write-Host ("{0,-6}" -f $s.Impacto) -ForegroundColor $color -NoNewline
        Write-Host (" {0}{1}" -f $s.Nombre, $rec) -ForegroundColor White
        Write-Host ("            {0}  -> " -f $s.Desc) -ForegroundColor DarkGray -NoNewline
        Write-Estado $s.Nombre
    }
    Write-Host ''
    Write-Host '  Números (ej. 1,3,5-8) = marcar/desmarcar   T = marcar recomendados   N = ninguno'
    Write-Host '  D = DESACTIVAR los marcados'
    if ($hayBackup) { Write-Host '  R = RESTABLECER todos los cambios' -ForegroundColor Green }
    else            { Write-Host '  R = Restablecer todos los cambios (no hay cambios guardados)' -ForegroundColor DarkGray }
    Write-Host '  S = Salir'
    Write-Host ''
    $op = (Read-Host '  Opción').Trim()

    switch -Regex ($op) {
        '^[tT]$' { $Seleccion.Clear(); for ($i = 0; $i -lt $Servicios.Count; $i++) { if ($Servicios[$i].Rec) { [void]$Seleccion.Add($i + 1) } } }
        '^[nN]$' { $Seleccion.Clear() }
        '^[dD]$' {
            if ($Seleccion.Count -eq 0) { Write-Host '  No hay servicios marcados.' -ForegroundColor Yellow; Start-Sleep 2; break }
            $nombres = $Seleccion | Sort-Object | ForEach-Object { $Servicios[$_ - 1].Nombre }
            Write-Host "`n  Se desactivarán: $($nombres -join ', ')" -ForegroundColor Yellow
            if ((Read-Host '  ¿Continuar? (S/N)') -match '^[sS]') {
                New-PuntoRestauracion
                Write-Host ''
                Disable-Servicios $nombres
                $Seleccion.Clear()
                Write-Host "`n  Listo. Se recomienda reiniciar el equipo." -ForegroundColor Cyan
                Read-Host '  Pulsa Enter para continuar' | Out-Null
            }
        }
        '^[rR]$' {
            if ((Read-Host '  ¿Restablecer TODOS los servicios a su estado original? (S/N)') -match '^[sS]') {
                Restore-Servicios
                Read-Host '  Pulsa Enter para continuar' | Out-Null
            }
        }
        '^[sS]$' { exit }
        '^[\d,\-\s]+$' {
            foreach ($idx in (ConvertTo-Indices $op $Servicios.Count)) {
                if (-not $Seleccion.Remove($idx)) { [void]$Seleccion.Add($idx) }
            }
        }
    }
}
