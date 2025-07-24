using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace WinFastGUI.Controls
{
    public partial class DismPlusPlusControl : UserControl
    {
        private string _currentSnapshotDevice = "";
        private string _currentSnapshotId = "";
        private readonly string _mountPath = Path.Combine(Path.GetTempPath(), "WinFastMount");
        private string _createdIsoPath = "";

        public DismPlusPlusControl()
        {
            InitializeComponent();
            this.Loaded += (s, e) => OnControlLoadedInternal();
        }

        private void OnControlLoadedInternal()
        {
            StatusTextBlock.Text = "Durum: Beklemede";
        }

        #region Buton Olayları

        private async void SnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiState(false, "Snapshot alınıyor...");
            try
            {
                await CleanupAsync(false);
                Log("VSS anlık görüntüsü oluşturuluyor...");
                var (device, id) = await Task.Run(() => CreateSnapshot());
                _currentSnapshotDevice = device;
                _currentSnapshotId = id;
                SnapshotPathBox.Text = device;
                Log($"Snapshot başarıyla alındı: {_currentSnapshotDevice}");
                StatusTextBlock.Text = "Snapshot hazır!";
            }
            catch (Exception ex)
            {
                await HandleErrorAsync("Snapshot alınamadı", ex);
            }
            finally
            {
                SetUiState(true);
            }
        }

        private async void DeleteSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiState(false, "Snapshot'lar siliniyor...");
            await CleanupAsync(true);
            _currentSnapshotDevice = "";
            _currentSnapshotId = "";
            SnapshotPathBox.Text = "";
            StatusTextBlock.Text = "Tüm Snapshot'lar silindi.";
            SetUiState(true);
        }

        private void BrowseWimButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Kaydedilecek WIM Dosyasını Seçin",
                Filter = "Windows Image File (*.wim)|*.wim",
                FileName = $"WinFastBackup_{DateTime.Now:yyyyMMdd_HHmmss}.wim"
            };
            if (dlg.ShowDialog() == true)
                WimPathTextBox.Text = dlg.FileName;
        }

        private async void WimSaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentSnapshotDevice))
            {
                MessageBox.Show("Lütfen önce bir snapshot alın."); return;
            }
            if (string.IsNullOrWhiteSpace(WimPathTextBox.Text))
            {
                MessageBox.Show("Lütfen kaydedilecek .wim dosyası için bir hedef seçin."); return;
            }

            SetUiState(false, "WIM İmajı Oluşturuluyor...");
            string wimTargetPath = WimPathTextBox.Text;

            try
            {
                await EnsureExclusionFileExists();
                Log("===== WIM YEDEKLEME İŞLEMİ BAŞLADI =====");
                await Task.Run(() => MountSnapshot(_currentSnapshotDevice, _mountPath));
                await Task.Run(() => CaptureWim(wimTargetPath, _mountPath));
                Log("İmaj alma başarıyla tamamlandı!");

                bool permSuccess = await FixFilePermissionsAsync(wimTargetPath);
                Log(permSuccess ? "Dosya izinleri ayarlandı." : "UYARI: İzinler ayarlanamadı.");

                MessageBox.Show("İmaj alma ve izinleri düzeltme işlemi tamamlandı!", "Başarılı");
            }
            catch (Exception ex)
            {
                await HandleErrorAsync("WIM yedeği oluşturulurken hata oluştu", ex);
            }
            finally
            {
                await CleanupAsync(false);
                SetUiState(true);
            }
        }

        private void BrowseUserImageButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Kullanılacak İmaj Dosyasını Seçin (.wim, .esd)",
                Filter = "Image Files|*.wim;*.esd"
            };
            if (dlg.ShowDialog() == true)
                UserImagePathTextBox.Text = dlg.FileName;
        }

        private async void MakeIsoFromUserImageButton_Click(object sender, RoutedEventArgs e)
        {
            string? sourceWimPath = UserImagePathTextBox?.Text;
            if (string.IsNullOrWhiteSpace(sourceWimPath) || !File.Exists(sourceWimPath))
            {
                MessageBox.Show("Lütfen geçerli bir .wim dosyası seçin!", "Hata");
                return;
            }
            if (!(ImageTemplateComboBox?.SelectedItem is ComboBoxItem selectedItem) || string.IsNullOrWhiteSpace(selectedItem.Tag?.ToString()))
            {
                MessageBox.Show("Lütfen bir ISO şablonu seçin!", "Hata");
                return;
            }

            var saveIsoDialog = new SaveFileDialog
            {
                Title = "Oluşacak ISO dosyasını kaydet",
                Filter = "ISO Dosyası (*.iso)|*.iso",
                FileName = $"Custom_{Path.GetFileNameWithoutExtension(sourceWimPath)}.iso"
            };
            if (saveIsoDialog.ShowDialog() != true) return;
            string outputIsoPath = saveIsoDialog.FileName;

            SetUiState(false, "ISO oluşturuluyor...");
            try
            {
                string modulesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
                string templateZipPath = Path.Combine(modulesPath, selectedItem.Tag?.ToString() ?? throw new Exception("Şablon dosyası belirtilmedi."));
                string scriptPath = Path.Combine(modulesPath, "ImageCreatorExecutor.ps1");

                string unattendPath = Path.Combine(Path.GetTempPath(), "unattend.xml");
                CreateUnattendXml(unattendPath);

                string arguments = $"-TemplateZipPath \"{templateZipPath}\" -SourceWimPath \"{sourceWimPath}\" -OutputIsoPath \"{outputIsoPath}\" -ModulesPath \"{modulesPath}\" -UnattendPath \"{unattendPath}\"";

                bool success = await RunPowerShellScriptAsync(scriptPath, arguments);

                if (success)
                {
                    _createdIsoPath = outputIsoPath;
                    bool permSuccess = await FixFilePermissionsAsync(outputIsoPath);
                    Log(permSuccess ? "ISO dosyası izinleri ayarlandı." : "UYARI: ISO dosyası izinleri ayarlanamadı.");

                    WriteIsoToUsbButton.IsEnabled = true;
                    StatusTextBlock.Text = "ISO hazır!";
                    MessageBox.Show($"ISO başarıyla oluşturuldu: {outputIsoPath}", "Başarılı");
                }
                else
                {
                    throw new Exception("ISO oluşturma başarısız oldu.");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync("ISO oluşturulurken hata", ex);
            }
            finally
            {
                SetUiState(true);
            }
        }

        private void BrowseIsoButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "ISO Dosyası Seç",
                Filter = "ISO Dosyası (*.iso)|*.iso"
            };
            if (dlg.ShowDialog() == true)
            {
                IsoPathTextBox.Text = dlg.FileName;
                _createdIsoPath = dlg.FileName;
                WriteIsoToUsbButton.IsEnabled = true;
            }
        }

        private async void WriteIsoToUsbButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_createdIsoPath) || !File.Exists(_createdIsoPath))
            {
                MessageBox.Show("Lütfen önce bir ISO dosyası oluşturun veya seçin!", "Hata");
                return;
            }
            var selectedItemObj = UsbDriveComboBox?.SelectedItem;
            if (selectedItemObj == null || selectedItemObj is not UsbDiskInfo usbDriveInfo)
            {
                MessageBox.Show("Lütfen bir USB sürücü seçin!", "Hata");
                SetUiState(true);
                return;
            }
            if (string.IsNullOrEmpty(usbDriveInfo.DeviceID))
            {
                MessageBox.Show("Seçilen USB sürücünün DeviceID’si geçersiz!", "Hata");
                SetUiState(true);
                return;
            }
            if (string.IsNullOrEmpty(usbDriveInfo.DriveLetter))
            {
                MessageBox.Show("Seçilen USB sürücünün sürücü harfi bulunamadı!", "Hata");
                SetUiState(true);
                return;
            }

            var result = MessageBox.Show(
                "USB sürücü formatlanacak ve TÜM veriler silinecek!\nDevam etmek istiyor musunuz?",
                "Uyarı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            SetUiState(false, "ISO USB'ye yazılıyor...");
            try
            {
                string usbDriveLetter = usbDriveInfo.DriveLetter;
                await UsbRawWriter.WriteIsoToUsbAsync(usbDriveInfo.DeviceID, _createdIsoPath, usbDriveLetter,
                    (written, total) =>
                    {
                        double progress = (double)written / total * 100.0;
                        Dispatcher.Invoke(() =>
                        {
                            ProgressBar.Value = progress;
                            StatusTextBlock.Text = $"Kopyalanıyor: %{Math.Round(progress)}";
                        });
                    },
                    message => Log(message));

                Log("ISO USB'ye başarıyla yazıldı!");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("ISO USB'ye yazıldı!", "Başarılı");
                    StatusTextBlock.Text = "ISO USB'ye yazıldı!";
                });
            }
            catch (Exception ex)
            {
                await HandleErrorAsync("ISO USB'ye yazılırken hata", ex);
            }
            finally
            {
                await Dispatcher.InvokeAsync(() => SetUiState(true));
            }
        }

        private void RefreshUsbListButton_Click(object sender, RoutedEventArgs e)
        {
            UsbDriveComboBox.Items.Clear();
            try
            {
                var usbDrives = UsbDiskInfo.ListUsbDrives();
                foreach (var drive in usbDrives)
                {
                    if (!string.IsNullOrEmpty(drive.DeviceID) && !string.IsNullOrEmpty(drive.DriveLetter))
                    {
                        UsbDriveComboBox.Items.Add(drive);
                    }
                }
                if (UsbDriveComboBox.Items.Count > 0)
                    UsbDriveComboBox.SelectedIndex = 0;
                else
                    Log("Hiçbir USB sürücü bulunamadı veya sürücü harfi atanmamış.");
            }
            catch (Exception ex)
            {
                _ = HandleErrorAsync("USB sürücüler listelenirken hata", ex);
            }
        }

        #endregion

        #region Sistem Fonksiyonları ve Yardımcılar

        private (string DeviceObject, string ShadowId) CreateSnapshot()
        {
            using var mngClass = new ManagementClass(@"\\.\root\cimv2", "Win32_ShadowCopy", null);
            using var inParams = mngClass.GetMethodParameters("Create");
            inParams["Volume"] = "C:\\";
            inParams["Context"] = "ClientAccessible";
            using var outParams = mngClass.InvokeMethod("Create", inParams, null) ?? throw new Exception("Snapshot oluşturma başarısız! WMI null döndü.");
            if ((uint?)outParams["ReturnValue"] != 0) throw new Exception($"Snapshot oluşturma başarısız! WMI Kodu: {outParams["ReturnValue"]}");
            string? shadowId = outParams["ShadowID"]?.ToString();
            if (string.IsNullOrEmpty(shadowId)) throw new Exception("Geçerli bir Snapshot ID alınamadı.");
            using var searcher = new ManagementObjectSearcher($@"\\.\root\cimv2", $"SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID = '{shadowId}'");
            var obj = searcher.Get().OfType<ManagementObject>().FirstOrDefault() ?? throw new Exception("Snapshot DeviceObject bulunamadı.");
            string? deviceObject = obj["DeviceObject"]?.ToString();
            if (string.IsNullOrEmpty(deviceObject)) throw new Exception("DeviceObject bulunamadı.");
            return (deviceObject, shadowId);
        }

        private void MountSnapshot(string devicePath, string mountPath)
        {
            if (Directory.Exists(mountPath)) UnmountSnapshot(mountPath);
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /d \"{mountPath}\" \"{devicePath.TrimEnd('\\')}\\\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                Verb = "runas"
            };
            using var proc = Process.Start(psi) ?? throw new Exception("Mount işlemi için process başlatılamadı.");
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0) throw new Exception($"Mount işlemi başarısız! Hata: {error}");
        }

        private void UnmountSnapshot(string mountPath)
        {
            if (!Directory.Exists(mountPath)) return;
            var psi = new ProcessStartInfo("cmd.exe", $"/c rmdir \"{mountPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }

        private void CaptureWim(string wimPath, string captureDir)
        {
            string exclusionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wimscript.ini");
            string args = $"/Capture-Image /ImageFile:\"{wimPath}\" /CaptureDir:\"{captureDir.TrimEnd('\\')}\" /Name:\"WinFastYedek\" /Compress:Max /CheckIntegrity";
            if (File.Exists(exclusionFile)) args += $" /ConfigFile:\"{exclusionFile}\"";
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "dism.exe"),
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Verb = "runas"
            };
            using var proc = Process.Start(psi) ?? throw new Exception("DISM işlemi başlatılamadı.");
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) { ParseProgress(e.Data); Log(e.Data); } };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Log($"HATA: {e.Data}"); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            if (proc.ExitCode != 0) throw new Exception($"DISM işlemi başarısız oldu! Çıkış Kodu: {proc.ExitCode}.");
        }

        private async Task<bool> RunPowerShellScriptAsync(string scriptPath, string arguments)
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    Verb = "runas"
                };
                using var process = Process.Start(psi) ?? throw new Exception("PowerShell işlemi başlatılamadı.");
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        ParseProgress(args.Data);
                        Log(args.Data);
                    }
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null && !args.Data.Contains("% complete"))
                        Log($"HATA: {args.Data}");
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                return process.ExitCode == 0;
            });
        }

        private async Task<bool> FixFilePermissionsAsync(string filePath)
        {
            try
            {
                string currentUser = WindowsIdentity.GetCurrent().Name;
                var psiTakeOwn = new ProcessStartInfo("cmd.exe", $"/c takeown /F \"{filePath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var proc = Process.Start(psiTakeOwn) ?? throw new Exception("takeown işlemi başlatılamadı."))
                {
                    await proc.WaitForExitAsync();
                }

                var psiIcacls = new ProcessStartInfo("cmd.exe", $"/c icacls \"{filePath}\" /grant \"{currentUser}:F\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var proc = Process.Start(psiIcacls) ?? throw new Exception("icacls işlemi başlatılamadı."))
                {
                    await proc.WaitForExitAsync();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"[FixFilePermissionsAsync HATA] {ex}");
                return false;
            }
        }

        private async Task CleanupAsync(bool deleteAllSnapshots = true)
        {
            Log("Temizlik işlemi başlatılıyor...");
            try
            {
                await Task.Run(() => UnmountSnapshot(_mountPath));
                Log("Geçici klasör bağlantısı kaldırıldı.");
            }
            catch (Exception ex)
            {
                Log($"Unmount hatası: {ex.Message}");
            }

            if (deleteAllSnapshots)
            {
                try
                {
                    bool deleted = await DeleteSnapshotsWithVssadminAsync();
                    if (deleted) Log("Tüm VSS anlık görüntüleri silindi.");
                }
                catch (Exception ex)
                {
                    Log($"VSS silme hatası: {ex.Message}");
                }
            }
        }

        private async Task<bool> DeleteSnapshotsWithVssadminAsync()
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "vssadmin.exe"),
                Arguments = "delete shadows /for=C: /all /quiet",
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }

        private async Task EnsureExclusionFileExists()
        {
            string exclusionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wimscript.ini");
            if (File.Exists(exclusionFilePath)) return;
            Log("UYARI: wimscript.ini bulunamadı. Varsayılan liste oluşturuluyor...");
            string defaultExclusions = @"[ExclusionList]
\pagefile.sys
\hiberfil.sys
\swapfile.sys
\$Recycle.Bin\*
\System Volume Information\*
\Windows\CSC\*
\Windows\Temp\*
\Temp\*
\Users\pc\AppData\Local\Temp\*
\Users\pc\AppData\Local\Microsoft\Windows\WER\*
\Users\pc\AppData\Local\Microsoft\Windows\INetCache\*
\Users\pc\AppData\Local\Microsoft\Windows\WebCache\*
\Users\pc\Downloads\*
\Users\pc\Videos\*
\Users\pc\Music\*
\Users\pc\Pictures\*
\Users\pc\OneDrive\*
\Users\pc\Google Drive\*
\Users\pc\Dropbox\*
\Windows\SoftwareDistribution\Download\*
\Windows\Logs\*
\Windows\Panther\*
\$WINDOWS.~BT\*
\$WINDOWS.~WS\*
";
            try
            {
                await File.WriteAllTextAsync(exclusionFilePath, defaultExclusions);
            }
            catch (Exception ex)
            {
                Log($"HATA: wimscript.ini oluşturulamadı: {ex.Message}");
            }
        }

        private void SetUiState(bool isEnabled, string statusMessage = "Durum: Beklemede")
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.IsIndeterminate = !isEnabled;
                if (isEnabled) ProgressBar.Value = 0;
                StatusTextBlock.Text = statusMessage;
                SnapshotButton.IsEnabled = isEnabled;
                DeleteSnapshotButton.IsEnabled = isEnabled;
                BrowseWimButton.IsEnabled = isEnabled;
                WimSaveButton.IsEnabled = isEnabled;
                MakeIsoFromUserImageButton.IsEnabled = isEnabled;
                BrowseUserImageButton.IsEnabled = isEnabled;
                RefreshUsbListButton.IsEnabled = isEnabled;
                ImageTemplateComboBox.IsEnabled = isEnabled;
                UsbDriveComboBox.IsEnabled = isEnabled;
                WriteIsoToUsbButton.IsEnabled = isEnabled && !string.IsNullOrEmpty(_createdIsoPath);
                WimPathTextBox.IsEnabled = isEnabled;
                UserImagePathTextBox.IsEnabled = isEnabled;
                SnapshotPathBox.IsEnabled = isEnabled;
            });
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (message.Trim() == "HATA:") return;
            var progressMatch = Regex.Match(message, @"^HATA:\s*(\d{1,3})\% complete");
            if (progressMatch.Success)
            {
                message = $"İŞLEM: {progressMatch.Groups[1].Value}% complete";
            }

            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                LogBox.ScrollToEnd();
            });
        }

        private void ParseProgress(string line)
        {
            var match = Regex.Match(line, @"(?:HATA:\s*)?\[?\s*(\d{1,3}(?:[.,]\d+)?)\%\s*\]?");
            if (match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                string displayStatusText = $"İŞLEM: %{Math.Round(val)}";
                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Value = val;
                    StatusTextBlock.Text = displayStatusText;
                });
            }
        }

        private async Task HandleErrorAsync(string message, Exception ex)
        {
            Log($"{message}: {ex.InnerException?.Message ?? ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                StatusTextBlock.Text = "Durum: Hata!";
                MessageBox.Show($"{message}:\n\n{ex.InnerException?.Message ?? ex.Message}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void CreateUnattendXml(string filePath)
        {
            var username = Environment.UserName;
            var computerName = Environment.MachineName;
            var lang = System.Globalization.CultureInfo.InstalledUICulture.Name;
            var locale = System.Globalization.CultureInfo.CurrentCulture.Name;
            var timezone = TimeZoneInfo.Local.StandardName;

            var unattend = new XElement("unattend",
                new XElement("settings",
                    new XElement("component",
                        new XAttribute("name", "Microsoft-Windows-Shell-Setup"),
                        new XElement("UserAccounts",
                            new XElement("LocalAccounts",
                                new XElement("LocalAccount",
                                    new XElement("Name", username),
                                    new XElement("Group", "Administrators")
                                )
                            )
                        ),
                        new XElement("ComputerName", computerName),
                        new XElement("TimeZone", timezone),
                        new XElement("RegisteredOwner", username),
                        new XElement("InputLocale", locale),
                        new XElement("SystemLocale", locale),
                        new XElement("UILanguage", lang),
                        new XElement("UserLocale", locale)
                    )
                )
            );
            unattend.Save(filePath);
        }

        #endregion
    }
}
