// Optimizador de Servicios - lógica: catálogo, servicios, copias de seguridad,
// mediciones y análisis de programas. Sin dependencias de la interfaz.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;
// --- Catálogo -------------------------------------------------------------------

class ServicioInfo
{
    public string Nombre, Impacto, Desc;
    public bool Rec;
    public ServicioInfo(string nombre, string impacto, bool rec, string desc)
    {
        Nombre = nombre; Impacto = impacto; Rec = rec; Desc = desc;
    }

    // Riesgo de que desactivarlo afecte a algo que el usuario usa:
    // Seguro, Moderado (puede notarse en algún caso) o Arriesgado (deja de funcionar algo).
    public string Riesgo
    {
        get
        {
            switch (Nombre)
            {
                case "WSearch": case "lfsvc": case "WbioSrvc":
                case "XblAuthManager": case "XblGameSave": case "XboxNetApiSvc": case "XboxGipSvc":
                    return "Moderado";
                case "Spooler": case "bthserv": case "TabletInputService":
                    return "Arriesgado";
                default:
                    return "Seguro";
            }
        }
    }
}

static class Catalogo
{
    // Rec = se marca con "Marcar recomendados". Los no recomendados pueden ser
    // necesarios según el uso del equipo (imprimir, Bluetooth, huella...).
    public static readonly ServicioInfo[] Todos =
    {
        new ServicioInfo("SysMain",          "Alto",  true,  "Superfetch: precarga apps en RAM. Causa disco al 100% en HDD"),
        new ServicioInfo("WSearch",          "Alto",  true,  "Indexador de Windows Search (la búsqueda seguirá funcionando, más lenta)"),
        new ServicioInfo("DiagTrack",        "Alto",  true,  "Telemetría y experiencias del usuario conectado"),
        new ServicioInfo("DoSvc",            "Medio", true,  "Optimización de distribución (comparte actualizaciones P2P)"),
        new ServicioInfo("dmwappushservice", "Medio", true,  "Enrutamiento de mensajes WAP push (telemetría)"),
        new ServicioInfo("PcaSvc",           "Medio", true,  "Asistente de compatibilidad de programas"),
        new ServicioInfo("WerSvc",           "Bajo",  true,  "Informe de errores de Windows"),
        new ServicioInfo("diagnosticshub.standardcollector.service", "Bajo", true, "Recopilador del concentrador de diagnósticos"),
        new ServicioInfo("TrkWks",           "Bajo",  true,  "Cliente de seguimiento de vínculos distribuidos"),
        new ServicioInfo("MapsBroker",       "Bajo",  true,  "Administrador de mapas descargados"),
        new ServicioInfo("RetailDemo",       "Bajo",  true,  "Modo demostración para tiendas"),
        new ServicioInfo("wisvc",            "Bajo",  true,  "Programa Windows Insider"),
        new ServicioInfo("WMPNetworkSvc",    "Bajo",  true,  "Uso compartido de red de Windows Media Player"),
        new ServicioInfo("RemoteRegistry",   "Bajo",  true,  "Registro remoto (también mejora la seguridad)"),
        new ServicioInfo("Fax",              "Bajo",  true,  "Servicio de fax"),
        new ServicioInfo("XblAuthManager",   "Bajo",  true,  "Xbox Live: autenticación (no usar si juegas con Xbox/Game Pass)"),
        new ServicioInfo("XblGameSave",      "Bajo",  true,  "Xbox Live: partidas guardadas"),
        new ServicioInfo("XboxNetApiSvc",    "Bajo",  true,  "Xbox Live: red"),
        new ServicioInfo("XboxGipSvc",       "Bajo",  true,  "Xbox: accesorios"),
        new ServicioInfo("lfsvc",            "Bajo",  false, "Geolocalización (la usan Mapas, Tiempo y la zona horaria automática)"),
        new ServicioInfo("Spooler",          "Medio", false, "Cola de impresión (NECESARIO para imprimir o guardar como PDF)"),
        new ServicioInfo("bthserv",          "Bajo",  false, "Bluetooth (NECESARIO para ratones o auriculares Bluetooth)"),
        new ServicioInfo("WbioSrvc",         "Bajo",  false, "Biometría (NECESARIO para huella o Windows Hello)"),
        new ServicioInfo("TabletInputService", "Bajo", false, "Teclado táctil y escritura a mano (puede afectar a la entrada de texto)"),
    };
}

// --- Perfiles -------------------------------------------------------------------

class Perfil
{
    public string Id, Nombre, Desc;
    public Func<ServicioInfo, bool> Incluye;
    public Perfil(string id, string nombre, string desc, Func<ServicioInfo, bool> incluye)
    {
        Id = id; Nombre = nombre; Desc = desc; Incluye = incluye;
    }
    public override string ToString() { return Nombre; }
}

