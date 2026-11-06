# Optimizador de Servicios

Herramienta portable para Windows 10 que mejora el rendimiento de equipos modestos (4 GB de RAM y
disco duro mecánico) desactivando de forma **reversible** servicios, efectos visuales, ajustes y
tareas programadas, y que **mide** el efecto antes y después.

## Descarga

El ejecutable y el manual en PDF están en la última versión de [Releases](../../releases/latest).

## Características

- **Panel:** procesador, memoria y disco (distingue HDD de SSD) con su uso en tiempo real, y
  recomendaciones según el equipo.
- **Servicios:** catálogo de servicios con su mejora y su nivel de riesgo (seguro, moderado,
  arriesgado), perfiles y restablecimiento con un clic.
- **Efectos y ajustes:** animaciones, transparencias, aplicaciones en segundo plano, Game Bar,
  plan de energía, hibernación, anuncios y telemetría. Cada ajuste se puede revertir.
- **Tareas programadas:** desactiva las tareas de telemetría y diagnóstico que despiertan el disco.
- **Programas:** programas de inicio con su impacto en el arranque, consumo de cada programa y
  eliminación de aplicaciones preinstaladas de la Tienda.
- **Limpieza de disco:** temporales, restos de Windows Update, volcados de memoria, cachés y papelera.
- **Análisis:** tiempo de arranque, RAM, CPU, actividad y cola del disco, antes y después de los
  cambios, con un ciclo guiado: medir, cambiar, reiniciar y volver a medir.
- **Interfaz:** estilo Fluent con tema claro u oscuro según Windows.
- **Portable:** un único `.exe` que usa .NET Framework 4.8, incluido en Windows 10.

Antes de cada cambio se guarda el estado original en `C:\ProgramData\OptimizadorServicios\` y se
puede crear un punto de restauración. No modifica Windows Update ni Windows Defender.

## Compilar

No requiere Visual Studio: usa el compilador de C# incluido en .NET Framework 4.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1              # genera dist\OptimizadorServicios.exe
powershell -ExecutionPolicy Bypass -File docs\build-manual.ps1  # genera el manual en PDF (usa Edge)
```

## Estructura

| Ruta | Contenido |
|---|---|
| `src/Nucleo.cs` | Servicios, copias de seguridad, mediciones y análisis de programas |
| `src/Ajustes.cs`, `src/Tareas.cs` | Ajustes reversibles y tareas programadas |
| `src/AppsTienda.cs`, `src/Limpieza.cs` | Apps de la Tienda y limpieza de disco |
| `src/InfoEquipo.cs` | Información del equipo para el Panel |
| `src/Ui/` | Interfaz WPF: tema, ventana principal y páginas |
| `docs/manual/` | Manual en HTML e imágenes (el PDF se genera con `docs\build-manual.ps1`) |

## Licencia

[MIT](LICENSE).
