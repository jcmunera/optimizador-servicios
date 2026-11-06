// Solo para desarrollo: genera capturas PNG de las páginas para revisar el diseño.
// Se compila únicamente con build.ps1 -Captura (define CAPTURA, sin manifiesto de administrador).
// Uso: OPTIMIZADOR_CAPTURA=<carpeta> [OPTIMIZADOR_TEMA=claro|oscuro] [OPTIMIZADOR_PASOS=panel,programas:apps,...]
#if CAPTURA
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class Captura
{
    public static string Carpeta { get { return Environment.GetEnvironmentVariable("OPTIMIZADOR_CAPTURA"); } }

    // Datos aislados en la carpeta de capturas, para no mezclarlos con los reales.
    public static void PrepararDatos()
    {
        string datos = Path.Combine(Carpeta, "datos");
        Directory.CreateDirectory(datos);
        typeof(Almacen).GetField("Dir", BindingFlags.Static | BindingFlags.Public).SetValue(null, datos);
        // Mediciones de ejemplo: evitan la medición automática de la primera ejecución.
        Almacen.GuardarMediciones(new Mediciones
        {
            Antes = new Medicion { Fecha = "27/10/2026 09:12", FechaIso = "2026-10-27T09:12:00", DuracionSeg = 60, ArranqueSeg = 142.3, EscritorioSeg = 68.5,
                                   ArranqueFecha = "27/10/2026 09:05", RamUsadaMB = 2890, CommitMB = 3950, CpuPct = 18.4, DiscoPct = 61.2, ColaDisco = 1.85,
                                   Procesos = 112, ServiciosActivos = 98 },
            Despues = new Medicion { Fecha = "27/10/2026 09:41", FechaIso = "2026-10-27T09:41:00", DuracionSeg = 60, ArranqueSeg = 98.6, EscritorioSeg = 52.1,
                                     ArranqueFecha = "27/10/2026 09:34", RamUsadaMB = 2410, CommitMB = 3380, CpuPct = 9.7, DiscoPct = 27.5, ColaDisco = 0.62,
                                     Procesos = 96, ServiciosActivos = 81 }
        });
    }

    public static async void Ejecutar(VentanaPrincipal w)
    {
        string pasos = Environment.GetEnvironmentVariable("OPTIMIZADOR_PASOS") ?? "panel,servicios,ajustes,tareas,programas:inicio,programas:apps,programas:consumo,analisis";
        string tema = Tema.Oscuro ? "oscuro" : "claro";
        await Task.Delay(2500);
        foreach (string paso in pasos.Split(','))
        {
            string[] partes = paso.Split(':');
            w.Ir(partes[0]);
            await Task.Delay(1500);
            if (partes.Length > 1)
            {
                var p = w.Pagina<PaginaProgramas>("programas");
                p.Seccion(partes[1]);
                if (partes[1] == "consumo") await p.Analizar();
            }
            await Task.Delay(4000);
            Guardar(w, Path.Combine(Carpeta, tema + "-" + paso.Replace(':', '-') + ".png"));
        }
        Application.Current.Shutdown();
    }

    static void Guardar(VentanaPrincipal w, string archivo)
    {
        var c = (FrameworkElement)w.Content;
        var rtb = new RenderTargetBitmap((int)c.ActualWidth, (int)c.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(c);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (FileStream fs = File.Create(archivo)) enc.Save(fs);
    }
}
#endif