static class Perfiles
{
    static readonly HashSet<string> SoloExtras = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "DiagTrack", "dmwappushservice", "WerSvc", "diagnosticshub.standardcollector.service", "RetailDemo",
        "wisvc", "MapsBroker", "Fax", "WMPNetworkSvc", "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc"
    };

    public static readonly Perfil[] Todos =
    {
        new Perfil("recomendado", "Recomendado", "Todos los servicios recomendados del catálogo",
                   s => s.Rec),
        new Perfil("conservador", "Conservador", "Solo telemetría, diagnósticos, Xbox y extras: no cambia nada que note el usuario",
                   s => SoloExtras.Contains(s.Nombre)),
        new Perfil("oficina", "Oficina / empresa", "Recomendado, pero mantiene Windows Search (lo necesita la búsqueda de Outlook)",
                   s => s.Rec && s.Nombre != "WSearch"),
        new Perfil("aulas", "Aulas / equipos compartidos", "Recomendado, más geolocalización y biometría (huella / Windows Hello)",
                   s => s.Rec || s.Nombre == "lfsvc" || s.Nombre == "WbioSrvc"),
    };

    public static Perfil Buscar(string id)
    {
        return Todos.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}

// --- Acceso a servicios ---------------------------------------------------------

static class Servicios
{
    static string RegPath(string n) { return @"SYSTEM\CurrentControlSet\Services\" + n; }

    public static bool Existe(string n)
    {
        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(RegPath(n)))
            return k != null && k.GetValue("Start") != null;
    }

    public static int GetStart(string n)
    {
        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(RegPath(n)))
        {
            object v = k == null ? null : k.GetValue("Start");
            return v == null ? -1 : Convert.ToInt32(v);
        }
    }

    // Se escribe directamente en el registro porque algunos servicios (p. ej. DoSvc)
    // no permiten cambiar el tipo de inicio a través del administrador de servicios.
    public static void SetStart(string n, int valor)
    {
        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(RegPath(n), true))
        {
            if (k == null) throw new InvalidOperationException("el servicio no existe");
            k.SetValue("Start", valor, RegistryValueKind.DWord);
        }
    }

    public static bool EnEjecucion(string n)
    {
        try { using (var sc = new ServiceController(n)) return sc.Status == ServiceControllerStatus.Running; }
        catch { return false; }
    }

    // Devuelve un texto vacío si se detuvo, o una nota si no se pudo detener ahora.
    public static string Detener(string n)
    {
        try
        {
            using (var sc = new ServiceController(n))
            {
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }
            }
            return "";
        }
        catch { return " (se detendrá al reiniciar)"; }
    }

    public static void Iniciar(string n)
    {
        try
        {
            using (var sc = new ServiceController(n))
            {
                if (sc.Status == ServiceControllerStatus.Stopped) sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            }
        }
        catch { }
    }

    public static string TipoInicio(int start)
    {
        switch (start)
        {
            case 0: case 1: return "Sistema";
            case 2: return "Automático";
            case 3: return "Manual";
            case 4: return "Deshabilitado";
            default: return "?";
        }
    }
}

// --- Persistencia ---------------------------------------------------------------

public class Medicion
{
    public string Fecha { get; set; }
    public string FechaIso { get; set; }         // para comparar con la fecha del último cambio
    public int DuracionSeg { get; set; }
    public double ArranqueSeg { get; set; }      // -1 = no disponible
    public double EscritorioSeg { get; set; }
    public string ArranqueFecha { get; set; }
    public double RamUsadaMB { get; set; }
    public double CommitMB { get; set; }
    public double CpuPct { get; set; }
    public double DiscoPct { get; set; }
    public double ColaDisco { get; set; }
    public double Procesos { get; set; }
    public double ServiciosActivos { get; set; }
}

public class Mediciones
{
    public Medicion Antes { get; set; }
    public Medicion Despues { get; set; }
}

public class Estado
{
    public string UltimoCambio { get; set; }     // fecha ISO del último desactivar/restablecer
    public bool OmitirMedicionInicial { get; set; }   // el usuario no quiere la medición de la primera ejecución

    public static DateTime? Fecha(string iso)
    {
        DateTime d;
        return iso != null && DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out d) ? d : (DateTime?)null;
    }
}

