// Página Modo perfil: se elige un perfil y se aplican, de forma desatendida, todas sus
// modificaciones (servicios, ajustes, tareas, apps de la Tienda y limpieza), informando
// del paso en el que se encuentra. Las demás páginas forman el modo manual.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

class PaginaPerfil : UserControl
{
    // Un paso del plan: se puede incluir o no, muestra cuánto hay que hacer y su estado.
    class Paso
    {
        public string Id, Titulo;
        public CheckBox Incluir;
        public TextBlock Detalle;
        public Border Estado;
        public bool HayTrabajo = true;
    }

    readonly VentanaPrincipal v;
    readonly Dictionary<string, Border> tarjetasPerfil = new Dictionary<string, Border>();
    readonly List<Paso> pasos = new List<Paso>();
    readonly StackPanel listaPasos = new StackPanel();
    readonly StackPanel actividad = new StackPanel();
    readonly Border tarjetaActividad;
    readonly TextBlock txtResumen = Ui.Texto("", "Secundario");
    readonly Button btnOptimizar;
    PerfilOptimizacion perfil = PerfilesOptimizacion.Todos[0];
    bool ejecutando;
    int preparacion;    // para descartar resultados de una preparación anterior

    // Trabajo pendiente calculado para el perfil elegido.
    List<string> servicios = new List<string>();
    List<Ajuste> ajustes = new List<Ajuste>();
    List<TareaInfo> tareas = new List<TareaInfo>();
    List<AppTienda> apps = new List<AppTienda>();
    List<AppTienda> todasApps;
    List<CategoriaLimpieza> limpieza = new List<CategoriaLimpieza>();

    public PaginaPerfil(VentanaPrincipal ventana)
    {
        v = ventana;

        var perfiles = new UniformGrid { Columns = PerfilesOptimizacion.Todos.Length, Margin = new Thickness(0, 0, -12, 12) };
        foreach (PerfilOptimizacion p in PerfilesOptimizacion.Todos) perfiles.Children.Add(TarjetaPerfil(p));

        var tituloPasos = Ui.Texto("Pasos", "Subtitulo");
        tituloPasos.Margin = new Thickness(0, 0, 0, 4);
        var notaPasos = Ui.Texto("Desmarca los pasos que no quieras aplicar. Lo que ya está aplicado en el equipo se omite.", "Secundario");
        notaPasos.Margin = new Thickness(0, 0, 0, 10);

        AgregarPaso("punto", "Crear punto de restauración", "Permite volver al estado anterior desde la Recuperación de Windows.");
        AgregarPaso("medir", "Medir el rendimiento antes", "Para comparar después del reinicio.");
        AgregarPaso("servicios", "Servicios", "");
        AgregarPaso("ajustes", "Efectos y ajustes", "");
        AgregarPaso("tareas", "Tareas programadas", "");
        AgregarPaso("apps", "Apps de la Tienda", "");
        AgregarPaso("limpieza", "Limpieza de disco", "");
        AgregarPaso("reiniciar", "Reiniciar al terminar", "El equipo se reinicia solo 30 segundos después. Si no se marca, se ofrece al final.");
        BuscarPaso("reiniciar").Incluir.IsChecked = false;

        var tituloActividad = Ui.Texto("Actividad", "Subtitulo");
        tituloActividad.Margin = new Thickness(0, 0, 0, 8);
        tarjetaActividad = Ui.Tarjeta(Ui.Columna(tituloActividad, actividad), 0);
        tarjetaActividad.Visibility = Visibility.Collapsed;

        btnOptimizar = Ui.Boton("Optimizar", async (s, e) => await Optimizar(), true, "");
        Content = Ui.PaginaConPie(Ui.Columna(
            Ui.Cabecera("Modo perfil", "Elige un perfil y el optimizador aplica todo lo recomendado de una vez, informando de cada paso. " +
                                       "La configuración manual sigue disponible en las demás secciones."),
            perfiles,
            Ui.Tarjeta(Ui.Columna(tituloPasos, notaPasos, listaPasos)),
            tarjetaActividad), txtResumen, btnOptimizar);

        SeleccionarPerfil(perfil);
        IsVisibleChanged += (s, e) => { if ((bool)e.NewValue && !ejecutando) Preparar(); };
    }

