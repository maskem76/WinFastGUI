using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression; // Zip dosyaları için eklendi
using System.Text;
using System.Threading.Tasks;

namespace WinFastGUI.Services
{
    /// <summary>
    /// WIM dosyasından boot edilebilir ISO oluşturan servis.
    /// </summary>
    public class IsoCreationService
    {
        /// <summary>
        /// Belirtilen WIM dosyasını ve şablonu kullanarak bir ISO dosyası oluşturur.
        /// </summary>
        public async Task CreateIsoAsync(string wimFilePath, string outputIsoPath, string templateZipPath, IProgress<BackupProgressReport> progress)
        {
            // Geçici bir çalışma klasörü oluştur
            string tempDir = Path.Combine(Path.GetTempPath(), $"WinFastISO_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = $"Geçici çalışma klasörü oluşturuldu: {tempDir}" });

            try
            {
                // Adım 1: Şablon ZIP dosyasını geçici klasöre aç
                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = "ISO şablonu ayıklanıyor..." });
                if (!File.Exists(templateZipPath))
                {
                    throw new FileNotFoundException($"Şablon dosyası bulunamadı: {templateZipPath}");
                }
                ZipFile.ExtractToDirectory(templateZipPath, tempDir);
                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = "Şablon başarıyla ayıklandı." });

                // Adım 2: WIM dosyasını 'sources' klasörüne 'install.wim' olarak kopyala
                string sourcesDir = Path.Combine(tempDir, "sources");
                if (!Directory.Exists(sourcesDir))
                {
                    Directory.CreateDirectory(sourcesDir);
                }
                string installWimPath = Path.Combine(sourcesDir, "install.wim");
                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = $".wim dosyası '{installWimPath}' konumuna kopyalanıyor..." });
                await CopyFileWithProgressAsync(wimFilePath, installWimPath, progress);
                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = ".wim dosyası başarıyla kopyalandı." });

                // Adım 3: oscdimg.exe ile ISO oluştur
                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = "ISO dosyası oluşturuluyor..." });
                bool isoSuccess = await RunOscdimgAsync(tempDir, outputIsoPath, progress);
                if (!isoSuccess)
                {
                    throw new Exception("oscdimg.exe ile ISO oluşturma başarısız oldu.");
                }

                progress.Report(new BackupProgressReport { Type = ReportType.Result, Success = true, ResultPath = outputIsoPath, Message = "ISO başarıyla oluşturuldu!" });
            }
            finally
            {
                // Adım 4: Geçici klasörü temizle
                progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = "Geçici dosyalar temizleniyor..." });
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// ISO oluşturmak için oscdimg.exe aracını çalıştırır.
        /// </summary>
        private async Task<bool> RunOscdimgAsync(string sourceDir, string outputIsoPath, IProgress<BackupProgressReport> progress)
        {
            string oscdimgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "oscdimg", "oscdimg.exe");
            if (!File.Exists(oscdimgPath))
            {
                throw new FileNotFoundException("oscdimg.exe bulunamadı. Lütfen 'Modules\\oscdimg' klasörüne ekleyin.");
            }

            // Boot edilebilir UEFI ISO için standart oscdimg argümanları
            string bootData = $"-bootdata:2#p0,e,b\"{Path.Combine(sourceDir, "boot", "etfsboot.com")}\"#pEF,e,b\"{Path.Combine(sourceDir, "efi", "microsoft", "boot", "efisys.bin")}\"";
            string args = $"-m -o -u2 -udfver102 {bootData} -l\"{Path.GetFileNameWithoutExtension(outputIsoPath)}\" \"{sourceDir}\" \"{outputIsoPath}\"";

            return await RunProcessAsync(oscdimgPath, args, progress);
        }

        /// <summary>
        /// Harici bir komut satırı aracını çalıştırır ve çıktısını raporlar.
        /// </summary>
        private async Task<bool> RunProcessAsync(string exePath, string args, IProgress<BackupProgressReport> progress)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default
            };

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) progress.Report(new BackupProgressReport { Type = ReportType.Log, Message = e.Data });
            };
            process.ErrorDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) progress.Report(new BackupProgressReport { Type = ReportType.Error, Message = e.Data });
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }

        /// <summary>
        /// Bir dosyayı kopyalarken ilerlemeyi raporlayan yardımcı metot.
        /// </summary>
        private async Task CopyFileWithProgressAsync(string sourceFile, string destinationFile, IProgress<BackupProgressReport> progress)
        {
            long totalBytes = new FileInfo(sourceFile).Length;
            long totalBytesCopied = 0;
            byte[] buffer = new byte[81920]; // 80 KB buffer

            await using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read);
            await using var destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write);
            
            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await destinationStream.WriteAsync(buffer, 0, bytesRead);
                totalBytesCopied += bytesRead;
                int percent = (int)((double)totalBytesCopied / totalBytes * 100);
                progress.Report(new BackupProgressReport { Type = ReportType.Progress, Percent = percent });
            }
        }
    }
}