static class Almacen
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OptimizadorServicios");
    static string BackupFile { get { return Path.Combine(Dir, "backup.json"); } }
    static string MedFile { get { return Path.Combine(Dir, "mediciones.json"); } }
    static string EstadoFile { get { return Path.Combine(Dir, "estado.json"); } }
    static string LogFile { get { return Path.Combine(Dir, "registro.log"); } }
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    // Nombre del servicio -> { Start, Running } con su estado original.
    public static Dictionary<string, Dictionary<string, object>> LeerBackup()
    {
        var res = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(BackupFile)) return res;
        var raw = Json.DeserializeObject(File.ReadAllText(BackupFile)) as Dictionary<string, object>;
        if (raw != null)
            foreach (var kv in raw)
            {
                var d = kv.Value as Dictionary<string, object>;
                if (d != null) res[kv.Key] = d;
            }
        return res;
    }

    public static void GuardarBackup(Dictionary<string, Dictionary<string, object>> backup)
    {
        Directory.CreateDirectory(Dir);
        if (backup.Count == 0) { if (File.Exists(BackupFile)) File.Delete(BackupFile); return; }
        File.WriteAllText(BackupFile, Json.Serialize(backup));
    }

    public static Mediciones LeerMediciones()
    {
        try
        {
            if (File.Exists(MedFile))
                return Json.Deserialize<Mediciones>(File.ReadAllText(MedFile)) ?? new Mediciones();
        }
        catch { }
        return new Mediciones();
    }

    public static void GuardarMediciones(Mediciones m)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(MedFile, Json.Serialize(m));
    }

    public static Estado LeerEstado()
    {
        try
        {
            if (File.Exists(EstadoFile))
                return Json.Deserialize<Estado>(File.ReadAllText(EstadoFile)) ?? new Estado();
        }
        catch { }
        return new Estado();
    }

    // Anota la fecha del último cambio conservando el resto del estado.
    public static void MarcarCambio()
    {
        Estado e = LeerEstado();
        e.UltimoCambio = DateTime.Now.ToString("o");
        GuardarEstado(e);
    }

    public static void GuardarEstado(Estado e)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(EstadoFile, Json.Serialize(e));
    }

    // Lectura y escritura genérica de los demás archivos de datos (ajustes, tareas...).
    public static T LeerJson<T>(string archivo) where T : class, new()
    {
        try
        {
            string ruta = Path.Combine(Dir, archivo);
            if (File.Exists(ruta)) return Json.Deserialize<T>(File.ReadAllText(ruta)) ?? new T();
        }
        catch { }
        return new T();
    }

    public static void GuardarJson(string archivo, object datos)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, archivo), Json.Serialize(datos));
    }

    public static void Log(string texto)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + texto + Environment.NewLine);
        }
        catch { }
    }
}

// --- Operaciones ----------------------------------------------------------------

static class Operaciones
{
    public static void Desactivar(string n, Action<string> log)
    {
        // El estado original solo se guarda la primera vez, para no sobrescribirlo
        // con "Deshabilitado" si se desactiva varias veces.
        var backup = Almacen.LeerBackup();
        if (!backup.ContainsKey(n))
        {
            int start = Servicios.GetStart(n);
            var d = new Dictionary<string, object>();
            d["Start"] = start < 0 ? null : (object)start;
            d["Running"] = Servicios.EnEjecucion(n);
            backup[n] = d;
            Almacen.GuardarBackup(backup);
        }

        try { Servicios.SetStart(n, 4); }
        catch (Exception ex) { log("ERROR al deshabilitar " + n + ": " + ex.Message); return; }
        log("Deshabilitado: " + n + Servicios.Detener(n));
    }

    public static void Restaurar(Action<string> log)
    {
        var backup = Almacen.LeerBackup();
        if (backup.Count == 0) { log("No hay cambios que restablecer."); return; }

        var pendientes = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in backup)
        {
            try
            {
                object st, run;
                int start = kv.Value.TryGetValue("Start", out st) && st != null ? Convert.ToInt32(st) : -1;
                bool running = kv.Value.TryGetValue("Running", out run) && run is bool && (bool)run;
                if (start >= 0) Servicios.SetStart(kv.Key, start);
                if (running && start != 4) Servicios.Iniciar(kv.Key);
                log("Restablecido: " + kv.Key + " (" + Servicios.TipoInicio(start) + ")");
            }
            catch (Exception ex)
            {
                log("ERROR al restablecer " + kv.Key + ": " + ex.Message);
                pendientes[kv.Key] = kv.Value;
            }
        }
        // Solo se conservan en la copia los que no se pudieron restablecer.
        Almacen.GuardarBackup(pendientes);
    }

    public static void CrearPuntoRestauracion(Action<string> log)
    {
        try
        {
            using (var mc = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null))
            {
                ManagementBaseObject inP = mc.GetMethodParameters("CreateRestorePoint");
                inP["Description"] = "Antes de Optimizador de Servicios";
                inP["RestorePointType"] = 12;   // MODIFY_SETTINGS
                inP["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
                ManagementBaseObject outP = mc.InvokeMethod("CreateRestorePoint", inP, null);
                uint rv = Convert.ToUInt32(outP["ReturnValue"]);
                log(rv == 0
                    ? "Punto de restauración solicitado (Windows solo crea uno cada 24 h)."
                    : "No se pudo crear el punto de restauración (código " + rv + "). Comprueba que la Protección del sistema esté activada.");
            }
        }
        catch (Exception ex) { log("No se pudo crear el punto de restauración: " + ex.Message); }
    }
}

// --- Reinicio y apertura tras reiniciar -----------------------------------------

static class Sistema
{
    const string NombreTarea = "OptimizadorServicios_MedirDespues";

    [DllImport("kernel32.dll")]
    static extern ulong GetTickCount64();

    public static double SegundosEncendido { get { return GetTickCount64() / 1000.0; } }