    // --- Perfiles ---

    UIElement TarjetaPerfil(PerfilOptimizacion p)
    {
        var lista = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (string r in p.Resumen)
        {
            var marca = Ui.Glifo("", 11, "VerdeTexto");
            marca.Margin = new Thickness(0, 3, 8, 0);
            marca.VerticalAlignment = VerticalAlignment.Top;
            var t = Ui.Texto(r, "Secundario");
            var fila = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            DockPanel.SetDock(marca, Dock.Left);
            fila.Children.Add(marca);
            fila.Children.Add(t);
            lista.Children.Add(fila);
        }
        var desc = Ui.Texto(p.Desc, "Secundario");
        desc.Margin = new Thickness(0, 4, 0, 0);
        var cab = Ui.Fila(Ui.Glifo(p.Glifo, 20, "Acento"), Ui.Margen(Ui.Texto(p.Nombre, "Subtitulo"), 10, 0, 0, 0));
        var tarjeta = Ui.Tarjeta(Ui.Columna(cab, desc, lista), 0);
        tarjeta.Margin = new Thickness(0, 0, 12, 0);
        tarjeta.BorderThickness = new Thickness(2);
        tarjeta.Cursor = Cursors.Hand;
        tarjeta.MouseLeftButtonUp += (s, e) => { if (!ejecutando) SeleccionarPerfil(p); };
        tarjetasPerfil[p.Id] = tarjeta;
        return tarjeta;
    }

    void SeleccionarPerfil(PerfilOptimizacion p)
    {
        perfil = p;
        foreach (var kv in tarjetasPerfil)
        {
            bool sel = kv.Key == p.Id;
            kv.Value.BorderBrush = Ui.B(sel ? "Acento" : "Borde");
            kv.Value.Background = Ui.B(sel ? "Seleccion" : "Tarjeta");
        }
        ((TextBlock)((StackPanel)btnOptimizar.Content).Children[1]).Text = "Optimizar con el perfil " + p.Nombre;
        if (IsVisible) Preparar();
    }

    // --- Pasos ---

