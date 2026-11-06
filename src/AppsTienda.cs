// Optimizador de Servicios - aplicaciones preinstaladas de la Tienda (AppX).
// Se listan y eliminan con los cmdlets de PowerShell Get-AppxPackage y Remove-AppxPackage
// para el usuario actual. Las aplicaciones del sistema, los marcos (VCLibs, .NET...) y las
// imprescindibles (Tienda, Seguridad de Windows, extensiones de imagen y vídeo) no se muestran.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

class AppTienda
{
    public string Paquete, NombreCompleto, Version, Nombre, Desc;
    public string Seguridad;    // Seguro, Moderado o Desconocido
}

static class AppsTienda
{
    class Entrada
    {
        public string Patron, Nombre, Seguridad, Desc;
        public Entrada(string patron, string nombre, string seguridad, string desc)
        {
            Patron = patron; Nombre = nombre; Seguridad = seguridad; Desc = desc;
        }
        public bool Coincide(string paquete)
        {
            return Patron.EndsWith("*")
                ? paquete.StartsWith(Patron.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                : paquete.Equals(Patron, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Nunca se ofrecen: sin ellas fallan otras aplicaciones o partes de Windows.
    static readonly string[] Excluidas =
    {
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.SecHealthUI",
        "Microsoft.Services.Store.Engagement", "Microsoft.VCLibs*", "Microsoft.NET.*", "Microsoft.UI.Xaml*",
        "Microsoft.*Extension*", "Microsoft.Windows.ShellExperienceHost", "Microsoft.WindowsTerminal", "Microsoft.Winget*",
        "Microsoft.LanguageExperiencePack*", "Microsoft.MicrosoftEdge*", "Microsoft.Paint", "Microsoft.WindowsNotepad",
        "MicrosoftCorporationII.WinAppRuntime*", "Microsoft.WindowsAppRuntime*",
    };

    static readonly Entrada[] Catalogo =
    {
        new Entrada("Microsoft.BingWeather", "El Tiempo", "Seguro", "Previsión meteorológica de MSN."),
        new Entrada("Microsoft.BingNews", "Noticias", "Seguro", "Noticias de MSN."),
        new Entrada("Microsoft.BingFinance", "Finanzas", "Seguro", "Información financiera de MSN."),
        new Entrada("Microsoft.BingSports", "Deportes", "Seguro", "Noticias deportivas de MSN."),
        new Entrada("Microsoft.BingSearch", "Búsqueda de Bing", "Seguro", "Integración de Bing en la búsqueda."),
        new Entrada("Microsoft.GetHelp", "Obtener ayuda", "Seguro", "Asistente de soporte de Microsoft."),
        new Entrada("Microsoft.Getstarted", "Consejos", "Seguro", "Consejos y presentación de Windows."),
        new Entrada("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "Seguro", "Juegos de cartas con anuncios."),
        new Entrada("Microsoft.MicrosoftOfficeHub", "Office (acceso directo)", "Seguro", "Lanzador de Office y Microsoft 365. No es Office."),
        new Entrada("Microsoft.People", "Contactos", "Seguro", "Agenda de contactos."),
        new Entrada("Microsoft.SkypeApp", "Skype", "Seguro", "Versión de la Tienda de Skype."),
        new Entrada("Microsoft.WindowsFeedbackHub", "Centro de opiniones", "Seguro", "Envío de comentarios a Microsoft."),
        new Entrada("Microsoft.WindowsMaps", "Mapas", "Seguro", "Mapas de Windows."),
        new Entrada("Microsoft.ZuneMusic", "Groove Música / Reproductor multimedia", "Moderado", "Reproductor de música. Si se elimina, usa otro reproductor."),
        new Entrada("Microsoft.ZuneVideo", "Películas y TV", "Moderado", "Reproductor de vídeo predeterminado."),
        new Entrada("Microsoft.MixedReality.Portal", "Portal de realidad mixta", "Seguro", "Para gafas de realidad virtual de Windows."),
        new Entrada("Microsoft.Microsoft3DViewer", "Visor 3D", "Seguro", "Visor de modelos 3D."),
        new Entrada("Microsoft.Print3D", "Print 3D", "Seguro", "Impresión 3D."),
        new Entrada("Microsoft.3DBuilder", "3D Builder", "Seguro", "Modelado 3D."),
        new Entrada("Microsoft.MSPaint", "Paint 3D", "Seguro", "Editor 3D. No es el Paint clásico."),
        new Entrada("Microsoft.Messaging", "Mensajes", "Seguro", "Aplicación de mensajería antigua."),
        new Entrada("Microsoft.OneConnect", "Planes de datos móviles", "Seguro", "Planes de datos para equipos con SIM."),
        new Entrada("Microsoft.Wallet", "Microsoft Pay", "Seguro", "Cartera de pagos."),
        new Entrada("Microsoft.YourPhone", "Tu teléfono / Vínculo móvil", "Moderado", "Conecta el móvil con el PC."),
        new Entrada("Microsoft.549981C3F5F10", "Cortana", "Seguro", "Asistente Cortana."),
        new Entrada("Microsoft.Todos", "Microsoft To Do", "Moderado", "Lista de tareas."),
        new Entrada("Microsoft.PowerAutomateDesktop", "Power Automate", "Seguro", "Automatización de tareas."),
        new Entrada("Microsoft.WindowsSoundRecorder", "Grabadora de voz", "Seguro", "Grabación de audio."),
        new Entrada("Microsoft.WindowsAlarms", "Alarmas y reloj", "Moderado", "Alarmas, temporizador y cronómetro."),
        new Entrada("Microsoft.XboxApp", "Xbox Console Companion", "Seguro", "Aplicación de Xbox antigua."),
        new Entrada("Microsoft.GamingApp", "Xbox", "Moderado", "Aplicación Xbox y Game Pass."),
        new Entrada("Microsoft.XboxGameOverlay", "Xbox Game Bar (complemento)", "Moderado", "Superposición de juegos."),
        new Entrada("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "Moderado", "Barra de juegos (Win + G)."),
        new Entrada("Microsoft.XboxSpeechToTextOverlay", "Xbox: voz a texto", "Seguro", "Chat de voz en juegos."),
        new Entrada("Microsoft.XboxIdentityProvider", "Xbox: identidad", "Moderado", "Inicio de sesión en juegos de Xbox y Game Pass."),
        new Entrada("Microsoft.Xbox.TCUI", "Xbox: interfaz común", "Moderado", "La usan otros juegos de Xbox."),
        new Entrada("Microsoft.windowscommunicationsapps", "Correo y Calendario", "Moderado", "Correo y calendario de Windows."),
        new Entrada("Microsoft.Office.OneNote", "OneNote para Windows 10", "Moderado", "Bloc de notas digital."),
        new Entrada("Microsoft.Windows.Photos", "Fotos", "Moderado", "Visor de imágenes predeterminado."),
        new Entrada("Microsoft.WindowsCamera", "Cámara", "Moderado", "Necesaria para usar la webcam fuera de otras aplicaciones."),
        new Entrada("Microsoft.WindowsCalculator", "Calculadora", "Moderado", "Calculadora de Windows."),
        new Entrada("Microsoft.ScreenSketch", "Recortes y anotaciones", "Moderado", "Capturas de pantalla (Win + Mayús + S)."),
        new Entrada("Microsoft.MicrosoftStickyNotes", "Notas rápidas", "Moderado", "Notas adhesivas en el escritorio."),
        new Entrada("MicrosoftTeams", "Microsoft Teams (personal)", "Moderado", "Versión personal de Teams."),
        new Entrada("MSTeams", "Microsoft Teams", "Moderado", "Nueva versión de Teams."),
        new Entrada("Microsoft.OutlookForWindows", "Outlook (nuevo)", "Moderado", "Nueva aplicación de correo Outlook."),
        new Entrada("Microsoft.Windows.DevHome", "Dev Home", "Seguro", "Panel para programadores."),
        new Entrada("Clipchamp.Clipchamp", "Clipchamp", "Seguro", "Editor de vídeo."),
        new Entrada("SpotifyAB.SpotifyMusic", "Spotify", "Seguro", "Preinstalado por promoción."),
        new Entrada("king.com.*", "Juego de King (Candy Crush...)", "Seguro", "Juego preinstalado por promoción."),
        new Entrada("Disney.*", "Disney+", "Seguro", "Preinstalado por promoción."),
        new Entrada("Facebook.*", "Facebook", "Seguro", "Preinstalado por promoción."),
        new Entrada("*", null, "Desconocido", "No está en el catálogo del optimizador: comprueba qué es antes de eliminarla."),
    };

    static string Powershell(string script)
    {
        string codificado = Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding = [Text.Encoding]::UTF8\n" + script));
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + codificado)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using (Process p = Process.Start(psi))
        {
            string salida = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return salida;
        }
    }

    public static List<AppTienda> Leer()
    {
        string json = Powershell(
            "$a = Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and [string]$_.SignatureKind -ne 'System' } |" +
            " Select-Object Name, PackageFullName, Version\n" +
            "ConvertTo-Json -InputObject @($a) -Compress");
        var res = new List<AppTienda>();
        var lista = new JavaScriptSerializer().DeserializeObject(json.Trim()) as object[];
        if (lista == null) return res;
        foreach (Dictionary<string, object> d in lista.OfType<Dictionary<string, object>>())
        {
            string paquete = Convert.ToString(d["Name"]);
            if (Excluidas.Any(x => Coincide(x, paquete))) continue;
            Entrada e = Catalogo.First(c => c.Patron == "*" || c.Coincide(paquete));
            res.Add(new AppTienda
            {
                Paquete = paquete, NombreCompleto = Convert.ToString(d["PackageFullName"]), Version = Convert.ToString(d["Version"]),
                Nombre = e.Nombre ?? Bonito(paquete), Desc = e.Desc, Seguridad = e.Seguridad
            });
        }
        return res.OrderBy(a => a.Seguridad == "Seguro" ? 0 : a.Seguridad == "Moderado" ? 1 : 2).ThenBy(a => a.Nombre).ToList();
    }

    static bool Coincide(string patron, string paquete)
    {
        if (!patron.Contains("*")) return paquete.Equals(patron, StringComparison.OrdinalIgnoreCase);
        string regex = "^" + System.Text.RegularExpressions.Regex.Escape(patron).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(paquete, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // "AppleInc.iCloud" -> "iCloud"; "Microsoft.Foo" -> "Foo";
    // "outlook.office.com-4D813D4" (aplicación web instalada desde Edge) -> "outlook.office.com (aplicación web)"
    static string Bonito(string paquete)
    {
        var web = System.Text.RegularExpressions.Regex.Match(paquete, @"^(.+\.[a-z]+)-[0-9A-F]{7}$");
        if (web.Success) return web.Groups[1].Value + " (aplicación web)";
        int i = paquete.LastIndexOf('.');
        return i >= 0 && i < paquete.Length - 1 ? paquete.Substring(i + 1) : paquete;
    }

    // Elimina las aplicaciones para el usuario actual. Se pueden volver a instalar desde la Tienda.
    public static void Eliminar(IEnumerable<AppTienda> apps, Action<string> log)
    {
        var sb = new StringBuilder();
        foreach (AppTienda a in apps)
            sb.Append("try { Remove-AppxPackage -Package '").Append(a.NombreCompleto.Replace("'", "''"))
              .Append("' -ErrorAction Stop; 'OK|").Append(a.Paquete).Append("' } catch { 'ERR|").Append(a.Paquete)
              .Append("|' + $_.Exception.Message }\n");
        string salida = Powershell(sb.ToString());
        foreach (string linea in salida.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            string[] partes = linea.Split(new[] { '|' }, 3);
            AppTienda app = apps.FirstOrDefault(a => partes.Length > 1 && a.Paquete == partes[1]);
            string nombre = app != null ? app.Nombre : (partes.Length > 1 ? partes[1] : linea);
            if (partes[0] == "OK") log("App eliminada: " + nombre);
            else if (partes[0] == "ERR") log("ERROR al eliminar «" + nombre + "»: " + (partes.Length > 2 ? partes[2] : ""));
        }
    }
}
