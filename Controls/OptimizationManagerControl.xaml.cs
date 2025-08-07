using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinFastGUI.Controls
{
    public partial class OptimizationManagerControl : UserControl, IDisposable
    {
        // --- Sabitler ve Yollar ---
        private readonly string executorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "Executor.ps1");
        private readonly string nsudoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "nsudo", "NSudo.exe");
        private readonly string optimizationListPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "optimization.json");
        private readonly string mainLogPath = Path.Combine(Path.GetTempPath(), "WinFastGUI.log");

        // --- Güvenlik ve Limitler ---
        private const int MaxSelections = 45;
        private readonly string[] DANGEROUS_OPTIMIZATIONS = { };

        // --- Durum ve Veri Değişkenleri ---
        private bool _isRunning = false;
        private bool _disposed = false;
        private List<OptimizationOption> loadedOptions = new List<OptimizationOption>();

        // --- Performance Manager ---
        private static readonly Lazy<PerformanceCounterManager> _performanceManager = 
            new Lazy<PerformanceCounterManager>(() => new PerformanceCounterManager());

        // --- Log Sistemi ---
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private readonly Timer _logFlushTimer;
        private readonly object _logLock = new object();

        // --- Process Limiting ---
        private readonly SemaphoreSlim _processLimitSemaphore = new SemaphoreSlim(2, 2);

        // --- Error Tracking ---
        private readonly List<OptimizationError> _errorHistory = new List<OptimizationError>();

        // --- Cancellation Token Source ---
        private CancellationTokenSource? _cancellationTokenSource;

        private class OptimizationOption
        {
            public string Name { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Warning { get; set; } = string.Empty;
            public bool IsSafe { get; set; }
            public string Category { get; set; } = "System";
            public int Priority { get; set; } = 1;
            public string ResourceIntensity { get; set; } = "Low";
            public bool RequiresReboot { get; set; }
            public List<string> ConflictsWith { get; set; } = new List<string>();
            public List<string> Dependencies { get; set; } = new List<string>();
        }

        private class OptimizationError
        {
            public string OptimizationName { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public bool IsRecoverable { get; set; }
        }

        private class PerformanceCounterManager : IDisposable
        {
            private PerformanceCounter? _cpuCounter;
            private PerformanceCounter? _ramCounter;
            private PerformanceCounter? _kernelCounter;
            private PerformanceCounter? _diskCounter;
            private readonly object _lockObject = new object();
            private DateTime _lastUpdate = DateTime.MinValue;
            private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(2);
            
            // Cached values
            private float _cachedCpuUsage;
            private float _cachedRamUsage;
            private float _cachedKernelUsage;
            private float _cachedDiskUsage;

            public async Task InitializeAsync()
            {
                await Task.Run(() =>
                {
                    try
                    {
                        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                        _ramCounter = new PerformanceCounter("Memory", "Available MBytes", true);
                        _kernelCounter = new PerformanceCounter("Processor", "% Privileged Time", "_Total", true);
                        _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
                        
                        // İlk değerleri al
                        _cpuCounter?.NextValue();
                        _kernelCounter?.NextValue();
                        _diskCounter?.NextValue();
                        Thread.Sleep(1000);
                        UpdateCachedValues();
                    }
                    catch (Exception)
                    {
                        // Hata durumunda null bırak
                        DisposeCounters();
                    }
                });
            }

            public (float cpu, float ram, float kernel, float disk) GetCurrentValues()
            {
                lock (_lockObject)
                {
                    if (DateTime.Now - _lastUpdate > _updateInterval)
                    {
                        UpdateCachedValues();
                    }
                    return (_cachedCpuUsage, _cachedRamUsage, _cachedKernelUsage, _cachedDiskUsage);
                }
            }

            private void UpdateCachedValues()
            {
                try
                {
                    _cachedCpuUsage = _cpuCounter?.NextValue() ?? 0;
                    _cachedRamUsage = _ramCounter?.NextValue() ?? 0;
                    _cachedKernelUsage = _kernelCounter?.NextValue() ?? 0;
                    _cachedDiskUsage = _diskCounter?.NextValue() ?? 0;
                    _lastUpdate = DateTime.Now;
                }
                catch (Exception)
                {
                    // Hata durumunda mevcut değerleri koru
                }
            }

            private void DisposeCounters()
            {
                _cpuCounter?.Dispose();
                _ramCounter?.Dispose();
                _kernelCounter?.Dispose();
                _diskCounter?.Dispose();
                _cpuCounter = null;
                _ramCounter = null;
                _kernelCounter = null;
                _diskCounter = null;
            }

            public bool IsAvailable => _cpuCounter != null && _ramCounter != null && _kernelCounter != null && _diskCounter != null;

            public void Dispose()
            {
                DisposeCounters();
                GC.SuppressFinalize(this);
            }
        }

        public OptimizationManagerControl()
        {
            InitializeComponent();
            SetupCrashMonitoring();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupTempFiles();
            
            // Log flush timer - batch log yazma için
            _logFlushTimer = new Timer(FlushLogQueue, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            
            _ = LoadInitialDataAsync();
        }

        private void SetupCrashMonitoring()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Log($"‼️ KRİTİK ÇÖKME: {args.ExceptionObject}", "CRASH");
                SaveCrashDump();
            };
        }

        private void SaveCrashDump()
        {
            try
            {
                string crashLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"WinFastGUI_CRASH_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(crashLogPath, $"⏰ {DateTime.Now}\n\n{LogTextBox?.Text ?? "Log alınamadı"}");
                Log($"Çökme raporu kaydedildi: {crashLogPath}", "INFO");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Crash raporu yazılamadı: {ex.Message}");
            }
        }

        private void FlushLogQueue(object? state)
        {
            if (_logQueue.IsEmpty) return;

            var logEntries = new List<string>();
            while (_logQueue.TryDequeue(out string? entry) && logEntries.Count < 50)
            {
                logEntries.Add(entry);
            }

            if (logEntries.Count > 0)
            {
                try
                {
                    string combinedLog = string.Join("", logEntries);
                    
                    // UI güncelleme
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (LogTextBox != null)
                        {
                            LogTextBox.AppendText(combinedLog);
                            if (LogTextBox.Text.Length > 100000)
                            {
                                LogTextBox.Text = LogTextBox.Text.Substring(LogTextBox.Text.Length - 50000);
                            }
                            LogTextBox.ScrollToEnd();
                        }
                    });

                    // Dosya yazma
                    lock (_logLock)
                    {
                        File.AppendAllText(mainLogPath, combinedLog);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Log flush hatası: {ex.Message}");
                }
            }
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                Log("Uygulama başlatılıyor...");
                await _performanceManager.Value.InitializeAsync();
                await VerifyLoggingSystem();
                CleanupTempFiles();
                await LoadOptimizationOptionsAsync();
                Log("Uygulama başarıyla başlatıldı.", "SUCCESS");
            }
            catch (Exception ex)
            {
                Log($"Uygulama başlatılırken kritik hata: {ex.Message}", "CRASH");
                HandleError(ex, "Uygulama başlatma");
            }
        }

        private void Log(string message, string level = "INFO")
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\n";
            _logQueue.Enqueue(logEntry);
        }

        private async Task LogAsync(string message, string level = "INFO")
        {
            await Task.Run(() => Log(message, level));
        }

        private async Task LoadOptimizationOptionsAsync()
        {
            try
            {
                Log("Optimizasyon seçenekleri yükleniyor...");
                
                // Progress göstergesi ekle
                await Dispatcher.InvokeAsync(() => 
                {
                    if (StatusLabel != null)
                        StatusLabel.Content = "Optimizasyon seçenekleri yükleniyor...";
                });
                
                if (!File.Exists(optimizationListPath))
                    throw new FileNotFoundException($"optimization.json bulunamadı: {optimizationListPath}");

                // Dosya okumayı async yap
                var json = await File.ReadAllTextAsync(optimizationListPath);
                var options = JsonSerializer.Deserialize<List<OptimizationOption>>(json);
                this.loadedOptions = options ?? new List<OptimizationOption>();

                // UI güncellemesini async yap
                await Dispatcher.InvokeAsync(() =>
                {
                    if (OptimizationOptionsPanel != null)
                    {
                        OptimizationOptionsPanel.Children.Clear();
                        
                        // UI elementlerini batch halinde ekle
                        foreach (var opt in this.loadedOptions)
                        {
                            var checkBox = CreateOptimizationCheckBox(opt);
                            OptimizationOptionsPanel.Children.Add(checkBox);
                        }
                    }
                });
                
                Log($"{loadedOptions.Count} optimizasyon seçeneği yüklendi.", "SUCCESS");
            }
            catch (Exception ex)
            {
                Log($"Optimizasyon seçenekleri yüklenemedi: {ex.Message}", "ERROR");
                HandleError(ex, "Optimizasyon seçenekleri yükleme");
            }
            finally
            {
                await Dispatcher.InvokeAsync(() => 
                {
                    if (StatusLabel != null)
                        StatusLabel.Content = "Hazır";
                });
            }
        }

        private CheckBox CreateOptimizationCheckBox(OptimizationOption opt)
        {
            var checkBox = new CheckBox
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = $"{opt.Title} ({opt.Category})", FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
                        new TextBlock { Text = opt.Description, FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = opt.Warning, FontSize = 10, Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, Visibility = string.IsNullOrEmpty(opt.Warning) ? Visibility.Collapsed : Visibility.Visible }
                    }
                },
                Margin = new Thickness(5),
                Foreground = Brushes.White,
                Tag = opt.Name
            };

            if (DANGEROUS_OPTIMIZATIONS.Contains(opt.Name))
            {
                checkBox.IsEnabled = false;
                checkBox.ToolTip = "Bu optimizasyon, sistem kararsızlığına neden olabileceğinden güvenlik için devre dışı bırakılmıştır.";
                checkBox.Opacity = 0.6;
            }

            checkBox.Checked += CheckBox_Checked;
            checkBox.Unchecked += CheckBox_Checked;
            return checkBox;
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var selectedOptions = OptimizationOptionsPanel?.Children.OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => loadedOptions.First(o => o.Name == cb.Tag?.ToString()))
                .ToList() ?? new List<OptimizationOption>();

            var orderedOptions = SortAndGroupOptimizations(selectedOptions);
            if (orderedOptions.Count > MaxSelections)
            {
                MessageBox.Show($"Sistem kararlılığını korumak için aynı anda en fazla {MaxSelections} optimizasyon seçebilirsiniz. Şu an {orderedOptions.Count} optimizasyon seçili.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (sender is CheckBox triggeredCheckBox)
                {
                    triggeredCheckBox.Checked -= CheckBox_Checked;
                    triggeredCheckBox.Unchecked -= CheckBox_Checked;
                    triggeredCheckBox.IsChecked = false;
                    triggeredCheckBox.Checked += CheckBox_Checked;
                    triggeredCheckBox.Unchecked += CheckBox_Checked;
                }
            }
        }

        private bool IsSystemHealthy(bool isCriticalCheck = false)
        {
            if (!_performanceManager.Value.IsAvailable)
            {
                Log("Performans sayaçları kullanılamıyor, sistem sağlığı kontrolü atlandı.", "WARNING");
                return true;
            }

            try
            {
                Log("Sistem sağlığı kontrol ediliyor...");
                var (cpuUsage, availableRam, kernelUsage, diskUsage) = _performanceManager.Value.GetCurrentValues();

                float cpuThreshold = isCriticalCheck ? 80 : 50;
                float ramThreshold = 1024;
                float kernelThreshold = isCriticalCheck ? 50 : 30;
                float diskThreshold = isCriticalCheck ? 70 : 50;

                if (cpuUsage > cpuThreshold || kernelUsage > kernelThreshold || availableRam < ramThreshold || diskUsage > diskThreshold)
                {
                    string warningMsg = $"{(isCriticalCheck ? "KRİTİK" : "UYARI")}: SİSTEM YÜKÜ YÜKSEK | CPU: {cpuUsage:F1}% | Kernel: {kernelUsage:F1}% | RAM: {availableRam:F0}MB | Disk: {diskUsage:F1}%";
                    Log(warningMsg, isCriticalCheck ? "CRITICAL" : "WARNING");
                    return false;
                }

                Log($"Sistem sağlığı iyi. CPU: {cpuUsage:F1}%, RAM: {availableRam:F0}MB, Disk: {diskUsage:F1}%", "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Sistem sağlık kontrolü başarısız: {ex.Message}", "ERROR");
                return false;
            }
        }

        private async Task WaitForSystemStability(CancellationToken cancellationToken = default)
        {
            if (!_performanceManager.Value.IsAvailable)
            {
                Log("Sabit bekleme uygulanıyor (2 saniye)...", "INFO");
                await Task.Delay(2000, cancellationToken);
                return;
            }

            Log("Sistem stabil duruma geçmesi bekleniyor...");
            await Dispatcher.InvokeAsync(() => 
            {
                if (StatusLabel != null)
                    StatusLabel.Content = "Sistem stabilize ediliyor...";
            });

            int maxWaitTimeMs = 15000;
            int checkIntervalMs = 1000;

            for (int elapsedMs = 0; elapsedMs < maxWaitTimeMs; elapsedMs += checkIntervalMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(checkIntervalMs, cancellationToken);
                var (cpuUsage, _, _, diskUsage) = _performanceManager.Value.GetCurrentValues();
                Log($"Mevcut CPU kullanımı: {cpuUsage:F1}%, Disk kullanımı: {diskUsage:F1}%");

                if (cpuUsage < 25.0f && diskUsage < 50.0f)
                {
                    Log("Sistem stabil duruma geçti.", "SUCCESS");
                    return;
                }
            }
            Log("Maksimum bekleme süresi doldu, devam ediliyor.", "WARNING");
        }

        private async Task<string> ExecutePowerShellOptimizationImproved(string optimizationName, CancellationToken cancellationToken = default)
        {
            await _processLimitSemaphore.WaitAsync(cancellationToken);
            try
            {
                return await ExecutePowerShellOptimizationInternal(optimizationName, cancellationToken);
            }
            finally
            {
                _processLimitSemaphore.Release();
            }
        }

        private async Task<string> ExecutePowerShellOptimizationInternal(string optimizationName, CancellationToken cancellationToken = default)
        {
            string tempLogPath = Path.Combine(Path.GetTempPath(), $"WinFastGUI_PS_{Guid.NewGuid()}.log");
            await LogAsync($"NSudo ile başlatılıyor: {optimizationName}. Geçici log: {tempLogPath}");

            try
            {
                if (!File.Exists(nsudoPath)) throw new FileNotFoundException($"NSudo.exe bulunamadı: {nsudoPath}");
                if (!File.Exists(executorPath)) throw new FileNotFoundException($"Executor.ps1 bulunamadı: {executorPath}");

                // İzin testi
                await VerifyFileAccess(tempLogPath);

                string psWrapperCommand = $"& {{ & '{executorPath}' -OptimizationCommand '{optimizationName}' -ExecutingScriptPath '{executorPath}' }} *>&1 | Out-File -FilePath '{tempLogPath}' -Encoding utf8";
                await LogAsync($"PowerShell komutu: {psWrapperCommand}", "DEBUG");
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psWrapperCommand));

                var psi = new ProcessStartInfo
                {
                    FileName = nsudoPath,
                    Arguments = $"-U:T -P:E powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executorPath)
                };

                await LogAsync($"NSudo komut satırı: {psi.Arguments}", "DEBUG");

                using (var proc = new Process { StartInfo = psi })
                {
                    if (!proc.Start())
                        throw new Exception("NSudo süreci başlatılamadı.");

                    try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* Ignore priority setting errors */ }

                    await proc.WaitForExitAsync(cancellationToken);

                    string capturedOutput = await ProcessLogOutput(tempLogPath, optimizationName);

                    if (proc.ExitCode != 0)
                    {
                        throw new Exception($"NSudo ile işlem başarısız. Çıkış kodu: {proc.ExitCode}. Detaylar: {capturedOutput}");
                    }

                    await LogAsync($"{optimizationName} başarıyla tamamlandı.", "SUCCESS");
                    return tempLogPath;
                }
            }
            catch (OperationCanceledException)
            {
                await LogAsync($"⏲️ İşlem zaman aşımına uğradı: {optimizationName}", "ERROR");
                return string.Empty;
            }
            catch (Exception ex)
            {
                await LogAsync($"KRİTİK HATA ({optimizationName}): {ex.Message}", "ERROR");
                return string.Empty;
            }
            finally
            {
                await CleanupTempFileAsync(tempLogPath);
            }
        }

        private async Task<bool> TryExecuteOptimization(OptimizationOption opt, int retryCount = 2, CancellationToken cancellationToken = default)
        {
            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                try
                {
                    await ExecutePowerShellOptimizationImproved(opt.Name, cancellationToken);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw; // Re-throw cancellation
                }
                catch (Exception ex)
                {
                    var error = new OptimizationError
                    {
                        OptimizationName = opt.Name,
                        ErrorMessage = ex.Message,
                        IsRecoverable = attempt < retryCount
                    };
                    _errorHistory.Add(error);

                    if (attempt < retryCount)
                    {
                        Log($"Optimizasyon başarısız, tekrar deneniyor ({attempt + 1}/{retryCount + 1}): {opt.Name}", "WARNING");
                        await Task.Delay(2000, cancellationToken);
                    }
                    else
                    {
                        Log($"Optimizasyon kalıcı olarak başarısız: {opt.Name} - {ex.Message}", "ERROR");
                        return false;
                    }
                }
            }
            return false;
        }

        private async Task RunOptimizationsWithProgress(List<OptimizationOption> orderedOptions, CancellationToken cancellationToken = default)
        {
            var progress = new Progress<(int current, int total, string current_name)>(report =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (StatusLabel != null)
                        StatusLabel.Content = $"İşleniyor: {report.current_name} ({report.current}/{report.total})";
                });
            });

            for (int i = 0; i < orderedOptions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var opt = orderedOptions[i];
                ((IProgress<(int, int, string)>)progress).Report((i + 1, orderedOptions.Count, opt.Title));
                
                await TryExecuteOptimization(opt, cancellationToken: cancellationToken);
                await Task.Delay(1000, cancellationToken); // Sistem için kısa bekleme
            }
        }

        private async Task<string> ProcessLogOutput(string tempLogPath, string optimizationName)
        {
            const int maxRetries = 3;
            const int delayMs = 500;
            string capturedOutput = string.Empty;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(tempLogPath))
                    {
                        capturedOutput = await File.ReadAllTextAsync(tempLogPath);
                        await LogAsync($"--- {optimizationName} Çıktısı ---");
                        await LogAsync(capturedOutput);
                        await LogAsync("--- Çıktı Sonu ---");
                        break;
                    }
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    await Task.Delay(delayMs);
                    continue;
                }
                catch (Exception ex)
                {
                    Log($"Log işleme hatası: {ex.Message}", "ERROR");
                    break;
                }
            }

            return capturedOutput;
        }

        private async Task CleanupTempFileAsync(string tempLogPath)
        {
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(tempLogPath))
                    {
                        FileInfo fileInfo = new FileInfo(tempLogPath);
                        if (fileInfo.LastWriteTime < DateTime.Now.AddMinutes(-30))
                        {
                            fileInfo.Delete();
                            Log($"Geçici dosya silindi: {tempLogPath}", "DEBUG");
                        }
                        break;
                    }
                }
                catch (Exception) when (i < maxRetries - 1)
                {
                    await Task.Delay(500);
                    continue;
                }
                catch (Exception ex)
                {
                    Log($"Geçici dosya silinemedi: {tempLogPath} - {ex.Message}", "WARNING");
                    break;
                }
            }
        }

        private void CleanupTempFiles()
        {
            try
            {
                string tempPath = Path.GetTempPath();
                var files = Directory.GetFiles(tempPath, "WinFastGUI_PS_*.log");
                foreach (var file in files)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTime < DateTime.Now.AddMinutes(-30))
                        {
                            fileInfo.Delete();
                            Log($"Geçici dosya silindi: {file}", "DEBUG");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Geçici dosya silinemedi: {file} - {ex.Message}", "WARNING");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Geçici dosya temizleme hatası: {ex.Message}", "ERROR");
            }
        }

        private async Task<bool> VerifyFileAccess(string path)
        {
            try
            {
                string testContent = $"Test yazısı {Guid.NewGuid()}";
                await File.WriteAllTextAsync(path, testContent);
                string readContent = await File.ReadAllTextAsync(path);

                if (readContent != testContent)
                    throw new Exception("Yazılan içerik okunan içerikle eşleşmiyor");

                File.Delete(path);
                Log($"Dosya erişim testi başarılı: {path}", "SUCCESS");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Log($"Dosya erişim izni yok: {path}", "ERROR");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Dosya erişim testi başarısız: {path} - {ex.Message}", "ERROR");
                return false;
            }
        }

        private async Task<bool> VerifyNSudoAccess()
        {
            try
            {
                FileInfo nsudoFile = new FileInfo(nsudoPath);
                if (!nsudoFile.Exists)
                    return false;

                var psi = new ProcessStartInfo
                {
                    FileName = nsudoPath,
                    Arguments = "-V",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();
                    await proc.WaitForExitAsync();
                    return proc.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Log($"NSudo erişim testi başarısız: {ex.Message}", "ERROR");
                return false;
            }
        }

        private async Task VerifyLoggingSystem()
        {
            string testPath = Path.Combine(Path.GetTempPath(), $"WinFastGUI_Test_{Guid.NewGuid()}.log");
            if (!await VerifyFileAccess(testPath))
            {
                HandleError(new Exception("Geçici dosya sistemi çalışmıyor"), "Log sistemi testi");
            }
        }

        private bool IsKernelResourcesAvailable()
        {
            try
            {
                using (var pc = new PerformanceCounter("System", "Handle Count"))
                {
                    float handleCount = pc.NextValue();
                    float threshold = 15000;
                    return handleCount < threshold;
                }
            }
            catch
            {
                return true;
            }
        }

        private class KernelResourceWatcher : IDisposable
        {
            private readonly OptimizationManagerControl _parent;
            private Timer? _timer;
            private int _processId;
            public bool HasExceededLimits { get; private set; }

            public KernelResourceWatcher(OptimizationManagerControl parent)
            {
                _parent = parent ?? throw new ArgumentNullException(nameof(parent));
                _timer = null;
                _processId = 0;
            }

            public void MonitorProcess(int pid)
            {
                _processId = pid;
                _timer = new Timer(CheckResources, null, 0, 5000);
            }

            private void CheckResources(object? state)
            {
                try
                {
                    using (var process = Process.GetProcessById(_processId))
                    {
                        float cpuUsage = process.TotalProcessorTime.Ticks / (float)TimeSpan.TicksPerSecond;
                        int handleCount = process.HandleCount;
                        long memoryUsage = process.WorkingSet64 / 1024 / 1024;

                        if (handleCount > 1000 || memoryUsage > 500)
                        {
                            HasExceededLimits = true;
                            _parent.Log($"⚠️ Kernel kaynak uyarısı - PID: {_processId}, Handles: {handleCount}, Memory: {memoryUsage}MB", "WARNING");
                        }
                    }
                }
                catch 
                { 
                    // İşlem zaten sonlanmış olabilir 
                }
            }

            public string GetCurrentState()
            {
                try
                {
                    using (var pc1 = new PerformanceCounter("System", "Handle Count"))
                    using (var pc2 = new PerformanceCounter("Process", "Handle Count", "_Total"))
                    {
                        float systemHandles = pc1.NextValue();
                        float processHandles = pc2.NextValue();
                        return $"System Handles: {systemHandles}, Total Process Handles: {processHandles}";
                    }
                }
                catch (Exception ex)
                {
                    _parent.Log($"KernelResourceWatcher hata: {ex.Message}", "ERROR");
                    return "Kaynak durumu alınamadı";
                }
            }

            public void Dispose()
            {
                _timer?.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private async Task ExecuteWithRecovery(Func<Task<string>> optimizationTask)
        {
            try
            {
                await optimizationTask();
            }
            catch (Exception ex)
            {
                Log($"Optimizasyon sırasında kurtarılabilir hata: {ex.Message}", "ERROR");
                await Task.Delay(2000);
            }
        }

private List<OptimizationOption> SortAndGroupOptimizations(List<OptimizationOption> options)
{
    Log("Optimizasyonlar sıralanıyor ve gruplandırılıyor...");
    var enabledOptimizations = new List<string>();
    var result = new List<OptimizationOption>();
    var skippedOptions = new List<string>();

    var grouped = options
        .OrderBy(o => o.Priority)
        .GroupBy(o => o.Category)
        .OrderBy(g => g.Min(o => o.Priority))
        .ToList();

    foreach (var group in grouped)
    {
        Log($"Kategori işleniyor: {group.Key}");
        foreach (var opt in group.OrderBy(o => o.Priority))
        {
            // Öncelikle bağımlılıkları kontrol et
            bool dependenciesMissing = opt.Dependencies.Any(d => !enabledOptimizations.Contains(d));
            if (dependenciesMissing)
            {
                // Eksik bağımlılık var ise
                Log($"Bağımlılık eksik olduğu için atlandı: {opt.Name} (Bağımlılıklar: {string.Join(", ", opt.Dependencies)})", "WARNING");

                // Eksik olan bağımlılıkları bul
                var missingDeps = opt.Dependencies.Where(d => !enabledOptimizations.Contains(d)).ToList();

                // Eksik bağımlılıkları loadedOptions listesinden bul ve listeye ekle (recursive olarak)
                foreach (var depName in missingDeps)
                {
                    var depOpt = loadedOptions.FirstOrDefault(o => o.Name == depName);
                    if (depOpt != null && !result.Contains(depOpt))
                    {
                        // Eğer eksik bağımlılıklar da bağımlılık gerekiyorsa onları da ekle (burada recursive çağrı veya loop ile çözüm olabilir)
                        AddOptimizationWithDependencies(depOpt, enabledOptimizations, result);
                    }
                }

                // Bu optimizasyonu da ekle
                if (!result.Contains(opt))
                {
                    result.Add(opt);
                    enabledOptimizations.Add(opt.Name);
                    Log($"Optimizasyon eklendi (bağımlılıklar eklendi): {opt.Name} (Öncelik: {opt.Priority}, Kaynak: {opt.ResourceIntensity})", "INFO");
                }
                continue;
            }

            // Çakışma kontrolü
            if (opt.ConflictsWith.Any(c => enabledOptimizations.Contains(c)))
            {
                Log($"Çakışma nedeniyle atlandı: {opt.Name} (Çakışan: {string.Join(", ", opt.ConflictsWith)})", "WARNING");
                skippedOptions.Add($"{opt.Name}: Çakışan optimizasyonlar ({string.Join(", ", opt.ConflictsWith)})");
                continue;
            }

            // Bağımlılığı sorunsuzsa direkt ekle
            if (!result.Contains(opt))
            {
                result.Add(opt);
                enabledOptimizations.Add(opt.Name);
                Log($"Optimizasyon eklendi: {opt.Name} (Öncelik: {opt.Priority}, Kaynak: {opt.ResourceIntensity})", "INFO");
            }
        }
    }

    if (skippedOptions.Count > 0)
    {
        Dispatcher.BeginInvoke(() =>
        {
            MessageBox.Show($"Bazı optimizasyonlar atlandı:\n{string.Join("\n", skippedOptions)}", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    Log($"Toplam {result.Count} optimizasyon seçildi.");
    return result;
}

// Bağımlılıkları ile birlikte ekleyen yardımcı metod
private void AddOptimizationWithDependencies(
    OptimizationOption opt,
    List<string> enabledOptimizations,
    List<OptimizationOption> result)
{
    // Öncelikle bağımlılıkları ekle
    foreach (var depName in opt.Dependencies)
    {
        if (!enabledOptimizations.Contains(depName))
        {
            var depOpt = loadedOptions.FirstOrDefault(o => o.Name == depName);
            if (depOpt != null && !result.Contains(depOpt))
                AddOptimizationWithDependencies(depOpt, enabledOptimizations, result);
        }
    }

    // Daha sonra kendisini ekle
    if (!enabledOptimizations.Contains(opt.Name))
    {
        result.Add(opt);
        enabledOptimizations.Add(opt.Name);
        Log($"Optimizasyon eklendi (bağımlılık ile birlikte): {opt.Name} (Öncelik: {opt.Priority}, Kaynak: {opt.ResourceIntensity})", "INFO");
    }
}


        private void HandleError(Exception ex, string context)
        {
            Log($"Hata: {context} - {ex.Message}", "ERROR");
            Dispatcher.BeginInvoke(() =>
            {
                MessageBox.Show($"Bir hata oluştu: {context}\nDetay: {ex.Message}\n\nLütfen logları kontrol edin.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private async void RunTweaksButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                MessageBox.Show("Zaten bir optimizasyon işlemi çalışıyor. Lütfen bekleyin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            
            if (RunTweaksButton != null) RunTweaksButton.IsEnabled = false;
            if (StatusLabel != null)
            {
                StatusLabel.Content = "Çalışıyor...";
                StatusLabel.Foreground = Brushes.Orange;
            }

            try
            {
                Log("Optimizasyon işlemi başlatılıyor...");
                CleanupTempFiles();
                
                var selectedOptions = OptimizationOptionsPanel?.Children.OfType<CheckBox>()
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => loadedOptions.First(o => o.Name == cb.Tag?.ToString()))
                    .ToList() ?? new List<OptimizationOption>();

                if (selectedOptions.Count == 0)
                {
                    MessageBox.Show("Lütfen en az bir optimizasyon seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Log($"Seçilen optimizasyon sayısı: {selectedOptions.Count}");
                var orderedOptions = SortAndGroupOptimizations(selectedOptions);

                if (orderedOptions.Count == 0)
                {
                    MessageBox.Show("Seçilen optimizasyonlar çakışmalar veya bağımlılıklar nedeniyle çalıştırılamadı.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await RunOptimizationsWithProgress(orderedOptions, _cancellationTokenSource.Token);

                bool requiresReboot = orderedOptions.Any(o => o.RequiresReboot);
                if (StatusLabel != null)
                {
                    StatusLabel.Content = "Tamamlandı";
                    StatusLabel.Foreground = Brushes.Green;
                }
                
                Log("Tüm optimizasyonlar başarıyla tamamlandı", "SUCCESS");
                MessageBox.Show(requiresReboot
                    ? "Optimizasyonlar tamamlandı! Bazı değişikliklerin etkili olması için sistemin yeniden başlatılması gerekiyor."
                    : "Optimizasyonlar başarıyla tamamlandı!",
                    "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                Log("Optimizasyon işlemi iptal edildi", "WARNING");
                MessageBox.Show("Optimizasyon işlemi iptal edildi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (StatusLabel != null)
                {
                    StatusLabel.Content = "Hata oluştu";
                    StatusLabel.Foreground = Brushes.Red;
                }
                Log($"Optimizasyon sırasında kritik hata: {ex.Message}", "ERROR");
                HandleError(ex, "Optimizasyon işlemi");
            }
            finally
            {
                _isRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                
                if (RunTweaksButton != null) RunTweaksButton.IsEnabled = true;
                if (StatusLabel != null)
                {
                    StatusLabel.Content = "Hazır";
                    StatusLabel.Foreground = Brushes.White;
                }
                CleanupTempFiles();
            }
        }

        private async Task RestoreMicrosoftEdge()
        {
            string tempLogPath = Path.Combine(Path.GetTempPath(), $"WinFastGUI_PS_{Guid.NewGuid()}.log");
            try
            {
                Log("Microsoft Edge geri yükleme işlemi başlatılıyor...");
                if (StatusLabel != null)
                {
                    StatusLabel.Content = "Edge geri yükleniyor...";
                    StatusLabel.Foreground = Brushes.Orange;
                }
                if (RunTweaksButton != null) RunTweaksButton.IsEnabled = false;

                string psCommand = $"& {{ & '{executorPath}' -OptimizationCommand 'Restore-MicrosoftEdge' }} *>&1 | Out-File -FilePath '{tempLogPath}' -Encoding utf8";
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psCommand));

                var psi = new ProcessStartInfo
                {
                    FileName = nsudoPath,
                    Arguments = $"-U:T -P:E powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand} -OutputFormat Text",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executorPath)
                };

                using (var process = new Process { StartInfo = psi })
                {
                    process.Start();
                    await process.WaitForExitAsync();

                    string capturedOutput = File.Exists(tempLogPath) ? await File.ReadAllTextAsync(tempLogPath) : "Log dosyası bulunamadı.";

                    if (process.ExitCode == 0)
                    {
                        Log("Microsoft Edge başarıyla geri yüklendi.", "SUCCESS");
                        MessageBox.Show("Microsoft Edge başarıyla geri yüklendi!", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        throw new Exception($"Edge geri yükleme başarısız oldu. Çıkış kodu: {process.ExitCode}. Detaylar: {capturedOutput}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Edge geri yükleme sırasında hata: {ex.Message}", "ERROR");
                HandleError(ex, "Edge geri yükleme");
            }
            finally
            {
                await CleanupTempFileAsync(tempLogPath);
                if (StatusLabel != null)
                {
                    StatusLabel.Content = "Hazır";
                    StatusLabel.Foreground = Brushes.White;
                }
                if (RunTweaksButton != null) RunTweaksButton.IsEnabled = true;
            }
        }

        private async void RunTests_Click(object sender, RoutedEventArgs e)
        {
            Log("RunTests_Click başlatıldı");
            if (StatusLabel != null)
            {
                StatusLabel.Content = "Testler çalışıyor...";
                StatusLabel.Foreground = Brushes.Orange;
            }
            if (RunTweaksButton != null) RunTweaksButton.IsEnabled = false;
            
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                await ExecutePowerShellOptimizationImproved("Test", cts.Token);
                MessageBox.Show("Testler başarıyla tamamlandı!", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                Log("Test işlemi zaman aşımına uğradı", "WARNING");
                MessageBox.Show("Test işlemi zaman aşımına uğradı.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log($"Testler sırasında hata: {ex.Message}", "ERROR");
                HandleError(ex, "Test işlemi");
            }
            finally
            {
                if (StatusLabel != null)
                {
                    StatusLabel.Content = "Hazır";
                    StatusLabel.Foreground = Brushes.White;
                }
                if (RunTweaksButton != null) RunTweaksButton.IsEnabled = true;
            }
        }

        private async void RestoreEdgeButton_Click(object sender, RoutedEventArgs e)
        {
            Log("RestoreEdgeButton_Click başlatıldı");
            await RestoreMicrosoftEdge();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
{
    var checkBoxes = OptimizationOptionsPanel?.Children.OfType<CheckBox>().Where(cb => cb.IsEnabled).ToList() ?? new List<CheckBox>();
    var selectedOptions = checkBoxes.Select(cb => loadedOptions.FirstOrDefault(o => o.Name == cb.Tag?.ToString())!).ToList();
    var validOptions = SortAndGroupOptimizations(selectedOptions);

    if (validOptions.Count > MaxSelections)
    {
        MessageBox.Show($"Toplam optimizasyon sayısı ({validOptions.Count}), izin verilen maksimum limiti ({MaxSelections}) aştığı için bu özellik kullanılamaz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    // Olay yöneticilerini kaldır
    foreach (var checkBox in checkBoxes)
    {
        checkBox.Checked -= CheckBox_Checked;
        checkBox.Unchecked -= CheckBox_Checked;
    }

    // CheckBox'ları işaretle
    foreach (var checkBox in checkBoxes)
    {
        if (validOptions.Any(o => o.Name == checkBox.Tag?.ToString()))
        {
            checkBox.IsChecked = true;
        }
    }

    // Olay yöneticilerini geri ekle
    foreach (var checkBox in checkBoxes)
    {
        checkBox.Checked += CheckBox_Checked;
        checkBox.Unchecked += CheckBox_Checked;
    }
}
        private void SelectSafeButton_Click(object sender, RoutedEventArgs e)
        {
            ClearSelectionButton_Click(sender, e);
            var safeOptions = OptimizationOptionsPanel?.Children.OfType<CheckBox>()
                .Where(cb =>
                {
                    var optName = cb.Tag?.ToString();
                    return cb.IsEnabled && (loadedOptions.FirstOrDefault(o => o.Name == optName)?.IsSafe ?? false);
                })
                .Select(cb => loadedOptions.First(o => o.Name == cb.Tag?.ToString()))
                .ToList() ?? new List<OptimizationOption>();

            var validOptions = SortAndGroupOptimizations(safeOptions);
            if (validOptions.Count > MaxSelections)
            {
                MessageBox.Show($"Güvenli optimizasyon sayısı ({validOptions.Count}), izin verilen maksimum limiti ({MaxSelections}) aştığı için bu özellik kullanılamaz. Sadece ilk {MaxSelections} tanesi seçilecek.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                validOptions.Take(MaxSelections).ToList().ForEach(opt =>
                {
                    var checkBox = OptimizationOptionsPanel?.Children.OfType<CheckBox>().FirstOrDefault(cb => cb.Tag?.ToString() == opt.Name);
                    if (checkBox != null) checkBox.IsChecked = true;
                });
                return;
            }

            foreach (var opt in validOptions)
            {
                var checkBox = OptimizationOptionsPanel?.Children.OfType<CheckBox>().FirstOrDefault(cb => cb.Tag?.ToString() == opt.Name);
                if (checkBox != null) checkBox.IsChecked = true;
            }
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
{
    if (OptimizationOptionsPanel != null)
    {
        foreach (CheckBox checkBox in OptimizationOptionsPanel.Children.OfType<CheckBox>())
        {
            // Olay yöneticilerini geçici olarak kaldır
            checkBox.Checked -= CheckBox_Checked;
            checkBox.Unchecked -= CheckBox_Checked;

            // Değeri programatik olarak güvenle değiştir
            checkBox.IsChecked = false;

            // Olay yöneticilerini tekrar ekle
            checkBox.Checked += CheckBox_Checked;
            checkBox.Unchecked += CheckBox_Checked;
        }
    }
}

        private void ImportNvidiaNibButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "NIB Files (*.nib)|*.nib|All Files (*.*)|*.*",
                    Title = "Nvidia NIB Dosyası Seç"
                };
                if (dialog.ShowDialog() == true)
                {
                    Log($"Nvidia NIB dosyası seçildi: {dialog.FileName}", "INFO");
                }
            }
            catch (Exception ex)
            {
                Log($"Nvidia NIB içe aktarılırken hata: {ex.Message}", "ERROR");
                HandleError(ex, "Nvidia NIB içe aktarma");
            }
        }

        private void OpenPowerProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try 
            { 
                Process.Start(new ProcessStartInfo("control", "powercfg.cpl") { UseShellExecute = true }); 
            }
            catch (Exception ex) 
            { 
                Log($"Güç profili açılamadı: {ex.Message}", "ERROR"); 
            }
        }

        private void OpenCoreAssignmentButton_Click(object sender, RoutedEventArgs e)
        {
            try 
            { 
                Process.Start(new ProcessStartInfo("msconfig") { UseShellExecute = true }); 
            }
            catch (Exception ex) 
            { 
                Log($"msconfig açılamadı: {ex.Message}", "ERROR"); 
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            RunTweaksButton_Click(sender, e);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            ClearSelectionButton_Click(sender, e);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _logFlushTimer?.Dispose();
                    _processLimitSemaphore?.Dispose();
                    _performanceManager.Value?.Dispose();
                    _cancellationTokenSource?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}