// Optimizador de Servicios - tareas programadas de telemetría y diagnóstico.
// Aunque su servicio esté parado, estas tareas despiertan el disco periódicamente.
// Se usa la API COM del Programador de tareas (Schedule.Service), que no depende
// del idioma de Windows como la salida de schtasks.exe.
using System;
using System.Collections.Generic;
using System.Linq;

class TareaInfo
{
    public string Ruta, Nombre, Desc, Riesgo;
    public bool Recomendada;
    public TareaInfo(string ruta, string nombre, string desc, string riesgo = "Seguro", bool recomendada = true)
    {
        Ruta = ruta; Nombre = nombre; Desc = desc; Riesgo = riesgo; Recomendada = recomendada;
    }
}

class EstadoTarea
{
    public bool Existe, Habilitada;
    public DateTime? UltimaEjecucion;
}

static class Tareas
{
    const string ArchivoCopia = "tareas-backup.json";
    const string W = @"\Microsoft\Windows\";

    public static readonly TareaInfo[] Catalogo =
    {
        new TareaInfo(W + @"Application Experience\Microsoft Compatibility Appraiser", "Evaluador de compatibilidad",
                      "Analiza todos los programas instalados para la telemetría. Es de las que más disco usa."),
        new TareaInfo(W + @"Application Experience\ProgramDataUpdater", "Actualizador de datos de programas", "Recopila datos de uso de programas para la telemetría."),
        new TareaInfo(W + @"Application Experience\StartupAppTask", "Análisis de programas de inicio",
                      "Calcula el «impacto en el inicio» del Administrador de tareas.", "Moderado", false),
        new TareaInfo(W + @"Customer Experience Improvement Program\Consolidator", "Programa de mejora (CEIP)", "Envía datos del Programa para la mejora de la experiencia del usuario."),
        new TareaInfo(W + @"Customer Experience Improvement Program\UsbCeip", "Programa de mejora: USB", "Recopila estadísticas de uso de dispositivos USB."),
        new TareaInfo(W + @"Autochk\Proxy", "Proxy de Autochk", "Recopila datos de comprobaciones de disco para la telemetría."),
        new TareaInfo(W + @"DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", "Diagnóstico de disco", "Envía información de diagnóstico del disco a Microsoft."),
        new TareaInfo(W + @"Feedback\Siuf\DmClient", "Comentarios (cliente)", "Recopila datos para los comentarios de Windows."),
        new TareaInfo(W + @"Feedback\Siuf\DmClientOnScenarioDownload", "Comentarios (escenarios)", "Descarga escenarios de recogida de comentarios."),
        new TareaInfo(W + @"Windows Error Reporting\QueueReporting", "Informe de errores en cola", "Envía los informes de errores pendientes."),
        new TareaInfo(W + @"Maintenance\WinSAT", "Evaluación del sistema (WinSAT)", "Prueba de rendimiento que exige mucho al disco y al procesador."),
        new TareaInfo(W + @"Power Efficiency Diagnostics\AnalyzeSystem", "Diagnóstico de eficiencia energética", "Analiza el consumo de energía del equipo."),
        new TareaInfo(W + @"NetTrace\GatherNetworkInfo", "Información de red", "Recopila información de la red para diagnósticos."),
        new TareaInfo(W + @"Device Information\Device", "Información del dispositivo", "Recopila información del equipo para la telemetría."),
        new TareaInfo(W + @"Maps\MapsUpdateTask", "Actualización de mapas", "Busca actualizaciones de los mapas sin conexión."),
        new TareaInfo(W + @"Maps\MapsToastTask", "Avisos de mapas", "Muestra notificaciones de la aplicación Mapas."),
    };

    static dynamic Servicio()
    {
        dynamic s = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        s.Connect();
        return s;
    }

    static dynamic Obtener(dynamic servicio, string ruta)
    {
        try
        {
            int i = ruta.LastIndexOf('\\');
            dynamic carpeta = servicio.GetFolder(ruta.Substring(0, i));
            return carpeta.GetTask(ruta.Substring(i + 1));
        }
        catch { return null; }
    }

    public static Dictionary<string, EstadoTarea> LeerEstados()
    {
        var res = new Dictionary<string, EstadoTarea>(StringComparer.OrdinalIgnoreCase);
        dynamic servicio = Servicio();
        foreach (TareaInfo t in Catalogo)
        {
            dynamic tarea = Obtener(servicio, t.Ruta);
            var e = new EstadoTarea { Existe = tarea != null };
            if (tarea != null)
            {
                e.Habilitada = (bool)tarea.Enabled;
                try
                {
                    DateTime ultima = (DateTime)tarea.LastRunTime;
                    if (ultima.Year > 2000) e.UltimaEjecucion = ultima;
                }
                catch { }
            }
            res[t.Ruta] = e;
        }
        return res;
    }

    static Dictionary<string, bool> LeerCopia() { return Almacen.LeerJson<Dictionary<string, bool>>(ArchivoCopia); }

    public static int Cambiadas { get { return LeerCopia().Count; } }

    public static bool TieneCopia(string ruta) { return LeerCopia().ContainsKey(ruta); }

    public static void Desactivar(IEnumerable<TareaInfo> tareas, Action<string> log)
    {
        dynamic servicio = Servicio();
        var copia = LeerCopia();
        foreach (TareaInfo t in tareas)
        {
            try
            {
                dynamic tarea = Obtener(servicio, t.Ruta);
                if (tarea == null) { log("Tarea no encontrada: " + t.Nombre); continue; }
                if (!copia.ContainsKey(t.Ruta))
                {
                    copia[t.Ruta] = (bool)tarea.Enabled;
                    Almacen.GuardarJson(ArchivoCopia, copia);
                }
                tarea.Enabled = false;
                log("Tarea desactivada: " + t.Nombre);
            }
            catch (Exception ex) { log("ERROR al desactivar la tarea «" + t.Nombre + "»: " + ex.Message); }
        }
    }

    public static void Restaurar(Action<string> log)
    {
        dynamic servicio = Servicio();
        var copia = LeerCopia();
        var pendientes = new Dictionary<string, bool>();
        foreach (var kv in copia)
        {
            TareaInfo info = Catalogo.FirstOrDefault(t => t.Ruta == kv.Key);
            string nombre = info != null ? info.Nombre : kv.Key;
            try
            {
                dynamic tarea = Obtener(servicio, kv.Key);
                if (tarea != null) tarea.Enabled = kv.Value;
                log("Tarea restablecida: " + nombre);
            }
            catch (Exception ex)
            {
                log("ERROR al restablecer la tarea «" + nombre + "»: " + ex.Message);
                pendientes[kv.Key] = kv.Value;
            }
        }
        Almacen.GuardarJson(ArchivoCopia, pendientes);
    }
}
