using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.Management;
using System.Threading;

public static class UsbRawWriter
{
    public static async Task WriteIsoToUsbAsync(string usbDeviceId, string isoPath, string usbDriveLetter, Action<long, long>? progressCallback = null, Action<string>? logCallback = null)
    {
        // 1. Yönetici yetkilerini kontrol et
        try
        {
            string testFile = Path.Combine(Path.GetTempPath(), "test.txt");
            await File.WriteAllTextAsync(testFile, "test");
            File.Delete(testFile);
            logCallback?.Invoke("Yönetici yetkileri doğrulandı.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Yönetici yetkileri eksik. NSudo ile SYSTEM yetkileriyle çalıştırın: {ex.Message}");
        }

        // 2. USB’nin kilidini ve varlığını kontrol et
        logCallback?.Invoke($"USB sürücü kontrol ediliyor: {usbDeviceId}");
        string listDiskScriptPath = Path.Combine(Path.GetTempPath(), "list_disk.txt");
        await File.WriteAllTextAsync(listDiskScriptPath, "list disk\nexit", Encoding.ASCII);

        var checkLockProcess = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "diskpart.exe"),
            Arguments = $"/s \"{listDiskScriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Verb = "runas",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        string diskpartOutput;
        using (var process = Process.Start(checkLockProcess))
        {
            if (process == null)
            {
                throw new Exception("Diskpart listeleme başlatılamadı.");
            }
            diskpartOutput = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            logCallback?.Invoke($"diskpart list disk çıktısı: {diskpartOutput}");
            if (process.ExitCode != 0)
            {
                throw new Exception($"Diskpart listeleme başarısız! Hata: {error}");
            }
            string diskNumber = usbDeviceId.Replace(@"\\.\PHYSICALDRIVE", "");
            if (!diskpartOutput.Contains($"Disk {diskNumber}") && !diskpartOutput.Contains($"Disk{diskNumber}"))
            {
                throw new Exception($"USB sürücü ({usbDeviceId}) bulunamadı! diskpart çıktısı: {diskpartOutput}");
            }
        }
        File.Delete(listDiskScriptPath);

        // 3. USB’yi diskpart ile formatla
        logCallback?.Invoke($"USB formatlanıyor: {usbDeviceId}");
        string diskpartScript = $@"
select disk {usbDeviceId.Replace(@"\\.\PHYSICALDRIVE", "")}
clean
create partition primary
select partition 1
active
format fs=NTFS quick
assign
exit";
        string diskpartScriptPath = Path.Combine(Path.GetTempPath(), "diskpart_script.txt");
        await File.WriteAllTextAsync(diskpartScriptPath, diskpartScript, Encoding.ASCII);

