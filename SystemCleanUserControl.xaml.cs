#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WinFastGUI
{
    [SupportedOSPlatform("windows")]
    public partial class SystemCleanUserControl : UserControl
    {
        private CancellationTokenSource? _cleanCts;
        private readonly CancellationTokenSource _loggerCts = new();
        private DateTime _startTime = DateTime.MinValue;
        private readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinFastGUI.log");
        private readonly List<string> _criticalExclusionPaths = new();
        private static readonly HashSet<string> _criticalProcesses = new()
        {
            "system", "csrss", "wininit", "services", "lsass", "smss", "winlogon", "explorer", "steamwebhelper"
        };
        
        private readonly SemaphoreSlim _ioSemaphore = new(Math.Min(Environment.ProcessorCount, 4), Math.Min(Environment.ProcessorCount, 4));
        private readonly SemaphoreSlim _nsudoSemaphore = new(1, 1);
        private readonly Channel<string> _logChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(1000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private readonly System.Threading.Timer _resourceMonitorTimer;

        public SystemCleanUserControl()
        {
            InitializeComponent();
            
            _resourceMonitorTimer = new System.Threading.Timer(async _ => await CheckSystemResources(_cleanCts?.Token ?? CancellationToken.None), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            InitializeDrives();
            InitializeCleanModes();
            InitializeCriticalPaths();
            
            Loaded += async (s, e) =>
            {
                await StartLogConsumer(_loggerCts.Token);
            };
            Unloaded += (s, e) => 
            {
                _loggerCts.Cancel();
                _logChannel.Writer.Complete();
                _resourceMonitorTimer?.Dispose();
            };

            LogMessage(Properties.Strings.AppStarted);
        }
        
        private async Task StartLogConsumer(CancellationToken token)
        {
            try
            {
                await foreach (var msg in _logChannel.Reader.ReadAllAsync(token))
                {
                    if (LogTextBox == null) continue;
                
                    try 
                    {
                        await File.AppendAllTextAsync(_logFilePath, msg + Environment.NewLine, token);
                    }
                    catch { /* Ignore */ }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        LogTextBox.AppendText(msg + Environment.NewLine);
                        LogTextBox.ScrollToEnd();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Görev iptal edildiğinde sessizce çık.
            }
        }
        
        private void LogMessage(string msg)
        {
            var logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
            _logChannel.Writer.TryWrite(logMsg);
        }

        private async Task StartTimer(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, token);
                    TimeSpan elapsed = DateTime.Now - _startTime;
                    if (TimerLabel != null)
                    {
                        await TimerLabel.Dispatcher.InvokeAsync(() =>
                        {
                            TimerLabel.Text = elapsed.ToString(@"mm\:ss");
                        });
                    }
                }
                catch (TaskCanceledException) { break; }
            }
        }

        private void SetUiState(bool isCleaning)
        {
            if (StartCleanButton == null || StopButton == null || RestartButton == null || ProgressBar == null || DriveSelectComboBox == null || CleanModeComboBox == null || RemoveEdgeCheckBox == null) return;

            StartCleanButton.IsEnabled = !isCleaning;
            StopButton.IsEnabled = isCleaning;
            DriveSelectComboBox.IsEnabled = !isCleaning;
            CleanModeComboBox.IsEnabled = !isCleaning;
            RemoveEdgeCheckBox.IsEnabled = !isCleaning;

            ProgressBar.IsIndeterminate = isCleaning;
            ProgressBar.Value = 0;
            
            RestartButton.Visibility = isCleaning ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void StartCleanButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsRunningAsAdmin())
            {
                LogMessage("UYARI: Uygulama yönetici olarak çalıştırılmıyor, bazı işlemler başarısız olabilir!");
            }

            SetUiState(isCleaning: true);
            _cleanCts = new CancellationTokenSource();
            _startTime = DateTime.Now;

            var minDurationTask = Task.Delay(2000);
            var timerTask = StartTimer(_cleanCts.Token);
            try
            {
                LogMessage("Temizlik işlemi başlatılıyor...");
                var selectedDrive = (DriveSelectComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "C:\\";
                var selectedMode = (CleanModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Quick";
                LogMessage($"Seçilen sürücü: {selectedDrive}, Mod: {selectedMode}");

                LogMessage(string.Format(Properties.Strings.CleanStarted, selectedDrive, selectedMode));
                
                var progress = new Progress<CleanProgressReport>(report =>
                {
                    if (report.CurrentPath != null && StatusText != null)
                    {
                        StatusText.Text = report.CurrentPath;
                    }
                });

                List<string> pathsToClean = GetPathsForCleaningMode(selectedMode, selectedDrive);
                if (pathsToClean.Count == 0)
                {
                    LogMessage("Temizlenecek yol bulunamadı, varsayılan yol ekleniyor: C:\\Windows\\Temp");
                    pathsToClean.Add(Path.Combine("C:\\", "Windows", "Temp"));
                }
                LogMessage($"Temizlenecek yollar: {string.Join(", ", pathsToClean)}");

                int totalDeletedFiles = await CleanPathsAsync(pathsToClean.ToArray(), _cleanCts.Token, progress);

                if (selectedMode == "Extended" && !_cleanCts.Token.IsCancellationRequested)
                {
                    await RunExtendedSystemTools(GetNSudoPath(), _cleanCts.Token);
                    if (RemoveEdgeCheckBox?.IsChecked == true)
                    {
                        await ExecutePowerShellOptimization("Optimize-MicrosoftEdge", _cleanCts.Token);
                    }
                }
                
                string endStatus = _cleanCts.IsCancellationRequested ? 
                    Properties.Strings.CleanCancelled : 
                    string.Format(Properties.Strings.CleanCompleted, totalDeletedFiles);
                StatusText.Text = endStatus;
                OperationStatusLabel.Text = _cleanCts.IsCancellationRequested ? "İptal Edildi" : "Tamamlandı";
                LogMessage(endStatus);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = Properties.Strings.CleanCancelled;
                OperationStatusLabel.Text = "İptal Edildi";
                LogMessage(Properties.Strings.CleanCancelled);
            }
            catch (AggregateException ae)
            {
                foreach (var ex in ae.InnerExceptions)
                {
                    LogMessage($"Toplu işlem hatası: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = Properties.Strings.ErrorOccurred;
                OperationStatusLabel.Text = "Hata";
                LogMessage($"KRİTİK HATA: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                SetUiState(isCleaning: false);
                await Task.WhenAny(minDurationTask, timerTask);
            }
        }

        private async Task ExecutePowerShellOptimization(string optimizationCommand, CancellationToken token)
        {
            try
            {
                string executorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "Executor.ps1");
                string nsudoPath = GetNSudoPath();
                if (!File.Exists(nsudoPath))
                {
                    throw new FileNotFoundException("NSudo.exe bulunamadı", nsudoPath);
                }
                if (!File.Exists(executorPath))
                {
                    throw new FileNotFoundException("Executor.ps1 bulunamadı", executorPath);
                }

                string command = $"{nsudoPath} /U:S /P:E /M:S /UseCurrentConsole powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{executorPath}\" -OptimizationCommand \"{optimizationCommand}\"";

                LogMessage($"Çalıştırılıyor: {optimizationCommand}");

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = psi })
                {
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            LogMessage(e.Data);
                        }
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            LogMessage($"[HATA] {e.Data}");
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync(token);

                    if (process.ExitCode == 0)
                    {
                        LogMessage($"{optimizationCommand} başarıyla tamamlandı");
                    }
                    else
                    {
                        LogMessage($"{optimizationCommand} başarısız oldu (ExitCode: {process.ExitCode})");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ExecutePowerShellOptimization hatası: {ex.Message}");
            }
        }

        private async Task<int> CleanPathsAsync(string[] paths, CancellationToken token, IProgress<CleanProgressReport> progress)
        {
            int totalDeletedFiles = 0;
            foreach (var path in paths)
            {
                if (token.IsCancellationRequested) break;
                
                UpdateProgress(string.Format(Properties.Strings.TaramaBaslaniyor, path), progress);
                LogMessage($"Tarama başlatılıyor: {path}");
                
                int deletedInPath = await CleanFilesInDirectoryAsync(path, token, true, progress);
                Interlocked.Add(ref totalDeletedFiles, deletedInPath);
            }
            return totalDeletedFiles;
        }

        private async Task<int> CleanFilesInDirectoryAsync(string directoryPath, CancellationToken token, bool recursive, IProgress<CleanProgressReport> progress)
        {
            int deletedCount = 0;
            string[] extensions = { "*.~*", "~*.*", "*.??~", "*.---", "*.tmp", "*._mp", "*~tmp.*", "*.??$", "*.fic", "*.$sa", "*._detmp", "*.^", "*.db", "*.$$$", "*.old", "*.bak", "*.bac", "*.cpy", "*.prv", "*.syd", "*.chk", "*.dmp", "*.log", "*.cache", "*.junk", "*.cab", "*.nfo", "thumbs.db" };
            
            var directoriesToScan = new Stack<string>();
            directoriesToScan.Push(directoryPath);

            int batchSize = 50;
            var currentBatch = new List<string>();

            while (directoriesToScan.Count > 0 && !token.IsCancellationRequested)
            {
                currentBatch.Clear();
                
                while (currentBatch.Count < batchSize && directoriesToScan.Count > 0)
                {
                    currentBatch.Add(directoriesToScan.Pop());
                }

                foreach (var currentDir in currentBatch)
                {
                    UpdateProgress(string.Format(Properties.Strings.Isleniyor, currentDir), progress);
                    LogMessage($"İşleniyor: {currentDir}");

                    if (IsPathProtected(currentDir)) continue;

                    var filesToDelete = SafeEnumerateFiles(currentDir, extensions).ToList();
                    LogMessage($"Bulunan dosyalar: {filesToDelete.Count}");

                    await Parallel.ForEachAsync(filesToDelete.Distinct(), new ParallelOptions 
                    { 
                        MaxDegreeOfParallelism = 2,
                        CancellationToken = token 
                    }, async (file, ct) =>
                    {
                        if (ct.IsCancellationRequested) return;
                        await _ioSemaphore.WaitAsync(ct);
                        try
                        {
                            if (await DeleteFileWithRetryAsync(file, ct))
                            {
                                Interlocked.Increment(ref deletedCount);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage(string.Format(Properties.Strings.DosyaIslenirkenHata, file, ex.Message));
                        }
                        finally
                        {
                            _ioSemaphore.Release();
                        }
                    });

                    if (recursive)
                    {
                        try
                        {
                            foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                            {
                                if (!IsPathProtected(subDir))
                                {
                                    directoriesToScan.Push(subDir);
                                }
                            }
                        }
                        catch (UnauthorizedAccessException) 
                        {
                            LogMessage(string.Format(Properties.Strings.SubDirAccessDenied, currentDir));
                        }
                        catch (Exception ex)
                        {
                            LogMessage(string.Format(Properties.Strings.SubDirError, currentDir, ex.Message));
                        }
                    }
                }

                await Task.Delay(200, token);
            }
            LogMessage($"Silinen dosya sayısı: {deletedCount}");
            return deletedCount;
        }

        private IEnumerable<string> SafeEnumerateFiles(string path, string[] searchPatterns)
        {
            foreach (var pattern in searchPatterns)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException)
                {
                    LogMessage(string.Format(Properties.Strings.AccessDenied, $"{path}\\{pattern}"));
                    continue;
                }
                catch (Exception ex)
                {
                    LogMessage(string.Format(Properties.Strings.EnumerationError, $"{path}\\{pattern}", ex.Message));
                    continue;
                }

                int fileCount = 0;
                foreach (var file in files)
                {
                    yield return file;
                    fileCount++;

                    if (fileCount >= 10000)
                    {
                        LogMessage("Çok fazla dosya bulundu, işlem parçalara ayrılıyor...");
                        yield break;
                    }
                }
            }
        }

        private async Task<bool> DeleteFileWithRetryAsync(string file, CancellationToken token)
        {
            if (!_ioSemaphore.Wait(0)) return false;

            try
            {
                if (IsPathProtected(file)) return false;

                try
                {
                    new FileInfo(file) { IsReadOnly = false }.Delete();
                    LogMessage(string.Format(Properties.Strings.FileDeleted, file));
                    return true;
                }
                catch (IOException)
                {
                    return await HandleLockedFile(file, token);
                }
                catch (UnauthorizedAccessException)
                {
                    return await HandleLockedFile(file, token);
                }
                catch (Exception ex)
                {
                    LogMessage(string.Format(Properties.Strings.DosyaSilinirkenHata, file, ex.Message));
                    return false;
                }
            }
            finally
            {
                _ioSemaphore.Release();
            }
        }
        
        private async Task<bool> HandleLockedFile(string file, CancellationToken token)
        {
            LogMessage($"Dosya kilitli: {file}, süreçler kontrol ediliyor...");
            
            Process[] lockingProcesses = Array.Empty<Process>();
            try
            {
                lockingProcesses = await Task.Run(() =>
                {
                    try
                    {
                        var processes = WindowsRestartManager.GetProcessesLockingFile(file) ?? new List<Process>();
                        return processes.ToArray();
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Süreç listeleme hatası: {ex.Message}");
                        return Array.Empty<Process>();
                    }
                }, token);
            }
            catch (Exception ex)
            {
                LogMessage($"Süreç listeleme hatası (Task.Run): {ex.Message}");
            }

            if (lockingProcesses.Length == 0)
            {
                LogMessage($"Kilitleyen süreç bulunamadı, zorla silme denenecek: {file}");
                return await ForceDeleteWithNSudoAsync(file, token);
            }

            var nonCritical = lockingProcesses
                .Where(p => p != null && !_criticalProcesses.Contains(p.ProcessName.ToLowerInvariant()) && p.Id != Process.GetCurrentProcess().Id)
                .ToList();

            bool anyKilled = false;
            foreach (var process in nonCritical)
            {
                try
                {
                    if (HasProcessExited(process)) continue;

                    LogMessage($"Süreç sonlandırılıyor: {process.ProcessName} (PID: {process.Id})");
                    if (await KillProcessWithNSudoAsync(process.Id.ToString(), token))
                    {
                        anyKilled = true;
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Süreç sonlandırma hatası (PID: {process?.Id}): {ex.Message}");
                }
                finally
                {
                    process?.Dispose();
                }
            }

            if (anyKilled)
            {
                await Task.Delay(300, token);
                try
                {
                    new FileInfo(file) { IsReadOnly = false }.Delete();
                    LogMessage($"Süreç sonlandırıldıktan sonra silindi: {file}");
                    return true;
                }
                catch (Exception ex)
                {
                    LogMessage($"Süreç sonlandırılsa da dosya silinemedi: {file} - {ex.Message}");
                }
            }

            return await ForceDeleteWithNSudoAsync(file, token);
        }

        private bool HasProcessExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        }

        private async Task<bool> KillProcessWithNSudoAsync(string processId, CancellationToken token)
        {
            string nsudoPath = GetNSudoPath();
            if (string.IsNullOrEmpty(nsudoPath))
            {
                LogMessage("NSudo bulunamadı!");
                return false;
            }

            string command = $"taskkill /F /PID {processId}";
            string arguments = $"-U:T -P:E -Wait -ShowWindowMode:Hide {command}";

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = nsudoPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = new Process { StartInfo = processInfo };
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => output.AppendLine(e.Data);
                process.ErrorDataReceived += (s, e) => error.AppendLine(e.Data);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(token);

                if (process.ExitCode == 0)
                {
                    LogMessage($"Süreç başarıyla sonlandırıldı (PID: {processId})");
                    return true;
                }
                else if (process.ExitCode == 5)
                {
                    LogMessage($"NSudo hatası (Kod: {process.ExitCode}): PPL koruması nedeniyle süreç sonlandırılamadı: {error}");
                    return false;
                }
                else
                {
                    LogMessage($"NSudo hatası (Kod: {process.ExitCode}): {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"NSudo çalıştırma hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ForceDeleteWithNSudoAsync(string filePath, CancellationToken token)
        {
            string nsudoPath = GetNSudoPath();
            if (string.IsNullOrEmpty(nsudoPath))
            {
                LogMessage("NSudo bulunamadı, zorla silme atlanıyor");
                return false;
            }

            string escapedPath = filePath.Replace("\"", "\"\"");
            string command = $"cmd.exe /c del /f /q \"{escapedPath}\"";

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = nsudoPath,
                    Arguments = $"-U:T -P:E -Wait -ShowWindowMode:Hide {command}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processInfo };
                process.Start();
                await process.WaitForExitAsync(token);

                if (process.ExitCode == 0)
                {
                    LogMessage($"Dosya zorla silindi: {filePath}");
                    return true;
                }
                else
                {
                    LogMessage($"Zorla silme başarısız (Kod: {process.ExitCode}): {filePath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Zorla silme hatası: {ex.Message}");
                return false;
            }
            finally
            {
                await Task.Delay(100, token);
                if (File.Exists(filePath))
                {
                    LogMessage($"Dosya hala mevcut: {filePath}");
                }
            }
        }
        
        private void UpdateProgress(string message, IProgress<CleanProgressReport> progress)
        {
            if ((DateTime.Now - _lastProgressUpdate).TotalMilliseconds < 500) return;
            
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    progress.Report(new CleanProgressReport { CurrentPath = message });
                }
                catch { /* UI thread hatasını yut */ }
            });
            
            _lastProgressUpdate = DateTime.Now;
        }

        private async Task RunExtendedSystemTools(string nsudoPath, CancellationToken token)
        {
            LogMessage("Genişletilmiş sistem araçları çalıştırılıyor.");
            var commands = new Dictionary<string, string>
            {
                { "Gölge kopyalar temizleniyor...", "vssadmin delete shadows /for=C: /all /quiet" },
                { "DISM bileşen temizliği başlatılıyor...", "dism /Online /Cleanup-Image /StartComponentCleanup" },
                { "Geri Dönüşüm Kutusu boşaltılıyor...", "powershell.exe -NoProfile -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"" }
            };
            
            if (ProgressBar == null || StatusText == null) return;
            
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            ProgressBar.Maximum = commands.Count + (RemoveEdgeCheckBox?.IsChecked == true ? 1 : 0);

            foreach (var command in commands)
            {
                if (token.IsCancellationRequested) break;
                LogMessage(command.Key);
                StatusText.Text = command.Key;
                await RunWithNSudo(nsudoPath, command.Value, "İşlem tamamlandı.", token);
                ProgressBar.Value++;
            }
        }

        private List<string> GetPathsForCleaningMode(string selectedMode, string selectedDrive)
        {
            var paths = new List<string>();
            switch (selectedMode)
            {
                case "Quick":
                    paths.AddRange(new[]
                    {
                        Path.GetTempPath(),
                        Environment.ExpandEnvironmentVariables("%SystemRoot%\\Temp"),
                        @"C:\Windows\Prefetch"
                    }.Where(Directory.Exists));
                    if (!paths.Any()) paths.Add(Path.Combine("C:\\", "Windows", "Temp"));
                    break;
                case "Deep":
                case "Extended":
                    if (Directory.Exists(selectedDrive)) paths.Add(selectedDrive);
                    else paths.Add(@"C:\");
                    break;
            }
            return paths;
        }

        private void InitializeDrives() 
        { 
            if (DriveSelectComboBox == null) return;
            DriveSelectComboBox.Items.Clear();
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => new ComboBoxItem { Content = d.Name, Tag = d.Name })
                .ToList();

            if (drives.Any())
            {
                DriveSelectComboBox.ItemsSource = drives;
                DriveSelectComboBox.SelectedIndex = 0;
            }
            else
            {
                DriveSelectComboBox.Items.Add(new ComboBoxItem { Content = "Hiç sabit disk bulunamadı", IsEnabled = false });
            }
        }

        private void InitializeCleanModes() 
        { 
            if (CleanModeComboBox == null) return;
            CleanModeComboBox.Items.Clear();
            var modes = new List<ComboBoxItem>
            {
                new ComboBoxItem { Content = Properties.Strings.QuickClean, Tag = "Quick" },
                new ComboBoxItem { Content = Properties.Strings.DeepClean, Tag = "Deep" },
                new ComboBoxItem { Content = Properties.Strings.ExtendedClean, Tag = "Extended" }
            };
            CleanModeComboBox.ItemsSource = modes;
            CleanModeComboBox.SelectedIndex = 0;
        }

        private void InitializeCriticalPaths() 
        {
            _criticalExclusionPaths.Clear();
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
            _criticalExclusionPaths.Add(@"C:\Users\pc\Templates".ToUpperInvariant());
        }

        private bool IsPathProtected(string path)
        {
            try
            {
                string fullPathUpper = Path.GetFullPath(path).ToUpperInvariant();
                return _criticalExclusionPaths.Any(p => fullPathUpper.StartsWith(p)) ||
                       fullPathUpper.StartsWith(AppDomain.CurrentDomain.BaseDirectory.ToUpperInvariant().TrimEnd('\\'));
            }
            catch { return true; }
        }

        private string GetNSudoPath()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "nsudo");
            string nsudoExe = Path.Combine(dir, "NSudo.exe");
            string nsudoLC = Path.Combine(dir, "NSudoLC.exe");
            return File.Exists(nsudoExe) ? nsudoExe : File.Exists(nsudoLC) ? nsudoLC : "";
        }

        private async Task RunWithNSudo(string nsudoPath, string command, string successMessage, CancellationToken token)
        {
            await _nsudoSemaphore.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = nsudoPath,
                    Arguments = $"-U:T -P:E -Wait -ShowWindowMode:Hide {command}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => output.AppendLine(e.Data);
                process.ErrorDataReceived += (s, e) => error.AppendLine(e.Data);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(token);

                if (process.ExitCode == 0)
                {
                    LogMessage(successMessage);
                    if (output.Length > 0) LogMessage(output.ToString());
                }
                else
                {
                    LogMessage($"NSudo komut hatası (Kod: {process.ExitCode}): {command}");
                    if (error.Length > 0) LogMessage($"Hata: {error}");
                }
            }
            catch (OperationCanceledException) { LogMessage("NSudo işlemi iptal edildi."); }
            catch (Exception ex) { LogMessage($"NSudo Hatası: {ex.Message}"); }
            finally
            {
                _nsudoSemaphore.Release();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _cleanCts?.Cancel();
            StatusText.Text = "İşlem durduruluyor...";
            LogMessage("Kullanıcı işlemi durdurmayı talep etti...");
            Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (LogTextBox == null || ProgressBar == null || TimerLabel == null || StatusText == null || OperationStatusLabel == null || RestartButton == null) return;
            LogTextBox.Clear();
            ProgressBar.Value = 0;
            TimerLabel.Text = "00:00";
            StatusText.Text = "Hazır";
            OperationStatusLabel.Text = "Hazır";
            SetUiState(isCleaning: false); 
            LogMessage(Properties.Strings.AppStarted);
        }

        private void CleanModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CleanModeComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                LogMessage(string.Format(Properties.Strings.SelectedMode, selectedItem.Content, selectedItem.Tag));
                if (RemoveEdgeCheckBox != null)
                {
                    RemoveEdgeCheckBox.Visibility = selectedItem.Tag.ToString() == "Extended" ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private bool IsRunningAsAdmin()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private async Task CheckSystemResources(CancellationToken token)
        {
            try
            {
                var cpuUsage = await GetCpuUsageAsync();
                var memUsage = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                
                if (cpuUsage > 80 || memUsage > 500)
                {
                    LogMessage($"Sistem yükü çok yüksek (CPU: {cpuUsage}%, Bellek: {memUsage}MB), işlem hızı düşürülüyor...");
                    await Task.Delay(5000, token);
                }
                else
                {
                    await Task.Delay(1000, token);
                }
            }
            catch { /* Hataları yut */ }
        }

        private async Task<double> GetCpuUsageAsync()
        {
            await Task.Delay(100);
            return new Random().Next(0, 100); // Gerçek bir izleyici ile değiştirilmeli
        }
    }

    public class CleanProgressReport
    {
        public string? CurrentPath { get; set; }
    }
}