// Optimizador de Servicios - ajustes de Windows reversibles: efectos visuales, rendimiento
// y privacidad. Antes de aplicar un ajuste se guarda su estado original en
// %ProgramData%\OptimizadorServicios\ajustes-backup.json para poder revertirlo.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// Estado original de un valor guardado en la copia de seguridad.
public class Original
{
    public string Clave { get; set; }
    public bool Existe { get; set; }
    public string Tipo { get; set; }
    public string Valor { get; set; }
}

abstract class Ajuste
{
    public string Id, Nombre, Desc, Categoria, Riesgo, Nota;
    public bool Recomendado;

    public abstract bool Aplicado();
    public abstract List<Original> Guardar();
    public abstract void Hacer();
    public abstract void Restaurar(List<Original> originales);
}

// --- Ajustes de registro --------------------------------------------------------

class ValorRegistro
{
    public RegistryHive Hive;
    public string Clave, Nombre;
    public RegistryValueKind Tipo;
    public object Optimo;
    public Func<object, bool> EsOptimo;     // comprobación propia (opcional)

    public string Id { get { return (Hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\") + Clave + "|" + Nombre; } }

    public ValorRegistro(RegistryHive hive, string clave, string nombre, RegistryValueKind tipo, object optimo)
    {
        Hive = hive; Clave = clave; Nombre = nombre; Tipo = tipo; Optimo = optimo;
    }
}

static class Reg
{
    static RegistryKey Base(RegistryHive h) { return RegistryKey.OpenBaseKey(h, RegistryView.Registry64); }

    public static object Leer(ValorRegistro v)
    {
        using (RegistryKey b = Base(v.Hive))
        using (RegistryKey k = b.OpenSubKey(v.Clave))
            return k == null ? null : k.GetValue(v.Nombre, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public static RegistryValueKind? TipoActual(ValorRegistro v)
    {
        using (RegistryKey b = Base(v.Hive))
        using (RegistryKey k = b.OpenSubKey(v.Clave))
        {
            if (k == null || k.GetValue(v.Nombre) == null) return null;
            return k.GetValueKind(v.Nombre);
        }
    }

    public static void Escribir(ValorRegistro v, object valor, RegistryValueKind tipo)
    {
        using (RegistryKey b = Base(v.Hive))
        {
            if (valor == null)
            {
                using (RegistryKey k = b.OpenSubKey(v.Clave, true))
                    if (k != null) k.DeleteValue(v.Nombre, false);
                return;
            }
            using (RegistryKey k = b.CreateSubKey(v.Clave))
                k.SetValue(v.Nombre, valor, tipo);
        }
    }

    public static bool Igual(object a, object b)
    {
        if (a == null || b == null) return a == b;
        var ba = a as byte[];
        var bb = b as byte[];
        if (ba != null || bb != null) return ba != null && bb != null && ba.SequenceEqual(bb);
        return Convert.ToString(a) == Convert.ToString(b);
    }
}

class AjusteRegistro : Ajuste
{
    public readonly List<ValorRegistro> Valores = new List<ValorRegistro>();

    public AjusteRegistro Valor(RegistryHive hive, string clave, string nombre, RegistryValueKind tipo, object optimo, Func<object, bool> esOptimo = null)
    {
        Valores.Add(new ValorRegistro(hive, clave, nombre, tipo, optimo) { EsOptimo = esOptimo });
        return this;
    }

    public override bool Aplicado()
    {
        foreach (ValorRegistro v in Valores)
        {
            object actual = Reg.Leer(v);
            if (v.EsOptimo != null ? !v.EsOptimo(actual) : !Reg.Igual(actual, v.Optimo)) return false;
        }
        return true;
    }

    public override List<Original> Guardar()
    {
        var res = new List<Original>();
        foreach (ValorRegistro v in Valores)
        {
            object actual = Reg.Leer(v);
            RegistryValueKind? tipo = Reg.TipoActual(v);
            var o = new Original { Clave = v.Id, Existe = actual != null };
            if (actual != null)
            {
                o.Tipo = tipo.HasValue ? tipo.Value.ToString() : v.Tipo.ToString();
                var bytes = actual as byte[];
                o.Valor = bytes != null ? Convert.ToBase64String(bytes) : Convert.ToString(actual);
            }
            res.Add(o);
        }
        return res;
    }

    public override void Hacer()
    {
        foreach (ValorRegistro v in Valores) Reg.Escribir(v, v.Optimo, v.Tipo);
    }

    public override void Restaurar(List<Original> originales)
    {
        foreach (ValorRegistro v in Valores)
        {
            Original o = originales.FirstOrDefault(x => x.Clave == v.Id);
            if (o == null) continue;
            if (!o.Existe) { Reg.Escribir(v, null, v.Tipo); continue; }
            var tipo = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), o.Tipo);
            object valor;
            switch (tipo)
            {
                case RegistryValueKind.DWord: valor = unchecked((int)long.Parse(o.Valor)); break;
                case RegistryValueKind.QWord: valor = long.Parse(o.Valor); break;
                case RegistryValueKind.Binary: valor = Convert.FromBase64String(o.Valor); break;
                case RegistryValueKind.MultiString: valor = o.Valor.Split('\n'); break;
                default: valor = o.Valor; break;
            }
            Reg.Escribir(v, valor, tipo);
        }
    }
}

// --- Ajustes especiales ---------------------------------------------------------

static class Comando
{
    public static string Ejecutar(string exe, string argumentos)
    {
        var psi = new ProcessStartInfo(exe, argumentos)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (Process p = Process.Start(psi))
        {
            string salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException(exe + " " + argumentos + ": " + salida.Trim());
            return salida;
        }
    }
}

// Plan de energía "Alto rendimiento". Si el equipo no lo tiene (algunos portátiles),
// se crea una copia y se recuerda su GUID para poder revertirlo.
class AjustePlanEnergia : Ajuste
{
    const string AltoRendimiento = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    static string Guid(string texto)
    {
        Match m = Regex.Match(texto, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    static string Activo() { return Guid(Comando.Ejecutar("powercfg.exe", "/getactivescheme")); }

    public override bool Aplicado()
    {
        try
        {
            string activo = Activo();
            if (activo == AltoRendimiento) return true;
            Original creado = Ajustes.Copia(Id).FirstOrDefault(o => o.Clave == "creado");
            return creado != null && creado.Valor == activo;
        }
        catch { return false; }
    }

    public override List<Original> Guardar()
    {
        return new List<Original> { new Original { Clave = "plan", Existe = true, Valor = Activo() } };
    }

    public override void Hacer()
    {
        try { Comando.Ejecutar("powercfg.exe", "/setactive " + AltoRendimiento); }
        catch
        {
            string nuevo = Guid(Comando.Ejecutar("powercfg.exe", "-duplicatescheme " + AltoRendimiento));
            Comando.Ejecutar("powercfg.exe", "/setactive " + nuevo);
            Ajustes.AnadirOriginal(Id, new Original { Clave = "creado", Existe = true, Valor = nuevo });
        }
    }

    public override void Restaurar(List<Original> originales)
    {
        Original plan = originales.FirstOrDefault(o => o.Clave == "plan");
        if (plan != null && plan.Valor != null) Comando.Ejecutar("powercfg.exe", "/setactive " + plan.Valor);
        Original creado = originales.FirstOrDefault(o => o.Clave == "creado");
        if (creado != null) try { Comando.Ejecutar("powercfg.exe", "/delete " + creado.Valor); } catch { }
    }
}

// Hibernación: libera el archivo hiberfil.sys (varios GB). También desactiva el inicio rápido.
class AjusteHibernacion : Ajuste
{
    static bool Activa()
    {
        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power"))
            return k == null || Convert.ToInt32(k.GetValue("HibernateEnabled", 1)) != 0;
    }

    public override bool Aplicado() { return !Activa(); }

    public override List<Original> Guardar()
    {
        return new List<Original> { new Original { Clave = "hibernacion", Existe = Activa() } };
    }

    public override void Hacer() { Comando.Ejecutar("powercfg.exe", "/hibernate off"); }

    public override void Restaurar(List<Original> originales)
    {
        Original o = originales.FirstOrDefault(x => x.Clave == "hibernacion");
        if (o != null && o.Existe) Comando.Ejecutar("powercfg.exe", "/hibernate on");
    }
}

// --- Catálogo y operaciones -----------------------------------------------------

static class Ajustes
{
    const string ArchivoCopia = "ajustes-backup.json";
    const RegistryHive HKCU = RegistryHive.CurrentUser;
    const RegistryHive HKLM = RegistryHive.LocalMachine;
    const RegistryValueKind DW = RegistryValueKind.DWord;
    const RegistryValueKind SZ = RegistryValueKind.String;

    public const string Visuales = "Efectos visuales";
    public const string Rendimiento = "Rendimiento";
    public const string Privacidad = "Privacidad y anuncios";

    static AjusteRegistro R(string id, string categoria, string nombre, string desc, string riesgo, bool recomendado, string nota = null)
    {
        return new AjusteRegistro { Id = id, Categoria = categoria, Nombre = nombre, Desc = desc, Riesgo = riesgo, Recomendado = recomendado, Nota = nota };
    }

    const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string Desktop = @"Control Panel\Desktop";
    const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public static readonly List<Ajuste> Todos = new List<Ajuste>
    {
        // Efectos visuales
        R("efectos", Visuales, "Animaciones y efectos de la interfaz",
          "Desactiva los desvanecimientos de menús e información sobre herramientas, las sombras y las animaciones de controles. Mantiene el suavizado de fuentes y las miniaturas.",
          "Seguro", true, "Tras cerrar sesión")
            .Valor(HKCU, Desktop, "UserPreferencesMask", RegistryValueKind.Binary, new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 })
            .Valor(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", DW, 3)
            .Valor(HKCU, Advanced, "ListviewAlphaSelect", DW, 0)
            .Valor(HKCU, Advanced, "ListviewShadow", DW, 0),
        R("minimizar", Visuales, "Animación al minimizar y maximizar", "Las ventanas aparecen y desaparecen al instante.", "Seguro", true, "Tras cerrar sesión")
            .Valor(HKCU, Desktop + @"\WindowMetrics", "MinAnimate", SZ, "0"),
        R("barratareas", Visuales, "Animaciones de la barra de tareas", "Quita las animaciones de los botones de la barra de tareas.", "Seguro", true, "Tras reiniciar")
            .Valor(HKCU, Advanced, "TaskbarAnimations", DW, 0),
        R("transparencia", Visuales, "Transparencias", "Menú Inicio, barra de tareas y centro de actividades opacos. Ahorra trabajo a la tarjeta gráfica.", "Seguro", true)
            .Valor(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", DW, 0),
        R("peek", Visuales, "Aero Peek", "Desactiva la vista previa del escritorio al pasar el ratón por la esquina de la barra de tareas.", "Seguro", true, "Tras reiniciar")
            .Valor(HKCU, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", DW, 0),
        R("arrastrar", Visuales, "Contenido de las ventanas al arrastrarlas", "Al mover una ventana se muestra solo su contorno, sin redibujar el contenido.", "Seguro", false, "Tras cerrar sesión")
            .Valor(HKCU, Desktop, "DragFullWindows", SZ, "0"),
        R("menus", Visuales, "Retardo de los menús", "Los submenús se abren antes (200 ms en lugar de 400 ms).", "Seguro", true, "Tras cerrar sesión")
            .Valor(HKCU, Desktop, "MenuShowDelay", SZ, "200", v => { int n; return v != null && int.TryParse(Convert.ToString(v), out n) && n <= 200; }),

        // Rendimiento
        R("segundoplano", Rendimiento, "Aplicaciones en segundo plano",
          "Impide que las aplicaciones de la Tienda sigan funcionando cuando no se usan. Algunas, como Correo o Calendario, dejarán de avisar.",
          "Moderado", true)
            .Valor(HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", DW, 1)
            .Valor(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", DW, 0),
        R("gamebar", Rendimiento, "Game Bar y grabación de juegos", "Desactiva la grabación en segundo plano de juegos (Xbox Game Bar), que consume CPU y disco.", "Seguro", true)
            .Valor(HKCU, @"System\GameConfigStore", "GameDVR_Enabled", DW, 0)
            .Valor(HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", DW, 0),
        R("ultimoacceso", Rendimiento, "Fecha de último acceso de los archivos",
          "NTFS deja de anotar la fecha de cada lectura de archivo, lo que ahorra escrituras en el disco.", "Seguro", true, "Tras reiniciar")
            .Valor(HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", DW, 1,
                   v => v != null && (Convert.ToInt64(v) & 1) == 1),
        new AjustePlanEnergia
        {
            Id = "energia", Categoria = Rendimiento, Nombre = "Plan de energía de alto rendimiento",
            Desc = "El procesador no reduce su velocidad. En portátiles gasta más batería.", Riesgo = "Moderado", Recomendado = false
        },
        new AjusteHibernacion
        {
            Id = "hibernacion", Categoria = Rendimiento, Nombre = "Hibernación",
            Desc = "Libera el archivo de hibernación (varios GB de disco). También desactiva el inicio rápido, que en discos duros acelera el encendido.",
            Riesgo = "Moderado", Recomendado = false
        },

        // Privacidad y anuncios
        R("sugerencias", Privacidad, "Sugerencias, consejos y anuncios de Windows",
          "Quita las sugerencias del menú Inicio y la instalación automática de aplicaciones promocionadas.", "Seguro", true)
            .Valor(HKCU, Cdm, "SubscribedContent-338388Enabled", DW, 0)
            .Valor(HKCU, Cdm, "SubscribedContent-338389Enabled", DW, 0)
            .Valor(HKCU, Cdm, "SubscribedContent-353694Enabled", DW, 0)
            .Valor(HKCU, Cdm, "SubscribedContent-353696Enabled", DW, 0)
            .Valor(HKCU, Cdm, "SystemPaneSuggestionsEnabled", DW, 0)
            .Valor(HKCU, Cdm, "SilentInstalledAppsEnabled", DW, 0)
            .Valor(HKCU, Cdm, "SoftLandingEnabled", DW, 0),
        R("publicidad", Privacidad, "Identificador de publicidad", "Las aplicaciones no pueden usar un identificador para mostrarte anuncios personalizados.", "Seguro", true)
            .Valor(HKCU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", DW, 0),
        R("telemetria", Privacidad, "Telemetría de Windows (directiva)",
          "Reduce los datos de diagnóstico que se envían a Microsoft al nivel mínimo que permite cada edición.", "Seguro", true)
            .Valor(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", DW, 0),
        R("cortana", Privacidad, "Cortana", "Desactiva Cortana. La búsqueda de Windows sigue funcionando.", "Seguro", true, "Tras reiniciar")
            .Valor(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", DW, 0),
    };

    // --- Copia de seguridad ---

    static Dictionary<string, List<Original>> LeerCopia() { return Almacen.LeerJson<Dictionary<string, List<Original>>>(ArchivoCopia); }

    public static List<Original> Copia(string id)
    {
        List<Original> l;
        return LeerCopia().TryGetValue(id, out l) ? l : new List<Original>();
    }

    public static bool TieneCopia(string id) { return LeerCopia().ContainsKey(id); }

    public static int Cambiados { get { return LeerCopia().Count; } }

    public static void AnadirOriginal(string id, Original o)
    {
        var copia = LeerCopia();
        List<Original> l;
        if (!copia.TryGetValue(id, out l)) copia[id] = l = new List<Original>();
        l.Add(o);
        Almacen.GuardarJson(ArchivoCopia, copia);
    }

    public static void Aplicar(Ajuste a, Action<string> log)
    {
        try
        {
            var copia = LeerCopia();
            if (!copia.ContainsKey(a.Id))
            {
                copia[a.Id] = a.Guardar();
                Almacen.GuardarJson(ArchivoCopia, copia);
            }
            a.Hacer();
            log("Ajuste aplicado: " + a.Nombre);
        }
        catch (Exception ex) { log("ERROR al aplicar «" + a.Nombre + "»: " + ex.Message); }
    }

    public static void Revertir(Ajuste a, Action<string> log)
    {
        try
        {
            var copia = LeerCopia();
            List<Original> originales;
            if (!copia.TryGetValue(a.Id, out originales)) { log("Sin copia de seguridad: no se puede revertir «" + a.Nombre + "»."); return; }
            a.Restaurar(originales);
            copia.Remove(a.Id);
            Almacen.GuardarJson(ArchivoCopia, copia);
            log("Ajuste revertido: " + a.Nombre);
        }
        catch (Exception ex) { log("ERROR al revertir «" + a.Nombre + "»: " + ex.Message); }
    }

    public static void RevertirTodos(Action<string> log)
    {
        var copia = LeerCopia();
        foreach (Ajuste a in Todos.Where(x => copia.ContainsKey(x.Id))) Revertir(a, log);
    }
}
