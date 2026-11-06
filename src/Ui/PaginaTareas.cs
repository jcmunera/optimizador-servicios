// Página Tareas programadas: tareas de telemetría y diagnóstico que despiertan el disco.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

class PaginaTareas : UserControl
{
    readonly VentanaPrincipal v;
    readonly StackPanel lista = new StackPanel();
    readonly Dictionary<string, CheckBox> casillas = new Dictionary<string, CheckBox>();
    readonly Button btnDesactivar, btnRestablecer;
    readonly TextBlock txtMarcadas = Ui.Texto("", "Secundario");

    public PaginaTareas(VentanaPrincipal ventana)
    {
        v = ventana;
        btnDesactivar = Ui.Boton("Desactivar marcadas", async (s, e) => await Desactivar(), true, "");
        btnRestablecer = Ui.Boton("Restablecer todo", async (s, e) => await Restablecer(), false, "");
        var acciones = new DockPanel();
        acciones.Children.Add(Ui.Fila(Ui.Boton("Marcar recomendadas", (s, e) => Marcar(true), false, ""),
                                      Ui.Boton("Desmarcar", (s, e) => Marcar(false))));

        var nota = Ui.Texto("La desfragmentación programada (Defrag) no se toca: en un disco duro mecánico es necesaria.", "Secundario");
        nota.Margin = new Thickness(0, 12, 0, 0);

        Content = Ui.PaginaConPie(Ui.Columna(
            Ui.Cabecera("Tareas programadas", "Tareas de telemetría y diagnóstico que despiertan el disco aunque su servicio esté parado."),
            Ui.Tarjeta(Ui.Columna(acciones, nota)),
            lista), txtMarcadas, btnRestablecer, btnDesactivar);

        IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Refrescar(); };
    }

    public async void Refrescar()
    {
        var marcadas = new HashSet<string>(casillas.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key));
        Dictionary<string, EstadoTarea> estados;
        try { estados = await Task.Run(() => Tareas.LeerEstados()); }
        catch (Exception ex)
        {
            lista.Children.Clear();
            lista.Children.Add(Ui.Texto("No se pudo leer el Programador de tareas: " + ex.Message, "Secundario"));
            return;
        }

        casillas.Clear();
        lista.Children.Clear();
        foreach (TareaInfo t in Tareas.Catalogo)
        {
            EstadoTarea e = estados[t.Ruta];
            if (!e.Existe) continue;     // no todas las versiones de Windows tienen todas las tareas
            lista.Children.Add(Fila(t, e, marcadas.Contains(t.Ruta)));
        }
        int cambiadas = Tareas.Cambiadas;
        btnRestablecer.IsEnabled = cambiadas > 0;
        btnRestablecer.ToolTip = cambiadas > 0 ? cambiadas + " tareas cambiadas por el optimizador" : "No hay tareas que restablecer";
        ActualizarBoton();
    }

    UIElement Fila(TareaInfo t, EstadoTarea e, bool marcada)
    {
        var casilla = new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsEnabled = e.Habilitada, IsChecked = marcada && e.Habilitada };
        casilla.Checked += (s, a) => ActualizarBoton();
        casilla.Unchecked += (s, a) => ActualizarBoton();
        casillas[t.Ruta] = casilla;

        var desc = Ui.Texto(t.Desc, "Secundario");
        desc.Margin = new Thickness(0, 2, 0, 0);
        var ruta = Ui.Texto(t.Ruta, "Secundario");
        ruta.FontSize = 11.5;
        ruta.Margin = new Thickness(0, 3, 0, 0);
        var textos = Ui.Margen(Ui.Columna(Ui.Texto(t.Nombre, "Fuerte"), desc, ruta), 14, 0, 16, 0);
        textos.VerticalAlignment = VerticalAlignment.Center;

        var chips = Ui.Fila(Ui.Chip(t.Riesgo, Ui.TonoRiesgo(t.Riesgo)));
        chips.Margin = new Thickness(0, 0, 18, 0);

        var estado = Ui.Columna(Ui.Fila(e.Habilitada ? Ui.Chip("Activa", "Gris") : Ui.Chip("Desactivada", "Verde")),
                                Ui.Margen(Ui.Texto(e.UltimaEjecucion.HasValue ? "Última vez: " + e.UltimaEjecucion.Value.ToString("dd'/'MM'/'yyyy HH:mm") : "No se ha ejecutado",
                                                   "Secundario"), 0, 4, 0, 0));
        if (Tareas.TieneCopia(t.Ruta)) estado.Children.Add(Ui.Margen(Ui.Texto("Cambiada por el optimizador", "Secundario"), 0, 2, 0, 0));
        estado.Width = 210;
        estado.VerticalAlignment = VerticalAlignment.Center;

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textos, 1);
        Grid.SetColumn(chips, 2);
        Grid.SetColumn(estado, 3);
        g.Children.Add(casilla);
        g.Children.Add(textos);
        g.Children.Add(chips);
        g.Children.Add(estado);

        var tarjeta = new Border { Style = Ui.S("EstiloTarjeta"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 6), Child = g };
        if (e.Habilitada)
        {
            tarjeta.Cursor = Cursors.Hand;
            tarjeta.MouseLeftButtonUp += (s, a) => casilla.IsChecked = casilla.IsChecked != true;
        }
        return tarjeta;
    }

    void ActualizarBoton()
    {
        int n = casillas.Values.Count(c => c.IsChecked == true);
        btnDesactivar.IsEnabled = n > 0;
        var p = btnDesactivar.Content as StackPanel;
        if (p != null) ((TextBlock)p.Children[1]).Text = n > 0 ? "Desactivar marcadas (" + n + ")" : "Desactivar marcadas";
        txtMarcadas.Text = n == 0 ? "Ninguna tarea marcada" : n == 1 ? "1 tarea marcada" : n + " tareas marcadas";
    }

    void Marcar(bool recomendadas)
    {
        foreach (TareaInfo t in Tareas.Catalogo)
        {
            CheckBox c;
            if (casillas.TryGetValue(t.Ruta, out c) && c.IsEnabled) c.IsChecked = recomendadas && t.Recomendada;
        }
    }

    async Task Desactivar()
    {
        List<TareaInfo> sel = Tareas.Catalogo.Where(t => casillas.ContainsKey(t.Ruta) && casillas[t.Ruta].IsChecked == true).ToList();
        if (sel.Count == 0) return;
        if (v.Preguntar("Desactivar " + sel.Count + " tareas", string.Join(", ", sel.Select(t => t.Nombre)) + ".", "Azul", "Desactivar", "Cancelar") != 0)
            return;
        await v.AplicarCambio("Desactivando tareas programadas...", () => Tareas.Desactivar(sel, v.Log));
        foreach (CheckBox c in casillas.Values) c.IsChecked = false;
        Refrescar();
    }

    async Task Restablecer()
    {
        if (v.Preguntar("Restablecer tareas", "Todas las tareas cambiadas por el optimizador volverán a su estado original.",
                        "Azul", "Restablecer", "Cancelar") != 0)
            return;
        await v.AplicarCambio("Restableciendo tareas programadas...", () => Tareas.Restaurar(v.Log));
        Refrescar();
    }
}
