// Página Limpieza de disco: analiza cuánto ocupa cada categoría y borra las marcadas.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

class PaginaLimpieza : UserControl
{
    readonly VentanaPrincipal v;
    readonly StackPanel lista = new StackPanel();
    readonly Dictionary<string, CheckBox> casillas = new Dictionary<string, CheckBox>();
    readonly Dictionary<string, long> tamanos = new Dictionary<string, long>();
    readonly TextBlock txtTotal = Ui.Texto("—", "Cifra");
    readonly TextBlock txtLibre = Ui.Texto("", "Secundario");
    readonly Button btnLimpiar, btnAnalizar;
    readonly TextBlock txtSeleccion = Ui.Texto("", "Secundario");
    bool analizado;

    public PaginaLimpieza(VentanaPrincipal ventana)
    {
        v = ventana;
        btnAnalizar = Ui.Boton("Analizar de nuevo", async (s, e) => await Analizar(), false, "");
        btnLimpiar = Ui.Boton("Limpiar seleccionadas", async (s, e) => await Limpiar(), true, "");
        var resumen = new DockPanel();
        resumen.Children.Add(Ui.Columna(Ui.Texto("Espacio que se puede liberar", "Secundario"), txtTotal, txtLibre));

        foreach (CategoriaLimpieza c in Limpieza.Todas) lista.Children.Add(Fila(c));

        var nota = Ui.Texto("No se borra la carpeta Prefetch: en un disco duro mecánico ayuda a que los programas se abran antes. " +
                            "Para restos de una instalación anterior de Windows (Windows.old) usa el Liberador de espacio en disco.", "Secundario");
        var liberador = Ui.Boton("Abrir el Liberador de espacio en disco", (s, e) =>
        {
            try { System.Diagnostics.Process.Start("cleanmgr.exe"); } catch { }
        }, false, "");
        liberador.Margin = new Thickness(0, 10, 0, 0);
        liberador.HorizontalAlignment = HorizontalAlignment.Left;

        Content = Ui.PaginaConPie(Ui.Columna(
            Ui.Cabecera("Limpieza de disco", "Libera espacio borrando archivos que Windows y los programas ya no necesitan."),
            Ui.Tarjeta(resumen),
            lista,
            Ui.Margen(Ui.Tarjeta(Ui.Columna(nota, liberador), 0), 0, 6, 0, 0)), txtSeleccion, btnAnalizar, btnLimpiar);

        IsVisibleChanged += async (s, e) => { if ((bool)e.NewValue && !analizado) await Analizar(); };
    }

    UIElement Fila(CategoriaLimpieza c)
    {
        var casilla = new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsChecked = c.Recomendada };
        casilla.Checked += (s, e) => ActualizarTotal();
        casilla.Unchecked += (s, e) => ActualizarTotal();
        casillas[c.Id] = casilla;

        var desc = Ui.Texto(c.Desc, "Secundario");
        desc.Margin = new Thickness(0, 2, 0, 0);
        var chips = Ui.Fila(Ui.Chip(c.Riesgo, Ui.TonoRiesgo(c.Riesgo)));
        chips.Margin = new Thickness(0, 8, 0, 0);
        var textos = Ui.Margen(Ui.Columna(Ui.Texto(c.Nombre, "Fuerte"), desc, chips), 14, 0, 16, 0);
        textos.VerticalAlignment = VerticalAlignment.Center;

        var tam = Ui.Texto(c.Lenta ? "Tamaño desconocido" : "…", c.Lenta ? "Secundario" : "Subtitulo");
        tam.Name = "tam_" + c.Id;
        tam.Width = 150;
        tam.TextAlignment = TextAlignment.Right;
        tam.VerticalAlignment = VerticalAlignment.Center;

