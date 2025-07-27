using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Windows;

namespace WinFastGUI.Controls
{
    public partial class CoreAssignmentWindow : Window
    {
        public CoreAssignmentWindow()
        {
            InitializeComponent();
            LoadDevices();
        }

        private void LoadDevices()
        {
            // Basitçe en yaygın donanımları getir: Ekran kartı, ağ kartı, ses kartı
            var devices = new List<string>();

            try
            {
                // Ekran Kartları
                using (var searcher = new ManagementObjectSearcher("select * from Win32_VideoController"))
                {
                    foreach (var obj in searcher.Get())
                        devices.Add("GPU: " + (obj["Name"]?.ToString() ?? "Bilinmiyor"));
                }
                // Ses Kartları
                using (var searcher = new ManagementObjectSearcher("select * from Win32_SoundDevice"))
                {
                    foreach (var obj in searcher.Get())
                        devices.Add("Ses: " + (obj["Name"]?.ToString() ?? "Bilinmiyor"));
                }
                // Ağ Kartları
                using (var searcher = new ManagementObjectSearcher("select * from Win32_NetworkAdapter where PhysicalAdapter = true"))
                {
                    foreach (var obj in searcher.Get())
                        devices.Add("Ağ: " + (obj["Name"]?.ToString() ?? "Bilinmiyor"));
                }
            }
            catch { /* ignore */ }

            if (devices.Count == 0)
                devices.Add("Sanal Cihaz Bulunamadı");

            DeviceComboBox.ItemsSource = devices;
            DeviceComboBox.SelectedIndex = 0;
        }

        private void AssignCoresButton_Click(object sender, RoutedEventArgs e)
        {
            // Not: Gerçek donanım başına core affinity Windows'ta yalnızca process-level yapılabilir!
            // Burada örnek için Not Defteri çalıştırıp core affinity uygulayacağız.
            // Gerçek donanım işlemlerinde doğrudan çekirdek atama sadece belirli sürücülerle mümkündür.

            var selected = DeviceComboBox.SelectedItem?.ToString() ?? "";
            var coreText = CoresTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(coreText) || selected.Contains("Bulunamadı"))
            {
                ResultTextBox.Text = "Lütfen cihaz ve çekirdek girin!";
                return;
            }
            try
            {
                // Çekirdek maskesi oluştur
                var mask = 0;
                foreach (var part in coreText.Split(','))
                {
                    if (int.TryParse(part.Trim(), out int n))
                        mask |= (1 << n);
                }
                if (mask == 0)
                {
                    ResultTextBox.Text = "Çekirdek girişi hatalı!";
                    return;
                }

                // Örnek: Notepad ile affinity gösteriyoruz
                var proc = Process.Start("notepad.exe");
                if (proc != null)
                {
                    proc.WaitForInputIdle();
                    proc.ProcessorAffinity = (IntPtr)mask;
                    ResultTextBox.Text = $"Notepad başlatıldı ve şu maskeye atandı: 0x{mask:X} (CPU {coreText})\n\nNot: Gerçek donanım için sürücü destekli özel yazılımlar gerekir.";
                }
            }
            catch (Exception ex)
            {
                ResultTextBox.Text = "HATA: " + ex.Message;
            }
        }
    }
}