    void AgregarPaso(string id, string titulo, string detalle)
    {
        var p = new Paso { Id = id, Titulo = titulo };
        p.Incluir = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        p.Incluir.Checked += (s, e) => ActualizarResumen();
        p.Incluir.Unchecked += (s, e) => ActualizarResumen();
        p.Detalle = Ui.Texto(detalle, "Secundario");
        p.Detalle.Margin = new Thickness(0, 2, 0, 0);
        p.Estado = Ui.Chip("", "Gris");
        p.Estado.Visibility = Visibility.Collapsed;
        p.Estado.Margin = new Thickness(12, 0, 0, 0);

        var textos = Ui.Margen(Ui.Columna(Ui.Texto(titulo, "Fuerte"), p.Detalle), 14, 0, 0, 0);
        textos.VerticalAlignment = VerticalAlignment.Center;
        var d = new DockPanel { Margin = new Thickness(0, 6, 0, 6), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        DockPanel.SetDock(p.Incluir, Dock.Left);
        DockPanel.SetDock(p.Estado, Dock.Right);
        d.Children.Add(p.Incluir);
        d.Children.Add(p.Estado);
        d.Children.Add(textos);
        d.MouseLeftButtonUp += (s, e) => { if (p.Incluir.IsEnabled) p.Incluir.IsChecked = p.Incluir.IsChecked != true; };
        if (listaPasos.Children.Count > 0)
            listaPasos.Children.Add(new Border { Height = 1, Background = Ui.B("Borde") });
        listaPasos.Children.Add(d);
        pasos.Add(p);
    }

    Paso BuscarPaso(string id) { return pasos.First(p => p.Id == id); }

    void PonerEstado(string id, string texto, string tono)
    {
        Paso p = BuscarPaso(id);
        var nuevo = Ui.Chip(texto, tono);
        p.Estado.Background = nuevo.Background;
        ((TextBlock)p.Estado.Child).Text = texto;
        ((TextBlock)p.Estado.Child).Foreground = Ui.B(tono + "Texto");
        p.Estado.Visibility = Visibility.Visible;
    }

    void Detalle(string id, int n, string uno, string varios, string ninguno)
    {
        Paso p = BuscarPaso(id);
        p.HayTrabajo = n > 0;
        p.Detalle.Text = n == 0 ? ninguno : n == 1 ? uno : n + " " + varios;
        p.Incluir.IsEnabled = n > 0;
        if (n == 0) p.Incluir.IsChecked = false;
        else if (!ejecutando) p.Incluir.IsChecked = true;
    }

    static string Lista(IEnumerable<string> nombres, int max = 6)
    {
        var l = nombres.ToList();
        return l.Count <= max ? string.Join(", ", l) : string.Join(", ", l.Take(max)) + " y " + (l.Count - max) + " más";
    }

    // Calcula qué queda por hacer en este equipo para el perfil elegido.
    async void Preparar()
    {
        int esta = ++preparacion;
        PerfilOptimizacion p = perfil;
        foreach (Paso paso in pasos.Where(x => x.Id != "punto" && x.Id != "reiniciar")) paso.Detalle.Text = "Calculando...";
        foreach (Paso paso in pasos) paso.Estado.Visibility = Visibility.Collapsed;
        btnOptimizar.IsEnabled = false;

        bool necesitaApps = p.AppsSeguras && todasApps == null;
        var datos = await Task.Run(() =>
        {
            var s = Catalogo.Todos.Where(x => Servicios.Existe(x.Nombre) && p.Servicios.Incluye(x) && Servicios.GetStart(x.Nombre) != 4)
                                  .Select(x => x.Nombre).ToList();
            var a = global::Ajustes.Todos.Where(x => p.Ajustes(x) && !x.Aplicado()).ToList();
            List<TareaInfo> t;
            try
            {
                var estados = Tareas.LeerEstados();
                t = Tareas.Catalogo.Where(x => x.Recomendada && estados[x.Ruta].Existe && estados[x.Ruta].Habilitada).ToList();
            }
            catch { t = new List<TareaInfo>(); }
            List<AppTienda> todas = null;
            if (necesitaApps) try { todas = AppsTienda.Leer(); } catch { todas = new List<AppTienda>(); }
            return new object[] { s, a, t, todas };
        });
        if (esta != preparacion) return;     // el usuario cambió de perfil mientras tanto

        servicios = (List<string>)datos[0];
        ajustes = (List<Ajuste>)datos[1];
        tareas = (List<TareaInfo>)datos[2];
        if (datos[3] != null) todasApps = (List<AppTienda>)datos[3];
        apps = p.AppsSeguras && todasApps != null ? todasApps.Where(x => x.Seguridad == "Seguro").ToList() : new List<AppTienda>();
        limpieza = p.Limpieza ? Limpieza.Todas.Where(c => c.Recomendada).ToList() : new List<CategoriaLimpieza>();

        Detalle("servicios", servicios.Count, "1 servicio por desactivar: " + Lista(servicios), "servicios por desactivar: " + Lista(servicios),
                "No hay servicios por desactivar en este perfil.");
        Detalle("ajustes", ajustes.Count, "1 ajuste por aplicar: " + Lista(ajustes.Select(x => x.Nombre)),
                "ajustes por aplicar: " + Lista(ajustes.Select(x => x.Nombre), 4), "Todos los ajustes del perfil ya están aplicados.");
        Detalle("tareas", tareas.Count, "1 tarea por desactivar", "tareas por desactivar", "No hay tareas por desactivar.");
        Detalle("apps", apps.Count, "1 app segura por eliminar: " + Lista(apps.Select(x => x.Nombre)),
                "apps seguras por eliminar: " + Lista(apps.Select(x => x.Nombre)),
                p.AppsSeguras ? "No hay apps seguras que eliminar." : "Este perfil no elimina aplicaciones.");
        Detalle("limpieza", limpieza.Count, "1 categoría", "categorías seguras: temporales, restos de Windows Update, volcados, informes y cachés",
                "Este perfil no limpia el disco.");

        // La medición previa solo se propone si no hay una válida.
        Mediciones meds = Almacen.LeerMediciones();
        DateTime? cambio = global::Estado.Fecha(Almacen.LeerEstado().UltimoCambio);
        DateTime? fechaAntes = meds.Antes == null ? null : global::Estado.Fecha(meds.Antes.FechaIso);
        bool hayAntes = meds.Antes != null && !(cambio.HasValue && fechaAntes.HasValue && fechaAntes.Value < cambio.Value);
        BuscarPaso("medir").Detalle.Text = hayAntes
            ? "Ya hay una medición ANTES válida (" + meds.Antes.Fecha + "). Márcalo para repetirla."
            : "Dura " + v.Pagina<PaginaAnalisis>("analisis").Duracion + " s. Permite comparar después del reinicio.";
        BuscarPaso("medir").Incluir.IsChecked = !hayAntes && !v.ReinicioPendiente;
        ActualizarResumen();
    }

    void ActualizarResumen()
    {
        if (btnOptimizar == null) return;     // aún construyendo la página
        int n = pasos.Count(p => p.Id != "reiniciar" && p.Id != "punto" && p.Id != "medir" && p.Incluir.IsChecked == true);
        btnOptimizar.IsEnabled = !ejecutando && n > 0;
        txtResumen.Text = "Perfil " + perfil.Nombre + "  ·  " + (n == 0 ? "nada que aplicar" : n == 1 ? "1 sección por optimizar" : n + " secciones por optimizar");
    }

    bool Incluido(string id) { return BuscarPaso(id).Incluir.IsChecked == true; }

    // --- Ejecución desatendida ---

    void Actividad(string texto)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(Actividad), texto); return; }
        bool error = texto.StartsWith("ERROR");
        var t = Ui.Texto(DateTime.Now.ToString("HH:mm:ss") + "   " + texto, "Secundario");
        if (error) t.Foreground = Ui.B("RojoTexto");
        t.Margin = new Thickness(0, 1, 0, 1);
        actividad.Children.Add(t);
        while (actividad.Children.Count > 14) actividad.Children.RemoveAt(0);
        v.Log(texto);
    }

    // Ejecuta un paso en segundo plano contando los errores que registre.
    async Task Ejecutar(string id, string mensaje, Action<Action<string>> trabajo)
    {
        if (!Incluido(id)) { PonerEstado(id, BuscarPaso(id).HayTrabajo ? "Omitido" : "Nada que hacer", "Gris"); return; }
        PonerEstado(id, "En curso...", "Azul");
        v.TextoEstado(mensaje);
        int errores = 0;
        Action<string> log = t => { if (t.StartsWith("ERROR")) errores++; Actividad(t); };
        try { await Task.Run(() => trabajo(log)); }
        catch (Exception ex) { log("ERROR en «" + BuscarPaso(id).Titulo + "»: " + ex.Message); }
        if (errores > 0) PonerEstado(id, errores == 1 ? "1 error" : errores + " errores", "Rojo");
        else PonerEstado(id, "Hecho", "Verde");
    }

    async Task Optimizar()
    {
        var incluidos = pasos.Where(p => p.Incluir.IsChecked == true && p.Id != "reiniciar").Select(p => "•  " + p.Titulo + (p.Id == "servicios" || p.Id == "ajustes" || p.Id == "tareas" || p.Id == "apps" ? ": " + p.Detalle.Text : "")).ToList();
        string aviso = "";
        if (Incluido("apps")) aviso += "\n\nLas apps se pueden volver a instalar desde Microsoft Store, pero no desde el optimizador.";
        if (Incluido("limpieza")) aviso += "\n\nLos archivos borrados por la limpieza no se pueden recuperar.";
        if (Incluido("reiniciar")) aviso += "\n\nAl terminar, el equipo se reiniciará automáticamente: guarda antes tu trabajo.";
        if (v.Preguntar("Optimizar con el perfil " + perfil.Nombre, string.Join("\n", incluidos) + aviso +
                        "\n\nEl resto de cambios se pueden restablecer desde cada sección.", "Azul", "Optimizar", "Cancelar") != 0)
            return;

        ejecutando = true;
        actividad.Children.Clear();
        tarjetaActividad.Visibility = Visibility.Visible;
        foreach (Paso p in pasos) { p.Incluir.IsEnabled = false; PonerEstado(p.Id, p.Incluir.IsChecked == true ? "Pendiente" : "Omitido", "Gris"); }
        ActualizarResumen();
        v.Ocupado(true, "Optimizando con el perfil " + perfil.Nombre + "...");
        Actividad("Inicio de la optimización con el perfil " + perfil.Nombre + ".");

        await Ejecutar("punto", "Creando punto de restauración...", log => Operaciones.CrearPuntoRestauracion(log));

        if (Incluido("medir"))
        {
            PonerEstado("medir", "En curso...", "Azul");
            bool medido = await v.MedirAsync(true);
            v.Ocupado(true, null);
            PonerEstado("medir", medido ? "Hecho" : "Omitido", medido ? "Verde" : "Gris");
            Actividad(medido ? "Medición ANTES guardada." : "Medición omitida.");
        }
        else PonerEstado("medir", "Omitido", "Gris");

        List<string> serv = servicios;
        await Ejecutar("servicios", "Desactivando servicios...", log => { foreach (string n in serv) Operaciones.Desactivar(n, log); });
        List<Ajuste> aj = ajustes;
        await Ejecutar("ajustes", "Aplicando efectos y ajustes...", log => { foreach (Ajuste a in aj) global::Ajustes.Aplicar(a, log); });
        List<TareaInfo> ta = tareas;
        await Ejecutar("tareas", "Desactivando tareas programadas...", log => Tareas.Desactivar(ta, log));
        List<AppTienda> ap = apps;
        await Ejecutar("apps", "Eliminando apps de la Tienda...", log => AppsTienda.Eliminar(ap, log));
        if (Incluido("apps")) todasApps = null;
        List<CategoriaLimpieza> li = limpieza;
        long liberado = 0;
        await Ejecutar("limpieza", "Limpiando el disco...", log => { foreach (CategoriaLimpieza c in li) liberado += c.Limpiar(log); });

        Almacen.MarcarCambio();
        bool programada = await Task.Run(() => Sistema.ProgramarApertura(Actividad));
        int errores = actividad.Children.OfType<TextBlock>().Count(t => t.Text.Contains("ERROR"));
        Actividad("Optimización terminada" + (liberado > 0 ? ": " + Limpieza.Formato(liberado) + " liberados." : "."));
        ejecutando = false;
        v.Ocupado(false, "Optimización con el perfil " + perfil.Nombre + " terminada.");
        foreach (Paso p in pasos) p.Incluir.IsEnabled = true;

        string tras = programada
            ? "Después del reinicio, el optimizador se abrirá solo para medir el rendimiento DESPUÉS."
            : "Después del reinicio, vuelve a abrir el optimizador para medir el rendimiento DESPUÉS.";
        if (Incluido("reiniciar"))
        {
            PonerEstado("reiniciar", "En 30 s", "Azul");
            Actividad("Reiniciando el equipo en 30 segundos.");
            Sistema.Reiniciar(30);
            v.Preguntar("Optimización terminada", "El equipo se reiniciará en 30 segundos. " + tras, "Verde");
            return;
        }
        if (v.Preguntar("Optimización terminada", (errores > 0 ? "Algunos pasos han tenido errores: revisa la actividad. " : "") +
                        "Reinicia el equipo para que los cambios tengan efecto completo.\n\n" + tras + "\n\nGuarda antes tu trabajo.",
                        errores > 0 ? "Amarillo" : "Verde", "Reiniciar ahora", "Más tarde") == 0)
            Sistema.Reiniciar();
    }
}