        var d = new DockPanel();
        DockPanel.SetDock(casilla, Dock.Left);
        DockPanel.SetDock(tam, Dock.Right);
        d.Children.Add(casilla);
        d.Children.Add(tam);
        d.Children.Add(textos);
        var tarjeta = new Border
        {
            Style = Ui.S("EstiloTarjeta"), Padding = new Thickness(16, 12, 18, 12), Margin = new Thickness(0, 0, 0, 6),
            Child = d, Cursor = Cursors.Hand, Tag = tam
        };
        tarjeta.MouseLeftButtonUp += (s, e) => casilla.IsChecked = casilla.IsChecked != true;
        return tarjeta;
    }

    TextBlock TextoTamano(string id)
    {
        int i = Limpieza.Todas.FindIndex(c => c.Id == id);
        return (TextBlock)((Border)lista.Children[i]).Tag;
    }

    async Task Analizar()
    {
        analizado = true;
        v.Ocupado(true, "Analizando el espacio que se puede liberar...");
        foreach (CategoriaLimpieza c in Limpieza.Todas)
        {
            CategoriaLimpieza cat = c;
            long bytes;
            try { bytes = await Task.Run(() => cat.Medir()); } catch { bytes = 0; }
            tamanos[c.Id] = bytes;
            if (!c.Lenta) TextoTamano(c.Id).Text = Limpieza.Formato(bytes);
        }
        v.Ocupado(false, "Análisis de espacio terminado.");
        ActualizarTotal();
    }

    void ActualizarTotal()
    {
        long total = Limpieza.Todas.Where(c => casillas[c.Id].IsChecked == true && tamanos.ContainsKey(c.Id) && tamanos[c.Id] > 0)
                                   .Sum(c => tamanos[c.Id]);
        txtTotal.Text = tamanos.Count == 0 ? "—" : Limpieza.Formato(total);
        int marcadas = casillas.Values.Count(c => c.IsChecked == true);
        txtSeleccion.Text = (marcadas == 1 ? "1 categoría marcada" : marcadas + " categorías marcadas") +
                            (tamanos.Count == 0 ? "" : "  ·  " + Limpieza.Formato(total) + " a liberar");
        btnLimpiar.IsEnabled = casillas.Values.Any(c => c.IsChecked == true) && tamanos.Count > 0;
        try
        {
            var unidad = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
            txtLibre.Text = "Libre ahora en " + unidad.Name.TrimEnd('\\') + " " + Limpieza.Formato(unidad.AvailableFreeSpace) +
                            " de " + Limpieza.Formato(unidad.TotalSize);
        }
        catch { }
    }

    async Task Limpiar()
    {
        List<CategoriaLimpieza> sel = Limpieza.Todas.Where(c => casillas[c.Id].IsChecked == true).ToList();
        if (sel.Count == 0) return;
        bool moderadas = sel.Any(c => c.Riesgo != "Seguro");
        string texto = string.Join(", ", sel.Select(c => c.Nombre)) + "." +
                       (sel.Any(c => c.Id == "papelera") ? "\n\nLo que hay en la papelera ya no se podrá recuperar." : "") +
                       (sel.Any(c => c.Lenta) ? "\n\nLa limpieza de componentes puede tardar varios minutos y después no se podrán desinstalar las actualizaciones ya instaladas." : "") +
                       "\n\nLos archivos borrados no se pueden recuperar. Los que estén en uso se dejarán.";
        if (v.Preguntar("Limpiar " + sel.Count + " categorías", texto, moderadas ? "Amarillo" : "Azul", "Limpiar", "Cancelar") != 0) return;

        v.Ocupado(true, "Limpiando...");
        long liberado = 0;
        foreach (CategoriaLimpieza c in sel)
        {
            CategoriaLimpieza cat = c;
            v.TextoEstado("Limpiando: " + c.Nombre + (c.Lenta ? " (puede tardar varios minutos)..." : "..."));
            try { liberado += await Task.Run(() => cat.Limpiar(v.Log)); }
            catch (Exception ex) { v.Log("ERROR al limpiar «" + c.Nombre + "»: " + ex.Message); }
        }
        v.Ocupado(false, null);
        await Analizar();
        v.TextoEstado("Limpieza terminada: " + Limpieza.Formato(liberado) + " liberados.");
        v.Preguntar("Limpieza terminada", "Se han liberado " + Limpieza.Formato(liberado) + ". Los archivos en uso se han dejado y se pueden limpiar más adelante.", "Verde");
    }
}
