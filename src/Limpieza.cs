// Optimizador de Servicios - limpieza de disco: temporales, restos de Windows Update,
// volcados de memoria, informes de errores, cachés y papelera. Primero se mide cuánto
// ocupa cada categoría; solo se borra lo que el usuario marca. Los archivos en uso se omiten.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

class CategoriaLimpieza
{
    public string Id, Nombre, Desc, Riesgo;
    public bool Recomendada = true;
    public bool Lenta;                       // DISM: tamaño desconocido y puede tardar minutos
    public Func<long> Medir;                 // bytes (-1 = desconocido)
    public Func<Action<string>, long> Limpiar;   // devuelve los bytes liberados
}

static class Limpieza
{
    static string Env(string v) { return Environment.ExpandEnvironmentVariables(v); }
    static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    // Carpeta a limpiar: se borra su contenido (no la carpeta), los archivos que cumplen el patrón
    // y tienen al menos la antigüedad indicada.
    class Destino
    {
        public string Carpeta, Patron = "*";
        public double HorasMinimas;
        public bool Recursivo = true;
        public Destino(string carpeta) { Carpeta = carpeta; }
    }

    static CategoriaLimpieza Carpetas(string id, string nombre, string desc, string riesgo, Func<IEnumerable<Destino>> destinos)
    {
        return new CategoriaLimpieza
        {
            Id = id, Nombre = nombre, Desc = desc, Riesgo = riesgo,
            Medir = () => destinos().Sum(d => Archivos(d).Sum(f => Tam(f))),
            Limpiar = log =>
            {
                long liberado = 0;
                int fallos = 0;
                foreach (Destino d in destinos())
                {
                    foreach (string f in Archivos(d).ToList())
                    {
                        long t = Tam(f);
                        try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); liberado += t; }
                        catch { fallos++; }     // en uso o protegido: se deja
                    }
                    if (d.Recursivo) BorrarCarpetasVacias(d.Carpeta);
                }
                log("Limpieza «" + nombre + "»: " + Formato(liberado) + " liberados" + (fallos > 0 ? " (" + fallos + " archivos en uso omitidos)" : "") + ".");
                return liberado;
            }
        };
    }

    // Recorre las carpetas sin seguir enlaces (puntos de reanálisis) y sin detenerse por errores de acceso.
    static IEnumerable<string> Archivos(Destino d)
    {
        if (!Directory.Exists(d.Carpeta)) yield break;
        DateTime limite = DateTime.Now.AddHours(-d.HorasMinimas);
        var pendientes = new Stack<string>();
        pendientes.Push(d.Carpeta);
        while (pendientes.Count > 0)
        {
            string dir = pendientes.Pop();
            string[] archivos = new string[0], subdirs = new string[0];
            try { archivos = Directory.GetFiles(dir, d.Patron); } catch { }
            if (d.Recursivo) try { subdirs = Directory.GetDirectories(dir); } catch { }
            foreach (string f in archivos)
            {
                bool vale;
                try { vale = d.HorasMinimas <= 0 || File.GetLastWriteTime(f) < limite; } catch { vale = false; }
                if (vale) yield return f;
            }
            foreach (string s in subdirs)
            {
                try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                pendientes.Push(s);
            }
        }
    }

    static long Tam(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }

    static void BorrarCarpetasVacias(string raiz)
    {
        string[] subdirs;
        try { subdirs = Directory.GetDirectories(raiz); } catch { return; }
        foreach (string s in subdirs)
        {
            try
            {
                if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue;
                BorrarCarpetasVacias(s);
                if (!Directory.EnumerateFileSystemEntries(s).Any()) Directory.Delete(s);
            }
            catch { }
        }
    }

    public static string Formato(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("F1") + " GB";
        if (bytes >= 1048576) return (bytes / 1048576.0).ToString("F0") + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("F0") + " KB";
        return bytes + " B";
    }

    // Perfiles de navegadores basados en Chromium: carpetas de caché de cada perfil.
    static IEnumerable<Destino> CachesChromium(string datosUsuario)
    {
        string raiz = Path.Combine(Local, datosUsuario);
        if (!Directory.Exists(raiz)) yield break;
        string[] perfiles;
        try { perfiles = Directory.GetDirectories(raiz); } catch { yield break; }
        foreach (string p in perfiles)
            foreach (string c in new[] { "Cache", "Code Cache", "GPUCache" })
                if (Directory.Exists(Path.Combine(p, c))) yield return new Destino(Path.Combine(p, c));
    }

    // --- Papelera de reciclaje ---

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string raiz, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr ventana, string raiz, uint opciones);

    static long TamPapelera()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : -1;
    }

    // --- DISM: limpieza del almacén de componentes ---

    static long LibreSistema()
    {
        try { return new DriveInfo(Path.GetPathRoot(Windows)).AvailableFreeSpace; } catch { return 0; }
    }

    public static readonly List<CategoriaLimpieza> Todas = new List<CategoriaLimpieza>
    {
        Carpetas("temp", "Archivos temporales del usuario",
                 "Archivos temporales de los programas con más de 24 horas de antigüedad.", "Seguro",
                 () => new[] { new Destino(Path.GetTempPath()) { HorasMinimas = 24 } }),
        Carpetas("wintemp", "Archivos temporales de Windows",
                 "Carpeta Windows\\Temp, con más de 24 horas de antigüedad.", "Seguro",
                 () => new[] { new Destino(Path.Combine(Windows, "Temp")) { HorasMinimas = 24 } }),
        Carpetas("update", "Descargas de Windows Update",
                 "Actualizaciones ya descargadas e instaladas. Windows vuelve a descargar lo que necesite.", "Seguro",
                 () => new[] { new Destino(Path.Combine(Windows, @"SoftwareDistribution\Download")) }),
        Carpetas("entrega", "Caché de optimización de distribución",
                 "Fragmentos de actualizaciones guardados para compartirlos con otros equipos.", "Seguro",
                 () => new[] { new Destino(Path.Combine(Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")) }),
        Carpetas("volcados", "Volcados de memoria",
                 "Archivos que Windows guarda tras un error grave (pantalla azul) o el cierre inesperado de un programa.", "Seguro",
                 () => new[]
                 {
                     new Destino(Windows) { Patron = "MEMORY.DMP", Recursivo = false },
                     new Destino(Path.Combine(Windows, "Minidump")),
                     new Destino(Path.Combine(Windows, "LiveKernelReports")) { Patron = "*.dmp" },
                     new Destino(Path.Combine(Local, "CrashDumps")),
                 }),
        Carpetas("informes", "Informes de errores de Windows",
                 "Informes de errores ya enviados o pendientes de enviar.", "Seguro",
                 () => new[]
                 {
                     new Destino(Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive")),
                     new Destino(Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue")),
                     new Destino(Path.Combine(Local, @"Microsoft\Windows\WER")),
                 }),
        Carpetas("miniaturas", "Caché de miniaturas",
                 "Miniaturas de imágenes y vídeos del Explorador. Se vuelven a generar al abrir las carpetas.", "Seguro",
                 () => new[] { new Destino(Path.Combine(Local, @"Microsoft\Windows\Explorer")) { Patron = "thumbcache_*.db", Recursivo = false } }),
        Carpetas("navegadores", "Caché de los navegadores",
                 "Archivos temporales de Edge, Chrome y Firefox. Cierra los navegadores antes para liberar más espacio.", "Seguro",
                 () => CachesChromium(@"Microsoft\Edge\User Data")
                       .Concat(CachesChromium(@"Google\Chrome\User Data"))
                       .Concat(Directory.Exists(Path.Combine(Local, @"Mozilla\Firefox\Profiles"))
                               ? Directory.GetDirectories(Path.Combine(Local, @"Mozilla\Firefox\Profiles")).Select(p => new Destino(Path.Combine(p, "cache2")))
                               : Enumerable.Empty<Destino>())),
        new CategoriaLimpieza
        {
            Id = "papelera", Nombre = "Papelera de reciclaje", Riesgo = "Moderado", Recomendada = false,
            Desc = "Vacía la papelera de todas las unidades. Lo que contiene ya no se podrá recuperar.",
            Medir = TamPapelera,
            Limpiar = log =>
            {
                long antes = Math.Max(0, TamPapelera());
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x7);   // sin confirmación, sin progreso, sin sonido
                long liberado = antes - Math.Max(0, TamPapelera());
                log("Papelera vaciada: " + Formato(liberado) + " liberados.");
                return liberado;
            }
        },
        new CategoriaLimpieza
        {
            Id = "componentes", Nombre = "Componentes de actualizaciones antiguas", Riesgo = "Moderado", Recomendada = false, Lenta = true,
            Desc = "Elimina versiones antiguas de componentes de Windows sustituidas por actualizaciones (DISM). Puede tardar varios minutos.",
            Medir = () => -1,
            Limpiar = log =>
            {
                long antes = LibreSistema();
                var psi = new ProcessStartInfo("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) { log("ERROR en la limpieza de componentes (DISM, código " + p.ExitCode + ")."); return 0; }
                }
                long liberado = Math.Max(0, LibreSistema() - antes);
                log("Limpieza de componentes: " + Formato(liberado) + " liberados.");
                return liberado;
            }
        },
    };
}
