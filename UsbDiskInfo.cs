using System;
using System.Collections.Generic;
using System.Management;
using System.Linq;

public class UsbDiskInfo
{
    public string? DeviceID { get; set; }    // Örn: \\.\PHYSICALDRIVE2
    public string? Model { get; set; }       // Örn: "SanDisk Ultra"
    public ulong Size { get; set; }         // Byte
    public string? DriveLetter { get; set; } // Örn: "E"

    public override string ToString()
    {
        double gb = Math.Round(Size / 1024.0 / 1024 / 1024, 2);
        return $"{DriveLetter ?? "Bilinmeyen Harf"}: {Model ?? "Bilinmeyen Model"} ({DeviceID ?? "Bilinmeyen ID"}, {gb} GB)";
    }

    public static List<UsbDiskInfo> ListUsbDrives()
    {
        var list = new List<UsbDiskInfo>();
        try
        {
            var diskSearcher = new ManagementObjectSearcher(@"SELECT DeviceID, Model, Size FROM Win32_DiskDrive WHERE InterfaceType='USB'");
            foreach (ManagementObject drive in diskSearcher.Get())
            {
                string? deviceId = drive["DeviceID"]?.ToString();
                if (string.IsNullOrEmpty(deviceId)) continue;

                // Diskten bölüme, oradan sürücü harfine ulaş
                string? driveLetter = null;
                var partitionSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (ManagementObject partition in partitionSearcher.Get())
                {
                    var logicalDiskSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                    foreach (ManagementObject logicalDisk in logicalDiskSearcher.Get())
                    {
                        driveLetter = logicalDisk["DeviceID"]?.ToString();
                        break;
                    }
                    if (!string.IsNullOrEmpty(driveLetter)) break;
                }

                list.Add(new UsbDiskInfo
                {
                    DeviceID = deviceId,
                    Model = drive["Model"]?.ToString() ?? "Bilinmeyen Model",
                    Size = drive["Size"] != null ? (ulong)drive["Size"] : 0,
                    DriveLetter = driveLetter
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"USB sürücüler listelenirken hata: {ex.Message}");
        }
        return list;
    }
}