    public static DateTime Arranque { get { return DateTime.Now.AddSeconds(-SegundosEncendido); } }

    public static void Reiniciar(int segundos = 5)
    {
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t " + segundos + " /c \"Reinicio solicitado por el Optimizador de Servicios\"")
        {
            UseShellExecute = false, CreateNoWindow = true
        });
    }

    // Tarea programada de un solo uso: abre el optimizador (con permisos de
    // administrador, sin aviso UAC) un minuto después del próximo inicio de sesión,
    // para que el usuario pueda medir el rendimiento DESPUÉS. Se borra al abrirse.
    public static bool ProgramarApertura(Action<string> log)
    {
        string xml = Path.Combine(Path.GetTempPath(), NombreTarea + ".xml");
        try
        {
            string usuario = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            File.WriteAllText(xml,
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Abre el Optimizador de Servicios tras reiniciar para medir el rendimiento. Se elimina sola.</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + usuario + "</UserId><Delay>PT1M</Delay></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + usuario + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(Rutas.Exe) + "</Command></Exec></Actions>\r\n" +
                "</Task>\r\n",
                System.Text.Encoding.Unicode);
            int codigo = Schtasks("/create /tn \"" + NombreTarea + "\" /xml \"" + xml + "\" /f");
            if (codigo != 0) log("No se pudo programar la apertura tras el reinicio (código " + codigo + ").");
            return codigo == 0;
        }
        catch (Exception ex)
        {
            log("No se pudo programar la apertura tras el reinicio: " + ex.Message);
            return false;
        }
        finally
        {
            try { File.Delete(xml); } catch { }
        }
    }

    public static void CancelarApertura()
    {
        try { Schtasks("/delete /tn \"" + NombreTarea + "\" /f"); } catch { }
    }

    static int Schtasks(string argumentos)
    {
        var psi = new ProcessStartInfo("schtasks.exe", argumentos)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (Process p = Process.Start(psi))
        {
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        }
    }
}

// --- Medición de rendimiento ----------------------------------------------------

static class Medidor
{
    // Barras literales: con "/" a secas se usa el separador de la configuración regional.
    const string FormatoFecha = "dd'/'MM'/'yyyy HH:mm";

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

    public static void Memoria(out double totalMB, out double libreMB)
    {
        var mem = new MEMORYSTATUSEX();
        GlobalMemoryStatusEx(mem);
        totalMB = mem.ullTotalPhys / 1048576.0;
        libreMB = mem.ullAvailPhys / 1048576.0;
    }

    public static void TiemposCpu(out long inactivo, out long total)
    {
        long i, k, u;
        GetSystemTimes(out i, out k, out u);
        inactivo = i;
        total = k + u;      // el tiempo de kernel incluye el inactivo
    }

    // Disco físico -> { tiempo inactivo, marca de tiempo } (contadores en bruto, 100 ns)
    internal static Dictionary<string, ulong[]> LeerDiscos(out double cola)
    {
        var res = new Dictionary<string, ulong[]>();
        cola = 0;
        try
        {
            using (var s = new ManagementObjectSearcher(
                "SELECT Name, PercentIdleTime, Timestamp_Sys100NS, CurrentDiskQueueLength FROM Win32_PerfRawData_PerfDisk_PhysicalDisk"))
            {
                foreach (ManagementObject o in s.Get())
                {
                    string name = (string)o["Name"];
                    if (name == "_Total") continue;
                    res[name] = new[] { Convert.ToUInt64(o["PercentIdleTime"]), Convert.ToUInt64(o["Timestamp_Sys100NS"]) };
                    cola += Convert.ToDouble(o["CurrentDiskQueueLength"]);
                }
            }
        }
        catch { }
        return res;
    }

    // % de tiempo ocupado del disco más ocupado entre dos lecturas
    internal static double DiscoOcupado(Dictionary<string, ulong[]> a, Dictionary<string, ulong[]> b)
    {
        double max = -1;
        foreach (var kv in b)
        {
            ulong[] prev;
            if (!a.TryGetValue(kv.Key, out prev)) continue;
            double dt = kv.Value[1] - prev[1];
            if (dt <= 0) continue;
            double busy = 100.0 - 100.0 * (kv.Value[0] - prev[0]) / dt;
            max = Math.Max(max, Math.Min(100, Math.Max(0, busy)));
        }
        return max;
    }

