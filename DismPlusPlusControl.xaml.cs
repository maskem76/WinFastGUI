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
using System.Globalization;
using System.Threading;

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
            UpdateLanguage();
            StatusTextBlock.Text = Properties.Strings.StatusReady;
        }

        // Dil güncellemesi
        public void UpdateLanguage()
        {
            // UI elemanlarını güncelle (XAML'de binding var, bu yedek)
            SnapshotButton.Content = Properties.Strings.TakeSnapshot;
            DeleteSnapshotButton.Content = Properties.Strings.DeleteSnapshot;
            WimSaveButton.Content = Properties.Strings.CaptureWim;
            BrowseWimButton.Content = Properties.Strings.BrowseWim;
            BrowseUserImageButton.Content = Properties.Strings.BrowseUserImage;
            MakeIsoFromUserImageButton.Content = Properties.Strings.CreateIsoFromImage;
            RefreshUsbListButton.Content = Properties.Strings.RefreshUsbList;
            BrowseIsoButton.Content = Properties.Strings.BrowseIso;
            WriteIsoToUsbButton.Content = Properties.Strings.WriteIsoToUsb;
        }

        // Dil değiştirme
        public void ChangeLanguageTo(string languageCode)
        {
            var culture = new CultureInfo(languageCode);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            UpdateLanguage();
            StatusTextBlock.Text = Properties.Strings.StatusReady; // Dil değişiminde durumu güncelle
        }

        #region Buton Olayları

        private async void SnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiState(false, Properties.Strings.StatusCreatingSnapshot);
            try
            {
                await CleanupAsync(false);
                Log(Properties.Strings.VssSnapshotCreating); // VSS anlık görüntüsü oluşturuluyor...
                var (device, id) = await Task.Run(() => CreateSnapshot());
                _currentSnapshotDevice = device;
                _currentSnapshotId = id;
                SnapshotPathBox.Text = device;
                Log(string.Format(Properties.Strings.SnapshotTakenSuccessfully, _currentSnapshotDevice)); // Snapshot başarıyla alındı: {0}
                StatusTextBlock.Text = Properties.Strings.SnapshotReady;
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(Properties.Strings.SnapshotFailed, ex); // Snapshot alınamadı
            }
            finally
            {
                SetUiState(true);
            }
        }

        private async void DeleteSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiState(false, Properties.Strings.StatusDeletingSnapshots);
            await CleanupAsync(true);
            _currentSnapshotDevice = "";
            _currentSnapshotId = "";
            SnapshotPathBox.Text = "";
            StatusTextBlock.Text = Properties.Strings.AllSnapshotsDeleted;
            SetUiState(true);
        }

        private void BrowseWimButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = Properties.Strings.SelectWimSavePath, // Kaydedilecek WIM Dosyasını Seçin
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
                MessageBox.Show(Properties.Strings.PleaseTakeSnapshotFirst); return; // Lütfen önce bir snapshot alın.
            }
            if (string.IsNullOrWhiteSpace(WimPathTextBox.Text))
            {
                MessageBox.Show(Properties.Strings.PleaseSelectWimTarget); return; // Lütfen kaydedilecek .wim dosyası için bir hedef seçin.
            }

            SetUiState(false, Properties.Strings.StatusCreatingWim);
            string wimTargetPath = WimPathTextBox.Text;

            try
            {
                await EnsureExclusionFileExists();
                Log(Properties.Strings.WimBackupStarted); // ===== WIM YEDEKLEME İŞLEMİ BAŞLADI =====
                await Task.Run(() => MountSnapshot(_currentSnapshotDevice, _mountPath));
                await Task.Run(() => CaptureWim(wimTargetPath, _mountPath));
                Log(Properties.Strings.WimCaptureCompleted); // İmaj alma başarıyla tamamlandı!

                bool permSuccess = await FixFilePermissionsAsync(wimTargetPath);
                Log(permSuccess ? Properties.Strings.PermissionsSet : Properties.Strings.PermissionsWarning); // Dosya izinleri ayarlandı. / UYARI: İzinler ayarlanamadı.

                MessageBox.Show(Properties.Strings.WimAndPermissionsCompleted, Properties.Strings.Success); // İmaj alma ve izinleri düzeltme işlemi tamamlandı!
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(Properties.Strings.WimBackupError, ex); // WIM yedeği oluşturulurken hata oluştu
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
                Title = Properties.Strings.SelectUserImage, // Kullanılacak İmaj Dosyasını Seçin (.wim, .esd)
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
                MessageBox.Show(Properties.Strings.SelectValidWimError, Properties.Strings.Error); // Lütfen geçerli bir .wim dosyası seçin!
                return;
            }
            if (!(ImageTemplateComboBox?.SelectedItem is ComboBoxItem selectedItem) || string.IsNullOrWhiteSpace(selectedItem.Tag?.ToString()))
            {
                MessageBox.Show(Properties.Strings.SelectIsoTemplateError, Properties.Strings.Error); // Lütfen bir ISO şablonu seçin!
                return;
            }

            var saveIsoDialog = new SaveFileDialog
            {
                Title = Properties.Strings.SaveIsoDialogTitle, // Oluşacak ISO dosyasını kaydet
                Filter = "ISO Dosyası (*.iso)|*.iso",
                FileName = $"Custom_{Path.GetFileNameWithoutExtension(sourceWimPath)}.iso"
            };
            if (saveIsoDialog.ShowDialog() != true) return;
            string outputIsoPath = saveIsoDialog.FileName;

            SetUiState(false, Properties.Strings.StatusCreatingIso);
            try
            {
                string modulesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
                string templateZipPath = Path.Combine(modulesPath, selectedItem.Tag?.ToString() ?? "DefaultString");
                string scriptPath = Path.Combine(modulesPath, "ImageCreatorExecutor.ps1");

                string unattendPath = Path.Combine(Path.GetTempPath(), "unattend.xml");
                CreateUnattendXml(unattendPath);

                string arguments = $"-TemplateZipPath \"{templateZipPath}\" -SourceWimPath \"{sourceWimPath}\" -OutputIsoPath \"{outputIsoPath}\" -ModulesPath \"{modulesPath}\" -UnattendPath \"{unattendPath}\"";

                bool success = await RunPowerShellScriptAsync(scriptPath, arguments);

                if (success)
                {
                    _createdIsoPath = outputIsoPath;
                    bool permSuccess = await FixFilePermissionsAsync(outputIsoPath);
                    Log(permSuccess ? Properties.Strings.IsoPermissionsSet : Properties.Strings.IsoPermissionsWarning); // ISO dosyası izinleri ayarlandı. / UYARI: ISO dosyası izinleri ayarlanamadı.

                    WriteIsoToUsbButton.IsEnabled = true;
                    StatusTextBlock.Text = Properties.Strings.IsoReady;
                    MessageBox.Show(string.Format(Properties.Strings.IsoCreatedSuccessfully, outputIsoPath), Properties.Strings.Success); // ISO başarıyla oluşturuldu: {0}
                }
                else
                {
                    throw new Exception(Properties.Strings.IsoCreationFailed); // ISO oluşturma başarısız oldu.
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(Properties.Strings.IsoCreationError, ex); // ISO oluşturulurken hata
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
                Title = Properties.Strings.SelectIsoTitle, // ISO Dosyası Seç
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
                MessageBox.Show(Properties.Strings.CreateOrSelectIsoError, Properties.Strings.Error); // Lütfen önce bir ISO dosyası oluşturun veya seçin!
                return;
            }
            var selectedItemObj = UsbDriveComboBox?.SelectedItem;
            if (selectedItemObj == null || selectedItemObj is not UsbDiskInfo usbDriveInfo)
            {
                MessageBox.Show(Properties.Strings.SelectUsbError, Properties.Strings.Error); // Lütfen bir USB sürücü seçin!
                SetUiState(true);
                return;
            }
            if (string.IsNullOrEmpty(usbDriveInfo.DeviceID))
            {
                MessageBox.Show(Properties.Strings.InvalidDeviceIdError, Properties.Strings.Error); // Seçilen USB sürücünün DeviceID’si geçersiz!
                SetUiState(true);
                return;
            }
            if (string.IsNullOrEmpty(usbDriveInfo.DriveLetter))
            {
                MessageBox.Show(Properties.Strings.NoDriveLetterError, Properties.Strings.Error); // Seçilen USB sürücünün sürücü harfi bulunamadı!
                SetUiState(true);
                return;
            }

            var result = MessageBox.Show(
                Properties.Strings.UsbFormatWarning, // USB sürücü formatlanacak ve TÜM veriler silinecek!\nDevam etmek istiyor musunuz?
                Properties.Strings.Warning, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            SetUiState(false, Properties.Strings.StatusWritingIsoToUsb);
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
                            StatusTextBlock.Text = string.Format(Properties.Strings.CopyingProgress, Math.Round(progress)); // Kopyalanıyor: %{0}
                        });
                    },
                    message => Log(message));

                Log(Properties.Strings.IsoWrittenToUsb); // ISO USB'ye başarıyla yazıldı!
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(Properties.Strings.IsoWrittenMessage, Properties.Strings.Success); // ISO USB'ye yazıldı!
                    StatusTextBlock.Text = Properties.Strings.IsoWrittenToUsb;
                });
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(Properties.Strings.IsoToUsbError, ex); // ISO USB'ye yazılırken hata
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
                    Log(Properties.Strings.NoUsbDrivesFound); // Hiçbir USB sürücü bulunamadı veya sürücü harfi atanmamış.
            }
            catch (Exception ex)
            {
                _ = HandleErrorAsync(Properties.Strings.UsbListError, ex); // USB sürücüler listelenirken hata
            }
        }

        #endregion

        #region Sistem Fonksiyonları ve Yardımcılar

        private (string DeviceObject, string ShadowId) CreateSnapshot()
        {
            using var mngClass = new ManagementClass(@"\\.\root\cimv2", "Win32_ShadowCopy", null);
            using var inParams = mngClass.GetMethodParameters("Create");
            inParams["Volume"] = "C:\\"; // Kendi cihaz yolunuzu belirtin.
            inParams["Context"] = "ClientAccessible";
            using var outParams = mngClass.InvokeMethod("Create", inParams, null) ?? throw new Exception(Properties.Strings.SnapshotCreationFailedWmiNull); // Snapshot oluşturma başarısız!
            if ((uint?)outParams["ReturnValue"] != 0) throw new Exception(string.Format(Properties.Strings.SnapshotCreationFailedWmiCode, outParams["ReturnValue"])); // Snapshot oluşturma başarısız!
            string? shadowId = outParams["ShadowID"]?.ToString();
            if (string.IsNullOrEmpty(shadowId)) throw new Exception(Properties.Strings.NoValidSnapshotId); // Geçerli bir Snapshot ID alınamadı.
            using var searcher = new ManagementObjectSearcher($@"\\.\root\cimv2", $"SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID = '{shadowId}'");
            var obj = searcher.Get().OfType<ManagementObject>().FirstOrDefault() ?? throw new Exception(Properties.Strings.NoSnapshotDeviceObject); // Snapshot DeviceObject bulunamadı.
            string? deviceObject = obj["DeviceObject"]?.ToString();
            if (string.IsNullOrEmpty(deviceObject)) throw new Exception(Properties.Strings.NoDeviceObject); // DeviceObject bulunamadı.
            return (deviceObject!, shadowId!); // Nullable değerleri non-null olarak işaretle
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
            using var proc = Process.Start(psi) ?? throw new Exception(Properties.Strings.MountProcessFailed); // Mount işlemi için process başlatılamadı.
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0) throw new Exception(string.Format(Properties.Strings.MountFailedError, error)); // Mount işlemi başarısız! Hata: {0}
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
            using var proc = Process.Start(psi) ?? throw new Exception(Properties.Strings.DismProcessFailed); // DISM işlemi başlatılamadı.
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) { ParseProgress(e.Data); Log(e.Data); } };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Log(string.Format(Properties.Strings.ErrorPrefix, e.Data)); }; // HATA: {0}
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            if (proc.ExitCode != 0) throw new Exception(string.Format(Properties.Strings.DismFailedCode, proc.ExitCode)); // DISM işlemi başarısız oldu! Çıkış Kodu: {0}.
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
                using var process = Process.Start(psi) ?? throw new Exception(Properties.Strings.PowerShellProcessFailed); // PowerShell işlemi başlatılamadı.
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
                        Log(string.Format(Properties.Strings.ErrorPrefix, args.Data)); // HATA: {0}
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
                using (var proc = Process.Start(psiTakeOwn) ?? throw new Exception(Properties.Strings.TakeownProcessFailed)) // takeown işlemi başlatılamadı.
                {
                    await proc.WaitForExitAsync();
                }

                var psiIcacls = new ProcessStartInfo("cmd.exe", $"/c icacls \"{filePath}\" /grant \"{currentUser}:F\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var proc = Process.Start(psiIcacls) ?? throw new Exception(Properties.Strings.IcaclsProcessFailed)) // icacls işlemi başlatılamadı.
                {
                    await proc.WaitForExitAsync();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log(string.Format(Properties.Strings.FixPermissionsError, ex)); // [FixFilePermissionsAsync HATA] {0}
                return false;
            }
        }

        private async Task CleanupAsync(bool deleteAllSnapshots = true)
        {
            Log(Properties.Strings.CleanupStarted); // Temizlik işlemi başlatılıyor...
            try
            {
                await Task.Run(() => UnmountSnapshot(_mountPath));
                Log(Properties.Strings.TempMountRemoved); // Geçici klasör bağlantısı kaldırıldı.
            }
            catch (Exception ex)
            {
                Log(string.Format(Properties.Strings.UnmountError, ex.Message)); // Unmount hatası: {0}
            }

            if (deleteAllSnapshots)
            {
                try
                {
                    bool deleted = await DeleteSnapshotsWithVssadminAsync();
                    if (deleted) Log(Properties.Strings.AllSnapshotsDeleted); // Tüm VSS anlık görüntüleri silindi.
                }
                catch (Exception ex)
                {
                    Log(string.Format(Properties.Strings.VssDeleteError, ex.Message)); // VSS silme hatası: {0}
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
            Log(Properties.Strings.WimscriptWarning); // UYARI: wimscript.ini bulunamadı. Varsayılan liste oluşturuluyor...
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
                Log(string.Format(Properties.Strings.WimscriptCreationError, ex.Message)); // HATA: wimscript.ini oluşturulamadı: {0}
            }
        }

        private void SetUiState(bool isEnabled, string? statusMessage = null)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.IsIndeterminate = !isEnabled;
                if (isEnabled) ProgressBar.Value = 0;
                StatusTextBlock.Text = statusMessage ?? Properties.Strings.StatusReady;
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
                message = string.Format(Properties.Strings.ProgressFormat, progressMatch.Groups[1].Value); // İŞLEM: {0}% complete
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
                string displayStatusText = string.Format(Properties.Strings.ProgressDisplay, Math.Round(val)); // İŞLEM: %{0}
                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Value = val;
                    StatusTextBlock.Text = displayStatusText;
                });
            }
        }

        private async Task HandleErrorAsync(string message, Exception ex)
        {
            Log(string.Format(Properties.Strings.ErrorMessage, ex.InnerException?.Message ?? ex.Message)); // {0}: {1}
            await Dispatcher.InvokeAsync(() =>
            {
                StatusTextBlock.Text = Properties.Strings.StatusError; // Durum: Hata!
                MessageBox.Show(string.Format(Properties.Strings.CriticalErrorMessage, message, ex.InnerException?.Message ?? ex.Message), Properties.Strings.CriticalError, MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void CreateUnattendXml(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty.");

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
                                    new XElement("Name", username ?? "UnknownUser"),
                                    new XElement("Group", "Administrators")
                                )
                            )
                        ),
                        new XElement("ComputerName", computerName ?? "UnknownComputer"),
                        new XElement("TimeZone", timezone ?? "UnknownTimeZone"),
                        new XElement("RegisteredOwner", username ?? "UnknownUser"),
                        new XElement("InputLocale", locale ?? "en-US"),
                        new XElement("SystemLocale", locale ?? "en-US"),
                        new XElement("UILanguage", lang ?? "en-US"),
                        new XElement("UserLocale", locale ?? "en-US")
                    )
                )
            );
            unattend.Save(filePath); // Nullable değil, bu satır artık uyarı vermemeli
        }

        #endregion
    }
}
