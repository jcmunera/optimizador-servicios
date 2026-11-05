// Ventana principal: menú lateral, área de páginas, barra de estado y el ciclo guiado
// medir -> cambiar -> reiniciar -> medir que comparten todas las páginas.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

class VentanaPrincipal : Window
{
    public const string Version = "3.0";

    readonly ContentControl contenido = new ContentControl();
    readonly StackPanel menu = new StackPanel();
    readonly TextBlock txtEstado;
    readonly ProgressBar barraEstado;
    readonly Dictionary<string, RadioButton> botonesMenu = new Dictionary<string, RadioButton>();
    readonly Dictionary<string, Func<FrameworkElement>> fabricas = new Dictionary<string, Func<FrameworkElement>>();
    readonly Dictionary<string, FrameworkElement> paginas = new Dictionary<string, FrameworkElement>();

    public VentanaPrincipal()
    {
        Title = "Optimizador de Servicios";
        Width = 1200;
        Height = 780;
        MinWidth = 980;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        Background = Ui.B("Fondo");
        Foreground = Ui.B("Texto");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // Menú lateral
        var marca = Ui.Fila(Ui.Glifo("", 22, "Acento"),
                            Ui.Margen(Ui.Columna(Ui.Texto("Optimizador", "Fuerte"), Ui.Texto("de Servicios", "Secundario")), 12, 0, 0, 0));
        marca.Margin = new Thickness(20, 22, 12, 22);
        menu.Children.Add(marca);
        AgregarPagina("panel", "Panel", "", () => new PaginaPanel(this));
        AgregarPagina("servicios", "Servicios", "", () => new PaginaServicios(this));
        AgregarPagina("ajustes", "Efectos y ajustes", "", () => new PaginaAjustes(this));
        AgregarPagina("tareas", "Tareas programadas", "", () => new PaginaTareas(this));
        AgregarPagina("analisis", "Análisis", "", () => new PaginaAnalisis(this));

        var registro = new Button { Style = Ui.S("BotonEnlace"), Content = "Ver registro de cambios", Margin = new Thickness(22, 0, 0, 6) };
        registro.Click += (s, e) => AbrirRegistro();
        var pieMenu = Ui.Columna(registro, Ui.Margen(Ui.Texto("Versión " + Version, "Secundario"), 22, 0, 0, 18));
        var lateral = new DockPanel { Background = Ui.B("Lateral") };
        DockPanel.SetDock(pieMenu, Dock.Bottom);
        lateral.Children.Add(pieMenu);
        lateral.Children.Add(menu);

        // Barra de estado
        txtEstado = Ui.Texto("Listo", "Secundario");
        txtEstado.TextWrapping = TextWrapping.NoWrap;
        txtEstado.TextTrimming = TextTrimming.CharacterEllipsis;
        txtEstado.VerticalAlignment = VerticalAlignment.Center;
        barraEstado = new ProgressBar { Width = 180, Margin = new Thickness(16, 0, 0, 0), Visibility = Visibility.Collapsed, Maximum = 100 };
        var estado = new DockPanel { Margin = new Thickness(32, 0, 32, 0) };
        DockPanel.SetDock(barraEstado, Dock.Right);
        estado.Children.Add(barraEstado);
        estado.Children.Add(txtEstado);
        var barra = new Border
        {
            Height = 36, Background = Ui.B("Fondo"), BorderBrush = Ui.B("Borde"),
            BorderThickness = new Thickness(0, 1, 0, 0), Child = estado
        };

        var derecha = new DockPanel();
        DockPanel.SetDock(barra, Dock.Bottom);
        derecha.Children.Add(barra);
        derecha.Children.Add(contenido);

        var raiz = new Grid { Background = Ui.B("Fondo") };
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        raiz.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(derecha, 1);
        raiz.Children.Add(lateral);
        raiz.Children.Add(derecha);
        Content = raiz;

        SourceInitialized += (s, e) => BarraTituloOscura();
        Loaded += (s, e) => { Ir("panel"); AlIniciar(); };
    }

