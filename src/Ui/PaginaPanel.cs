// Página Panel: datos del equipo, uso en tiempo real y recomendaciones antes de optimizar.
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

class PaginaPanel : UserControl
{
    readonly VentanaPrincipal v;
    readonly MonitorSistema monitor = new MonitorSistema();
    readonly DispatcherTimer reloj = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    bool muestreando;
    InfoEquipo info;

    readonly TextBlock txtEquipo = Ui.Texto("", "Subtitulo");
    readonly TextBlock txtWindows = Ui.Texto("", "Secundario");
    readonly TextBlock txtEncendido = Ui.Texto("", "Secundario");
    readonly Medidor_ cpu, ram, disco;
    readonly StackPanel recomendaciones = new StackPanel();
    readonly StackPanel resumen = new StackPanel();

    // Tarjeta de un recurso: título, detalle, cifra grande y barra.
    class Medidor_
    {
        public TextBlock Detalle = Ui.Texto("", "Secundario");
        public TextBlock Extra = Ui.Texto("", "Secundario");
        public TextBlock Cifra = Ui.Texto("—", "Cifra");
        public ProgressBar Barra = new ProgressBar { Maximum = 100, Margin = new Thickness(0, 10, 0, 0) };
        public Border Tarjeta;

        public Medidor_(string titulo, string glifo, string pie)
        {
            Detalle.TextTrimming = TextTrimming.CharacterEllipsis;
            Detalle.TextWrapping = TextWrapping.NoWrap;
            Cifra.Margin = new Thickness(0, 10, 0, 0);
            var cab = Ui.Fila(Ui.Glifo(glifo, 18, "Acento"), Ui.Margen(Ui.Texto(titulo, "Fuerte"), 10, 0, 0, 0));
            var piePagina = Ui.Texto(pie, "Secundario");
            piePagina.Margin = new Thickness(0, 6, 0, 0);
            Tarjeta = Ui.Tarjeta(Ui.Columna(cab, Ui.Margen(Detalle, 0, 6, 0, 0), Extra, Cifra, Barra, piePagina), 0);
            Tarjeta.Margin = new Thickness(0, 0, 12, 12);
        }

        public void Poner(double pct)
        {
            Cifra.Text = pct.ToString("F0") + " %";
            Barra.Value = pct;
            Barra.Foreground = Ui.B(pct >= 85 ? "RojoTexto" : pct >= 60 ? "AmarilloTexto" : "Acento");
        }
    }

