using System;
using System.Linq;
using System.Management;

namespace WinFastGUI.Services
{
    /// <summary>
    /// VSS (Volume Shadow Copy Service) işlemlerini yöneten yardımcı sınıf.
    /// </summary>
    public static class VssSnapshotHelper
    {
        private const string WmiScope = @"\\.\root\cimv2";

        /// <summary>
        /// Belirtilen sürücü harfinin Volume GUID'sini WMI kullanarak alır.
        /// </summary>
        public static string? GetVolumeGuid(string drivePath, IProgress<BackupProgressReport>? progress = null)
        {
            try
            {
                string cleanDriveLetter = drivePath.Substring(0, 2);
                var options = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(30) };
                var scope = new ManagementScope(WmiScope, options);
                var query = new SelectQuery($"SELECT DeviceID FROM Win32_Volume WHERE DriveLetter = '{cleanDriveLetter}'");
                
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var volumeObject = searcher.Get().OfType<ManagementObject>().FirstOrDefault();

                if (volumeObject?["DeviceID"] is string deviceId && !string.IsNullOrEmpty(deviceId))
                {
                    progress?.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"WMI ile Volume GUID bulundu: {deviceId}" });
                    return deviceId;
                }
                return null;
            }
            catch (Exception ex)
            {
                progress?.Report(new BackupProgressReport { Type = ReportType.Error, Message = $"GetVolumeGuid hata: {ex.Message}" });
                return null;
            }
        }

        /// <summary>
        /// Belirtilen volume için bir VSS anlık görüntüsü oluşturur.
        /// </summary>
        public static string? CreateVssSnapshot(string volume, out string shadowId, IProgress<BackupProgressReport>? progress = null)
        {
            shadowId = string.Empty;
            try
            {
                var options = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(90) };
                var scope = new ManagementScope(WmiScope, options);
                var managementPath = new ManagementPath("Win32_ShadowCopy");

                // UYARI GİDERİLDİ: Derleyiciye bu 'null' değerinin kasıtlı olduğunu bildirmek için '!' eklendi.
                using var classInstance = new ManagementClass(scope, managementPath, null!);
                using var inParams = classInstance.GetMethodParameters("Create");
                
                inParams["Volume"] = volume;
                inParams["Context"] = "ClientAccessible";

                // UYARI GİDERİLDİ: Derleyiciye bu 'null' değerinin kasıtlı olduğunu bildirmek için '!' eklendi.
                using ManagementBaseObject outParams = classInstance.InvokeMethod("Create", inParams, null!);

                if ((uint)outParams["ReturnValue"] != 0)
                {
                    throw new ManagementException($"VSS snapshot oluşturma başarısız. WMI Hata Kodu: {outParams["ReturnValue"]}");
                }

                shadowId = outParams["ShadowID"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(shadowId))
                {
                    throw new Exception("VSS snapshot oluşturuldu ancak bir ShadowID döndürmedi.");
                }
                
                progress?.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"Snapshot başarıyla oluşturuldu. ID: {shadowId}" });

                using (var searcher = new ManagementObjectSearcher(scope, new SelectQuery($"SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID = '{shadowId}'")))
                {
                    using var shadowObject = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                    if (shadowObject?["DeviceObject"] is string deviceObject && !string.IsNullOrEmpty(deviceObject))
                    {
                        progress?.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"Cihaz Yolu: {deviceObject}" });
                        return deviceObject;
                    }
                }
                
                throw new Exception("Oluşturulan snapshot'ın Cihaz Yolu (DeviceObject) bulunamadı.");
            }
            catch (Exception ex)
            {
                progress?.Report(new BackupProgressReport { Type = ReportType.Error, Message = $"VSS snapshot hata: {ex.Message}" });
                return null;
            }
        }
    }
}