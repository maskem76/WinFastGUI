using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management; // VssSnapshotHelper için gerekli olabilir.
using System.Text.RegularExpressions; // Regex kullanımı için eklendi.

namespace WinFastGUI.Services
{
    // BackupProgressReport, ReportType ve VssSnapshotHelper sınıflarının
    // bu 'WinFastGUI.Services' ad alanında başka bir yerde (ayrı dosyalarda)
    // tanımlandığı varsayılmaktadır. Bu dosya içinde tekrar tanımlanmayacaklardır.

    public class VssBackupService
    {
        public async Task CreateBackupWithDosdevWimlibAsync(string backupDir, string imageType, string mountLetter, IProgress<BackupProgressReport> progress)
        {
            string? shadowId = null; 
            string? masterScriptPath = null;
            string? scratchDir = null;
            string? configFile = null;

            try
            {
                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = "Ön kontroller başlatılıyor..." });

                // Adım 1: VSS anlık görüntüsünü manuel olarak oluştur ve cihaz yolunu al
                string? volumeGuid = VssSnapshotHelper.GetVolumeGuid("C:\\", progress);
                if (string.IsNullOrWhiteSpace(volumeGuid))
                {
                    progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = "HATA: C:\\ için VolumeId bulunamadı!" });
                    throw new Exception("VolumeId bulunamadı! (C:\\)");
                }

                string? deviceObject = VssSnapshotHelper.CreateVssSnapshot(volumeGuid, out shadowId, progress);
                if (string.IsNullOrWhiteSpace(deviceObject) || string.IsNullOrWhiteSpace(shadowId))
                {
                    progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = "HATA: VSS snapshot oluşturulamadı." });
                    throw new Exception("VSS snapshot oluşturulamadı.");
                }

                // Adım 2: Tüm işlemleri yapacak ana komut dosyasını oluştur
                string driveLetter = mountLetter.Trim(':');
                string backupPath = Path.Combine(backupDir, $"SystemBackup_{DateTime.Now:yyyyMMdd_HHmmss}.wim");
                scratchDir = Path.Combine(Path.GetTempPath(), "DISM_Scratch");
                Directory.CreateDirectory(scratchDir);
                configFile = CreateDismExclusionFile(progress);

                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string imdiskExe = Path.Combine(appDir, "Modules", "ImDisk", "imdisk.exe");
                string dismExe = GetSystemToolPath("dism.exe");

                if (!File.Exists(imdiskExe))
                {
                    progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = "HATA: ImDisk aracı bulunamadı. Lütfen 'Modules\\ImDisk\\imdisk.exe' yolunu kontrol edin." });
                    throw new FileNotFoundException("ImDisk aracı bulunamadı. Lütfen ImDisk'in kurulu olduğundan ve doğru yolda olduğundan emin olun.");
                }
                if (!File.Exists(dismExe))
                {
                    progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = "HATA: Dism.exe sistemi yollarında bulunamadı." });
                    throw new FileNotFoundException("Dism.exe sistemi yollarında bulunamadı.");
                }

                var scriptContent = new StringBuilder();
                scriptContent.AppendLine("@echo off");
                scriptContent.AppendLine($"echo. & echo *** {driveLetter}: surucusu ImDisk ile olusturuluyor... ***");
                scriptContent.AppendLine($"\"{imdiskExe}\" -a -t vm -m {driveLetter}: -o ro -f \"{deviceObject}\"");
                scriptContent.AppendLine("if %errorlevel% neq 0 ( echo HATA: ImDisk ile sürücü atanamadı. & exit /b 1 )");

                scriptContent.AppendLine($"echo. & echo *** {driveLetter}: surucusunun hazir olmasi bekleniyor (en fazla 2 dakika)... ***");
                scriptContent.AppendLine("setlocal");
                scriptContent.AppendLine("set /a count=0");
                scriptContent.AppendLine(":wait_loop");
                scriptContent.AppendLine($"dir {driveLetter}:\\Windows\\System32\\kernel32.dll > nul 2>&1");
                scriptContent.AppendLine("if %errorlevel% equ 0 ( goto :drive_ready )");
                scriptContent.AppendLine("set /a count+=1");
                scriptContent.AppendLine("if %count% geq 120 ( echo HATA: Surucu 2 dakika icinde hazir olmadi. & exit /b 1 )");
                scriptContent.AppendLine("timeout /t 1 /nobreak > nul");
                scriptContent.AppendLine("goto :wait_loop");
                scriptContent.AppendLine(":drive_ready");
                scriptContent.AppendLine("echo Surucu hazir.");
                scriptContent.AppendLine("endlocal");

                scriptContent.AppendLine("echo. & echo *** DISM ile yedekleme baslatiliyor... ***");
                scriptContent.AppendLine($"\"{dismExe}\" /capture-image /imagefile:\"{backupPath}\" /capturedir:{driveLetter}:\\ /name:\"Windows Backup\" /compress:max /scratchdir:\"{scratchDir}\" /configfile:\"{configFile}\"");
                scriptContent.AppendLine("set dism_exit_code=%errorlevel%");

                scriptContent.AppendLine("echo. & echo *** Temizlik: Surucu kaldiriliyor... ***");
                scriptContent.AppendLine($"\"{imdiskExe}\" -d -m {driveLetter}:");

                scriptContent.AppendLine("exit /b %dism_exit_code%");

                masterScriptPath = Path.Combine(Path.GetTempPath(), $"WinFast_Master_{Guid.NewGuid()}.bat");
                await File.WriteAllTextAsync(masterScriptPath, scriptContent.ToString(), Encoding.Default);
                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = "Ana yedekleme script'i oluşturuldu." });

                // Adım 3: Ana script'i NSudo ile tek seferde çalıştır
                bool scriptSuccess = await RunMasterScriptAsync(masterScriptPath, progress);
                if (!scriptSuccess)
                {
                    throw new Exception("Ana yedekleme script'i başarısız oldu.");
                }

                if (!File.Exists(backupPath) || new FileInfo(backupPath).Length < 100 * 1024 * 1024)
                {
                    progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = "HATA: Yedekleme tamamlandı ancak dosya boyutu çok küçük. Güvenlik özelliği engelliyor olabilir." });
                    throw new Exception($"Yedekleme tamamlandı ancak dosya boyutu çok küçük. Sisteminizdeki bir güvenlik özelliği engelliyor olabilir.");
                }

                progress.Report(new BackupProgressReport { Type = ReportType.Result, Success = true, ResultPath = backupPath });
            }
            catch (Exception ex)
            {
                progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = $"KRİTİK HATA: {ex.Message}" });
            }
            finally
            {
                // Adım 4: Temizlik
                if (!string.IsNullOrEmpty(shadowId))
                {
                    progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"Temizlik: Snapshot ({shadowId}) siliniyor..." });
                    await RunVssAdminAsync($"delete shadows /shadow={{{shadowId}}} /quiet", progress);
                }

                if (!string.IsNullOrEmpty(masterScriptPath) && File.Exists(masterScriptPath))
                {
                    try { File.Delete(masterScriptPath); } catch (Exception ex) { progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"UYARI: Master script silinirken hata: {ex.Message}" }); }
                }
                if (!string.IsNullOrEmpty(configFile) && File.Exists(configFile))
                {
                    try { File.Delete(configFile); } catch (Exception ex) { progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"UYARI: Konfigürasyon dosyası silinirken hata: {ex.Message}" }); }
                }
                if (!string.IsNullOrEmpty(scratchDir) && Directory.Exists(scratchDir))
                {
                    try { Directory.Delete(scratchDir, true); } catch (Exception ex) { progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"UYARI: Scratch dizini silinirken hata: {ex.Message}" }); }
                }

                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = "İşlem tamamlandı." });
            }
        }

        private string CreateDismExclusionFile(IProgress<BackupProgressReport> progress)
        {
            string configFile = Path.Combine(Path.GetTempPath(), "dism_exclude.ini");
            var exclusionList = new[]
            {
                "[ExclusionList]",
                "\\pagefile.sys", "\\hiberfil.sys", "\\swapfile.sys",
                "\\$Recycle.Bin\\*", "\\System Volume Information\\*",
                "\\Windows\\CSC\\*", "\\Windows\\Temp\\*", "\\Temp\\*",
                "\\Users\\*\\AppData\\Local\\Temp\\*"
            };
            File.WriteAllLines(configFile, exclusionList, Encoding.ASCII);
            progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = "DISM için dışlama listesi oluşturuldu." });
            return configFile;
        }

        private async Task<bool> RunMasterScriptAsync(string scriptPath, IProgress<BackupProgressReport> progress)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string nsudoPath = Path.Combine(appDir, "Modules", "nsudo", "NSudo.exe");
            if (!File.Exists(nsudoPath))
            {
                nsudoPath = Path.Combine(appDir, "Modules", "nsudo", "NSudoLC.exe");
            }

            if (!File.Exists(nsudoPath))
            {
                progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = "HATA: NSudo yürütülebilir dosyası bulunamadı. Lütfen 'Modules\\nsudo\\NSudo.exe' veya 'Modules\\nsudo\\NSudoLC.exe' yollarını kontrol edin." });
                return false;
            }

            string nsudoArgs = $"-U:T -P:E -Wait cmd /c \"{scriptPath}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = nsudoPath,
                    Arguments = nsudoArgs,
                    WorkingDirectory = Path.GetDirectoryName(nsudoPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding = Encoding.Default
                };

                using var process = new Process { StartInfo = psi };

                var outputReadingTask = Task.Run(async () =>
                {
                    using var reader = process.StandardOutput;
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrEmpty(line)) continue;

                        if (line.Contains('%'))
                        {
                            var match = Regex.Match(line, @"(\d+\.?\d*)\%"); // Regex'i biraz daha genişlettim
                            if (match.Success && double.TryParse(match.Groups[1].Value, out double percent))
                            {
                                progress.Report(new BackupProgressReport { Type = ReportType.Progress, Percent = (int)percent });
                            }
                        }
                        progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = line });
                    }
                });

                var errorReadingTask = Task.Run(async () =>
                {
                    using var reader = process.StandardError;
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (!string.IsNullOrEmpty(line))
                        {
                            progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = $"HATA: {line}" });
                        }
                    }
                });

                process.Start();
                await Task.WhenAll(process.WaitForExitAsync(), outputReadingTask, errorReadingTask);

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = $"NSudo ile işlem yürütülürken kritik hata: {ex.Message}" });
                return false;
            }
        }

        private async Task<bool> RunVssAdminAsync(string args, IProgress<BackupProgressReport> progress)
        {
            string vssAdminPath = GetSystemToolPath("vssadmin.exe");
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string nsudoPath = Path.Combine(appDir, "Modules", "nsudo", "NSudo.exe");
            if (!File.Exists(nsudoPath))
            {
                nsudoPath = Path.Combine(appDir, "Modules", "nsudo", "NSudoLC.exe");
            }
            if (!File.Exists(nsudoPath))
            {
                progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = "HATA: NSudo bulunamadı, VssAdmin çalıştırılamadı." });
                return false;
            }

            string nsudoArgs = $"-U:T -P:E -Wait \"{vssAdminPath}\" {args}";
            var psi = new ProcessStartInfo
            {
                FileName = nsudoPath,
                Arguments = nsudoArgs,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };

            try
            {
                using var process = new Process { StartInfo = psi };

                var outputReadTask = Task.Run(async () =>
                {
                    using var reader = process.StandardOutput;
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (!string.IsNullOrEmpty(line)) progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"VssAdmin Çıktısı: {line}" });
                    }
                });

                var errorReadTask = Task.Run(async () =>
                {
                    using var reader = process.StandardError;
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (!string.IsNullOrEmpty(line)) progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = $"VssAdmin Hata: {line}" });
                    }
                });

                process.Start();
                await Task.WhenAll(process.WaitForExitAsync(), outputReadTask, errorReadTask);

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = $"VssAdmin yürütülürken hata: {ex.Message}" });
                return false;
            }
        }

        private string GetSystemToolPath(string toolName)
        {
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                string sysnativePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative", toolName);
                if (File.Exists(sysnativePath))
                {
                    return sysnativePath;
                }
            }
            return Path.Combine(Environment.SystemDirectory, toolName);
        }
    }
}