    // Devuelve null si se cancela.
    public static Medicion Medir(int segundos, IProgress<int> progreso, Func<bool> cancelar = null)
    {
        var m = new Medicion { Fecha = DateTime.Now.ToString(FormatoFecha), FechaIso = DateTime.Now.ToString("o"), DuracionSeg = segundos };

        double cola;
        var discos0 = LeerDiscos(out cola);     // también "calienta" el proveedor WMI
        long i0, k0, u0;
        GetSystemTimes(out i0, out k0, out u0);

        double sumRam = 0, sumCommit = 0, sumCola = 0, sumProc = 0, sumDisco = 0;
        int n = 0, nDisco = 0;
        var reloj = Stopwatch.StartNew();
        while (reloj.Elapsed.TotalSeconds < segundos)
        {
            Thread.Sleep(1000);
            if (cancelar != null && cancelar()) return null;

            var mem = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(mem);
            sumRam += (mem.ullTotalPhys - mem.ullAvailPhys) / 1048576.0;
            sumCommit += (mem.ullTotalPageFile - mem.ullAvailPageFile) / 1048576.0;

            Process[] procs = Process.GetProcesses();
            sumProc += procs.Length;
            foreach (Process p in procs) p.Dispose();

            var discos1 = LeerDiscos(out cola);
            sumCola += cola;
            double ocupado = DiscoOcupado(discos0, discos1);
            if (ocupado >= 0) { sumDisco += ocupado; nDisco++; }
            discos0 = discos1;

            n++;
            progreso.Report((int)Math.Min(100, reloj.Elapsed.TotalSeconds * 100 / segundos));
        }

        long i1, k1, u1;
        GetSystemTimes(out i1, out k1, out u1);
        double total = (k1 - k0) + (u1 - u0);    // el tiempo de kernel incluye el inactivo
        m.CpuPct = total > 0 ? Math.Max(0, 100.0 * (1 - (i1 - i0) / total)) : -1;

        m.RamUsadaMB = n > 0 ? sumRam / n : -1;
        m.CommitMB = n > 0 ? sumCommit / n : -1;
        m.Procesos = n > 0 ? sumProc / n : -1;
        m.ColaDisco = n > 0 ? sumCola / n : -1;
        m.DiscoPct = nDisco > 0 ? sumDisco / nDisco : -1;

        ServiceController[] svcs = ServiceController.GetServices();
        m.ServiciosActivos = svcs.Count(s => s.Status == ServiceControllerStatus.Running);
        foreach (ServiceController s in svcs) s.Dispose();

        LeerArranque(m);
        return m;
    }

    // Último arranque registrado por Windows (evento 100 de Diagnostics-Performance).
    static void LeerArranque(Medicion m)
    {
        m.ArranqueSeg = -1; m.EscritorioSeg = -1; m.ArranqueFecha = null;
        try
        {
            var q = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational",
                                      PathType.LogName, "*[System[(EventID=100)]]") { ReverseDirection = true };
            using (var r = new EventLogReader(q))
            using (EventRecord e = r.ReadEvent())
            {
                if (e == null) return;
                string xml = e.ToXml();
                Match total = Regex.Match(xml, "Name=['\"]BootTime['\"]>(\\d+)<");
                Match escritorio = Regex.Match(xml, "Name=['\"]MainPathBootTime['\"]>(\\d+)<");
                if (total.Success) m.ArranqueSeg = double.Parse(total.Groups[1].Value) / 1000.0;
                if (escritorio.Success) m.EscritorioSeg = double.Parse(escritorio.Groups[1].Value) / 1000.0;
                if (e.TimeCreated.HasValue) m.ArranqueFecha = e.TimeCreated.Value.ToString(FormatoFecha);
            }
        }
        catch { }
    }
}

// --- Programas instalados -------------------------------------------------------

class ProgramaInstalado
{
    public string Nombre, Editor, Version, Ubicacion, Icono, Desinstalar;
}

static class ProgramasInstalados
{
    const string ClaveUninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    const string ClaveUninstall32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<ProgramaInstalado> Leer()
    {
        var res = new List<ProgramaInstalado>();
        LeerDe(RegistryHive.LocalMachine, ClaveUninstall, res);
        LeerDe(RegistryHive.LocalMachine, ClaveUninstall32, res);
        LeerDe(RegistryHive.CurrentUser, ClaveUninstall, res);
        return res;
    }

