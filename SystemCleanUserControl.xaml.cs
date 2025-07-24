#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WinFastGUI
{
    [SupportedOSPlatform("windows")]
    public partial class SystemCleanUserControl : UserControl
    {
        private CancellationTokenSource? cancellationTokenSource;
        private DateTime startTime;
        private readonly string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinFastGUI.log");
        private readonly List<string> _criticalExclusionPaths = new();
        private readonly HashSet<string> _processedDirectories = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _criticalProcesses = new()
        {
            "system", "csrss", "wininit", "services", "lsass", "smss", "winlogon", "explorer", "steamwebhelper"
        };
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly SemaphoreSlim _ioSemaphore = new(4);
        private readonly System.Windows.Threading.DispatcherTimer _logFlushTimer;

        public SystemCleanUserControl()
        {
            InitializeComponent();
            if (ProgressBar != null) ProgressBar.Maximum = 100;
            LogMessage("Uygulama başlatıldı.");

            // Dışlanacak kritik klasörler (C: için)
            _criticalExclusionPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToUpperInvariant());
            _criticalExclusionPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToUpperInvariant());
            _criticalExclusionPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToUpperInvariant());
            _criticalExclusionPaths.Add(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\').ToUpperInvariant());
            _criticalExclusionPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "config").ToUpperInvariant());
            _criticalExclusionPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS").ToUpperInvariant());
            _criticalExclusionPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps").ToUpperInvariant());
            _criticalExclusionPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "LogFiles").ToUpperInvariant());
            _criticalExclusionPaths.Add(@"C:\ProgramData\Desktop".ToUpperInvariant());
            _criticalExclusionPaths.Add(@"C:\ProgramData\Belgeler".ToUpperInvariant());
            _criticalExclusionPaths.Add(@"C:\ProgramData\Application Data".ToUpperInvariant());
            _criticalExclusionPaths.Add(@"C:\Program Files\Windows NT\Donatılar".ToUpperInvariant());
            _criticalExclusionPaths.Add(@"C:\Documents and Settings".ToUpperInvariant());
            _criticalExclusionPaths.Add(@"C:\ProgramData\Microsoft\Windows\AppRepository".ToUpperInvariant());
            _criticalExclusionPaths.Add(@"D:\System Volume Information".ToUpperInvariant());

            // Log Flush timer (UI donmasın)
            _logFlushTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _logFlushTimer.Tick += LogFlushTimer_Tick;
            _logFlushTimer.Start();
        }

        private void LogFlushTimer_Tick(object? sender, EventArgs e)
        {
            if (_logQueue.IsEmpty) return;
            while (_logQueue.TryDequeue(out var msg))
            {
                LogTextBox?.AppendText(msg + "\n");
                LogTextBox?.ScrollToEnd();
                try { File.AppendAllText(logFilePath, msg + "\n"); }
                catch { }
            }
        }

        private string GetNSudoPath()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "nsudo");
            string nsudoExe = Path.Combine(dir, "NSudo.exe");
            string nsudoLC = Path.Combine(dir, "NSudoLC.exe");
            if (File.Exists(nsudoExe)) return nsudoExe;
            if (File.Exists(nsudoLC)) return nsudoLC;
            return "";
        }

        private void LogMessage(string msg)
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _logQueue.Enqueue(logEntry);
        }

        private async void StartCleanButton_Click(object sender, RoutedEventArgs e)
        {
            LogMessage("Temizlik işlemi başlatıldı.");
            if (StartCleanButton == null || StopButton == null || LogTextBox == null || ProgressBar == null || StatusText == null || OperationStatusLabel == null || TimerLabel == null || CleanModeComboBox == null) return;
            if (!IsRunningAsAdministrator()) { LogMessage("UYARI: Yönetici olarak çalıştırın!"); return; }
            string nsudoPath = GetNSudoPath();
            if (string.IsNullOrEmpty(nsudoPath)) { LogMessage("Hata: NSudo.exe bulunamadı!"); return; }

            cancellationTokenSource = new CancellationTokenSource();
            _processedDirectories.Clear();
            LogTextBox.Clear();
            ProgressBar.IsIndeterminate = true;
            StatusText.Text = "Temizlik Başlatılıyor...";
            OperationStatusLabel.Text = "İşlem Başladı";
            TimerLabel.Text = "00:00";
            StartCleanButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            RestartButton.Visibility = Visibility.Collapsed;

            startTime = DateTime.Now;
            _ = Task.Run(() => StartTimer(cancellationTokenSource.Token));

            string selectedMode = (CleanModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Hızlı Genel Temizliği (Önerilen)";
            LogMessage($"'{selectedMode}' modu ile temizlik işlemi başlatıldı.");

            int totalDeletedFiles = 0;
            try
            {
                var progress = new Progress<CleanProgressReport>(report =>
                {
                    if (report.CurrentPath != null) StatusText.Text = report.CurrentPath;
                });

                totalDeletedFiles = await Task.Run(async () =>
                {
                    List<string> pathsToClean = GetPathsForMode(selectedMode);
                    return pathsToClean.Any() ? await CleanPathsAsync(pathsToClean.ToArray(), cancellationTokenSource.Token, progress) : 0;
                }, cancellationTokenSource.Token);

                if (selectedMode.Contains("Genişletilmiş"))
                {
                    await RunExtendedSystemTools(nsudoPath, cancellationTokenSource.Token);
                }

                StatusText.Text = "Temizlik tamamlandı!";
                OperationStatusLabel.Text = "Tamamlandı";
                LogMessage($"İşlem başarıyla tamamlandı. Toplam {totalDeletedFiles} dosya silindi.");
            }
            catch (TaskCanceledException)
            {
                StatusText.Text = "Temizlik iptal edildi.";
                OperationStatusLabel.Text = "İptal Edildi";
                LogMessage("İşlem kullanıcı tarafından iptal edildi.");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Bir hata oluştu!";
                OperationStatusLabel.Text = "Hata";
                LogMessage($"KRİTİK HATA: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 100;
                StartCleanButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                RestartButton.Visibility = Visibility.Visible;
                LogMessage("Temizlik işlemi sona erdi.");

                cancellationTokenSource?.Cancel();
            }
        }

        private List<string> GetPathsForMode(string selectedMode)
        {
            if (selectedMode.Contains("Hızlı"))
            {
                LogMessage("Hızlı temizlik yolları alınıyor.");
                return new List<string> { Path.GetTempPath(), Environment.ExpandEnvironmentVariables("%SystemRoot%\\Temp"), @"C:\Windows\Prefetch" };
            }
            if (selectedMode.Contains("Derin"))
            {
                LogMessage("Derin temizlikte C: ve D: sürücüleri tam taranacak.");
                var paths = new List<string>();
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed &&
                        (drive.Name.Equals("C:\\", StringComparison.OrdinalIgnoreCase) ||
                         drive.Name.Equals("D:\\", StringComparison.OrdinalIgnoreCase)))
                    {
                        paths.Add(drive.Name);
                        LogMessage($"Taranacak sürücü eklendi: {drive.Name}");
                    }
                }
                if (!paths.Any())
                {
                    LogMessage("UYARI: C: veya D: sürücüsü bulunamadı veya erişilebilir değil.");
                }
                else
                {
                    LogMessage($"Taranacak sürücüler: {string.Join(", ", paths)}");
                }
                return paths;
            }
            return new List<string>();
        }

        private async Task<int> CleanPathsAsync(string[] paths, CancellationToken token, IProgress<CleanProgressReport> progress)
        {
            int totalDeletedFiles = 0;
            var report = new CleanProgressReport();

            var tasks = paths.Select(async path =>
            {
                if (token.IsCancellationRequested) return 0;

                report.CurrentPath = $"Taranıyor: {path}";
                progress.Report(report);
                LogMessage($"Taramaya başlanıyor: {path}");

                int deleted = await CleanFilesInDirectoryAsync(path, token, true);
                return deleted;
            }).ToArray();

            int[] results = await Task.WhenAll(tasks);
            totalDeletedFiles = results.Sum();

            return totalDeletedFiles;
        }

        private async Task<int> CleanFilesInDirectoryAsync(string directoryPath, CancellationToken token, bool recursive = false)
        {
            int deletedCount = 0;
            string[] extensions = new[] { "*.~*", "~*.*", "*.??~", "*.---", "*.tmp", "*._mp", "*~tmp.*", "*.??$", "*.fic",
                "*.$sa", "*._detmp", "*.^", "*.db$", "*.$db", "*.$$$", "*.old", "*.bak", "*.bac",
                "*.cpy", "*.prv", "*.syd", "*.MS", "*.chk", "t3v?????.*", "*.gid", "thumbs.db",
                "dxva_sig.txt", "mscreate.dir", "chklist.*", "0???????.nch", "*.dmp", "_istmp*.*",
                "*.fnd", "*_ofidx.*", "0*.nch", "scandisk.log", "SchedLgU.txt", "*.err",
                "*.errorlog", "*.xlk", "*.mch", "*.temp", "*.shd", "*.log", "*.log1", "*.log2",
                "*.etl", "*.bup", "*.nu3", "*.nu4", "file????._dd", "*.cache", "*.junk", "*.cab", "*.nfo" };
            int currentProcessId = Process.GetCurrentProcess().Id;

            var directoriesToScan = new Stack<string>();
            directoriesToScan.Push(directoryPath);

            while (directoriesToScan.Count > 0)
            {
                if (token.IsCancellationRequested) break;
                string currentDir = directoriesToScan.Pop();
                LogMessage($"Dizin taranıyor: {currentDir}");

                string currentDirUpper = Path.GetFullPath(currentDir).ToUpperInvariant();
                if (_criticalExclusionPaths.Any(path => currentDirUpper.StartsWith(path)))
                {
                    LogMessage($"Kritik klasör atlanıyor: {currentDir}");
                    continue;
                }

                try
                {
                    await _ioSemaphore.WaitAsync(token);
                    try
                    {
                        var filesInDir = new List<string>();
                        foreach (var ext in extensions)
                        {
                            try
                            {
                                filesInDir.AddRange(Directory.EnumerateFiles(currentDir, ext, SearchOption.TopDirectoryOnly));
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Dosya listeleme hatası: {currentDir} - {ex.Message}");
                                continue;
                            }
                        }

                        // --- Paralel silme ve task sınırı ---
                        await Parallel.ForEachAsync(filesInDir.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (file, ct) =>
                        {
                            if (ct.IsCancellationRequested) return;
                            if (IsProtectedFile(file))
                            {
                                LogMessage($"Korunan dosya atlanıyor: {file}");
                                return;
                            }
                            bool deleted = false;
                            try
                            {
                                await _ioSemaphore.WaitAsync(ct);
                                try
                                {
                                    var fileInfo = new FileInfo(file) { IsReadOnly = false };
                                    await Task.Run(() => fileInfo.Delete(), ct);
                                    Interlocked.Increment(ref deletedCount);
                                    LogMessage($"Silindi: {file}");
                                    deleted = true;
                                }
                                finally
                                {
                                    _ioSemaphore.Release();
                                }
                            }
                            catch (IOException)
                            {
                                var lockingProcesses = WindowsRestartManager.GetProcessesLockingFile(file);
                                bool killedAny = false;

                                foreach (var p in lockingProcesses)
                                {
                                    if (p.Id == currentProcessId)
                                    {
                                        LogMessage($"Kendi prosesini sonlandırma atlandı: {file}");
                                        continue;
                                    }
                                    if (_criticalProcesses.Contains(p.ProcessName.ToLower()))
                                    {
                                        LogMessage($"Korunan proses atlanıyor: {p.ProcessName} (PID: {p.Id}) - {file}");
                                        continue;
                                    }
                                    LogMessage($"Kilitli proses sonlandırılıyor: {p.ProcessName} (PID: {p.Id})");
                                    await RunWithNSudo(GetNSudoPath(), $"taskkill /F /PID {p.Id}", $"-> '{p.ProcessName}' başarıyla sonlandırıldı.");
                                    killedAny = true;
                                    p.Dispose();
                                }

                                if (killedAny)
                                {
                                    try
                                    {
                                        await Task.Delay(300, ct);
                                        await _ioSemaphore.WaitAsync(ct);
                                        try
                                        {
                                            var fileInfo = new FileInfo(file) { IsReadOnly = false };
                                            await Task.Run(() => fileInfo.Delete(), ct);
                                            Interlocked.Increment(ref deletedCount);
                                            LogMessage($"Proses kapatıldıktan sonra silindi: {file}");
                                            deleted = true;
                                        }
                                        finally
                                        {
                                            _ioSemaphore.Release();
                                        }
                                    }
                                    catch { }
                                }
                                if (!deleted)
                                {
                                    LogMessage($"NSudo ile zorla siliniyor: {file}");
                                    await RunWithNSudo(GetNSudoPath(), $"cmd.exe /c del /f /q \"{file}\"", $"-> NSudo ile zorla silindi: {file}");
                                    Interlocked.Increment(ref deletedCount);
                                    deleted = true;
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                LogMessage($"Erişim reddedildi, NSudo ile deneniyor: {file}");
                                await RunWithNSudo(GetNSudoPath(), $"cmd.exe /c del /f /q \"{file}\"", $"-> NSudo ile zorla silindi: {file}");
                                Interlocked.Increment(ref deletedCount);
                                deleted = true;
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"HATA: Silinemedi: {file} - {ex.Message}");
                            }
                            if (!deleted)
                            {
                                LogMessage($"HATA: Silinemedi: {file}");
                            }
                        });

                        // Alt klasörleri tara
                        if (recursive)
                        {
                            try
                            {
                                var subDirs = Directory.EnumerateDirectories(currentDir).ToList();
                                foreach (var subDir in subDirs)
                                {
                                    string subDirUpper = Path.GetFullPath(subDir).ToUpperInvariant();
                                    if (_criticalExclusionPaths.Any(path => subDirUpper.StartsWith(path)))
                                    {
                                        LogMessage($"Kritik alt klasör atlanıyor: {subDir}");
                                        continue;
                                    }
                                    directoriesToScan.Push(subDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Dizin tarama hatası: {currentDir} - {ex.Message}");
                            }
                        }
                    }
                    finally
                    {
                        _ioSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Dizin tarama hatası: {currentDir} - {ex.Message}");
                }
            }
            return deletedCount;
        }

        private async Task RunExtendedSystemTools(string nsudoPath, CancellationToken token)
        {
            LogMessage("Genişletilmiş sistem araçları çalıştırılıyor.");
            var commands = new Dictionary<string, string>
            {
                { "Gölge kopyalar temizleniyor...", "vssadmin delete shadows /for=C: /all /quiet" },
                { "DISM bileşen temizliği başlatılıyor...", "cmd.exe /c dism /Online /Cleanup-Image /StartComponentCleanup" },
                { "Geri Dönüşüm Kutusu boşaltılıyor...", "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"" }
            };

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            ProgressBar.Maximum = commands.Count;

            foreach (var command in commands)
            {
                if (token.IsCancellationRequested) break;
                LogMessage(command.Key);
                StatusText.Text = command.Key;
                await RunWithNSudo(nsudoPath, command.Value, "İşlem tamamlandı.");
                ProgressBar.Value++;
            }
        }

        private async Task RunWithNSudo(string nsudoPath, string command, string successMessage)
        {
            if (cancellationTokenSource?.IsCancellationRequested ?? true) return;
            try
            {
                var processStartInfo = new ProcessStartInfo { FileName = nsudoPath, Arguments = $"-U:T -P:E -Wait -ShowWindowMode:Hide {command}", UseShellExecute = false, CreateNoWindow = true };
                using var process = new Process { StartInfo = processStartInfo };
                process.Start();
                await process.WaitForExitAsync(cancellationTokenSource.Token);
                if (process.ExitCode == 0) LogMessage(successMessage);
                else LogMessage($"NSudo komut hatası (Kod: {process.ExitCode}): {command}");
            }
            catch (TaskCanceledException) { LogMessage("İşlem iptal edildi."); }
            catch (Exception ex) { LogMessage($"NSudo Hatası: {ex.Message}"); }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e) { cancellationTokenSource?.Cancel(); LogMessage("İptal isteği gönderildi..."); }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (LogTextBox == null || ProgressBar == null || TimerLabel == null || StatusText == null || OperationStatusLabel == null || RestartButton == null) return;
            LogTextBox.Clear();
            ProgressBar.Value = 0;
            TimerLabel.Text = "00:00";
            StatusText.Text = "Hazır";
            OperationStatusLabel.Text = "Hazır";
            RestartButton.Visibility = Visibility.Collapsed;
            LogMessage("Uygulama yeniden başlatıldı.");
        }

        private async Task StartTimer(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, token);
                    TimeSpan elapsed = DateTime.Now - startTime;
                    await Dispatcher.InvokeAsync(() => TimerLabel.Text = elapsed.ToString(@"mm\:ss"));
                }
                catch (TaskCanceledException) { break; }
            }
        }

        private bool IsRunningAsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private bool IsProtectedFile(string filePath)
        {
            try
            {
                string filePathUpper = Path.GetFullPath(filePath).ToUpperInvariant();
                return _criticalExclusionPaths.Any(path => filePathUpper.StartsWith(path)) ||
                       filePathUpper.StartsWith(AppDomain.CurrentDomain.BaseDirectory.ToUpperInvariant().TrimEnd('\\'));
            }
            catch { return true; }
        }
    }

    public class CleanProgressReport
    {
        public string? CurrentPath { get; set; }
    }
}
