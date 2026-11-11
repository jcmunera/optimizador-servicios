// Optimizador de Servicios - perfiles de optimización completa (modo perfil).
// Cada perfil indica qué se aplica en cada sección: servicios, efectos y ajustes,
// tareas programadas, apps de la Tienda y limpieza de disco.
using System;
using System.Linq;

class PerfilOptimizacion
{
    public string Id, Nombre, Desc, Glifo;
    public Perfil Servicios;                  // perfil de servicios del catálogo
    public Func<Ajuste, bool> Ajustes;        // ajustes que se aplican
    public bool Tareas, AppsSeguras, Limpieza;
    public string[] Resumen;                  // qué hace, en frases cortas
}

static class PerfilesOptimizacion
{
    public static readonly PerfilOptimizacion[] Todos =
    {
        new PerfilOptimizacion
        {
            Id = "aulas", Nombre = "Aula", Glifo = "",
            Desc = "Equipos compartidos de un aula o biblioteca: máxima ligereza.",
            Servicios = Perfiles.Buscar("aulas"), Ajustes = a => a.Recomendado,
            Tareas = true, AppsSeguras = true, Limpieza = true,
            Resumen = new[] { "Servicios recomendados, geolocalización y biometría", "Todos los ajustes recomendados",
                              "Tareas de telemetría y diagnóstico", "Apps de la Tienda seguras de eliminar", "Limpieza de disco" }
        },
        new PerfilOptimizacion
        {
            Id = "oficina", Nombre = "Oficina", Glifo = "",
            Desc = "Puestos de trabajo: mantiene la búsqueda de Outlook y los avisos de Correo y Calendario.",
            Servicios = Perfiles.Buscar("oficina"), Ajustes = a => a.Recomendado && a.Id != "segundoplano",
            Tareas = true, AppsSeguras = false, Limpieza = true,
            Resumen = new[] { "Servicios recomendados, salvo Windows Search", "Ajustes recomendados, salvo apps en segundo plano",
                              "Tareas de telemetría y diagnóstico", "Limpieza de disco" }
        },
        new PerfilOptimizacion
        {
            Id = "domestico", Nombre = "Doméstico", Glifo = "",
            Desc = "Equipo personal o familiar: todo lo recomendado, sin eliminar aplicaciones.",
            Servicios = Perfiles.Buscar("recomendado"), Ajustes = a => a.Recomendado,
            Tareas = true, AppsSeguras = false, Limpieza = true,
            Resumen = new[] { "Servicios recomendados", "Todos los ajustes recomendados",
                              "Tareas de telemetría y diagnóstico", "Limpieza de disco" }
        },
        new PerfilOptimizacion
        {
            Id = "conservador", Nombre = "Conservador", Glifo = "",
            Desc = "Solo telemetría, anuncios y extras: el usuario no nota ningún cambio.",
            Servicios = Perfiles.Buscar("conservador"), Ajustes = a => a.Recomendado && a.Categoria == global::Ajustes.Privacidad,
            Tareas = true, AppsSeguras = false, Limpieza = true,
            Resumen = new[] { "Servicios de telemetría, diagnóstico y extras", "Ajustes de privacidad y anuncios",
                              "Tareas de telemetría y diagnóstico", "Limpieza de disco" }
        },
    };
}