    static void LeerDe(RegistryHive hive, string ruta, List<ProgramaInstalado> res)
    {
        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey raiz = baseKey.OpenSubKey(ruta))
            {
                if (raiz == null) return;
                foreach (string sub in raiz.GetSubKeyNames())
                {
                    using (RegistryKey k = raiz.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        string nombre = k.GetValue("DisplayName") as string;
                        string desinstalar = k.GetValue("UninstallString") as string;
                        // Se omiten componentes del sistema y actualizaciones, que no se desinstalan por separado.
                        if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(desinstalar)) continue;
                        if (Convert.ToInt32(k.GetValue("SystemComponent", 0)) == 1 || k.GetValue("ParentKeyName") != null) continue;
                        res.Add(new ProgramaInstalado
                        {
                            Nombre = nombre,
                            Editor = k.GetValue("Publisher") as string ?? "",
                            Version = k.GetValue("DisplayVersion") as string ?? "",
                            Ubicacion = Carpeta(k.GetValue("InstallLocation") as string, false),
                            Icono = Carpeta(k.GetValue("DisplayIcon") as string, true),
                            Desinstalar = desinstalar
                        });
                    }
                }
            }
        }
        catch { }
    }

    // Normaliza una ruta del registro ("C:\x\app.exe",0) a la carpeta que la contiene.
    static string Carpeta(string valor, bool esArchivo)
    {
        if (string.IsNullOrEmpty(valor)) return null;
        string ruta = Environment.ExpandEnvironmentVariables(valor.Split(',')[0].Trim().Trim('"'));
        try { if (esArchivo) ruta = Path.GetDirectoryName(ruta); } catch { return null; }
        return string.IsNullOrEmpty(ruta) ? null : ruta.TrimEnd('\\');
    }

    // Carpetas que contienen muchos programas: no sirven para identificar uno concreto.
    static readonly HashSet<string> Genericas = new HashSet<string>(new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
    }.Where(c => !string.IsNullOrEmpty(c)).Select(c => c.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);

    static bool Contiene(string carpeta, string archivo)
    {
        if (string.IsNullOrEmpty(carpeta) || Genericas.Contains(carpeta)) return false;
        if (carpeta.IndexOf("Package Cache", StringComparison.OrdinalIgnoreCase) >= 0 ||
            carpeta.IndexOf(@"\Windows\Installer", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return archivo.StartsWith(carpeta + "\\", StringComparison.OrdinalIgnoreCase);
    }

    // Busca el programa instalado al que pertenece un ejecutable (la carpeta coincidente más concreta).
    public static ProgramaInstalado Buscar(List<ProgramaInstalado> programas, string exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        ProgramaInstalado mejor = null;
        int largo = 0;
        foreach (ProgramaInstalado p in programas)
        {
            string desinst = Carpeta(ExeDeComando(p.Desinstalar), true);
            foreach (string c in new[] { p.Ubicacion, p.Icono, desinst })
                if (Contiene(c, exe) && c.Length > largo) { mejor = p; largo = c.Length; }
        }
        return mejor;
    }

    public static ProgramaInstalado BuscarPorNombre(List<ProgramaInstalado> programas, string nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return null;
        return programas.FirstOrDefault(p => p.Nombre.IndexOf(nombre, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // Extrae el ejecutable de una línea de comandos ("C:\a b\x.exe" /arg  o  C:\x.exe /arg).
    public static string ExeDeComando(string comando)
    {
        if (string.IsNullOrEmpty(comando)) return null;
        string c = Environment.ExpandEnvironmentVariables(comando.Trim());
        if (c.StartsWith("\"")) { int fin = c.IndexOf('"', 1); return fin > 0 ? c.Substring(1, fin - 1) : c.Trim('"'); }
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c.Substring(0, exe + 4) : c.Split(' ')[0];
    }

    // Lanza el desinstalador del propio programa (interactivo, para que el usuario lo controle).
    public static void LanzarDesinstalador(ProgramaInstalado p)
    {
        string cmd = p.Desinstalar;
        // "MsiExec.exe /I{GUID}" abre el modo de modificación; /X desinstala.
        if (cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0)
            cmd = Regex.Replace(cmd, @"\s/I\s*\{", " /X{", RegexOptions.IgnoreCase);
        Process.Start(new ProcessStartInfo("cmd.exe", "/s /c \"" + cmd + "\"") { UseShellExecute = false, CreateNoWindow = true });
    }

    static readonly string[] PalabrasDelicadas =
    {
        "antivirus", "security", "seguridad", "defender", "firewall", "endpoint", "protection", "vpn",
        "driver", "controlador", "chipset", "audio", "realtek", "intel", "nvidia", "amd ", "radeon", "synaptics", "elan", "bios"
    };

    // Programas cuya desinstalación puede dejar el equipo sin protección o sin hardware funcionando.
    public static bool EsDelicado(ProgramaInstalado p)
    {
        string t = (p.Nombre + " " + p.Editor).ToLowerInvariant() + " ";
        return PalabrasDelicadas.Any(t.Contains);
    }
}

// --- Programas al inicio --------------------------------------------------------

class EntradaInicio
{
    public string Nombre, Origen, Comando, Exe;
    public bool Habilitada = true;
    public bool Conmutable;                 // se puede activar/desactivar (tiene clave StartupApproved)
    public RegistryHive Hive;
    public string ClaveAprobado, Valor;
    public int Retrasos;                    // veces que Windows registró que retrasó el arranque
    public double RetrasoMaxSeg;
    public ProgramaInstalado Programa;
}

static class Inicio
{
    const string Run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string Run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    const string Aprobado = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    class Retraso { public int Veces; public double MaxSeg; public string Nombre; }

    public static List<EntradaInicio> Leer(List<ProgramaInstalado> programas)
    {
        var res = new List<EntradaInicio>();
        LeerRun(RegistryHive.LocalMachine, Run, "Run", "Registro (todos los usuarios)", res);
        LeerRun(RegistryHive.LocalMachine, Run32, "Run32", "Registro (todos los usuarios, 32 bits)", res);
        LeerRun(RegistryHive.CurrentUser, Run, "Run", "Registro (usuario actual)", res);
        LeerCarpeta(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Carpeta Inicio (todos los usuarios)", res);
        LeerCarpeta(Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Carpeta Inicio (usuario actual)", res);

        // Los componentes de Windows (p. ej. el icono de Seguridad de Windows) no se tocan.
        string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        res.RemoveAll(e => e.Exe != null && e.Exe.StartsWith(windir + "\\", StringComparison.OrdinalIgnoreCase));

        Dictionary<string, Retraso> retrasos = LeerRetrasos();
        foreach (EntradaInicio e in res)
        {
            Retraso r;
            if (e.Exe != null && retrasos.TryGetValue(Path.GetFileName(e.Exe).ToLowerInvariant(), out r))
            {
                e.Retrasos = r.Veces; e.RetrasoMaxSeg = r.MaxSeg;
                retrasos.Remove(Path.GetFileName(e.Exe).ToLowerInvariant());
            }
            e.Programa = ProgramasInstalados.Buscar(programas, e.Exe);
        }

        // Programas que Windows registró como lentos pero que no se inician desde
        // las ubicaciones anteriores (p. ej. los lanza un servicio o una tarea).
        foreach (var kv in retrasos)
        {
            if (File.Exists(Path.Combine(windir, "System32", kv.Key)) || File.Exists(Path.Combine(windir, kv.Key))) continue;
            res.Add(new EntradaInicio
            {
                Nombre = string.IsNullOrEmpty(kv.Value.Nombre) ? kv.Key : kv.Value.Nombre,
                Origen = "Detectado por Windows (otro origen)",
                Comando = kv.Key, Retrasos = kv.Value.Veces, RetrasoMaxSeg = kv.Value.MaxSeg,
                Programa = ProgramasInstalados.BuscarPorNombre(programas, kv.Value.Nombre)
            });
        }
        return res.OrderByDescending(e => e.Retrasos).ThenBy(e => e.Nombre).ToList();
    }

    static void LeerRun(RegistryHive hive, string ruta, string aprobado, string origen, List<EntradaInicio> res)
    {
        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey k = baseKey.OpenSubKey(ruta))
            {
                if (k == null) return;
                foreach (string nombre in k.GetValueNames())
                {
                    string comando = k.GetValue(nombre) as string;
                    if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(comando)) continue;
                    var e = new EntradaInicio
                    {
                        Nombre = nombre, Origen = origen, Comando = comando,
                        Exe = ProgramasInstalados.ExeDeComando(comando),
                        Conmutable = true, Hive = hive, ClaveAprobado = Aprobado + aprobado, Valor = nombre
                    };
                    e.Habilitada = EstaHabilitada(e);
                    res.Add(e);
                }
            }
        }
        catch { }
    }

    static void LeerCarpeta(string carpeta, RegistryHive hive, string origen, List<EntradaInicio> res)
    {
        if (string.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta)) return;
        foreach (string archivo in Directory.GetFiles(carpeta))
        {
            string nombre = Path.GetFileName(archivo);
            if (nombre.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            string destino = archivo.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? DestinoAcceso(archivo) : archivo;
            var e = new EntradaInicio
            {
                Nombre = Path.GetFileNameWithoutExtension(archivo), Origen = origen, Comando = destino ?? archivo, Exe = destino,
                Conmutable = true, Hive = hive, ClaveAprobado = Aprobado + "StartupFolder", Valor = nombre
            };
            e.Habilitada = EstaHabilitada(e);
            res.Add(e);
        }
    }

    static string DestinoAcceso(string lnk)
    {
        try
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object acceso = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            string destino = (string)acceso.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, acceso, null);
            return string.IsNullOrEmpty(destino) ? null : destino;
        }
        catch { return null; }
    }

    // Windows (y el Administrador de tareas) guardan el estado activado/desactivado de cada
    // entrada en StartupApproved: primer byte par = activada, impar = desactivada.
    static bool EstaHabilitada(EntradaInicio e)
    {
        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Registry64))
            using (RegistryKey k = baseKey.OpenSubKey(e.ClaveAprobado))
            {
                byte[] v = k == null ? null : k.GetValue(e.Valor) as byte[];
                return v == null || v.Length == 0 || (v[0] & 1) == 0;
            }
        }
        catch { return true; }
    }

    public static void Cambiar(EntradaInicio e, bool habilitar)
    {
        var valor = new byte[12];
        valor[0] = (byte)(habilitar ? 2 : 3);
        if (!habilitar) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(valor, 4);
        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Registry64))
        using (RegistryKey k = baseKey.CreateSubKey(e.ClaveAprobado))
            k.SetValue(e.Valor, valor, RegistryValueKind.Binary);
        e.Habilitada = habilitar;
    }

    // Eventos 101 de Diagnostics-Performance: "esta aplicación tardó más de lo normal en
    // iniciarse, lo que degradó el arranque del sistema".
    static Dictionary<string, Retraso> LeerRetrasos()
    {
        var res = new Dictionary<string, Retraso>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var q = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational",
                                      PathType.LogName, "*[System[(EventID=101)]]");
            using (var r = new EventLogReader(q))
            {
                EventRecord e;
                while ((e = r.ReadEvent()) != null)
                {
                    using (e)
                    {
                        string xml = e.ToXml();
                        string archivo = Dato(xml, "FileName");
                        if (string.IsNullOrEmpty(archivo)) continue;
                        archivo = Path.GetFileName(archivo).ToLowerInvariant();
                        double seg;
                        double.TryParse(Dato(xml, "DegradationTime"), NumberStyles.Any, CultureInfo.InvariantCulture, out seg);
                        Retraso ret;
                        if (!res.TryGetValue(archivo, out ret)) res[archivo] = ret = new Retraso();
                        ret.Veces++;
                        ret.MaxSeg = Math.Max(ret.MaxSeg, seg / 1000.0);
                        ret.Nombre = Dato(xml, "FriendlyName") ?? ret.Nombre;
                    }
                }
            }
        }
        catch { }
        return res;
    }

    static string Dato(string xml, string nombre)
    {
        Match m = Regex.Match(xml, "Name=['\"]" + nombre + "['\"]>([^<]*)<");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}

