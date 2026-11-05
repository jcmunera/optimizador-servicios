// Página Análisis: medición del rendimiento antes y después de los cambios.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

class PaginaAnalisis : UserControl
{
    class Metrica
    {
        public string Nombre, Unidad, Formato;
        public Func<Medicion, double> Valor;
        public Metrica(string nombre, string unidad, string formato, Func<Medicion, double> valor)
        {
            Nombre = nombre; Unidad = unidad; Formato = formato; Valor = valor;
        }
    }

    // En todas las métricas, un valor menor es mejor.
    static readonly Metrica[] Metricas =
    {
        new Metrica("Tiempo de arranque total",   "s",  "F1", m => m.ArranqueSeg),
        new Metrica("Tiempo hasta el escritorio", "s",  "F1", m => m.EscritorioSeg),
        new Metrica("RAM en uso",                 "MB", "F0", m => m.RamUsadaMB),
        new Metrica("Memoria comprometida",       "MB", "F0", m => m.CommitMB),
        new Metrica("Uso medio de CPU",           "%",  "F1", m => m.CpuPct),
        new Metrica("Actividad media del disco",  "%",  "F1", m => m.DiscoPct),
        new Metrica("Cola media del disco",       "",   "F2", m => m.ColaDisco),
        new Metrica("Procesos en ejecución",      "",   "F0", m => m.Procesos),
        new Metrica("Servicios en ejecución",     "",   "F0", m => m.ServiciosActivos),
    };

    readonly VentanaPrincipal v;
    readonly Grid tabla = new Grid();
    readonly TextBlock aviso = Ui.Texto("", "Secundario");
    int duracion = 60;

    public int Duracion { get { return duracion; } }

