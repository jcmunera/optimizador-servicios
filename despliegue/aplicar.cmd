@echo off
rem ===========================================================================
rem  Aplica un perfil del Optimizador de Servicios en este equipo.
rem
rem  Uso como script de inicio por GPO:
rem    Configuración del equipo > Directivas > Configuración de Windows >
rem    Scripts (inicio o apagado) > Inicio
rem  Se ejecuta como SYSTEM, sin aviso UAC. Es seguro ejecutarlo en cada
rem  arranque: los servicios ya deshabilitados se omiten.
rem
rem  Con Intune / SCCM / PDQ: ejecútalo en contexto de sistema.
rem
rem  IMPORTANTE: al ejecutarse como SYSTEM, el acceso a la red se hace con la
rem  cuenta del equipo. Da permiso de lectura a "Equipos del dominio" en la
rem  carpeta del .exe y de escritura en la carpeta de registros.
rem ===========================================================================

rem --- Configuración ---------------------------------------------------------
set "EXE=\\servidor\software\OptimizadorServicios.exe"
rem Perfiles: recomendado, conservador, oficina, aulas  (o la ruta a un .txt)
set "PERFIL=aulas"
set "LOG=\\servidor\registros\optimizador\%COMPUTERNAME%.log"
rem Añade /reiniciar para reiniciar a los 60 s si se ha cambiado algo
set "OPCIONES="
rem ---------------------------------------------------------------------------

start "" /wait "%EXE%" /aplicar:%PERFIL% /log:"%LOG%" %OPCIONES%
exit /b %errorlevel%