// --- Consumo de programas en uso normal -----------------------------------------

class ConsumoPrograma
{
    public string Exe, Nombre;
    public int Procesos;
    public double CpuPct, RamMB, DiscoKBs;
    public ProgramaInstalado Programa;

    public bool Alto { get { return CpuPct >= 5 || RamMB >= 300 || DiscoKBs >= 1024; } }
}

static class Consumo
{
    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount,
                     ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint acceso, bool heredar, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder ruta, ref int tam);
    [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr h, out long creacion, out long salida, out long kernel, out long usuario);
    [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS io);

    class Muestra { public string Exe; public long Cpu; public ulong Io; public double RamMB; }

    static Dictionary<int, Muestra> Tomar()
    {
        var res = new Dictionary<int, Muestra>();
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4) continue;
                IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var sb = new System.Text.StringBuilder(1024);
                    int tam = sb.Capacity;
                    if (!QueryFullProcessImageName(h, 0, sb, ref tam)) continue;
                    long c, s, k, u;
                    IO_COUNTERS io;
                    res[p.Id] = new Muestra
                    {
                        Exe = sb.ToString(),
                        Cpu = GetProcessTimes(h, out c, out s, out k, out u) ? k + u : 0,
                        Io = GetProcessIoCounters(h, out io) ? io.ReadTransferCount + io.WriteTransferCount : 0,
                        RamMB = p.WorkingSet64 / 1048576.0
                    };
                }
                finally { CloseHandle(h); }
            }
            catch { }
            finally { p.Dispose(); }
        }
        return res;
    }

    public static List<ConsumoPrograma> Medir(int segundos, List<ProgramaInstalado> programas, IProgress<int> progreso)
    {
        Dictionary<int, Muestra> antes = Tomar();
        var reloj = Stopwatch.StartNew();
        while (reloj.Elapsed.TotalSeconds < segundos)
        {
            Thread.Sleep(500);
            progreso.Report((int)Math.Min(100, reloj.Elapsed.TotalSeconds * 100 / segundos));
        }
        Dictionary<int, Muestra> despues = Tomar();
        double seg = reloj.Elapsed.TotalSeconds;

        // Se agrupan los procesos por ejecutable (un navegador abre muchos procesos).
        string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
        string propio = Rutas.Exe;
        var grupos = new Dictionary<string, ConsumoPrograma>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in despues)
        {
            Muestra d = kv.Value, a;
            if (d.Exe.StartsWith(windir, StringComparison.OrdinalIgnoreCase) ||
                d.Exe.Equals(propio, StringComparison.OrdinalIgnoreCase)) continue;
            ConsumoPrograma g;
            if (!grupos.TryGetValue(d.Exe, out g)) grupos[d.Exe] = g = new ConsumoPrograma { Exe = d.Exe };
            g.Procesos++;
            g.RamMB += d.RamMB;
            if (antes.TryGetValue(kv.Key, out a) && a.Exe == d.Exe)
            {
                g.CpuPct += (d.Cpu - a.Cpu) / (seg * 1e7 * Environment.ProcessorCount) * 100;
                g.DiscoKBs += (d.Io - a.Io) / 1024.0 / seg;
            }
        }

        foreach (ConsumoPrograma g in grupos.Values)
        {
            g.Nombre = Descripcion(g.Exe);
            g.Programa = ProgramasInstalados.Buscar(programas, g.Exe);
        }
        return grupos.Values.OrderByDescending(g => g.Alto).ThenByDescending(g => g.CpuPct).ThenByDescending(g => g.RamMB).ToList();
    }

    static string Descripcion(string exe)
    {
        try
        {
            string d = FileVersionInfo.GetVersionInfo(exe).FileDescription;
            if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
        }
        catch { }
        return Path.GetFileName(exe);
    }
}


static class Rutas
{
    public static readonly string Exe = Process.GetCurrentProcess().MainModule.FileName;
}
