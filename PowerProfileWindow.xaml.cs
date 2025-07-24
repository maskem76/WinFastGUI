using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace WinFastGUI.Controls
{
    public partial class PowerProfileWindow : Window
    {
        public PowerProfileWindow()
        {
            InitializeComponent();
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            string profileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerProfiles");
            if (!Directory.Exists(profileDir))
            {
                PowerProfilesComboBox.Items.Add("Klasör bulunamadı: PowerProfiles");
                PowerProfilesComboBox.IsEnabled = false;
                return;
            }
            var files = Directory.GetFiles(profileDir, "*.pow");
            foreach (var file in files)
                PowerProfilesComboBox.Items.Add(Path.GetFileName(file));

            if (PowerProfilesComboBox.Items.Count > 0)
                PowerProfilesComboBox.SelectedIndex = 0;
            else
                PowerProfilesComboBox.Items.Add("Hiçbir .pow profili yok!");
        }

        private void ApplyPowerProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (PowerProfilesComboBox.SelectedItem == null ||
                !PowerProfilesComboBox.SelectedItem.ToString()!.EndsWith(".pow"))
            {
                PowerResultTextBox.Text = "Lütfen yüklemek için bir güç profili seçin.";
                return;
            }

            try
            {
                string selectedFile = PowerProfilesComboBox.SelectedItem.ToString()!;
                string profileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerProfiles");
                string powPath = Path.Combine(profileDir, selectedFile);

                // Import profili
                var psi = new ProcessStartInfo("powercfg.exe", $"/import \"{powPath}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        string error = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                        if (proc.ExitCode != 0)
                        {
                            PowerResultTextBox.Text = "[HATA] Import: " + error;
                            return;
                        }
                    }
                    else
                    {
                        PowerResultTextBox.Text = "[HATA] İşlem başlatılamadı.";
                        return;
                    }
                }

                // GUID bul ve aktif et
                string guid = "";
                var guidPsi = new ProcessStartInfo("powercfg.exe", "/list")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(guidPsi))
                {
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        foreach (var line in output.Split('\n'))
                        {
                            if (line.Contains(selectedFile, StringComparison.OrdinalIgnoreCase))
                            {
                                int g1 = line.IndexOf(":");
                                if (g1 > 0)
                                    guid = line.Substring(g1 + 1).Trim().Split(' ')[0];
                            }
                        }
                    }
                    else
                    {
                        PowerResultTextBox.Text = "[HATA] GUID listeleme işlemi başlatılamadı.";
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(guid))
                {
                    PowerResultTextBox.Text = "Profil GUID bulunamadı, lütfen elle etkinleştirin.";
                    return;
                }

                var setPsi = new ProcessStartInfo("powercfg.exe", $"/setactive {guid}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(setPsi))
                {
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        string error = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                        if (proc.ExitCode == 0)
                            PowerResultTextBox.Text = "[BAŞARILI] Profil uygulandı.";
                        else
                            PowerResultTextBox.Text = "[HATA] Profil etkinleştirme: " + error;
                    }
                    else
                    {
                        PowerResultTextBox.Text = "[HATA] Profil etkinleştirme işlemi başlatılamadı.";
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                PowerResultTextBox.Text = "[HATA] " + ex.Message;
            }
        }
    }
}