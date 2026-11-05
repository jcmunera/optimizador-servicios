// Optimizador de Servicios - modo línea de comandos.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;
// --- Línea de comandos (despliegue en varios equipos) ---------------------------

static class Cli
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int pid);

    static string logExtra;
    static int errores;

    const string TextoAyuda =
@"Optimizador de Servicios - modo línea de comandos

Uso (desde una consola de administrador, un script de inicio de GPO, Intune, etc.):
  OptimizadorServicios.exe /aplicar:<perfil | archivo.txt | Serv1,Serv2> [/punto] [/reiniciar] [/log:<ruta>]
  OptimizadorServicios.exe /restablecer [/reiniciar] [/log:<ruta>]
  OptimizadorServicios.exe /estado [/log:<ruta>]
  OptimizadorServicios.exe /perfiles
  OptimizadorServicios.exe /ayuda

  /aplicar      Deshabilita los servicios de un perfil, de un archivo de texto
                (un nombre por línea, # para comentarios) o de una lista separada por comas.
                Solo se admiten servicios del catálogo. Se puede ejecutar en cada
                arranque: los que ya están deshabilitados se omiten.
  /restablecer  Devuelve todos los servicios modificados a su estado original.
  /estado       Muestra el estado de los servicios del catálogo (útil para inventario).
  /punto        Crea un punto de restauración antes de aplicar.
  /reiniciar    Reinicia el equipo en 60 s si se ha cambiado algo.
  /log:<ruta>   Copia el registro a otro archivo (admite %COMPUTERNAME%, rutas de red...).

Códigos de salida: 0 = correcto, 1 = algún servicio falló, 2 = argumentos no válidos.
Desde cmd usa 'start """" /wait OptimizadorServicios.exe ...' para esperar y obtener el código.";

    static void Salida(string texto)
    {
        Console.WriteLine(texto);
        Almacen.Log(texto);
        if (texto.StartsWith("ERROR")) errores++;
        if (logExtra == null) return;
        try
        {
            File.AppendAllText(logExtra, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " +
                               Environment.MachineName + "  " + texto + Environment.NewLine);
        }
        catch { }
    }

    public static int Ejecutar(string[] args)
    {
        AttachConsole(-1);   // escribe en la consola desde la que se lanzó, si la hay
        Console.WriteLine();

        string accion = null, valor = null;
        bool punto = false, reiniciar = false;
        foreach (string a in args)
        {
            string arg = a.TrimStart('/', '-');
            int dosPuntos = arg.IndexOf(':');
            string clave = dosPuntos < 0 ? arg : arg.Substring(0, dosPuntos);
            string val = dosPuntos < 0 ? null : arg.Substring(dosPuntos + 1).Trim('"');
            switch (clave.ToLowerInvariant())
            {
                case "aplicar": accion = "aplicar"; valor = val; break;
                case "restablecer": accion = "restablecer"; break;
                case "estado": accion = "estado"; break;
                case "perfiles": accion = "perfiles"; break;
                case "ayuda": case "?": case "h": accion = "ayuda"; break;
                case "punto": punto = true; break;
                case "reiniciar": reiniciar = true; break;
                case "log":
                    if (string.IsNullOrEmpty(val)) return Error("Falta la ruta en /log:<ruta>");
                    logExtra = Environment.ExpandEnvironmentVariables(val);
                    break;
                default: return Error("Argumento no reconocido: " + a);
            }
        }

        switch (accion)
        {
            case "aplicar": return Aplicar(valor, punto, reiniciar);
            case "restablecer": return Restablecer(reiniciar);
            case "estado": return MostrarEstado();
            case "perfiles": return MostrarPerfiles();
            case "ayuda": Console.WriteLine(TextoAyuda); return 0;
            default: return Error("Indica una acción: /aplicar, /restablecer, /estado o /perfiles");
        }
    }

    static int Error(string texto)
    {
        Console.WriteLine(texto);
        Console.WriteLine();
        Console.WriteLine(TextoAyuda);
        return 2;
    }

    // Convierte el valor de /aplicar en nombres del catálogo que existen en este equipo.
    static List<string> Resolver(string valor)
    {
        List<ServicioInfo> existentes = Catalogo.Todos.Where(s => Servicios.Existe(s.Nombre)).ToList();
        Perfil perfil = Perfiles.Buscar(valor);
        if (perfil != null)
        {
            Salida("Perfil: " + perfil.Nombre);
            return existentes.Where(perfil.Incluye).Select(s => s.Nombre).ToList();
        }

        IEnumerable<string> pedidos;
        if (File.Exists(valor))
        {
            Salida("Archivo de servicios: " + valor);
            pedidos = File.ReadAllLines(valor).Select(l => l.Split('#')[0].Trim());
        }
        else pedidos = valor.Split(',').Select(n => n.Trim());

        var res = new List<string>();
        foreach (string n in pedidos.Where(n => n.Length > 0))
        {
            ServicioInfo s = existentes.FirstOrDefault(x => string.Equals(x.Nombre, n, StringComparison.OrdinalIgnoreCase));
            if (s == null) Salida("Omitido (no está en el catálogo o no existe en este equipo): " + n);
            else if (!res.Contains(s.Nombre)) res.Add(s.Nombre);
        }
        return res;
    }

    static int Aplicar(string valor, bool punto, bool reiniciar)
    {
        if (string.IsNullOrEmpty(valor)) return Error("Falta el perfil o la lista en /aplicar:<...>");
        List<string> nombres = Resolver(valor);
        if (nombres.Count == 0) { Salida("No hay servicios que aplicar."); return 0; }

        var backup = Almacen.LeerBackup();
        List<string> pendientes = nombres.Where(n => !(Servicios.GetStart(n) == 4 && backup.ContainsKey(n))).ToList();
        foreach (string n in nombres.Except(pendientes)) Salida("Ya deshabilitado: " + n);
        if (pendientes.Count == 0) { Salida("Sin cambios: todo estaba ya aplicado."); return 0; }

        if (punto) Operaciones.CrearPuntoRestauracion(Salida);
        foreach (string n in pendientes) Operaciones.Desactivar(n, Salida);
        Almacen.GuardarEstado(new Estado { UltimoCambio = DateTime.Now.ToString("o") });
        Salida("Aplicado: " + (pendientes.Count - errores) + " servicios deshabilitados, " + errores + " errores.");

        if (reiniciar) { Salida("Reiniciando el equipo en 60 s."); Sistema.Reiniciar(60); }
        return errores > 0 ? 1 : 0;
    }

    static int Restablecer(bool reiniciar)
    {
        int cambiados = Almacen.LeerBackup().Count;
        if (cambiados == 0) { Salida("No hay cambios que restablecer."); return 0; }
        Operaciones.Restaurar(Salida);
        Almacen.GuardarEstado(new Estado { UltimoCambio = DateTime.Now.ToString("o") });
        Salida("Restablecido: " + (cambiados - errores) + " servicios, " + errores + " errores.");

        if (reiniciar) { Salida("Reiniciando el equipo en 60 s."); Sistema.Reiniciar(60); }
        return errores > 0 ? 1 : 0;
    }

    static int MostrarEstado()
    {
        var backup = Almacen.LeerBackup();
        Salida(string.Format("{0,-42} {1,-14} {2,-14} {3}", "SERVICIO", "INICIO", "ESTADO", "CAMBIADO"));
        foreach (ServicioInfo s in Catalogo.Todos.Where(x => Servicios.Existe(x.Nombre)))
            Salida(string.Format("{0,-42} {1,-14} {2,-14} {3}", s.Nombre,
                                 Servicios.TipoInicio(Servicios.GetStart(s.Nombre)),
                                 Servicios.EnEjecucion(s.Nombre) ? "En ejecución" : "Detenido",
                                 backup.ContainsKey(s.Nombre) ? "Sí" : ""));
        return 0;
    }

    static int MostrarPerfiles()
    {
        foreach (Perfil p in Perfiles.Todos)
        {
            Console.WriteLine(p.Id + "  -  " + p.Nombre + ": " + p.Desc);
            Console.WriteLine("    " + string.Join(", ", Catalogo.Todos.Where(p.Incluye).Select(s => s.Nombre)));
            Console.WriteLine();
        }
        return 0;
    }
}

