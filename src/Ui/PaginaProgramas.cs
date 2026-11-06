// Página Programas: programas que se inician con Windows, consumo en uso normal y
// aplicaciones preinstaladas de la Tienda.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

class PaginaProgramas : UserControl
{
    const int SegundosConsumo = 20;

    readonly VentanaPrincipal v;
    readonly StackPanel cuerpo = new StackPanel();
    readonly StackPanel barra = new StackPanel();
    string seccion = "inicio";

    List<ProgramaInstalado> programas;
    List<EntradaInicio> inicio;
    List<ConsumoPrograma> consumo;
    List<AppTienda> apps;
    readonly Dictionary<EntradaInicio, CheckBox> interruptores = new Dictionary<EntradaInicio, CheckBox>();
    readonly Dictionary<AppTienda, CheckBox> casillasApps = new Dictionary<AppTienda, CheckBox>();
    readonly Dictionary<string, RadioButton> selectores = new Dictionary<string, RadioButton>();
    readonly Button btnAplicarInicio, btnAnalizar, btnEliminar;
    readonly TextBlock txtPie = Ui.Texto("", "Secundario");

    public PaginaProgramas(VentanaPrincipal ventana)
    {
        v = ventana;
        var selector = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var s in new[] { new[] { "inicio", "Programas de inicio" }, new[] { "consumo", "Consumo en uso normal" }, new[] { "apps", "Apps de la Tienda" } })
        {
            string id = s[0];
            var rb = new RadioButton { Style = Ui.S("Segmento"), GroupName = "seccionProgramas", Content = s[1], IsChecked = id == seccion };
            rb.Checked += (o, e) => { seccion = id; Mostrar(); };
            selectores[id] = rb;
            selector.Children.Add(rb);
        }
        var paneles = Ui.Fila(
            Ui.Boton("Programas y características", (s, e) => Abrir("control.exe", "appwiz.cpl"), false, ""),
            Ui.Boton("Aplicaciones y características", (s, e) => Abrir("ms-settings:appsfeatures", null), false, ""));
        ((Button)paneles.Children[1]).Margin = new Thickness(0);
        paneles.Margin = new Thickness(0, 0, 0, 14);

        btnAplicarInicio = Ui.Boton("Aplicar cambios", async (s, e) => await AplicarInicio(), true, "");
        btnAnalizar = Ui.Boton("Analizar (" + SegundosConsumo + " s)", async (s, e) => await AnalizarConsumo(), true, "");
        btnEliminar = Ui.Boton("Eliminar marcadas", async (s, e) => await EliminarApps(), true, "");

        Content = Ui.PaginaConPie(Ui.Columna(
            Ui.Cabecera("Programas", "Programas que ralentizan el arranque o consumen recursos, y aplicaciones preinstaladas que no se usan."),
            selector, paneles, barra, cuerpo), txtPie, btnAplicarInicio, btnAnalizar, btnEliminar);

        IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Mostrar(); };
    }

    public void Seccion(string id) { selectores[id].IsChecked = true; }

    public Task Analizar() { return AnalizarConsumo(); }

    static void Abrir(string archivo, string argumentos)
    {
        try { Process.Start(new ProcessStartInfo(archivo, argumentos ?? "") { UseShellExecute = true }); } catch { }
    }

    void Mostrar()
    {
        barra.Children.Clear();
        cuerpo.Children.Clear();
        if (seccion == "inicio") MostrarInicio();
        else if (seccion == "consumo") MostrarConsumo();
        else MostrarApps();
    }

    static TextBlock Cargando(string texto)
    {
        var t = Ui.Texto(texto, "Secundario");
        t.Margin = new Thickness(0, 8, 0, 0);
        return t;
    }

    async Task<List<ProgramaInstalado>> Programas()
    {
        if (programas == null) programas = await Task.Run(() => ProgramasInstalados.Leer());
        return programas;
    }

    // --- Programas de inicio ---

    async void MostrarInicio()
    {
        Pie(btnAplicarInicio);
        btnAplicarInicio.IsEnabled = false;
        var txtInicio = Ui.Texto("Desactiva los que no necesites al encender el equipo: no se desinstalan y puedes volver a activarlos aquí o en el Administrador de tareas.", "Secundario");
        barra.Children.Add(Ui.Tarjeta(txtInicio));

        if (inicio == null)
        {
            cuerpo.Children.Add(Cargando("Buscando programas de inicio..."));
            List<ProgramaInstalado> p = await Programas();
            inicio = await Task.Run(() => Inicio.Leer(p));
            if (seccion != "inicio") return;
            cuerpo.Children.Clear();
        }

        interruptores.Clear();
        if (inicio.Count == 0) cuerpo.Children.Add(Cargando("No hay programas de terceros que se inicien con Windows."));
        foreach (EntradaInicio e in inicio)
        {
            var interruptor = new CheckBox { Style = Ui.S("Interruptor"), IsChecked = e.Habilitada, IsEnabled = e.Conmutable, VerticalAlignment = VerticalAlignment.Center };
            interruptor.ToolTip = e.Conmutable ? "Activado = se inicia con Windows" : "Se lanza desde otro origen: no se puede desactivar desde aquí";
            interruptor.Checked += (s, a) => ActualizarInicio();
            interruptor.Unchecked += (s, a) => ActualizarInicio();
            interruptores[e] = interruptor;

            var chips = Ui.Fila(e.Retrasos > 0
                ? Ui.Chip("Retrasó el arranque " + e.Retrasos + (e.Retrasos == 1 ? " vez" : " veces") + " (hasta " + e.RetrasoMaxSeg.ToString("F1") + " s)", "Amarillo")
                : Ui.Chip("Sin retrasos registrados", "Gris"));
            chips.Children.Add(Ui.Chip(e.Origen, "Gris"));
            chips.Margin = new Thickness(0, 8, 0, 0);
            var detalle = Ui.Texto(TextoPrograma(e.Programa, e.Exe) + "  ·  " + e.Comando, "Secundario");
            detalle.TextTrimming = TextTrimming.CharacterEllipsis;
            detalle.TextWrapping = TextWrapping.NoWrap;
            detalle.ToolTip = e.Comando;
            detalle.Margin = new Thickness(0, 2, 0, 0);

            cuerpo.Children.Add(FilaPrograma(Ui.Columna(Ui.Texto(e.Nombre, "Fuerte"), detalle, chips), e.Programa, e.Exe, interruptor));
        }
        ActualizarInicio();
    }

    List<EntradaInicio> PendientesInicio()
    {
        return interruptores.Where(kv => kv.Key.Conmutable && (kv.Value.IsChecked == true) != kv.Key.Habilitada).Select(kv => kv.Key).ToList();
    }

    async Task AplicarInicio()
    {
        List<EntradaInicio> cambios = PendientesInicio();
        if (cambios.Count == 0) return;
        var quitar = cambios.Where(e => interruptores[e].IsChecked != true).Select(e => e.Nombre).ToList();
        var poner = cambios.Where(e => interruptores[e].IsChecked == true).Select(e => e.Nombre).ToList();
        string texto = (quitar.Count > 0 ? "Dejarán de iniciarse con Windows: " + string.Join(", ", quitar) + "." : "") +
                       (poner.Count > 0 ? (quitar.Count > 0 ? "\n\n" : "") + "Volverán a iniciarse: " + string.Join(", ", poner) + "." : "");
        if (v.Preguntar("Cambiar programas de inicio", texto, "Azul", "Aplicar", "Cancelar") != 0) return;

        var deseado = cambios.ToDictionary(e => e, e => interruptores[e].IsChecked == true);
        await v.AplicarCambio("Cambiando programas de inicio...", () =>
        {
            foreach (var kv in deseado)
            {
                try
                {
                    Inicio.Cambiar(kv.Key, kv.Value);
                    v.Log((kv.Value ? "Activado en el inicio: " : "Desactivado del inicio: ") + kv.Key.Nombre);
                }
                catch (Exception ex) { v.Log("ERROR al cambiar el inicio de " + kv.Key.Nombre + ": " + ex.Message); }
            }
        });
        Mostrar();
    }

    // --- Consumo en uso normal ---

    void MostrarConsumo()
    {
        Pie(btnAnalizar);
        txtPie.Text = consumo == null ? "Sin analizar" : consumo.Count + " programas analizados, " + consumo.Count(c => c.Alto) + " con consumo alto";
        var acciones = new DockPanel();
        var txt = Ui.Texto("Mide CPU, memoria y disco de cada programa de terceros mientras usas el equipo con normalidad. " +
                           "Se destaca si supera el 5 % de CPU, 300 MB de RAM o 1 MB/s de disco.", "Secundario");
        txt.VerticalAlignment = VerticalAlignment.Center;
        acciones.Children.Add(txt);
        barra.Children.Add(Ui.Tarjeta(acciones));

        if (consumo == null) { cuerpo.Children.Add(Cargando("Pulsa «Analizar» para medir el consumo de los programas.")); return; }
        if (consumo.Count == 0) cuerpo.Children.Add(Cargando("No hay programas de terceros en ejecución."));
        foreach (ConsumoPrograma c in consumo)
        {
            var cifras = Ui.Fila(Cifra("CPU", c.CpuPct.ToString("F1") + " %"), Cifra("RAM", c.RamMB.ToString("F0") + " MB"),
                                 Cifra("Disco", c.DiscoKBs >= 1024 ? (c.DiscoKBs / 1024).ToString("F1") + " MB/s" : c.DiscoKBs.ToString("F0") + " KB/s"));
            cifras.Margin = new Thickness(0, 8, 0, 0);
            var chips = Ui.Fila();
            if (c.Alto) chips.Children.Add(Ui.Chip("Consumo alto", "Amarillo"));
            if (c.Procesos > 1) chips.Children.Add(Ui.Chip(c.Procesos + " procesos", "Gris"));
            chips.Margin = new Thickness(0, 8, 0, 0);
            var detalle = Ui.Texto(TextoPrograma(c.Programa, c.Exe) + "  ·  " + c.Exe, "Secundario");
            detalle.TextTrimming = TextTrimming.CharacterEllipsis;
            detalle.TextWrapping = TextWrapping.NoWrap;
            detalle.ToolTip = c.Exe;
            detalle.Margin = new Thickness(0, 2, 0, 0);
            cuerpo.Children.Add(FilaPrograma(Ui.Columna(Ui.Texto(c.Nombre, "Fuerte"), detalle, cifras, chips), c.Programa, c.Exe, null));
        }
    }

    static UIElement Cifra(string nombre, string valor)
    {
        return Ui.Margen(Ui.Fila(Ui.Margen(Ui.Texto(nombre, "Secundario"), 0, 0, 6, 0), Ui.Texto(valor, "Fuerte")), 0, 0, 22, 0);
    }

    async Task AnalizarConsumo()
    {
        v.Ocupado(true, "Analizando programas durante " + SegundosConsumo + " s... usa el equipo con normalidad.");
        v.Progreso(0);
        var progreso = new Progress<int>(v.Progreso);
        try
        {
            List<ProgramaInstalado> p = await Programas();
            consumo = await Task.Run(() => Consumo.Medir(SegundosConsumo, p, progreso));
            int altos = consumo.Count(c => c.Alto);
            v.Ocupado(false, consumo.Count + " programas analizados" + (altos > 0 ? ", " + altos + " con consumo alto." : "."));
        }
        catch (Exception ex)
        {
            v.Ocupado(false, "Error en el análisis.");
            v.Preguntar("Error en el análisis", ex.Message, "Rojo");
        }
        v.Progreso(-1);
        if (seccion == "consumo") Mostrar();
    }

    // --- Fila común de programa: texto, acciones y (opcional) interruptor ---

    UIElement FilaPrograma(UIElement textos, ProgramaInstalado programa, string exe, CheckBox interruptor)
    {
        var acciones = Ui.Fila();
        acciones.VerticalAlignment = VerticalAlignment.Center;
        if (exe != null && File.Exists(exe))
            acciones.Children.Add(Ui.BotonIcono("", "Abrir ubicación", (s, e) => Process.Start("explorer.exe", "/select,\"" + exe + "\"")));
        if (programa != null)
            acciones.Children.Add(Ui.BotonIcono("", "Desinstalar " + programa.Nombre, (s, e) => Desinstalar(programa)));
        if (interruptor != null) acciones.Children.Add(Ui.Margen(interruptor, 18, 0, 0, 0));

        var d = new DockPanel();
        DockPanel.SetDock(acciones, Dock.Right);
        d.Children.Add(acciones);
        ((FrameworkElement)textos).Margin = new Thickness(0, 0, 16, 0);
        d.Children.Add(textos);
        return new Border { Style = Ui.S("EstiloTarjeta"), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 6), Child = d };
    }

    // Las aplicaciones de la Tienda (WindowsApps) no aparecen en el registro de desinstalación.
    static string TextoPrograma(ProgramaInstalado p, string exe)
    {
        if (p != null) return p.Nombre;
        if (exe != null && exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0) return "Aplicación de la Tienda";
        return "Programa no identificado";
    }

    void Desinstalar(ProgramaInstalado p)
    {
        bool delicado = ProgramasInstalados.EsDelicado(p);
        string texto = p.Nombre + (p.Version.Length > 0 ? " " + p.Version : "") + (p.Editor.Length > 0 ? "\n" + p.Editor : "") +
                       (delicado ? "\n\nATENCIÓN: parece un antivirus, un controlador o software del fabricante del equipo. " +
                                   "Desinstalarlo puede dejar el equipo sin protección o hacer que deje de funcionar algún dispositivo." : "") +
                       "\n\nSe abrirá su desinstalador. La desinstalación no se puede deshacer desde el optimizador.";
        if (v.Preguntar("Desinstalar programa", texto, delicado ? "Rojo" : "Amarillo", "Cancelar", "Abrir desinstalador") != 1) return;
        try
        {
            ProgramasInstalados.LanzarDesinstalador(p);
            v.Log("Desinstalador iniciado: " + p.Nombre);
            programas = null;
            inicio = null;
            consumo = null;
        }
        catch (Exception ex)
        {
            v.Log("ERROR al iniciar el desinstalador de " + p.Nombre + ": " + ex.Message);
            v.Preguntar("No se pudo iniciar el desinstalador", ex.Message + "\n\nPuedes desinstalarlo desde «Programas y características».", "Rojo");
        }
    }

    // --- Apps de la Tienda ---

    async void MostrarApps()
    {
        Pie(btnEliminar);
        btnEliminar.IsEnabled = false;
        var acciones = new DockPanel();
        acciones.Children.Add(Ui.Fila(Ui.Boton("Marcar seguras", (s, e) => MarcarApps(true), false, ""), Ui.Boton("Desmarcar", (s, e) => MarcarApps(false))));
        var nota = Ui.Texto("Se eliminan para tu usuario y se pueden volver a instalar desde Microsoft Store. " +
                            "No se muestran las aplicaciones del sistema ni las imprescindibles (Tienda, Seguridad de Windows, extensiones...).", "Secundario");
        nota.Margin = new Thickness(0, 12, 0, 0);
        barra.Children.Add(Ui.Tarjeta(Ui.Columna(acciones, nota)));

        if (apps == null)
        {
            cuerpo.Children.Add(Cargando("Buscando aplicaciones de la Tienda..."));
            apps = await Task.Run(() => AppsTienda.Leer());
            if (seccion != "apps") return;
            cuerpo.Children.Clear();
        }

        casillasApps.Clear();
        if (apps.Count == 0) cuerpo.Children.Add(Cargando("No hay aplicaciones preinstaladas que se puedan eliminar."));
        foreach (AppTienda a in apps)
        {
            var casilla = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            casilla.Checked += (s, e) => ActualizarEliminar();
            casilla.Unchecked += (s, e) => ActualizarEliminar();
            casillasApps[a] = casilla;

            string tono = a.Seguridad == "Seguro" ? "Verde" : a.Seguridad == "Moderado" ? "Amarillo" : "Gris";
            string texto = a.Seguridad == "Seguro" ? "Segura de eliminar" : a.Seguridad == "Moderado" ? "Se usa a menudo" : "Desconocida";
            var desc = Ui.Texto(a.Desc, "Secundario");
            desc.Margin = new Thickness(0, 2, 0, 0);
            var paquete = Ui.Texto(a.Paquete + "  ·  versión " + a.Version, "Secundario");
            paquete.FontSize = 11.5;
            paquete.Margin = new Thickness(0, 3, 0, 0);
            var textos = Ui.Margen(Ui.Columna(Ui.Texto(a.Nombre, "Fuerte"), desc, paquete), 14, 0, 16, 0);
            textos.VerticalAlignment = VerticalAlignment.Center;
            var chip = Ui.Chip(texto, tono);

            var d = new DockPanel();
            DockPanel.SetDock(casilla, Dock.Left);
            DockPanel.SetDock(chip, Dock.Right);
            d.Children.Add(casilla);
            d.Children.Add(chip);
            d.Children.Add(textos);
            var tarjeta = new Border { Style = Ui.S("EstiloTarjeta"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 6), Child = d, Cursor = Cursors.Hand };
            tarjeta.MouseLeftButtonUp += (s, e) => casilla.IsChecked = casilla.IsChecked != true;
            cuerpo.Children.Add(tarjeta);
        }
        ActualizarEliminar();
    }

    void ActualizarEliminar()
    {
        int n = casillasApps.Values.Count(c => c.IsChecked == true);
        btnEliminar.IsEnabled = n > 0;
        ((TextBlock)((StackPanel)btnEliminar.Content).Children[1]).Text = n > 0 ? "Eliminar marcadas (" + n + ")" : "Eliminar marcadas";
        txtPie.Text = n == 0 ? "Ninguna aplicación marcada" : n == 1 ? "1 aplicación marcada" : n + " aplicaciones marcadas";
    }

    void ActualizarInicio()
    {
        int n = PendientesInicio().Count;
        btnAplicarInicio.IsEnabled = n > 0;
        txtPie.Text = n == 0 ? "Sin cambios pendientes" : n == 1 ? "1 cambio pendiente" : n + " cambios pendientes";
    }

    // La barra fija de abajo muestra el botón principal de la sección visible.
    void Pie(Button b)
    {
        foreach (Button x in new[] { btnAplicarInicio, btnAnalizar, btnEliminar }) x.Visibility = x == b ? Visibility.Visible : Visibility.Collapsed;
        txtPie.Text = "";
    }

    void MarcarApps(bool seguras)
    {
        foreach (var kv in casillasApps) kv.Value.IsChecked = seguras && kv.Key.Seguridad == "Seguro";
    }

    async Task EliminarApps()
    {
        List<AppTienda> sel = casillasApps.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        if (sel.Count == 0) return;
        var noSeguras = sel.Where(a => a.Seguridad != "Seguro").Select(a => a.Nombre).ToList();
        string texto = string.Join(", ", sel.Select(a => a.Nombre)) + "." +
                       (noSeguras.Count > 0 ? "\n\nAtención, no son seguras en todos los casos: " + string.Join(", ", noSeguras) + "." : "") +
                       "\n\nNo se puede deshacer desde el optimizador: para recuperarlas hay que instalarlas de nuevo desde Microsoft Store.";
        if (v.Preguntar("Eliminar " + sel.Count + " aplicaciones", texto, noSeguras.Count > 0 ? "Rojo" : "Amarillo", "Cancelar", "Eliminar") != 1) return;

        v.Ocupado(true, "Eliminando aplicaciones...");
        await Task.Run(() => AppsTienda.Eliminar(sel, v.Log));
        apps = await Task.Run(() => AppsTienda.Leer());
        v.Ocupado(false, null);
        int quedan = sel.Count(a => apps.Any(x => x.Paquete == a.Paquete));
        v.TextoEstado(quedan == 0 ? sel.Count + " aplicaciones eliminadas." : (sel.Count - quedan) + " eliminadas; " + quedan + " no se pudieron eliminar (ver registro).");
        if (seccion == "apps") Mostrar();
    }
}
