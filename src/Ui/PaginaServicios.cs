// Página Servicios: catálogo con mejora, nivel de riesgo y estado; desactivar y restablecer.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

class PaginaServicios : UserControl
{
    readonly VentanaPrincipal v;
    readonly List<ServicioInfo> servicios = Catalogo.Todos.Where(s => Servicios.Existe(s.Nombre)).ToList();
    readonly Dictionary<string, CheckBox> casillas = new Dictionary<string, CheckBox>();
    readonly StackPanel lista = new StackPanel();
    readonly CheckBox chkPunto = new CheckBox { Content = "Crear punto de restauración", IsChecked = true };
    readonly Button btnAplicar, btnRestablecer;
    readonly TextBlock txtMarcados = Ui.Texto("", "Secundario");
    Perfil perfil = Perfiles.Todos[0];

    public PaginaServicios(VentanaPrincipal ventana)
    {
        v = ventana;
        chkPunto.Style = Ui.S("Interruptor");

        var perfiles = new WrapPanel();
        foreach (Perfil p in Perfiles.Todos)
        {
            Perfil este = p;
            var rb = new RadioButton { Style = Ui.S("Segmento"), GroupName = "perfil", Content = p.Nombre, ToolTip = p.Desc, IsChecked = p == perfil };
            rb.Checked += (s, e) => perfil = este;
            perfiles.Children.Add(rb);
        }
        var marcar = Ui.Boton("Marcar perfil", (s, e) => Marcar(perfil), false, "");
        var desmarcar = Ui.Boton("Desmarcar", (s, e) => Marcar(null));
        var etiqueta = Ui.Margen(Ui.Texto("Perfil", "Fuerte"), 0, 0, 14, 0);
        etiqueta.VerticalAlignment = VerticalAlignment.Center;
        var filaPerfil = Ui.Fila(etiqueta, perfiles);
        filaPerfil.Margin = new Thickness(0, 0, 0, 12);

        btnAplicar = Ui.Boton("Desactivar marcados", async (s, e) => await Aplicar(), true, "");
        btnRestablecer = Ui.Boton("Restablecer todo", async (s, e) => await Restablecer(), false, "");
        var acciones = Ui.Fila(marcar, desmarcar, Ui.Margen(chkPunto, 8, 0, 0, 0));

        // Leyenda de los fondos de las tarjetas.
        var leyenda = Ui.Fila(Ui.Margen(Ui.Texto("En ejecución:", "Secundario"), 0, 0, 10, 0),
                              Ui.Chip("mejora alta", "Rojo"), Ui.Chip("mejora media o baja", "Naranja"));
        ((TextBlock)leyenda.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        leyenda.Margin = new Thickness(0, 14, 0, 0);

        // Los botones de aplicar están en una barra fija abajo, siempre visible.
        Content = Ui.PaginaConPie(Ui.Columna(
            Ui.Cabecera("Servicios", "Desactiva los servicios que más penalizan el rendimiento. Todos los cambios se pueden restablecer."),
            Ui.Tarjeta(Ui.Columna(filaPerfil, acciones, leyenda)),
            lista), txtMarcados, btnRestablecer, btnAplicar);

        IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Refrescar(); };
    }

    public void Refrescar()
    {
        var marcados = new HashSet<string>(casillas.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key));
        var backup = Almacen.LeerBackup();
        casillas.Clear();
        lista.Children.Clear();

        Grupo("Recomendados", "Seguros de desactivar en la mayoría de equipos.");
        foreach (ServicioInfo s in servicios.Where(x => x.Rec)) lista.Children.Add(Fila(s, backup, marcados));
        Grupo("Opcionales", "Desactívalos solo si no usas esa función.");
        foreach (ServicioInfo s in servicios.Where(x => !x.Rec)) lista.Children.Add(Fila(s, backup, marcados));