        var diskpartProcess = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "diskpart.exe"),
            Arguments = $"/s \"{diskpartScriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Verb = "runas",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using (var process = Process.Start(diskpartProcess))
        {
            if (process == null)
            {
                throw new Exception("Diskpart formatlama başlatılamadı.");
            }
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            logCallback?.Invoke($"diskpart formatlama çıktısı: {output}");
            if (process.ExitCode != 0)
            {
                throw new Exception($"Diskpart formatlama başarısız! Hata: {error}");
            }
        }
        File.Delete(diskpartScriptPath);

        // 4. Sürücü harfini yeniden al
        logCallback?.Invoke("USB sürücü harfi yeniden kontrol ediliyor...");
        string? newUsbDriveLetter = GetDriveLetterFromDeviceId(usbDeviceId, logCallback);
        if (string.IsNullOrEmpty(newUsbDriveLetter))
        {
            throw new Exception($"USB sürücü harfi alınamadı! DeviceID: {usbDeviceId}");
        }
        usbDriveLetter = newUsbDriveLetter;
        logCallback?.Invoke($"USB sürücü harfi: {usbDriveLetter}");

        // 5. Birim hazır olana kadar bekle
        logCallback?.Invoke("USB biriminin hazır olması bekleniyor...");
        bool driveReady = false;
        for (int i = 0; i < 5; i++)
        {
            if (Directory.Exists($"{usbDriveLetter}:\\"))
            {
                driveReady = true;
                break;
            }
            logCallback?.Invoke($"USB sürücü ({usbDriveLetter}:\\) henüz hazır değil, tekrar deneniyor...");
            await Task.Delay(1000);
        }
        if (!driveReady)
        {
            throw new Exception($"USB sürücü ({usbDriveLetter}:\\) erişilebilir değil! Birim hazır olmadı.");
        }
        logCallback?.Invoke($"USB sürücü ({usbDriveLetter}:\\) erişilebilir.");

        // 6. USB’ye izinleri ayarla
        logCallback?.Invoke($"USB sürücüsüne izinler ayarlanıyor: {usbDriveLetter}:\\");
        var icaclsProcess = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            Arguments = $"{usbDriveLetter}:\\ /grant SYSTEM:F /grant Administrators:F /T /C",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Verb = "runas",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using (var process = Process.Start(icaclsProcess))
        {
            if (process == null)
            {
                throw new Exception("icacls işlemi başlatılamadı.");
            }
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            logCallback?.Invoke($"icacls çıktısı: {output}, Hata: {error}");
            if (process.ExitCode != 0)
            {
                logCallback?.Invoke("icacls başarısız, izinler atlanıyor, doğrudan xcopy deneniyor...");
            }
            else
            {
                logCallback?.Invoke("icacls başarılı, izinler ayarlandı.");
            }
        }

        // 7. ISO’yu sanal sürücüye bağla
        logCallback?.Invoke($"ISO bağlanıyor: {isoPath}");
        var mountProcess = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Mount-DiskImage -ImagePath '{isoPath}' -PassThru | Get-Volume | Select-Object -ExpandProperty DriveLetter\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Verb = "runas",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        string? isoDriveLetter;
        using (var process = Process.Start(mountProcess))
        {
            if (process == null)
            {
                throw new Exception("ISO bağlama işlemi başlatılamadı.");
            }
            isoDriveLetter = (await process.StandardOutput.ReadToEndAsync()).Trim();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            logCallback?.Invoke($"ISO bağlama çıktısı: {isoDriveLetter}, Hata: {error}");
            if (process.ExitCode != 0 || string.IsNullOrEmpty(isoDriveLetter))
            {
                throw new Exception($"ISO bağlanamadı! Hata: {error}");
            }
        }

        // 8. ISO içeriğini USB’ye kopyala
        logCallback?.Invoke($"ISO içeriği USB’ye kopyalanıyor: {isoDriveLetter}:\\ -> {usbDriveLetter}:\\");
        long totalBytes = 0;
        long copiedBytes = 0;
        var files = Directory.GetFiles($"{isoDriveLetter}:\\", "*", SearchOption.AllDirectories);
        totalBytes = files.Sum(f => new FileInfo(f).Length);

        var xcopyProcess = new ProcessStartInfo
        {
            FileName = "xcopy.exe",
            Arguments = $"\"{isoDriveLetter}:\\*.*\" \"{usbDriveLetter}:\\\" /s /e /h /y /v",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Verb = "runas",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using (var process = Process.Start(xcopyProcess))
        {
            if (process == null)
            {
                throw new Exception("Xcopy işlemi başlatılamadı.");
            }
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            logCallback?.Invoke($"xcopy çıktısı: {output}, Hata: {error}");
            if (process.ExitCode != 0)
            {
                throw new Exception($"Dosya kopyalama başarısız! Hata: {error}");
            }
            copiedBytes = totalBytes;
            progressCallback?.Invoke(copiedBytes, totalBytes);
            logCallback?.Invoke("xcopy başarılı, dosyalar kopyalandı.");
        }

        // 9. ISO’yu ayır
        logCallback?.Invoke($"ISO ayrılıyor: {isoPath}");
        var dismountProcess = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Dismount-DiskImage -ImagePath '{isoPath}'\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Verb = "runas",
            StandardErrorEncoding = Encoding.UTF8
        };
        using (var process = Process.Start(dismountProcess))
        {
            if (process == null)
            {
                throw new Exception("ISO ayırma işlemi başlatılamadı.");
            }
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            logCallback?.Invoke($"ISO ayırma hatası: {error}");
            if (process.ExitCode != 0)
            {
                throw new Exception($"ISO ayrılmadı! Hata: {error}");
            }
        }

        // 10. USB’yi önyüklenebilir yap
        logCallback?.Invoke($"USB önyüklenebilir yapılıyor: {usbDriveLetter}");
        string bootsectPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "bootsect.exe");
        logCallback?.Invoke($"bootsect.exe yolu kontrol ediliyor: {bootsectPath}");
        if (!File.Exists(bootsectPath))
        {
            throw new FileNotFoundException($"bootsect.exe bulunamadı! Lütfen {bootsectPath} yoluna bootsect.exe dosyasını yerleştirin.", bootsectPath);
        }
        var bootsectProcess = new ProcessStartInfo
        {
            FileName = bootsectPath,
            Arguments = $"/nt60 {usbDriveLetter}: /force",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Verb = "runas",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using (var process = Process.Start(bootsectProcess))
        {
            if (process == null)
            {
                throw new Exception("Bootsect işlemi başlatılamadı.");
            }
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            logCallback?.Invoke($"bootsect çıktısı: {output}, Hata: {error}");
            if (process.ExitCode != 0)
            {
                throw new Exception($"Bootsect başarısız! Hata: {error}");
            }
            logCallback?.Invoke("bootsect başarılı, USB önyüklenebilir yapıldı.");
        }
    }

    private static string? GetDriveLetterFromDeviceId(string deviceId, Action<string>? logCallback)
    {
        try
        {
            var partitionSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
            foreach (ManagementObject partition in partitionSearcher.Get())
            {
                var logicalDiskSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                foreach (ManagementObject logicalDisk in logicalDiskSearcher.Get())
                {
                    string? driveLetter = logicalDisk["DeviceID"]?.ToString()?.Trim().TrimEnd(':');
                    if (!string.IsNullOrEmpty(driveLetter))
                    {
                        logCallback?.Invoke($"Sürücü harfi bulundu: {driveLetter}");
                        return driveLetter;
                    }
                }
            }
            logCallback?.Invoke("Sürücü harfi bulunamadı.");
            return null;
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Sürücü harfi alınırken hata: {ex.Message}");
            return null;
        }
    }
}