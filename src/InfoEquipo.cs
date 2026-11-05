// Optimizador de Servicios - información del equipo para el Panel.
using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using Microsoft.Win32;

class InfoEquipo
{
    public string Equipo, Windows, Cpu, DiscoModelo, Unidad;
    public string DiscoTipo = "Desconocido";     // HDD, SSD o Desconocido
    public int Nucleos;
    public double RamGB, DiscoTotalGB, DiscoLibreGB;

    public bool EsHdd { get { return DiscoTipo == "HDD"; } }
    public double DiscoLibrePct { get { return DiscoTotalGB > 0 ? DiscoLibreGB * 100 / DiscoTotalGB : 100; } }

    public static InfoEquipo Leer()
    {
        var info = new InfoEquipo { Equipo = Environment.MachineName, Nucleos = Environment.ProcessorCount };

        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            string producto = k == null ? "Windows" : (k.GetValue("ProductName") as string ?? "Windows");
            string version = k == null ? null : (k.GetValue("DisplayVersion") as string ?? k.GetValue("ReleaseId") as string);
            string build = k == null ? "" : (k.GetValue("CurrentBuild") as string ?? "");
            int numero;
            // Windows 11 sigue declarándose "Windows 10" en ProductName.
            if (int.TryParse(build, out numero) && numero >= 22000) producto = producto.Replace("Windows 10", "Windows 11");
            info.Windows = producto + (version != null ? " " + version : "") + (build.Length > 0 ? " (compilación " + build + ")" : "");
        }

        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            info.Cpu = k == null ? "Procesador" : ((k.GetValue("ProcessorNameString") as string) ?? "Procesador").Trim();

        double total, libre;
        Medidor.Memoria(out total, out libre);
        info.RamGB = total / 1024;

        string sistema = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
        info.Unidad = sistema;
        try
        {
            var d = new DriveInfo(sistema);
            info.DiscoTotalGB = d.TotalSize / 1073741824.0;
            info.DiscoLibreGB = d.AvailableFreeSpace / 1073741824.0;
        }
        catch { }
        LeerTipoDisco(info, sistema[0]);
        return info;
    }

    // MediaType de MSFT_PhysicalDisk: 3 = HDD, 4 = SSD. BusType 17 = NVMe (siempre SSD).
    static void LeerTipoDisco(InfoEquipo info, char letra)
    {
        try
        {
            var ambito = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            string disco = null;
            using (var s = new ManagementObjectSearcher(ambito, new ObjectQuery("SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '" + letra + "'")))
                foreach (ManagementObject o in s.Get()) { disco = Convert.ToString(o["DiskNumber"]); break; }
            if (disco == null) return;
            using (var s = new ManagementObjectSearcher(ambito, new ObjectQuery("SELECT FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk WHERE DeviceId = '" + disco + "'")))
                foreach (ManagementObject o in s.Get())
                {
                    info.DiscoModelo = Convert.ToString(o["FriendlyName"]);
                    int media = Convert.ToInt32(o["MediaType"]), bus = Convert.ToInt32(o["BusType"]);
                    info.DiscoTipo = media == 3 ? "HDD" : media == 4 || bus == 17 ? "SSD" : "Desconocido";
                    break;
                }
        }
        catch { }
    }
}

// Uso instantáneo de CPU, RAM y disco: cada llamada compara con la anterior.
class MonitorSistema
{
    public double CpuPct, RamPct, RamUsadaGB, DiscoPct;

    long inactivoAnt, totalAnt;
    Dictionary<string, ulong[]> discosAnt;

    public void Muestrear()
    {
        long inactivo, total;
        Medidor.TiemposCpu(out inactivo, out total);
        if (totalAnt > 0 && total > totalAnt)
            CpuPct = Math.Max(0, Math.Min(100, 100.0 * (1 - (double)(inactivo - inactivoAnt) / (total - totalAnt))));
        inactivoAnt = inactivo;
        totalAnt = total;

        double totalMB, libreMB;
        Medidor.Memoria(out totalMB, out libreMB);
        RamUsadaGB = (totalMB - libreMB) / 1024;
        RamPct = totalMB > 0 ? (totalMB - libreMB) * 100 / totalMB : 0;

        double cola;
        var discos = Medidor.LeerDiscos(out cola);
        if (discosAnt != null)
        {
            double ocupado = Medidor.DiscoOcupado(discosAnt, discos);
            if (ocupado >= 0) DiscoPct = ocupado;
        }
        discosAnt = discos;
    }
}
