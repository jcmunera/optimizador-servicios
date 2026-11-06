// Optimizador de Servicios - punto de entrada.
// Sin parámetros abre la interfaz gráfica (WPF); con parámetros, el modo de línea de comandos.
using System;
using System.Reflection;
using System.Windows;

[assembly: AssemblyTitle("Optimizador de Servicios")]
[assembly: AssemblyDescription("Optimizador para Windows 10 en equipos modestos")]
[assembly: AssemblyProduct("Optimizador de Servicios")]
[assembly: AssemblyVersion("3.1.0.0")]
[assembly: AssemblyFileVersion("3.1.0.0")]

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0) return Cli.Ejecutar(args);

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Tema.Aplicar(app);
        app.DispatcherUnhandledException += (s, e) =>
        {
            Almacen.Log("ERROR inesperado: " + e.Exception);
            Dialogo.Mostrar(app.MainWindow, "Error inesperado", e.Exception.Message, "Rojo");
            e.Handled = true;
        };
#if CAPTURA
        if (Captura.Carpeta != null)
        {
            Captura.PrepararDatos();
            var w = new VentanaPrincipal();
            w.Loaded += (s, e) => Captura.Ejecutar(w);
            app.Run(w);
            return 0;
        }
#endif
        app.Run(new VentanaPrincipal());
        return 0;
    }
}
