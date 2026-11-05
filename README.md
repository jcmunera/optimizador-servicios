# Optimizador de Servicios

Herramienta portable para Windows 10 que desactiva de forma selectiva y **reversible** los servicios y
programas que más penalizan el rendimiento en equipos modestos (4 GB de RAM y disco duro mecánico),
**mide** el efecto antes y después, y permite **desplegar** la configuración en los equipos de una
empresa, centro educativo u organización.

## Características

- **Servicios:** catálogo de servicios con impacto (alto, medio, bajo) y perfiles: recomendado,
  conservador, oficina y aulas. Copia de seguridad automática y restablecimiento con un clic.
- **Programas:** detecta los programas que se inician con Windows (con los retrasos de arranque que
  registra el propio Windows) y los que más CPU, RAM y disco consumen. Permite desactivarlos del
  inicio o abrir su desinstalador.
- **Análisis antes/después:** tiempo de arranque, RAM, CPU, actividad y cola del disco, procesos y
  servicios. Ciclo guiado: medir, cambiar, reiniciar y volver a medir.
- **Despliegue:** modo de línea de comandos (`/aplicar`, `/restablecer`, `/estado`, `/perfiles`) para
  GPO, Intune, SCCM o PDQ, con registro por equipo y códigos de salida.
- **Portable:** un único `.exe` de unos 70 KB que usa .NET Framework 4, incluido en Windows 10.

## Uso rápido

```bat
OptimizadorServicios.exe                          :: interfaz gráfica
OptimizadorServicios.exe /aplicar:oficina         :: aplicar un perfil sin ventanas
OptimizadorServicios.exe /restablecer             :: revertir todos los cambios
OptimizadorServicios.exe /estado                  :: inventario del estado actual
```

El manual completo, con la guía de despliegue por GPO e Intune, está en
[`docs/manual/manual.html`](docs/manual/manual.html). La versión PDF se genera con el script descrito más abajo.

## Compilar

No requiere Visual Studio: usa el compilador de C# incluido en .NET Framework 4.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1              # genera dist\OptimizadorServicios.exe
powershell -ExecutionPolicy Bypass -File docs\build-manual.ps1  # genera dist\Manual-Optimizador-Servicios.pdf (usa Edge)
```

## Estructura

| Ruta | Contenido |
|---|---|
| `src/Optimizador.cs` | Código de la aplicación (WinForms) y del modo de línea de comandos |
| `src/app.manifest` | Manifiesto: pide permisos de administrador |
| `despliegue/` | Scripts de ejemplo para GPO/Intune y perfil personalizado |
| `docs/manual/` | Manual en HTML e imágenes |
| `Optimizar-Servicios.ps1` | Versión de consola simplificada (PowerShell) |

## Descarga

El ejecutable y el manual en PDF están en la sección
[Releases](../../releases/latest) del repositorio.

## Licencia

[MIT](LICENSE).

## Seguridad

No modifica Windows Update, Windows Defender ni ningún servicio fuera de su catálogo. Cada cambio se
puede revertir: la copia del estado original se guarda en `C:\ProgramData\OptimizadorServicios\`.