    public PaginaPanel(VentanaPrincipal ventana)
    {
        v = ventana;
        cpu = new Medidor_("Procesador", "", "Uso en este momento");
        ram = new Medidor_("Memoria", "", "Memoria en uso");
        disco = new Medidor_("Disco del sistema", "", "Actividad del disco");

        var cabEquipo = Ui.Fila(Ui.Glifo("", 34, "Acento"),
                                Ui.Margen(Ui.Columna(txtEquipo, txtWindows, txtEncendido), 18, 0, 0, 0));

        var recursos = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) };
        recursos.Children.Add(cpu.Tarjeta);
        recursos.Children.Add(ram.Tarjeta);
        recursos.Children.Add(disco.Tarjeta);

        var tituloRec = Ui.Texto("Recomendaciones", "Subtitulo");
        tituloRec.Margin = new Thickness(0, 0, 0, 10);
        var tituloRes = Ui.Texto("Optimizador", "Subtitulo");
        tituloRes.Margin = new Thickness(0, 0, 0, 10);

        Content = Ui.Pagina(Ui.Columna(
            Ui.Cabecera("Panel", "Estado del equipo y recomendaciones antes de optimizar."),
            Ui.Tarjeta(cabEquipo),
            recursos,
            Ui.Tarjeta(Ui.Columna(tituloRec, recomendaciones)),
            Ui.Tarjeta(Ui.Columna(tituloRes, resumen), 0)));

        reloj.Tick += async (s, e) => await Muestrear();
        IsVisibleChanged += (s, e) =>
        {
            if ((bool)e.NewValue) { Refrescar(); reloj.Start(); }
            else reloj.Stop();
        };
    }

    public async void Refrescar()
    {
        txtEquipo.Text = Environment.MachineName;
        txtEncendido.Text = "Encendido desde hace " + Duracion(Sistema.SegundosEncendido);
        if (info == null)
        {
            txtWindows.Text = "Leyendo información del equipo...";
            info = await Task.Run(() => InfoEquipo.Leer());
        }
        txtWindows.Text = info.Windows;
        cpu.Detalle.Text = info.Cpu;
        cpu.Extra.Text = info.Nucleos + " procesadores lógicos";
        ram.Detalle.Text = info.RamGB.ToString("F1") + " GB instalados";
        disco.Detalle.Text = info.Unidad + "  ·  " + (info.DiscoModelo ?? "Disco") +
                             (info.DiscoTipo != "Desconocido" ? "  ·  " + info.DiscoTipo : "");
        disco.Extra.Text = info.DiscoLibreGB.ToString("F0") + " GB libres de " + info.DiscoTotalGB.ToString("F0") + " GB (" +
                           info.DiscoLibrePct.ToString("F0") + " %)";
        PintarRecomendaciones();
        PintarResumen();
        await Muestrear();
    }

    async Task Muestrear()
    {
        if (muestreando) return;
        muestreando = true;
        try
        {
            await Task.Run(() => monitor.Muestrear());
            cpu.Poner(monitor.CpuPct);
            ram.Poner(monitor.RamPct);
            ram.Extra.Text = monitor.RamUsadaGB.ToString("F1") + " GB en uso";
            disco.Poner(monitor.DiscoPct);
        }
        finally { muestreando = false; }
    }

    static string Duracion(double segundos)
    {
        var t = TimeSpan.FromSeconds(segundos);
        if (t.TotalDays >= 1) return (int)t.TotalDays + " d " + t.Hours + " h";
        if (t.TotalHours >= 1) return (int)t.TotalHours + " h " + t.Minutes + " min";
        return t.Minutes + " min";
    }

    void PintarRecomendaciones()
    {
        recomendaciones.Children.Clear();
        if (v.ReinicioPendiente)
            Recomendacion("Amarillo", "", "Hay cambios aplicados pendientes de reinicio.",
                          "Reiniciar ahora", () => Sistema.Reiniciar());
        if (Almacen.LeerMediciones().Antes == null)
            Recomendacion("Azul", "", "Aún no has medido el rendimiento. Mide antes de optimizar para poder comparar después.",
                          "Ir a Análisis", () => v.Ir("analisis"));
        if (info.RamGB <= 4.5)
            Recomendacion("Amarillo", "", "Memoria justa (" + info.RamGB.ToString("F1") + " GB). Desactivar servicios y programas de inicio libera RAM.",
                          "Ir a Servicios", () => v.Ir("servicios"));
        if (info.EsHdd)
            Recomendacion("Amarillo", "", "Disco mecánico (HDD). SysMain y Windows Search son los servicios que más lo penalizan.",
                          "Ir a Servicios", () => v.Ir("servicios"));
        int sinAplicar = Ajustes.Todos.Count(a => a.Recomendado && !a.Aplicado());
        if (sinAplicar > 0)
            Recomendacion("Azul", "", (sinAplicar == 1 ? "1 ajuste recomendado" : sinAplicar + " ajustes recomendados") +
                          " sin aplicar (efectos visuales, rendimiento o anuncios).",
                          "Ir a Efectos y ajustes", () => v.Ir("ajustes"));
        if (info.DiscoLibrePct < 15)
            Recomendacion("Rojo", "", "Poco espacio libre en " + info.Unidad + " (" + info.DiscoLibreGB.ToString("F1") +
                          " GB). Windows se ralentiza con menos de un 15 % libre.", "Ir a Limpieza", () => v.Ir("limpieza"));
        if (recomendaciones.Children.Count == 0)
            Recomendacion("Verde", "", "El equipo no muestra problemas evidentes.", null, null);
    }

    void Recomendacion(string tono, string glifo, string texto, string accion, Action alPulsar)
    {
        var icono = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Ui.B(tono + "Fondo"),
            Child = Ui.Glifo(glifo, 15, tono + "Texto")
        };
        ((TextBlock)icono.Child).HorizontalAlignment = HorizontalAlignment.Center;
        var txt = Ui.Texto(texto);
        txt.VerticalAlignment = VerticalAlignment.Center;
        txt.Margin = new Thickness(14, 0, 14, 0);
        var fila = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(icono, Dock.Left);
        fila.Children.Add(icono);
        if (accion != null)
        {
            Button b = Ui.Boton(accion, (s, e) => alPulsar());
            b.Margin = new Thickness(0);
            DockPanel.SetDock(b, Dock.Right);
            fila.Children.Add(b);
        }
        fila.Children.Add(txt);
        recomendaciones.Children.Add(fila);
    }

    void PintarResumen()
    {
        resumen.Children.Clear();
        int cambiados = Almacen.LeerBackup().Count;
        int deshabilitados = Catalogo.Todos.Count(s => Servicios.Existe(s.Nombre) && Servicios.GetStart(s.Nombre) == 4);
        Mediciones m = Almacen.LeerMediciones();
        Dato("Servicios del catálogo deshabilitados", deshabilitados + " de " + Catalogo.Todos.Count(s => Servicios.Existe(s.Nombre)));
        Dato("Servicios cambiados por el optimizador", cambiados == 0 ? "Ninguno" : cambiados + " (se pueden restablecer)");
        Dato("Medición ANTES", m.Antes == null ? "Sin medir" : m.Antes.Fecha);
        Dato("Medición DESPUÉS", m.Despues == null ? "Sin medir" : m.Despues.Fecha);
    }

    void Dato(string nombre, string valor)
    {
        var fila = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var val = Ui.Texto(valor, "Fuerte");
        DockPanel.SetDock(val, Dock.Right);
        fila.Children.Add(val);
        fila.Children.Add(Ui.Texto(nombre, "Secundario"));
        resumen.Children.Add(fila);
    }
}
