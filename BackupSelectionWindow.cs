using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media; // Brushes, Color için
using System.Linq; // List için

namespace WinFastGUI
{
    public class BackupSelectionWindow : Window 
    {
        public string? SelectedOption { get; private set; }
        public Action<string>? LogMessageToMain { get; set; }

        private ComboBox _optionComboBox;

        public BackupSelectionWindow(List<string> options)
        {
            Title = "Yedekleme Seçenekleri";
            Width = 300;
            Height = 250; 
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A)); 
            Foreground = Brushes.White; 

            _optionComboBox = new ComboBox
            {
                Margin = new Thickness(10),
                Width = 250,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Center, 
                Background = Brushes.LightGray,
                Foreground = Brushes.Black 
            };
            // ComboBoxItem'lara Content ve Tag ataması yapılıyor (Tag değerleri doğrudan case'lerle eşleşmeli)
            _optionComboBox.Items.Add(new ComboBoxItem { Content = "1. Sistem Geri Yükleme Noktası", Tag = "SystemRestorePoint" });
            _optionComboBox.Items.Add(new ComboBoxItem { Content = "2. Kayıt Defteri Tam Yedeklemesi", Tag = "RegistryBackup" });
            _optionComboBox.Items.Add(new ComboBoxItem { Content = "3. Dosya Yedekleme", Tag = "FileBackup" });
            
            _optionComboBox.SelectedIndex = 0; 

            var textBlockPrompt = new TextBlock 
            { 
                Text = "Lütfen bir yedekleme türü seçin:", 
                Margin = new Thickness(10, 10, 10, 0), 
                Foreground = Brushes.White, 
                HorizontalAlignment = HorizontalAlignment.Center 
            };

            var okButton = new Button
            {
                Content = "Tamam",
                Width = 100,
                Height = 35,
                Margin = new Thickness(5), 
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50)), 
                Foreground = Brushes.White
            };
            okButton.Click += (s, e) =>
            {
                if (_optionComboBox.SelectedItem is ComboBoxItem selectedComboBoxItem && selectedComboBoxItem.Tag != null)
                {
                    SelectedOption = selectedComboBoxItem.Tag.ToString(); 
                    LogMessageToMain?.Invoke($"Seçilen yedekleme türü (Tag): {SelectedOption}");
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Lütfen bir seçenek belirleyin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var cancelButton = new Button
            {
                Content = "İptal",
                Width = 100,
                Height = 35,
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF4, 0x43, 0x36)),
                Foreground = Brushes.White
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            var mainStackPanel = new StackPanel { Margin = new Thickness(10) };
            mainStackPanel.Children.Add(textBlockPrompt);
            mainStackPanel.Children.Add(_optionComboBox);
            mainStackPanel.Children.Add(buttonPanel);

            Content = mainStackPanel;
        }
    }
}