    public PaginaAnalisis(VentanaPrincipal ventana)
    {
        v = ventana;

        var duraciones = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (int seg in new[] { 30, 60, 120 })
        {
            int este = seg;
            var rb = new RadioButton { Style = Ui.S("Segmento"), GroupName = "duracion", Content = seg + " s", IsChecked = seg == duracion };
            rb.Checked += (s, e) => duracion = este;
            duraciones.Children.Add(rb);
        }
        var acciones = new DockPanel();
        var botones = Ui.Fila(
            Ui.Boton("Medir ANTES", async (s, e) => await v.MedirAsync(true), true, ""),
            Ui.Boton("Medir DESPUÉS", async (s, e) => await v.MedirAsync(false), false, ""),
            Ui.Boton("Borrar mediciones", (s, e) => Borrar(), false, ""));
        ((Button)botones.Children[2]).Margin = new Thickness(0);
        DockPanel.SetDock(botones, Dock.Right);
        acciones.Children.Add(botones);
        var etiqueta = Ui.Margen(Ui.Texto("Duración", "Fuerte"), 0, 0, 14, 0);
        etiqueta.VerticalAlignment = VerticalAlignment.Center;
        var izquierda = Ui.Fila(etiqueta, duraciones);
        izquierda.VerticalAlignment = VerticalAlignment.Center;
        acciones.Children.Add(izquierda);

        aviso.Margin = new Thickness(0, 12, 0, 0);
        var ayuda = Ui.Columna(
            Ui.Texto("Cómo obtener una comparación fiable", "Fuerte"),
            Ui.Margen(Ui.Texto("1.  Reinicia el equipo (usa «Reiniciar», no «Apagar»), espera 2-3 minutos sin abrir programas y pulsa «Medir ANTES».", "Secundario"), 0, 8, 0, 0),
            Ui.Margen(Ui.Texto("2.  Aplica los cambios y reinicia cuando el optimizador te lo proponga.", "Secundario"), 0, 4, 0, 0),
            Ui.Margen(Ui.Texto("3.  Tras reiniciar, el optimizador se abre solo y propone medir DESPUÉS.", "Secundario"), 0, 4, 0, 0),
            Ui.Margen(Ui.Texto("El tiempo de arranque lo registra Windows unos minutos después de cada inicio.", "Secundario"), 0, 8, 0, 0));

        Content = Ui.Pagina(Ui.Columna(
            Ui.Cabecera("Análisis", "Mide el rendimiento antes y después de los cambios para comprobar la mejora."),
            Ui.Tarjeta(acciones),
            Ui.Tarjeta(Ui.Columna(tabla, aviso)),
            Ui.Tarjeta(ayuda, 0)));

        IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) Refrescar(); };
    }

    void Borrar()
    {
        if (v.Preguntar("Borrar mediciones", "Se borrarán las mediciones ANTES y DESPUÉS guardadas.", "Amarillo", "Borrar", "Cancelar") != 0) return;
        Almacen.GuardarMediciones(new Mediciones());
        Refrescar();
    }

    static string Formatear(double valor, Metrica m)
    {
        return (valor.ToString(m.Formato) + " " + m.Unidad).Trim();
    }

    public void Refrescar()
    {
        Mediciones meds = Almacen.LeerMediciones();
        Medicion a = meds.Antes, d = meds.Despues;

        tabla.Children.Clear();
        tabla.RowDefinitions.Clear();
        tabla.ColumnDefinitions.Clear();
        tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });

        Celdas("Métrica", "Antes", "Después", "Cambio", null, true);
        Celdas("Fecha de la medición", a == null ? "—" : a.Fecha, d == null ? "—" : d.Fecha, "", null, false);
        foreach (Metrica m in Metricas)
        {
            double va = a == null ? -1 : m.Valor(a);
            double vd = d == null ? -1 : m.Valor(d);
            string cambio = "";
            string tono = null;
            if (va >= 0 && vd >= 0)
            {
                double dif = vd - va;
                double pct = va != 0 ? dif / va * 100 : 0;
                cambio = (dif > 0 ? "+" : "") + Formatear(dif, m) + (va != 0 ? "  (" + (pct > 0 ? "+" : "") + pct.ToString("F0") + " %)" : "");
                tono = Math.Abs(pct) < 3 ? "Gris" : dif < 0 ? "Verde" : "Rojo";
            }
            Celdas(m.Nombre, a == null ? "—" : va < 0 ? "n/d" : Formatear(va, m), d == null ? "—" : vd < 0 ? "n/d" : Formatear(vd, m), cambio, tono, false);
        }

        if (a != null && d != null && a.ArranqueFecha != null && a.ArranqueFecha == d.ArranqueFecha)
            aviso.Text = "Las dos mediciones usan el mismo arranque: reinicia el equipo y repite la medición DESPUÉS para comparar el tiempo de arranque.";
        else if (a == null)
            aviso.Text = "Todavía no hay mediciones. Pulsa «Medir ANTES» para registrar el estado actual del equipo.";
        else
            aviso.Text = "Verde = mejora, rojo = empeora, gris = sin cambio apreciable (menos de un 3 %).";
    }

    void Celdas(string c0, string c1, string c2, string c3, string tono, bool cabecera)
    {
        int fila = tabla.RowDefinitions.Count;
        tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (!cabecera)
        {
            var linea = new Border { BorderBrush = Ui.B("Borde"), BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetRow(linea, fila);
            Grid.SetColumnSpan(linea, 4);
            tabla.Children.Add(linea);
        }
        string[] textos = { c0, c1, c2, c3 };
        for (int i = 0; i < 4; i++)
        {
            UIElement celda;
            if (i == 3 && tono != null && textos[i].Length > 0)
            {
                var chip = Ui.Chip(textos[i], tono);
                chip.HorizontalAlignment = HorizontalAlignment.Right;
                chip.Margin = new Thickness(0, 7, 0, 7);
                celda = chip;
            }
            else
            {
                var t = Ui.Texto(textos[i], cabecera ? "Secundario" : i == 0 ? "Cuerpo" : "Fuerte");
                t.Margin = new Thickness(0, cabecera ? 0 : 9, 0, cabecera ? 8 : 9);
                if (i > 0) t.TextAlignment = TextAlignment.Right;
                celda = t;
            }
            Grid.SetRow(celda, fila);
            Grid.SetColumn(celda, i);
            tabla.Children.Add(celda);
        }
    }
}