    void AgregarPagina(string id, string texto, string glifo, Func<FrameworkElement> fabrica)
    {
        var icono = Ui.Glifo(glifo, 16);
        icono.ClearValue(TextBlock.ForegroundProperty);
        var b = new RadioButton
        {
            Style = Ui.S("Nav"), GroupName = "menu",
            Content = Ui.Fila(icono, Ui.Margen(new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center }, 14, 0, 0, 0))
        };
        b.Checked += (s, e) => Mostrar(id);
        botonesMenu[id] = b;
        fabricas[id] = fabrica;
        menu.Children.Add(b);
    }

    void Mostrar(string id)
    {
        FrameworkElement p;
        if (!paginas.TryGetValue(id, out p)) paginas[id] = p = fabricas[id]();
        contenido.Content = p;
    }

    public void Ir(string id) { botonesMenu[id].IsChecked = true; }

    public T Pagina<T>(string id) where T : FrameworkElement
    {
        FrameworkElement p;
        if (!paginas.TryGetValue(id, out p)) paginas[id] = p = fabricas[id]();
        return (T)p;
    }

    // --- Estado y avisos ---

    public void Log(string texto)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(Log), texto); return; }
        txtEstado.Text = texto;
        Almacen.Log(texto);
    }

    public void TextoEstado(string texto) { txtEstado.Text = texto; }

    public void Ocupado(bool ocupado, string mensaje)
    {
        contenido.IsEnabled = !ocupado;
        menu.IsEnabled = !ocupado;
        Cursor = ocupado ? Cursors.Wait : null;
        if (mensaje != null) txtEstado.Text = mensaje;
    }

    public void Progreso(int valor)
    {
        if (valor < 0) { barraEstado.Visibility = Visibility.Collapsed; return; }
        barraEstado.Visibility = Visibility.Visible;
        barraEstado.Value = valor;
    }

    public int Preguntar(string titulo, string mensaje, string tono, params string[] botones)
    {
        return Dialogo.Mostrar(this, titulo, mensaje, tono, botones);
    }

    void AbrirRegistro()
    {
        string log = Path.Combine(Almacen.Dir, "registro.log");
        if (File.Exists(log)) Process.Start("notepad.exe", "\"" + log + "\"");
        else Preguntar("Registro de cambios", "Todavía no se ha registrado ningún cambio.", "Azul");
    }

    // --- Ciclo guiado: medir, cambiar, reiniciar, medir ---

    public bool ReinicioPendiente
    {
        get
        {
            DateTime? cambio = global::Estado.Fecha(Almacen.LeerEstado().UltimoCambio);
            return cambio.HasValue && Sistema.Arranque < cambio.Value;
        }
    }

    async void AlIniciar()
    {
        Mediciones meds = Almacen.LeerMediciones();
        DateTime? cambio = global::Estado.Fecha(Almacen.LeerEstado().UltimoCambio);

        if (cambio.HasValue)
        {
            if (Sistema.Arranque < cambio.Value)
            {
                // Cambios aplicados pero sin reiniciar: la tarea de apertura sigue programada.
                if (Preguntar("Reinicio pendiente", "Hay cambios aplicados que todavía no tienen efecto completo porque el equipo no se ha reiniciado.",
                              "Amarillo", "Reiniciar ahora", "Más tarde") == 0)
                    Sistema.Reiniciar();
                return;
            }

            Sistema.CancelarApertura();
            Medicion d = meds.Despues;
            DateTime? fechaDespues = d == null ? null : global::Estado.Fecha(d.FechaIso);
            if (!fechaDespues.HasValue || fechaDespues.Value < cambio.Value)
            {
                if (Preguntar("Medir el resultado", "El equipo se ha reiniciado después de aplicar cambios.\n\n¿Medir ahora el rendimiento DESPUÉS para compararlo con el de ANTES?",
                              "Azul", "Medir ahora", "Ahora no") == 0)
                {
                    Ir("analisis");
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
        Ir("analisis");
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
        Ocupado(true, null);
        int restante;
        while ((restante = minimo - (int)Sistema.SegundosEncendido) > 0)
        {
            TextoEstado("El equipo acaba de iniciarse. Esperando " + restante + " s a que se estabilice antes de medir...");
            await Task.Delay(1000);
        }
        Ocupado(false, "");
    }

    public async Task<bool> MedirAsync(bool antes)
    {
        int segundos = Pagina<PaginaAnalisis>("analisis").Duracion;
        Ocupado(true, "Midiendo durante " + segundos + " s... no uses el equipo mientras tanto.");
        Progreso(0);
        var progreso = new Progress<int>(Progreso);
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
            Preguntar("Error en la medición", ex.Message, "Rojo");
        }
        Progreso(-1);
        Pagina<PaginaAnalisis>("analisis").Refrescar();
        return ok;
    }

    // Aplica un cambio dentro del ciclo: ofrece medir antes, ejecuta el trabajo en segundo
    // plano, programa la reapertura tras reiniciar y ofrece reiniciar.
    public async Task<bool> AplicarCambio(string mensaje, Action trabajo)
    {
        if (!await OfrecerMedicionAntes()) return false;

        Ocupado(true, mensaje);
        await Task.Run(trabajo);
        Almacen.GuardarEstado(new Estado { UltimoCambio = DateTime.Now.ToString("o") });
        bool programada = await Task.Run(() => Sistema.ProgramarApertura(Log));
        Ocupado(false, "Cambios aplicados. Reinicia el equipo para que tengan efecto completo.");

        if (Preguntar("Cambios aplicados", "Reinicia el equipo para que tengan efecto completo.\n\n" +
                      (programada
                          ? "Después del reinicio, el optimizador se abrirá solo para medir el rendimiento DESPUÉS."
                          : "Después del reinicio, vuelve a abrir el optimizador para medir el rendimiento DESPUÉS.") +
                      "\n\nGuarda antes tu trabajo.", "Verde", "Reiniciar ahora", "Más tarde") == 0)
            Sistema.Reiniciar();
        return true;
    }

    // Devuelve false si el usuario cancela la operación.
    async Task<bool> OfrecerMedicionAntes()
    {
        Mediciones meds = Almacen.LeerMediciones();
        DateTime? cambio = global::Estado.Fecha(Almacen.LeerEstado().UltimoCambio);

        // Con cambios anteriores aún sin reiniciar, una medición ahora no reflejaría ningún estado real.
        if (cambio.HasValue && Sistema.Arranque < cambio.Value) return true;

        DateTime? fechaAntes = meds.Antes == null ? null : global::Estado.Fecha(meds.Antes.FechaIso);
        string motivo;
        if (meds.Antes == null)
            motivo = "Todavía no has medido el rendimiento ANTES de los cambios.";
        else if (cambio.HasValue && fechaAntes.HasValue && fechaAntes.Value < cambio.Value)
            motivo = "La medición ANTES es anterior al último cambio, así que ya no refleja el estado actual del equipo.";
        else
            return true;

        int r = Preguntar("¿Medir antes de cambiar?", motivo + "\n\nSe recomienda medir ahora (" + Pagina<PaginaAnalisis>("analisis").Duracion +
                          " s) para poder comparar después del reinicio.", "Azul", "Medir y aplicar", "Aplicar sin medir", "Cancelar");
        if (r == 2 || r == -1) return false;
        if (r == 1) return true;

        FrameworkElement actual = contenido.Content as FrameworkElement;
        string idActual = null;
        foreach (var kv in paginas) if (kv.Value == actual) idActual = kv.Key;
        Ir("analisis");
        bool ok = await MedirAsync(true);
        if (idActual != null) Ir(idActual);
        return ok;
    }

    // --- Barra de título oscura en modo oscuro (Windows 10 2004 o posterior) ---

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tam);

    void BarraTituloOscura()
    {
        if (!Tema.Oscuro) return;
        IntPtr h = new WindowInteropHelper(this).Handle;
        int si = 1;
        if (DwmSetWindowAttribute(h, 20, ref si, 4) != 0) DwmSetWindowAttribute(h, 19, ref si, 4);
    }
}
