// Piezas reutilizables de la interfaz: textos, tarjetas, distintivos, botones y el
// cuadro de diálogo con el estilo de la aplicación.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

static class Ui
{
    public static Brush B(string clave) { return (Brush)Application.Current.Resources[clave]; }
    public static Style S(string clave) { return (Style)Application.Current.Resources[clave]; }

    public static TextBlock Texto(string texto, string estilo = "Cuerpo")
    {
        return new TextBlock { Text = texto, Style = S(estilo), TextWrapping = TextWrapping.Wrap };
    }

    // Iconos de la fuente Segoe MDL2 Assets, incluida en Windows 10.
    public static TextBlock Glifo(string codigo, double tam = 16, string brocha = "Texto")
    {
        return new TextBlock
        {
            Text = codigo, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = tam,
            Foreground = B(brocha), VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static Border Tarjeta(UIElement hijo, double margenInferior = 12)
    {
        return new Border { Style = S("EstiloTarjeta"), Child = hijo, Margin = new Thickness(0, 0, 0, margenInferior) };
    }

    // Distintivo de color. Tonos: Verde, Amarillo, Rojo, Azul, Gris.
    public static Border Chip(string texto, string tono)
    {
        return new Border
        {
            Background = B(tono + "Fondo"), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3),
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = texto, Foreground = B(tono + "Texto"), FontSize = 12, FontWeight = FontWeights.SemiBold }
        };
    }

    public static string TonoRiesgo(string riesgo)
    {
        return riesgo == "Arriesgado" ? "Rojo" : riesgo == "Moderado" ? "Amarillo" : "Verde";
    }

    public static Button Boton(string texto, RoutedEventHandler click, bool acento = false, string glifo = null)
    {
        var b = new Button { Margin = new Thickness(0, 0, 8, 0) };
        if (acento) b.Style = S("BotonAcento");
        if (glifo == null) b.Content = texto;
        else
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Glifo(glifo, 14);
            g.ClearValue(TextBlock.ForegroundProperty);    // hereda el color del botón
            g.Margin = new Thickness(0, 0, 8, 0);
            p.Children.Add(g);
            p.Children.Add(new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center });
            b.Content = p;
        }
        if (click != null) b.Click += click;
        return b;
    }

    // Botón pequeño solo con icono, con la descripción en la información sobre herramientas.
    public static Button BotonIcono(string glifo, string ayuda, RoutedEventHandler click)
    {
        var g = Glifo(glifo, 14);
        g.ClearValue(TextBlock.ForegroundProperty);
        var b = new Button { Content = g, ToolTip = ayuda, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(6, 0, 0, 0) };
        if (click != null) b.Click += click;
        return b;
    }

    public static StackPanel Fila(params UIElement[] hijos)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (UIElement h in hijos) p.Children.Add(h);
        return p;
    }

    public static StackPanel Columna(params UIElement[] hijos)
    {
        var p = new StackPanel();
        foreach (UIElement h in hijos) p.Children.Add(h);
        return p;
    }

    public static UIElement Cabecera(string titulo, string descripcion)
    {
        var desc = Texto(descripcion, "Secundario");
        desc.Margin = new Thickness(0, 4, 0, 18);
        desc.FontSize = 14;
        return Columna(Texto(titulo, "Titulo"), desc);
    }

    public static T Margen<T>(T e, double izq, double arr, double der, double aba) where T : FrameworkElement
    {
        e.Margin = new Thickness(izq, arr, der, aba);
        return e;
    }

    // Página desplazable con un margen común.
    public static ScrollViewer Pagina(UIElement contenido)
    {
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Padding = new Thickness(32, 26, 32, 26), Child = contenido }
        };
    }
}

// Cuadro de diálogo con el estilo de la aplicación. Devuelve el índice del botón pulsado
// (el primero se muestra destacado) o -1 si se cierra con Esc.
class Dialogo : Window
{
    int resultado = -1;

    Dialogo(Window dueno, string titulo, string mensaje, string tono, string[] botones)
    {
        Owner = dueno;
        Title = titulo;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = dueno != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        Background = Ui.B("Tarjeta");
        BorderBrush = Ui.B("Borde");
        BorderThickness = new Thickness(1);
        FontFamily = new FontFamily("Segoe UI");

        string glifo = tono == "Rojo" ? "" : tono == "Amarillo" ? "" : tono == "Verde" ? "" : "";
        var icono = Ui.Glifo(glifo, 22, tono + "Texto");
        icono.VerticalAlignment = VerticalAlignment.Top;
        icono.Margin = new Thickness(0, 3, 14, 0);
        var txtMensaje = Ui.Texto(mensaje);
        txtMensaje.Margin = new Thickness(0, 8, 0, 0);
        txtMensaje.LineHeight = 20;
        var cuerpo = new DockPanel { Margin = new Thickness(24, 22, 24, 22) };
        DockPanel.SetDock(icono, Dock.Left);
        cuerpo.Children.Add(icono);
        cuerpo.Children.Add(Ui.Columna(Ui.Texto(titulo, "Subtitulo"), txtMensaje));

        var pie = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < botones.Length; i++)
        {
            int indice = i;
            Button b = Ui.Boton(botones[i], (s, e) => { resultado = indice; Close(); }, i == 0);
            b.MinWidth = 96;
            if (i == botones.Length - 1) b.Margin = new Thickness(0);
            if (i == 0) b.IsDefault = true;
            pie.Children.Add(b);
        }
        var piePanel = new Border { Background = Ui.B("Fondo"), Padding = new Thickness(24, 14, 24, 14), Child = pie };

        var raiz = new DockPanel();
        DockPanel.SetDock(piePanel, Dock.Bottom);
        raiz.Children.Add(piePanel);
        raiz.Children.Add(cuerpo);
        Content = raiz;

        MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
        KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public static int Mostrar(Window dueno, string titulo, string mensaje, string tono, params string[] botones)
    {
        var d = new Dialogo(dueno, titulo, mensaje, tono, botones.Length == 0 ? new[] { "Aceptar" } : botones);
        d.ShowDialog();
        return d.resultado;
    }
}