        btnRestablecer.IsEnabled = backup.Count > 0;
        btnRestablecer.ToolTip = backup.Count > 0 ? backup.Count + " servicios cambiados por el optimizador" : "No hay cambios que restablecer";
        ActualizarAplicar();
    }

    void Grupo(string titulo, string desc)
    {
        var t = Ui.Texto(titulo, "Subtitulo");
        t.Margin = new Thickness(0, lista.Children.Count == 0 ? 6 : 18, 0, 2);
        var d = Ui.Texto(desc, "Secundario");
        d.Margin = new Thickness(0, 0, 0, 10);
        lista.Children.Add(t);
        lista.Children.Add(d);
    }

    UIElement Fila(ServicioInfo s, Dictionary<string, Dictionary<string, object>> backup, HashSet<string> marcados)
    {
        int start = Servicios.GetStart(s.Nombre);
        bool deshabilitado = start == 4;
        bool enEjecucion = Servicios.EnEjecucion(s.Nombre);

        var casilla = new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsEnabled = !deshabilitado, IsChecked = marcados.Contains(s.Nombre) };
        casilla.Checked += (o, e) => ActualizarAplicar();
        casilla.Unchecked += (o, e) => ActualizarAplicar();
        casillas[s.Nombre] = casilla;

        var nombre = Ui.Texto(s.Nombre, "Fuerte");
        var desc = Ui.Texto(s.Desc, "Secundario");
        desc.Margin = new Thickness(0, 2, 0, 0);
        var textos = Ui.Margen(Ui.Columna(nombre, desc), 14, 0, 16, 0);
        textos.VerticalAlignment = VerticalAlignment.Center;

        string mejora = s.Impacto == "Alto" ? "alta" : s.Impacto == "Medio" ? "media" : "baja";
        var chips = Ui.Fila(Ui.Chip("Mejora " + mejora, "Azul"), Ui.Chip(s.Riesgo, Ui.TonoRiesgo(s.Riesgo)));

        // Estado: tipo de inicio y si está en marcha.
        StackPanel estado;
        if (deshabilitado)
            estado = Ui.Columna(Ui.Fila(Ui.Chip("Deshabilitado", "Verde")),
                                Ui.Margen(Ui.Texto(enEjecucion ? "En ejecución hasta reiniciar" : "Detenido", "Secundario"), 0, 4, 0, 0));
        else
        {
            var punto = Ui.Glifo(enEjecucion ? "" : "", 9, enEjecucion ? "AzulTexto" : "TextoSecundario");
            estado = Ui.Columna(Ui.Texto(Servicios.TipoInicio(start), "Cuerpo"),
                                Ui.Margen(Ui.Fila(punto, Ui.Margen(Ui.Texto(enEjecucion ? "En ejecución" : "Detenido", "Secundario"), 6, 0, 0, 0)), 0, 4, 0, 0));
        }
        if (deshabilitado && enEjecucion) ((TextBlock)estado.Children[1]).Foreground = Ui.B("AmarilloTexto");
        if (backup.ContainsKey(s.Nombre))
            estado.Children.Add(Ui.Margen(Ui.Texto("Cambiado por el optimizador", "Secundario"), 0, 2, 0, 0));
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
        chips.Margin = new Thickness(0, 0, 18, 0);
        g.Children.Add(casilla);
        g.Children.Add(textos);
        g.Children.Add(chips);
        g.Children.Add(estado);

        var tarjeta = new Border { Style = Ui.S("EstiloTarjeta"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 6), Child = g };
        // Un servicio activo y en ejecución se resalta según lo que penaliza: rojo si su mejora es alta, naranja si no.
        if (!deshabilitado && enEjecucion)
        {
            string tono = s.Impacto == "Alto" ? "Rojo" : "Naranja";
            tarjeta.Background = Ui.B(tono + "Fondo");
            tarjeta.BorderBrush = Ui.B(tono + "Fondo");
            tarjeta.ToolTip = "En ejecución: consume recursos ahora mismo";
        }
        if (!deshabilitado)
        {
            tarjeta.Cursor = Cursors.Hand;
            tarjeta.MouseLeftButtonUp += (o, e) => casilla.IsChecked = casilla.IsChecked != true;
        }
        return tarjeta;
    }

    void ActualizarAplicar()
    {
        int n = casillas.Values.Count(c => c.IsChecked == true);
        btnAplicar.IsEnabled = n > 0;
        var p = btnAplicar.Content as StackPanel;
        if (p != null) ((TextBlock)p.Children[1]).Text = n > 0 ? "Desactivar marcados (" + n + ")" : "Desactivar marcados";
        int activos = servicios.Count(x => Servicios.GetStart(x.Nombre) != 4 && Servicios.EnEjecucion(x.Nombre));
        txtMarcados.Text = (n == 0 ? "Ningún servicio marcado" : n == 1 ? "1 servicio marcado" : n + " servicios marcados") +
                           "  ·  " + activos + " servicios del catálogo en ejecución";
    }

    void Marcar(Perfil p)
    {
        foreach (ServicioInfo s in servicios)
        {
            CheckBox c;
            if (casillas.TryGetValue(s.Nombre, out c) && c.IsEnabled) c.IsChecked = p != null && p.Incluye(s);
        }
    }

    async System.Threading.Tasks.Task Aplicar()
    {
        List<string> sel = casillas.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        if (sel.Count == 0) return;
        var arriesgados = servicios.Where(s => sel.Contains(s.Nombre) && s.Riesgo != "Seguro").Select(s => s.Nombre + " (" + s.Riesgo.ToLower() + ")").ToList();
        string aviso = arriesgados.Count > 0 ? "\n\nAtención, no son seguros en todos los equipos: " + string.Join(", ", arriesgados) + "." : "";
        if (v.Preguntar("Desactivar " + sel.Count + " servicios", string.Join(", ", sel) + aviso,
                        arriesgados.Count > 0 ? "Amarillo" : "Azul", "Desactivar", "Cancelar") != 0)
            return;

        bool punto = chkPunto.IsChecked == true;
        bool hecho = await v.AplicarCambio("Desactivando servicios...", () =>
        {
            if (punto) Operaciones.CrearPuntoRestauracion(v.Log);
            foreach (string n in sel) Operaciones.Desactivar(n, v.Log);
        });
        if (hecho) foreach (CheckBox c in casillas.Values) c.IsChecked = false;
        Refrescar();
    }

    async System.Threading.Tasks.Task Restablecer()
    {
        if (v.Preguntar("Restablecer servicios", "Todos los servicios cambiados por el optimizador volverán a su estado original.",
                        "Azul", "Restablecer", "Cancelar") != 0)
            return;
        await v.AplicarCambio("Restableciendo servicios...", () => Operaciones.Restaurar(v.Log));
        Refrescar();
    }
}
