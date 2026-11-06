// Página Efectos y ajustes: interruptores reversibles para efectos visuales, rendimiento y privacidad.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

class PaginaAjustes : UserControl
{
    readonly VentanaPrincipal v;
    readonly StackPanel lista = new StackPanel();
    readonly Dictionary<string, CheckBox> interruptores = new Dictionary<string, CheckBox>();
    readonly Dictionary<string, bool> aplicados = new Dictionary<string, bool>();
    readonly Button btnAplicar, btnRestablecer;
    readonly TextBlock txtPendientes = Ui.Texto("", "Secundario");

    public PaginaAjustes(VentanaPrincipal ventana)
    {
        v = ventana;
        btnAplicar = Ui.Boton("Aplicar cambios", async (s, e) => await Aplicar(), true, "");
        btnRestablecer = Ui.Boton("Restablecer todo", async (s, e) => await RestablecerTodo(), false, "");
        var acciones = Ui.Fila(Ui.Boton("Activar recomendados", (s, e) => ActivarRecomendados(), false, ""),
                               Ui.Boton("Deshacer selección", (s, e) => Refrescar()));

        Content = Ui.PaginaConPie(Ui.Columna(
            Ui.Cabecera("Efectos y ajustes", "Activa un interruptor para aplicar el ajuste y pulsa «Aplicar cambios». Todos se pueden revertir."),
            Ui.Tarjeta(acciones),
            lista), txtPendientes, btnRestablecer, btnAplicar);

        IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Refrescar(); };
    }

    public async void Refrescar()
    {
        // Comprobar el estado puede tardar un poco (powercfg), así que se hace en segundo plano.
        Dictionary<string, bool> estado = await Task.Run(() => Ajustes.Todos.ToDictionary(a => a.Id, a => a.Aplicado()));
        var copia = new HashSet<string>(Ajustes.Todos.Where(a => Ajustes.TieneCopia(a.Id)).Select(a => a.Id));
        aplicados.Clear();
        interruptores.Clear();
        lista.Children.Clear();

        foreach (string categoria in new[] { Ajustes.Visuales, Ajustes.Rendimiento, Ajustes.Privacidad })
        {
            var t = Ui.Texto(categoria, "Subtitulo");
            t.Margin = new Thickness(0, lista.Children.Count == 0 ? 6 : 18, 0, 10);
            lista.Children.Add(t);
            foreach (Ajuste a in Ajustes.Todos.Where(x => x.Categoria == categoria))
                lista.Children.Add(Fila(a, estado[a.Id], copia.Contains(a.Id)));
        }
        btnRestablecer.IsEnabled = copia.Count > 0;
        btnRestablecer.ToolTip = copia.Count > 0 ? copia.Count + " ajustes cambiados por el optimizador" : "No hay ajustes que restablecer";
        ActualizarPendientes();
    }

    UIElement Fila(Ajuste a, bool aplicado, bool tieneCopia)
    {
        aplicados[a.Id] = aplicado;
        var interruptor = new CheckBox { Style = Ui.S("Interruptor"), IsChecked = aplicado, VerticalAlignment = VerticalAlignment.Center };
        // Si Windows ya estaba así antes de usar el optimizador, no hay estado original al que volver.
        if (aplicado && !tieneCopia)
        {
            interruptor.IsEnabled = false;
            interruptor.ToolTip = "Ya estaba configurado así en Windows";
        }
        interruptor.Checked += (s, e) => ActualizarPendientes();
        interruptor.Unchecked += (s, e) => ActualizarPendientes();
        interruptores[a.Id] = interruptor;

        var chips = Ui.Fila(Ui.Chip(a.Riesgo, Ui.TonoRiesgo(a.Riesgo)));
        if (a.Recomendado) chips.Children.Insert(0, Ui.Chip("Recomendado", "Azul"));
        if (a.Nota != null) chips.Children.Add(Ui.Chip(a.Nota, "Gris"));
        chips.Margin = new Thickness(0, 8, 0, 0);
        var desc = Ui.Texto(a.Desc, "Secundario");
        desc.Margin = new Thickness(0, 2, 0, 0);
        var textos = Ui.Columna(Ui.Texto(a.Nombre, "Fuerte"), desc, chips);
        textos.Margin = new Thickness(0, 0, 24, 0);

        var estado = Ui.Texto(aplicado ? (tieneCopia ? "Aplicado" : "Ya aplicado") : "Sin aplicar", "Secundario");
        estado.Width = 90;
        estado.TextAlignment = TextAlignment.Right;
        estado.Margin = new Thickness(0, 0, 14, 0);
        estado.VerticalAlignment = VerticalAlignment.Center;

        var g = new DockPanel();
        var derecha = Ui.Fila(estado, interruptor);
        DockPanel.SetDock(derecha, Dock.Right);
        g.Children.Add(derecha);
        g.Children.Add(textos);

        var tarjeta = new Border { Style = Ui.S("EstiloTarjeta"), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 6), Child = g };
        if (interruptor.IsEnabled)
        {
            tarjeta.Cursor = Cursors.Hand;
            tarjeta.MouseLeftButtonUp += (s, e) => interruptor.IsChecked = interruptor.IsChecked != true;
        }
        return tarjeta;
    }

    List<Ajuste> Pendientes(out List<Ajuste> aRevertir)
    {
        var aplicar = new List<Ajuste>();
        aRevertir = new List<Ajuste>();
        foreach (Ajuste a in Ajustes.Todos)
        {
            CheckBox c;
            if (!interruptores.TryGetValue(a.Id, out c)) continue;
            bool quiere = c.IsChecked == true;
            if (quiere && !aplicados[a.Id]) aplicar.Add(a);
            else if (!quiere && aplicados[a.Id]) aRevertir.Add(a);
        }
        return aplicar;
    }

    void ActualizarPendientes()
    {
        List<Ajuste> revertir;
        int n = Pendientes(out revertir).Count + revertir.Count;
        btnAplicar.IsEnabled = n > 0;
        txtPendientes.Text = n == 0 ? "Sin cambios pendientes" : n == 1 ? "1 cambio pendiente" : n + " cambios pendientes";
    }

    void ActivarRecomendados()
    {
        foreach (Ajuste a in Ajustes.Todos.Where(x => x.Recomendado))
        {
            CheckBox c;
            if (interruptores.TryGetValue(a.Id, out c) && c.IsEnabled) c.IsChecked = true;
        }
    }

    async Task Aplicar()
    {
        List<Ajuste> revertir;
        List<Ajuste> aplicar = Pendientes(out revertir);
        if (aplicar.Count + revertir.Count == 0) return;
        string texto = "";
        if (aplicar.Count > 0) texto += "Aplicar: " + string.Join(", ", aplicar.Select(a => a.Nombre)) + ".";
        if (revertir.Count > 0) texto += (texto.Length > 0 ? "\n\n" : "") + "Revertir: " + string.Join(", ", revertir.Select(a => a.Nombre)) + ".";
        var moderados = aplicar.Where(a => a.Riesgo != "Seguro").Select(a => a.Nombre).ToList();
        if (moderados.Count > 0) texto += "\n\nAtención, no son seguros en todos los equipos: " + string.Join(", ", moderados) + ".";
        if (v.Preguntar("Aplicar " + (aplicar.Count + revertir.Count) + " cambios", texto, moderados.Count > 0 ? "Amarillo" : "Azul", "Aplicar", "Cancelar") != 0)
            return;

        await v.AplicarCambio("Aplicando ajustes...", () =>
        {
            foreach (Ajuste a in aplicar) Ajustes.Aplicar(a, v.Log);
            foreach (Ajuste a in revertir) Ajustes.Revertir(a, v.Log);
        });
        Refrescar();
    }

    async Task RestablecerTodo()
    {
        if (v.Preguntar("Restablecer ajustes", "Todos los ajustes cambiados por el optimizador volverán a su estado original.",
                        "Azul", "Restablecer", "Cancelar") != 0)
            return;
        await v.AplicarCambio("Restableciendo ajustes...", () => Ajustes.RevertirTodos(v.Log));
        Refrescar();
    }
}
