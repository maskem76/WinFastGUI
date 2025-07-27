using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinFastGUI.Controls
{
    public partial class UpdateManagerControl : UserControl
    {
        public UpdateManagerControl()
        {
            InitializeComponent();
            Loaded += UpdateManagerControl_Loaded;
        }

        // Uygulama başlatıldığında güncellemeleri yükleyelim
        private async void UpdateManagerControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadUpdatesAsync();
            UpdateLanguage(); // Başlangıçta dili güncelle
        }

        // Dil güncellemesi (MainWindow'dan çağrılacak)
        public void UpdateLanguage()
        {
            string currentCulture = System.Threading.Thread.CurrentThread.CurrentUICulture.Name;
            RefreshButton.Content = WinFastGUI.Properties.Strings.Refresh;
            InstallSelectedButton.Content = WinFastGUI.Properties.Strings.InstallSelectedUpdates;

            // Başlık ve diğer statik metinler
            var titleTextBlock = this.FindName("titleTextBlock") as TextBlock;
            if (titleTextBlock != null)
                titleTextBlock.Text = WinFastGUI.Properties.Strings.PendingWindowsUpdates;
        }

        // Güncellemeleri çekmek için PowerShell scripti
        private async Task LoadUpdatesAsync()
        {
            InfoTextBlock.Text = "";
            UpdateListBox.Items.Clear();
            SetButtonsEnabled(false);
            ProgressBar1.Value = 0;
            LogInfo("[WinFast] " + WinFastGUI.Properties.Strings.ScanningUpdates, Brushes.LightGreen);

            // PSWindowsUpdate modülünün kurulu olduğundan emin ol!
            string psScript = @"
                $Session = New-Object -ComObject Microsoft.Update.Session
                $Searcher = $Session.CreateUpdateSearcher()
                $Results = $Searcher.Search('IsInstalled=0')
                $Results.Updates | ForEach-Object {
                    $kb = $_.KBArticleIDs -join ', '
                    ""$kb - $($_.Title)""
                }
            ";

            var (stdout, stderr) = await RunPowerShellScriptAsync(psScript);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                LogInfo("[WinFast] " + WinFastGUI.Properties.Strings.PowerShellError + $"\n{stderr}", Brushes.Red);
                return;
            }

            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                LogInfo(WinFastGUI.Properties.Strings.NoUpdatesFound, Brushes.Orange);
                return;
            }

            foreach (var line in lines)
                UpdateListBox.Items.Add(line);

            InfoTextBlock.Text = $"{lines.Length} {WinFastGUI.Properties.Strings.UpdatesFound}. {WinFastGUI.Properties.Strings.SelectAndInstall}";
            InfoTextBlock.Foreground = Brushes.Red;
            SetButtonsEnabled(true);
        }

        // "Yenile" butonuna tıklandığında güncellemeleri yeniden çekiyoruz
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadUpdatesAsync();
        }

        // Seçilen güncellemeleri yüklemek için
        private async void InstallSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = UpdateListBox.SelectedItems.Cast<string>().ToList();
            if (!selected.Any())
            {
                InfoTextBlock.Text = WinFastGUI.Properties.Strings.PleaseSelectAtLeastOneUpdate;
                InfoTextBlock.Foreground = Brushes.OrangeRed;
                return;
            }

            SetButtonsEnabled(false);
            ProgressBar1.Value = 0;
            InfoTextBlock.Text = WinFastGUI.Properties.Strings.InstallingSelectedUpdates;

            int idx = 0;
            foreach (var item in selected)
            {
                string kb = item.Split('-')[0].Trim().Replace("KB", "");
                string psScript = $@"
                    try {{
                        Import-Module PSWindowsUpdate -Force;
                        Install-WindowsUpdate -KBArticleID {kb} -AcceptAll -ForceDownload -ForceInstall -ErrorAction Stop
                    }} catch {{
                        Write-Error $_.Exception.Message
                    }}
                ";
                var (stdout, stderr) = await RunPowerShellScriptAsync(psScript);

                if (!string.IsNullOrWhiteSpace(stderr))
                    LogInfo($"[WinFast] {item} {WinFastGUI.Properties.Strings.ErrorOccurred}:\n{stderr}", Brushes.Red);
                else
                    LogInfo($"[WinFast] {item} {WinFastGUI.Properties.Strings.SuccessfullyInstalled}", Brushes.LimeGreen);

                // Yükleme ilerlemesini gösterme
                ProgressBar1.Value = ((double)(idx + 1) / selected.Count) * 100;
                idx++;
            }

            InfoTextBlock.Text = WinFastGUI.Properties.Strings.InstallationCompleted;
            SetButtonsEnabled(true);
        }

        // Butonları etkinleştirme / devre dışı bırakma
        private void SetButtonsEnabled(bool enabled)
        {
            InstallSelectedButton.IsEnabled = enabled;
            RefreshButton.IsEnabled = enabled;
            UpdateListBox.IsEnabled = enabled;
        }

        // Loglama işlemi
        private void LogInfo(string text, Brush? color = null)
        {
            InfoTextBlock.Text = text;
            InfoTextBlock.Foreground = color ?? Brushes.White;
        }

        // PowerShell komutlarını çalıştırma
        private async Task<(string stdout, string stderr)> RunPowerShellScriptAsync(string script)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        return ("", "PowerShell başlatılamadı.");
                    string stdout = await process.StandardOutput.ReadToEndAsync();
                    string stderr = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return (stdout, stderr);
                }
            }
            catch (Exception ex)
            {
                return ("", ex.Message);
            }
        }
    }
}