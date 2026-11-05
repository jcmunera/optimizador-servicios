// Optimizador de Servicios - interfaz gráfica (WinForms, .NET Framework 4.x).
// Desactiva de forma opcional servicios que penalizan el rendimiento en equipos
// con 4 GB de RAM y disco duro mecánico, permite restablecerlos y mide el
// rendimiento antes y después de los cambios.
//
// Comparte la copia de seguridad (%ProgramData%\OptimizadorServicios\backup.json)
// con la versión de consola Optimizar-Servicios.ps1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
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
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Optimizador de Servicios")]
[assembly: AssemblyDescription("Desactiva servicios de Windows 10 que penalizan el rendimiento")]
[assembly: AssemblyProduct("Optimizador de Servicios")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

// --- Catálogo -------------------------------------------------------------------

class ServicioInfo
{
    public string Nombre, Impacto, Desc;
    public bool Rec;
    public ServicioInfo(string nombre, string impacto, bool rec, string desc)
    {
        Nombre = nombre; Impacto = impacto; Rec = rec; Desc = desc;
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

    public static void GuardarEstado(Estado e)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(EstadoFile, Json.Serialize(e));
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
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(Application.ExecutablePath) + "</Command></Exec></Actions>\r\n" +
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

    // Disco físico -> { tiempo inactivo, marca de tiempo } (contadores en bruto, 100 ns)
    static Dictionary<string, ulong[]> LeerDiscos(out double cola)
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
    static double DiscoOcupado(Dictionary<string, ulong[]> a, Dictionary<string, ulong[]> b)
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

    public static Medicion Medir(int segundos, IProgress<int> progreso)
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
        string propio = Application.ExecutablePath;
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

// --- Interfaz -------------------------------------------------------------------

class Metrica
{
    public string Nombre, Unidad, Formato;
    public Func<Medicion, double> Valor;
    public Metrica(string nombre, string unidad, string formato, Func<Medicion, double> valor)
    {
        Nombre = nombre; Unidad = unidad; Formato = formato; Valor = valor;
    }
}

class MainForm : Form
{
    static readonly Color Verde = Color.FromArgb(0, 128, 0);
    static readonly Color Rojo = Color.FromArgb(192, 0, 0);
    static readonly Color Naranja = Color.FromArgb(200, 110, 0);
    static readonly Color Azul = Color.FromArgb(0, 90, 170);
    static readonly Color FondoDeshabilitado = Color.FromArgb(228, 244, 228);
    static readonly Color FondoPendiente = Color.FromArgb(255, 238, 214);

    // En todas las métricas, un valor menor es mejor.
    static readonly Metrica[] Metricas =
    {
        new Metrica("Tiempo de arranque total",     "s",  "F1", m => m.ArranqueSeg),
        new Metrica("Tiempo hasta el escritorio",   "s",  "F1", m => m.EscritorioSeg),
        new Metrica("RAM en uso",                   "MB", "F0", m => m.RamUsadaMB),
        new Metrica("Memoria comprometida",         "MB", "F0", m => m.CommitMB),
        new Metrica("Uso medio de CPU",             "%",  "F1", m => m.CpuPct),
        new Metrica("Actividad media del disco",    "%",  "F1", m => m.DiscoPct),
        new Metrica("Cola media del disco",         "",   "F2", m => m.ColaDisco),
        new Metrica("Procesos en ejecución",        "",   "F0", m => m.Procesos),
        new Metrica("Servicios en ejecución",       "",   "F0", m => m.ServiciosActivos),
    };

    readonly List<ServicioInfo> servicios;
    TabControl tabs;
    ListView lvServ, lvComp;
    ListViewGroup grpRec, grpOpt;
    TextBox txtLog;
    CheckBox chkPunto;
    Button btnRec, btnNinguno, btnActualizar, btnDesactivar, btnRestaurar, btnAntes, btnDespues, btnBorrar;
    ComboBox cmbDur, cmbPerfil;
    ListView lvInicio, lvConsumo, lvProgActiva;
    Button btnAnalizar, btnQuitarInicio, btnPonerInicio, btnDesinstalar, btnUbicacion;
    ProgressBar progProg;
    Label lblProgramas;
    ProgressBar prog;
    ToolStripStatusLabel lblEstado;

    public MainForm()
    {
        Text = "Optimizador de Servicios - Windows 10";
        Font = new Font("Segoe UI", 9f);
        Size = new Size(1180, 700);
        MinimumSize = new Size(820, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        servicios = Catalogo.Todos.Where(s => Servicios.Existe(s.Nombre)).ToList();

        tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
        tabs.TabPages.Add(CrearTabServicios());
        tabs.TabPages.Add(CrearTabAnalisis());
        tabs.TabPages.Add(CrearTabProgramas());

        var status = new StatusStrip();
        lblEstado = new ToolStripStatusLabel("Listo");
        status.Items.Add(lblEstado);

        Controls.Add(tabs);
        Controls.Add(status);

        Load += (s, e) => { RefrescarServicios(); RefrescarComparacion(); };
        Shown += (s, e) => AlIniciar();
    }

    // Guía al usuario según el punto del ciclo medir -> cambiar -> reiniciar -> medir
    // en el que se quedó la última vez.
    async void AlIniciar()
    {
        Mediciones meds = Almacen.LeerMediciones();
        DateTime? cambio = Estado.Fecha(Almacen.LeerEstado().UltimoCambio);

        if (cambio.HasValue)
        {
            if (Sistema.Arranque < cambio.Value)
            {
                // Cambios aplicados pero sin reiniciar: la tarea de apertura sigue programada.
                if (MessageBox.Show(this, "Hay cambios aplicados que todavía no tienen efecto completo porque el equipo no se ha reiniciado.\n\n" +
                                    "¿Reiniciar ahora? (guarda antes tu trabajo)", Text,
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    Sistema.Reiniciar();
                return;
            }

            Sistema.CancelarApertura();
            Medicion d = meds.Despues;
            DateTime? fechaDespues = d == null ? null : Estado.Fecha(d.FechaIso);
            if (!fechaDespues.HasValue || fechaDespues.Value < cambio.Value)
            {
                if (MessageBox.Show(this, "El equipo se ha reiniciado después de aplicar cambios en los servicios.\n\n" +
                                    "¿Medir ahora el rendimiento DESPUÉS para compararlo con el de ANTES?", Text,
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    tabs.SelectedIndex = 1;
                    await EsperarEstabilizacion();
                    await MedirAsync(false);
                }
            }
            return;
        }

        // Primera ejecución (sin medición ANTES y sin cambios aplicados):
        // se mide automáticamente el estado original del equipo.
        if (meds.Antes != null) return;
        if (Almacen.LeerBackup().Count > 0)
        {
            Log("No se ha medido el estado original: ya hay servicios modificados. Restablece los cambios para poder medirlo.");
            return;
        }
        tabs.SelectedIndex = 1;
        Log("Primera ejecución: midiendo el rendimiento actual (ANTES).");
        await EsperarEstabilizacion();
        await MedirAsync(true);
    }

    // Justo después de arrancar, Windows sigue cargando cosas en segundo plano;
    // medir en ese momento daría resultados peores de lo real.
    async Task EsperarEstabilizacion()
    {
        const int minimo = 120;
        if (Sistema.SegundosEncendido >= minimo) return;
        Ocupado(true, "");
        int restante;
        while ((restante = minimo - (int)Sistema.SegundosEncendido) > 0)
        {
            lblEstado.Text = "El equipo acaba de iniciarse. Esperando " + restante + " s a que se estabilice antes de medir...";
            await Task.Delay(1000);
        }
        Ocupado(false, "");
    }

    static Button Boton(string texto, EventHandler click)
    {
        var b = new Button { Text = texto, AutoSize = true, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(4) };
        b.Click += click;
        return b;
    }

    // --- Pestaña Servicios ---

    TabPage CrearTabServicios()
    {
        var tab = new TabPage("Servicios");

        lvServ = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
            HideSelection = false, ShowItemToolTips = true
        };
        lvServ.Columns.Add("Servicio", 215);
        lvServ.Columns.Add("Impacto", 65);
        lvServ.Columns.Add("Descripción", 360);
        lvServ.Columns.Add("Tipo de inicio", 115);
        lvServ.Columns.Add("Estado", 175);
        lvServ.Columns.Add("Cambiado", 75);
        grpRec = new ListViewGroup("Recomendados: seguros de desactivar en la mayoría de equipos");
        grpOpt = new ListViewGroup("Opcionales: desactívalos solo si no usas esa función");
        lvServ.Groups.Add(grpRec);
        lvServ.Groups.Add(grpOpt);

        var botones = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(2, 4, 2, 4) };
        var lblPerfil = new Label { Text = "Perfil:", AutoSize = true, Margin = new Padding(4, 9, 0, 4) };
        cmbPerfil = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 185, Margin = new Padding(4, 6, 0, 4) };
        cmbPerfil.Items.AddRange(Perfiles.Todos);
        cmbPerfil.SelectedIndex = 0;
        cmbPerfil.SelectedIndexChanged += (s, e) => lblEstado.Text = ((Perfil)cmbPerfil.SelectedItem).Desc;
        btnRec = Boton("Marcar", (s, e) => Marcar((Perfil)cmbPerfil.SelectedItem));
        btnNinguno = Boton("Desmarcar todo", (s, e) => Marcar(null));
        btnActualizar = Boton("Actualizar estado", (s, e) => RefrescarServicios());
        chkPunto = new CheckBox { Text = "Crear punto de restauración", Checked = true, AutoSize = true, Margin = new Padding(16, 9, 4, 4) };
        btnDesactivar = Boton("Desactivar marcados", BtnDesactivar_Click);
        btnDesactivar.Font = new Font(Font, FontStyle.Bold);
        btnRestaurar = Boton("Restablecer todos los cambios", BtnRestaurar_Click);
        botones.Controls.AddRange(new Control[] { lblPerfil, cmbPerfil, btnRec, btnNinguno, btnActualizar, chkPunto, btnDesactivar, btnRestaurar });

        txtLog = new TextBox
        {
            Dock = DockStyle.Bottom, Height = 90, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window, Font = new Font("Consolas", 9f)
        };

        var leyenda = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4, 4, 4, 0) };
        leyenda.Controls.Add(Muestra(FondoDeshabilitado, "Deshabilitado y detenido"));
        leyenda.Controls.Add(Muestra(FondoPendiente, "Deshabilitado pero aún en ejecución (se detendrá al reiniciar)"));
        leyenda.Controls.Add(Muestra(lvServ.BackColor, "No deshabilitado (Automático o Manual)"));

        tab.Controls.Add(lvServ);
        tab.Controls.Add(leyenda);
        tab.Controls.Add(botones);
        tab.Controls.Add(txtLog);
        return tab;
    }

    static Control Muestra(Color fondo, string texto)
    {
        return new Label
        {
            Text = texto, AutoSize = true, BackColor = fondo, BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6, 2, 6, 2), Margin = new Padding(4, 0, 8, 0)
        };
    }

    // Marca los servicios del perfil que aún no están deshabilitados (null = desmarcar todo).
    void Marcar(Perfil perfil)
    {
        foreach (ListViewItem it in lvServ.Items)
        {
            var s = (ServicioInfo)it.Tag;
            it.Checked = perfil != null && perfil.Incluye(s) && Servicios.GetStart(s.Nombre) != 4;
        }
    }

    void RefrescarServicios()
    {
        var marcados = new HashSet<string>(lvServ.CheckedItems.Cast<ListViewItem>().Select(i => ((ServicioInfo)i.Tag).Nombre));
        var backup = Almacen.LeerBackup();

        lvServ.BeginUpdate();
        lvServ.Items.Clear();
        Font negrita = new Font(lvServ.Font, FontStyle.Bold);
        foreach (ServicioInfo s in servicios)
        {
            int start = Servicios.GetStart(s.Nombre);
            bool deshabilitado = start == 4;
            bool enEjecucion = Servicios.EnEjecucion(s.Nombre);
            bool cambiado = backup.ContainsKey(s.Nombre);

            // El fondo de la fila resume el estado de un vistazo (ver leyenda).
            Color fondo = !deshabilitado ? lvServ.BackColor : enEjecucion ? FondoPendiente : FondoDeshabilitado;

            string inicio;
            Color colorInicio;
            if (deshabilitado) { inicio = "✖ Deshabilitado"; colorInicio = Verde; }
            else { inicio = Servicios.TipoInicio(start); colorInicio = lvServ.ForeColor; }

            string estado;
            Color colorEstado;
            if (enEjecucion && deshabilitado) { estado = "⚠ En ejecución (hasta reiniciar)"; colorEstado = Naranja; }
            else if (enEjecucion) { estado = "● En ejecución"; colorEstado = Azul; }
            else { estado = "○ Detenido"; colorEstado = SystemColors.GrayText; }

            var it = new ListViewItem(s.Nombre)
            {
                Tag = s, Group = s.Rec ? grpRec : grpOpt, UseItemStyleForSubItems = false,
                Checked = marcados.Contains(s.Nombre), BackColor = fondo,
                ToolTipText = cambiado ? "Modificado por el optimizador (se puede restablecer)" : ""
            };
            Color colorImpacto = s.Impacto == "Alto" ? Rojo : s.Impacto == "Medio" ? Naranja : SystemColors.GrayText;
            it.SubItems.Add(s.Impacto, colorImpacto, fondo, negrita);
            it.SubItems.Add(s.Desc, lvServ.ForeColor, fondo, lvServ.Font);
            it.SubItems.Add(inicio, colorInicio, fondo, deshabilitado ? negrita : lvServ.Font);
            it.SubItems.Add(estado, colorEstado, fondo, lvServ.Font);
            it.SubItems.Add(cambiado ? "✓ Sí" : "", Verde, fondo, lvServ.Font);
            lvServ.Items.Add(it);
        }
        lvServ.EndUpdate();

        btnRestaurar.Enabled = backup.Count > 0;
        btnRestaurar.Text = backup.Count > 0
            ? "Restablecer todos los cambios (" + backup.Count + ")"
            : "Restablecer todos los cambios";
    }

    void Log(string texto)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(Log), texto); return; }
        txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + texto + Environment.NewLine);
        Almacen.Log(texto);
    }

    void Ocupado(bool ocupado, string mensaje)
    {
        foreach (Button b in new[] { btnRec, btnNinguno, btnActualizar, btnDesactivar, btnRestaurar, btnAntes, btnDespues, btnBorrar, btnAnalizar })
            b.Enabled = !ocupado;
        if (ocupado) foreach (Button b in new[] { btnQuitarInicio, btnPonerInicio, btnDesinstalar, btnUbicacion }) b.Enabled = false;
        else ActualizarBotonesProgramas();
        chkPunto.Enabled = cmbDur.Enabled = cmbPerfil.Enabled = lvServ.Enabled = !ocupado;
        UseWaitCursor = ocupado;
        lblEstado.Text = mensaje;
    }

    async void BtnDesactivar_Click(object sender, EventArgs e)
    {
        List<string> sel = lvServ.CheckedItems.Cast<ListViewItem>().Select(i => ((ServicioInfo)i.Tag).Nombre).ToList();
        if (sel.Count == 0)
        {
            MessageBox.Show(this, "Marca al menos un servicio en la lista.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, "Se desactivarán " + sel.Count + " servicios:\n\n" + string.Join(", ", sel) + "\n\n¿Continuar?",
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        bool punto = chkPunto.Checked;
        await AplicarCambio("Desactivando servicios...", () =>
        {
            if (punto) Operaciones.CrearPuntoRestauracion(Log);
            foreach (string n in sel) Operaciones.Desactivar(n, Log);
        });
        foreach (ListViewItem it in lvServ.Items) it.Checked = false;
    }

    async void BtnRestaurar_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show(this, "¿Restablecer TODOS los servicios a su estado original?", Text,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        await AplicarCambio("Restableciendo servicios...", () => Operaciones.Restaurar(Log));
    }

    async Task AplicarCambio(string mensaje, Action trabajo)
    {
        if (!await OfrecerMedicionAntes()) return;

        Ocupado(true, mensaje);
        await Task.Run(trabajo);
        Almacen.GuardarEstado(new Estado { UltimoCambio = DateTime.Now.ToString("o") });
        bool programada = await Task.Run(() => Sistema.ProgramarApertura(Log));
        Ocupado(false, "Cambios aplicados. Reinicia el equipo para que tengan efecto completo.");
        RefrescarServicios();

        if (MessageBox.Show(this, "Cambios aplicados. Reinicia el equipo para que tengan efecto completo.\n\n" +
                            (programada
                                ? "Después del reinicio, el optimizador se abrirá solo para medir el rendimiento DESPUÉS.\n\n"
                                : "Después del reinicio, vuelve a abrir el optimizador para medir el rendimiento DESPUÉS.\n\n") +
                            "¿Reiniciar ahora? (guarda antes tu trabajo)", Text,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Sistema.Reiniciar();
    }

    // Devuelve false si el usuario cancela la operación.
    async Task<bool> OfrecerMedicionAntes()
    {
        Mediciones meds = Almacen.LeerMediciones();
        DateTime? cambio = Estado.Fecha(Almacen.LeerEstado().UltimoCambio);

        // Con cambios anteriores aún sin reiniciar, una medición ahora no reflejaría ningún estado real.
        if (cambio.HasValue && Sistema.Arranque < cambio.Value) return true;

        DateTime? fechaAntes = meds.Antes == null ? null : Estado.Fecha(meds.Antes.FechaIso);
        string motivo;
        if (meds.Antes == null)
            motivo = "Todavía no has medido el rendimiento ANTES de los cambios.";
        else if (cambio.HasValue && fechaAntes.HasValue && fechaAntes.Value < cambio.Value)
            motivo = "La medición ANTES es anterior al último cambio, así que ya no refleja el estado actual del equipo.";
        else
            return true;

        DialogResult r = MessageBox.Show(this, motivo + "\n\nSe recomienda medir ahora (" + Duracion() + " s) para poder comparar después del reinicio.\n\n" +
                                         "Sí: medir y después aplicar los cambios\nNo: aplicar los cambios sin medir\nCancelar: no hacer nada",
                                         Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.No) return true;

        int pestana = tabs.SelectedIndex;
        tabs.SelectedIndex = 1;
        bool ok = await MedirAsync(true);
        tabs.SelectedIndex = pestana;
        return ok;
    }

    // --- Pestaña Análisis ---

    TabPage CrearTabAnalisis()
    {
        var tab = new TabPage("Análisis");

        var barra = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2, 4, 2, 4) };
        barra.Controls.Add(new Label { Text = "Duración:", AutoSize = true, Margin = new Padding(4, 9, 2, 4) });
        cmbDur = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(2, 6, 8, 4) };
        cmbDur.Items.AddRange(new object[] { "30 s", "60 s", "120 s" });
        cmbDur.SelectedIndex = 1;
        btnAntes = Boton("Medir ANTES", (s, e) => Medir(true));
        btnDespues = Boton("Medir DESPUÉS", (s, e) => Medir(false));
        btnAntes.Font = btnDespues.Font = new Font(Font, FontStyle.Bold);
        btnBorrar = Boton("Borrar mediciones", BtnBorrar_Click);
        prog = new ProgressBar { Width = 180, Height = 20, Margin = new Padding(12, 8, 4, 4), Visible = false };
        barra.Controls.AddRange(new Control[] { cmbDur, btnAntes, btnDespues, btnBorrar, prog });

        lvComp = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = new Font("Segoe UI", 10f)
        };
        lvComp.Columns.Add("Métrica", 260);
        lvComp.Columns.Add("Antes", 170, HorizontalAlignment.Right);
        lvComp.Columns.Add("Después", 170, HorizontalAlignment.Right);
        lvComp.Columns.Add("Cambio", 200, HorizontalAlignment.Right);

        var ayuda = new Label
        {
            Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(8, 6, 8, 6), ForeColor = SystemColors.GrayText,
            Text = "Cómo obtener una comparación fiable:\n" +
                   "1. Reinicia el equipo (usa «Reiniciar», no «Apagar»), espera 2-3 minutos sin abrir programas y pulsa «Medir ANTES».\n" +
                   "2. Desactiva los servicios en la pestaña Servicios y vuelve a reiniciar.\n" +
                   "3. Espera de nuevo 2-3 minutos sin abrir programas y pulsa «Medir DESPUÉS».\n" +
                   "Verde = mejora, rojo = empeora, gris = sin cambio apreciable. El tiempo de arranque lo registra Windows unos minutos después de iniciar."
        };

        tab.Controls.Add(lvComp);
        tab.Controls.Add(barra);
        tab.Controls.Add(ayuda);
        return tab;
    }

    int Duracion() { return new[] { 30, 60, 120 }[cmbDur.SelectedIndex]; }

    async void Medir(bool antes) { await MedirAsync(antes); }

    async Task<bool> MedirAsync(bool antes)
    {
        int segundos = Duracion();
        Ocupado(true, "Midiendo durante " + segundos + " s... no uses el equipo mientras tanto.");
        prog.Value = 0;
        prog.Visible = true;
        var progreso = new Progress<int>(p => prog.Value = p);
        bool ok = true;
        try
        {
            Medicion m = await Task.Run(() => Medidor.Medir(segundos, progreso));
            Mediciones meds = Almacen.LeerMediciones();
            if (antes)
            {
                meds.Antes = m;
                meds.Despues = null;    // una nueva medición ANTES empieza una comparación nueva
            }
            else meds.Despues = m;
            Almacen.GuardarMediciones(meds);
            Ocupado(false, "Medición " + (antes ? "ANTES" : "DESPUÉS") + " guardada.");
        }
        catch (Exception ex)
        {
            ok = false;
            Ocupado(false, "Error en la medición.");
            MessageBox.Show(this, "Error durante la medición: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        prog.Visible = false;
        RefrescarServicios();
        RefrescarComparacion();
        return ok;
    }

    void BtnBorrar_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show(this, "¿Borrar las mediciones guardadas?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Almacen.GuardarMediciones(new Mediciones());
        RefrescarComparacion();
    }

    // --- Pestaña Programas ---

    const int SegundosConsumo = 20;

    TabPage CrearTabProgramas()
    {
        var tab = new TabPage("Programas");

        var barra = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2, 4, 2, 4) };
        btnAnalizar = Boton("Analizar programas (" + SegundosConsumo + " s)", BtnAnalizar_Click);
        btnAnalizar.Font = new Font(Font, FontStyle.Bold);
        progProg = new ProgressBar { Width = 180, Height = 20, Margin = new Padding(12, 8, 4, 4), Visible = false };
        lblProgramas = new Label
        {
            AutoSize = true, Margin = new Padding(12, 9, 4, 4), ForeColor = SystemColors.GrayText,
            Text = "Detecta los programas que se inician con Windows y los que más consumen mientras usas el equipo."
        };
        var leyenda = Muestra(FondoPendiente, "Consumo alto o retrasa el arranque");
        leyenda.Margin = new Padding(12, 8, 4, 4);
        barra.Controls.AddRange(new Control[] { btnAnalizar, progProg, leyenda, lblProgramas });

        lvInicio = ListaProgramas();
        lvInicio.Columns.Add("Programa", 200);
        lvInicio.Columns.Add("Impacto en el arranque", 230);
        lvInicio.Columns.Add("Estado", 110);
        lvInicio.Columns.Add("Origen", 230);
        lvInicio.Columns.Add("Programa instalado", 220);
        lvInicio.Columns.Add("Comando", 500);

        lvConsumo = ListaProgramas();
        lvConsumo.Columns.Add("Programa", 230);
        lvConsumo.Columns.Add("CPU media", 80, HorizontalAlignment.Right);
        lvConsumo.Columns.Add("RAM", 80, HorizontalAlignment.Right);
        lvConsumo.Columns.Add("Disco", 90, HorizontalAlignment.Right);
        lvConsumo.Columns.Add("Procesos", 70, HorizontalAlignment.Right);
        lvConsumo.Columns.Add("Programa instalado", 230);
        lvConsumo.Columns.Add("Ejecutable", 500);

        var grpInicio = new GroupBox { Text = "Programas que se inician con Windows", Dock = DockStyle.Fill, Padding = new Padding(6) };
        grpInicio.Controls.Add(lvInicio);
        var grpConsumo = new GroupBox { Text = "Consumo en uso normal (programas de terceros; no se muestran los componentes de Windows)", Dock = DockStyle.Fill, Padding = new Padding(6) };
        grpConsumo.Controls.Add(lvConsumo);
        var division = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        division.Panel1.Controls.Add(grpInicio);
        division.Panel2.Controls.Add(grpConsumo);
        division.HandleCreated += (s, e) => division.SplitterDistance = division.Height / 2;

        var botones = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(2, 4, 2, 4) };
        btnQuitarInicio = Boton("Desactivar del inicio", (s, e) => CambiarInicio(false));
        btnPonerInicio = Boton("Activar en el inicio", (s, e) => CambiarInicio(true));
        btnDesinstalar = Boton("Desinstalar programa…", BtnDesinstalar_Click);
        btnUbicacion = Boton("Abrir ubicación", BtnUbicacion_Click);
        var btnApps = Boton("Aplicaciones y características", (s, e) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true }); } catch { }
        });
        // Panel clásico de desinstalación (también lista programas que la configuración moderna agrupa).
        var btnPanel = Boton("Programas y características", (s, e) =>
        {
            try { Process.Start(new ProcessStartInfo("control.exe", "appwiz.cpl") { UseShellExecute = true }); } catch { }
        });
        botones.Controls.AddRange(new Control[] { btnQuitarInicio, btnPonerInicio, btnDesinstalar, btnUbicacion, btnPanel, btnApps });

        tab.Controls.Add(division);
        tab.Controls.Add(barra);
        tab.Controls.Add(botones);
        ActualizarBotonesProgramas();
        return tab;
    }

    ListView ListaProgramas()
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            MultiSelect = false, ShowItemToolTips = true
        };
        lv.Enter += (s, e) => { lvProgActiva = lv; ActualizarBotonesProgramas(); };
        lv.SelectedIndexChanged += (s, e) => { lvProgActiva = lv; ActualizarBotonesProgramas(); };
        return lv;
    }

    object ElementoProgramas()
    {
        if (lvProgActiva == null || lvProgActiva.SelectedItems.Count == 0) return null;
        return lvProgActiva.SelectedItems[0].Tag;
    }

    // Las aplicaciones de la Tienda (WindowsApps) no aparecen en el registro de desinstalación.
    static string TextoPrograma(ProgramaInstalado p, string exe)
    {
        if (p != null) return p.Nombre;
        if (exe != null && exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Aplicación de la Tienda (ver «Aplicaciones y características»)";
        return "No identificado";
    }

    static ProgramaInstalado ProgramaDe(object o)
    {
        var e = o as EntradaInicio;
        if (e != null) return e.Programa;
        var c = o as ConsumoPrograma;
        return c == null ? null : c.Programa;
    }

    void ActualizarBotonesProgramas()
    {
        if (btnQuitarInicio == null) return;
        object o = ElementoProgramas();
        var e = o as EntradaInicio;
        btnQuitarInicio.Enabled = e != null && e.Conmutable && e.Habilitada;
        btnPonerInicio.Enabled = e != null && e.Conmutable && !e.Habilitada;
        btnDesinstalar.Enabled = ProgramaDe(o) != null;
        string exe = e != null ? e.Exe : o is ConsumoPrograma ? ((ConsumoPrograma)o).Exe : null;
        btnUbicacion.Enabled = exe != null && File.Exists(exe);
    }

    async void BtnAnalizar_Click(object sender, EventArgs e)
    {
        Ocupado(true, "Analizando programas durante " + SegundosConsumo + " s... usa el equipo con normalidad.");
        progProg.Value = 0;
        progProg.Visible = true;
        var progreso = new Progress<int>(p => progProg.Value = p);
        try
        {
            List<ProgramaInstalado> programas = null;
            List<EntradaInicio> inicio = await Task.Run(() =>
            {
                programas = ProgramasInstalados.Leer();
                return Inicio.Leer(programas);
            });
            MostrarInicio(inicio);
            List<ConsumoPrograma> consumo = await Task.Run(() => Consumo.Medir(SegundosConsumo, programas, progreso));
            MostrarConsumo(consumo);
            int lentos = inicio.Count(x => x.Retrasos > 0 && x.Habilitada) + consumo.Count(x => x.Alto);
            lblProgramas.Text = inicio.Count + " programas al inicio, " + consumo.Count + " programas en ejecución. " +
                                (lentos > 0 ? lentos + " destacados en naranja." : "Ninguno destaca por su consumo.");
            Ocupado(false, "Análisis de programas terminado.");
        }
        catch (Exception ex)
        {
            Ocupado(false, "Error en el análisis de programas.");
            MessageBox.Show(this, "Error durante el análisis: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        progProg.Visible = false;
    }

    void MostrarInicio(List<EntradaInicio> entradas)
    {
        lvInicio.BeginUpdate();
        lvInicio.Items.Clear();
        foreach (EntradaInicio e in entradas)
        {
            bool destacar = e.Retrasos > 0 && e.Habilitada;
            Color fondo = destacar ? FondoPendiente : lvInicio.BackColor;
            Color texto = e.Habilitada ? lvInicio.ForeColor : SystemColors.GrayText;
            var it = new ListViewItem(e.Nombre) { Tag = e, BackColor = fondo, ForeColor = texto, ToolTipText = e.Comando };
            it.SubItems.Add(e.Retrasos > 0
                ? "Retrasó el arranque " + e.Retrasos + (e.Retrasos == 1 ? " vez" : " veces") + " (hasta " + e.RetrasoMaxSeg.ToString("F1") + " s)"
                : "Sin retrasos registrados");
            it.SubItems.Add(!e.Conmutable ? "—" : e.Habilitada ? "Activado" : "Desactivado");
            it.SubItems.Add(e.Origen);
            it.SubItems.Add(TextoPrograma(e.Programa, e.Exe));
            it.SubItems.Add(e.Comando);
            lvInicio.Items.Add(it);
        }
        lvInicio.EndUpdate();
    }

    void MostrarConsumo(List<ConsumoPrograma> consumo)
    {
        lvConsumo.BeginUpdate();
        lvConsumo.Items.Clear();
        foreach (ConsumoPrograma c in consumo)
        {
            var it = new ListViewItem(c.Nombre) { Tag = c, BackColor = c.Alto ? FondoPendiente : lvConsumo.BackColor, ToolTipText = c.Exe };
            it.SubItems.Add(c.CpuPct.ToString("F1") + " %");
            it.SubItems.Add(c.RamMB.ToString("F0") + " MB");
            it.SubItems.Add(c.DiscoKBs >= 1024 ? (c.DiscoKBs / 1024).ToString("F1") + " MB/s" : c.DiscoKBs.ToString("F0") + " KB/s");
            it.SubItems.Add(c.Procesos.ToString());
            it.SubItems.Add(TextoPrograma(c.Programa, c.Exe));
            it.SubItems.Add(c.Exe);
            lvConsumo.Items.Add(it);
        }
        lvConsumo.EndUpdate();
    }

    async void CambiarInicio(bool habilitar)
    {
        var e = ElementoProgramas() as EntradaInicio;
        if (e == null) return;
        if (!habilitar && MessageBox.Show(this, "«" + e.Nombre + "» dejará de iniciarse con Windows. El programa no se desinstala y " +
                                          "puedes volver a activarlo aquí o en el Administrador de tareas.\n\n¿Continuar?", Text,
                                          MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        await AplicarCambio(habilitar ? "Activando en el inicio..." : "Desactivando del inicio...", () =>
        {
            try
            {
                Inicio.Cambiar(e, habilitar);
                Log((habilitar ? "Activado en el inicio: " : "Desactivado del inicio: ") + e.Nombre + " (" + e.Origen + ")");
            }
            catch (Exception ex) { Log("ERROR al cambiar el inicio de " + e.Nombre + ": " + ex.Message); }
        });
        foreach (ListViewItem it in lvInicio.Items)
            if (it.Tag == e)
            {
                it.SubItems[2].Text = e.Habilitada ? "Activado" : "Desactivado";
                it.ForeColor = e.Habilitada ? lvInicio.ForeColor : SystemColors.GrayText;
                it.BackColor = e.Retrasos > 0 && e.Habilitada ? FondoPendiente : lvInicio.BackColor;
            }
        ActualizarBotonesProgramas();
    }

    void BtnDesinstalar_Click(object sender, EventArgs ev)
    {
        ProgramaInstalado p = ProgramaDe(ElementoProgramas());
        if (p == null) return;
        string aviso = ProgramasInstalados.EsDelicado(p)
            ? "\n\nATENCIÓN: parece un antivirus, un controlador o un componente del fabricante del equipo. " +
              "Desinstalarlo puede dejar el equipo sin protección o hacer que deje de funcionar algún dispositivo."
            : "";
        if (MessageBox.Show(this, "Se abrirá el desinstalador de:\n\n" + p.Nombre + (p.Version.Length > 0 ? " " + p.Version : "") +
                            (p.Editor.Length > 0 ? "\n" + p.Editor : "") + aviso +
                            "\n\nLa desinstalación no se puede deshacer desde el optimizador. ¿Continuar?", Text,
                            MessageBoxButtons.YesNo, aviso.Length > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            ProgramasInstalados.LanzarDesinstalador(p);
            Log("Desinstalador iniciado: " + p.Nombre + " (" + p.Desinstalar + ")");
            lblEstado.Text = "Sigue las instrucciones del desinstalador. Después pulsa «Analizar programas» para actualizar la lista.";
        }
        catch (Exception ex)
        {
            Log("ERROR al iniciar el desinstalador de " + p.Nombre + ": " + ex.Message);
            MessageBox.Show(this, "No se pudo iniciar el desinstalador: " + ex.Message + "\n\nPuedes desinstalarlo desde «Aplicaciones y características».",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void BtnUbicacion_Click(object sender, EventArgs ev)
    {
        object o = ElementoProgramas();
        string exe = o is EntradaInicio ? ((EntradaInicio)o).Exe : o is ConsumoPrograma ? ((ConsumoPrograma)o).Exe : null;
        if (exe != null && File.Exists(exe)) Process.Start("explorer.exe", "/select,\"" + exe + "\"");
    }

    static string Formatear(double v, Metrica met)
    {
        return (v.ToString(met.Formato) + " " + met.Unidad).Trim();
    }

    void AgregarFila(string metrica, string antes, string despues, string cambio, Color color)
    {
        var it = new ListViewItem(metrica) { UseItemStyleForSubItems = false };
        it.SubItems.Add(antes);
        it.SubItems.Add(despues);
        it.SubItems.Add(cambio, color, lvComp.BackColor, new Font(lvComp.Font, FontStyle.Bold));
        lvComp.Items.Add(it);
    }

    void RefrescarComparacion()
    {
        Mediciones meds = Almacen.LeerMediciones();
        Medicion a = meds.Antes, d = meds.Despues;

        lvComp.BeginUpdate();
        lvComp.Items.Clear();
        AgregarFila("Fecha de la medición", a == null ? "—" : a.Fecha, d == null ? "—" : d.Fecha, "", lvComp.ForeColor);

        foreach (Metrica met in Metricas)
        {
            double va = a == null ? -1 : met.Valor(a);
            double vd = d == null ? -1 : met.Valor(d);
            string cambio = "";
            Color color = lvComp.ForeColor;
            if (va >= 0 && vd >= 0)
            {
                double diff = vd - va;
                double pct = va != 0 ? diff / va * 100 : 0;
                cambio = (diff > 0 ? "+" : "") + Formatear(diff, met) + (va != 0 ? "  (" + (pct > 0 ? "+" : "") + pct.ToString("F0") + "%)" : "");
                color = Math.Abs(pct) < 3 ? SystemColors.GrayText : diff < 0 ? Verde : Rojo;
            }
            AgregarFila(met.Nombre,
                        a == null ? "—" : va < 0 ? "n/d" : Formatear(va, met),
                        d == null ? "—" : vd < 0 ? "n/d" : Formatear(vd, met),
                        cambio, color);
        }

        if (a != null && d != null && a.ArranqueFecha != null && a.ArranqueFecha == d.ArranqueFecha)
            AgregarFila("Aviso: ambas mediciones usan el mismo arranque", "", "", "Reinicia y repite DESPUÉS", Naranja);
        else if (a != null && a.ArranqueFecha != null || d != null && d.ArranqueFecha != null)
            AgregarFila("Arranque registrado el", a == null || a.ArranqueFecha == null ? "—" : a.ArranqueFecha,
                        d == null || d.ArranqueFecha == null ? "—" : d.ArranqueFecha, "", lvComp.ForeColor);
        lvComp.EndUpdate();
    }
}

// --- Línea de comandos (despliegue en varios equipos) ---------------------------

static class Cli
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int pid);

    static string logExtra;
    static int errores;

    const string TextoAyuda =
@"Optimizador de Servicios - modo línea de comandos

Uso (desde una consola de administrador, un script de inicio de GPO, Intune, etc.):
  OptimizadorServicios.exe /aplicar:<perfil | archivo.txt | Serv1,Serv2> [/punto] [/reiniciar] [/log:<ruta>]
  OptimizadorServicios.exe /restablecer [/reiniciar] [/log:<ruta>]
  OptimizadorServicios.exe /estado [/log:<ruta>]
  OptimizadorServicios.exe /perfiles
  OptimizadorServicios.exe /ayuda

  /aplicar      Deshabilita los servicios de un perfil, de un archivo de texto
                (un nombre por línea, # para comentarios) o de una lista separada por comas.
                Solo se admiten servicios del catálogo. Se puede ejecutar en cada
                arranque: los que ya están deshabilitados se omiten.
  /restablecer  Devuelve todos los servicios modificados a su estado original.
  /estado       Muestra el estado de los servicios del catálogo (útil para inventario).
  /punto        Crea un punto de restauración antes de aplicar.
  /reiniciar    Reinicia el equipo en 60 s si se ha cambiado algo.
  /log:<ruta>   Copia el registro a otro archivo (admite %COMPUTERNAME%, rutas de red...).

Códigos de salida: 0 = correcto, 1 = algún servicio falló, 2 = argumentos no válidos.
Desde cmd usa 'start """" /wait OptimizadorServicios.exe ...' para esperar y obtener el código.";

    static void Salida(string texto)
    {
        Console.WriteLine(texto);
        Almacen.Log(texto);
        if (texto.StartsWith("ERROR")) errores++;
        if (logExtra == null) return;
        try
        {
            File.AppendAllText(logExtra, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " +
                               Environment.MachineName + "  " + texto + Environment.NewLine);
        }
        catch { }
    }

    public static int Ejecutar(string[] args)
    {
        AttachConsole(-1);   // escribe en la consola desde la que se lanzó, si la hay
        Console.WriteLine();

        string accion = null, valor = null;
        bool punto = false, reiniciar = false;
        foreach (string a in args)
        {
            string arg = a.TrimStart('/', '-');
            int dosPuntos = arg.IndexOf(':');
            string clave = dosPuntos < 0 ? arg : arg.Substring(0, dosPuntos);
            string val = dosPuntos < 0 ? null : arg.Substring(dosPuntos + 1).Trim('"');
            switch (clave.ToLowerInvariant())
            {
                case "aplicar": accion = "aplicar"; valor = val; break;
                case "restablecer": accion = "restablecer"; break;
                case "estado": accion = "estado"; break;
                case "perfiles": accion = "perfiles"; break;
                case "ayuda": case "?": case "h": accion = "ayuda"; break;
                case "punto": punto = true; break;
                case "reiniciar": reiniciar = true; break;
                case "log":
                    if (string.IsNullOrEmpty(val)) return Error("Falta la ruta en /log:<ruta>");
                    logExtra = Environment.ExpandEnvironmentVariables(val);
                    break;
                default: return Error("Argumento no reconocido: " + a);
            }
        }

        switch (accion)
        {
            case "aplicar": return Aplicar(valor, punto, reiniciar);
            case "restablecer": return Restablecer(reiniciar);
            case "estado": return MostrarEstado();
            case "perfiles": return MostrarPerfiles();
            case "ayuda": Console.WriteLine(TextoAyuda); return 0;
            default: return Error("Indica una acción: /aplicar, /restablecer, /estado o /perfiles");
        }
    }

    static int Error(string texto)
    {
        Console.WriteLine(texto);
        Console.WriteLine();
        Console.WriteLine(TextoAyuda);
        return 2;
    }

    // Convierte el valor de /aplicar en nombres del catálogo que existen en este equipo.
    static List<string> Resolver(string valor)
    {
        List<ServicioInfo> existentes = Catalogo.Todos.Where(s => Servicios.Existe(s.Nombre)).ToList();
        Perfil perfil = Perfiles.Buscar(valor);
        if (perfil != null)
        {
            Salida("Perfil: " + perfil.Nombre);
            return existentes.Where(perfil.Incluye).Select(s => s.Nombre).ToList();
        }

        IEnumerable<string> pedidos;
        if (File.Exists(valor))
        {
            Salida("Archivo de servicios: " + valor);
            pedidos = File.ReadAllLines(valor).Select(l => l.Split('#')[0].Trim());
        }
        else pedidos = valor.Split(',').Select(n => n.Trim());

        var res = new List<string>();
        foreach (string n in pedidos.Where(n => n.Length > 0))
        {
            ServicioInfo s = existentes.FirstOrDefault(x => string.Equals(x.Nombre, n, StringComparison.OrdinalIgnoreCase));
            if (s == null) Salida("Omitido (no está en el catálogo o no existe en este equipo): " + n);
            else if (!res.Contains(s.Nombre)) res.Add(s.Nombre);
        }
        return res;
    }

    static int Aplicar(string valor, bool punto, bool reiniciar)
    {
        if (string.IsNullOrEmpty(valor)) return Error("Falta el perfil o la lista en /aplicar:<...>");
        List<string> nombres = Resolver(valor);
        if (nombres.Count == 0) { Salida("No hay servicios que aplicar."); return 0; }

        var backup = Almacen.LeerBackup();
        List<string> pendientes = nombres.Where(n => !(Servicios.GetStart(n) == 4 && backup.ContainsKey(n))).ToList();
        foreach (string n in nombres.Except(pendientes)) Salida("Ya deshabilitado: " + n);
        if (pendientes.Count == 0) { Salida("Sin cambios: todo estaba ya aplicado."); return 0; }

        if (punto) Operaciones.CrearPuntoRestauracion(Salida);
        foreach (string n in pendientes) Operaciones.Desactivar(n, Salida);
        Almacen.GuardarEstado(new Estado { UltimoCambio = DateTime.Now.ToString("o") });
        Salida("Aplicado: " + (pendientes.Count - errores) + " servicios deshabilitados, " + errores + " errores.");

        if (reiniciar) { Salida("Reiniciando el equipo en 60 s."); Sistema.Reiniciar(60); }
        return errores > 0 ? 1 : 0;
    }

    static int Restablecer(bool reiniciar)
    {
        int cambiados = Almacen.LeerBackup().Count;
        if (cambiados == 0) { Salida("No hay cambios que restablecer."); return 0; }
        Operaciones.Restaurar(Salida);
        Almacen.GuardarEstado(new Estado { UltimoCambio = DateTime.Now.ToString("o") });
        Salida("Restablecido: " + (cambiados - errores) + " servicios, " + errores + " errores.");

        if (reiniciar) { Salida("Reiniciando el equipo en 60 s."); Sistema.Reiniciar(60); }
        return errores > 0 ? 1 : 0;
    }

    static int MostrarEstado()
    {
        var backup = Almacen.LeerBackup();
        Salida(string.Format("{0,-42} {1,-14} {2,-14} {3}", "SERVICIO", "INICIO", "ESTADO", "CAMBIADO"));
        foreach (ServicioInfo s in Catalogo.Todos.Where(x => Servicios.Existe(x.Nombre)))
            Salida(string.Format("{0,-42} {1,-14} {2,-14} {3}", s.Nombre,
                                 Servicios.TipoInicio(Servicios.GetStart(s.Nombre)),
                                 Servicios.EnEjecucion(s.Nombre) ? "En ejecución" : "Detenido",
                                 backup.ContainsKey(s.Nombre) ? "Sí" : ""));
        return 0;
    }

    static int MostrarPerfiles()
    {
        foreach (Perfil p in Perfiles.Todos)
        {
            Console.WriteLine(p.Id + "  -  " + p.Nombre + ": " + p.Desc);
            Console.WriteLine("    " + string.Join(", ", Catalogo.Todos.Where(p.Incluye).Select(s => s.Nombre)));
            Console.WriteLine();
        }
        return 0;
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0) return Cli.Ejecutar(args);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
