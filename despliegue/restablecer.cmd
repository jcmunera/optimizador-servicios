@echo off
rem ===========================================================================
rem  Devuelve todos los servicios modificados a su estado original.
rem  Para revertir un despliegue: quita aplicar.cmd de la GPO y asigna este
rem  script en su lugar (mismos requisitos de permisos que aplicar.cmd).
rem ===========================================================================

set "EXE=\\servidor\software\OptimizadorServicios.exe"
set "LOG=\\servidor\registros\optimizador\%COMPUTERNAME%.log"
set "OPCIONES="

start "" /wait "%EXE%" /restablecer /log:"%LOG%" %OPCIONES%
exit /b %errorlevel%
