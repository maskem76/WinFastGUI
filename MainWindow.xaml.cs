using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls; // UserControl için gerekli
using WinFastGUI.Controls;

namespace WinFastGUI
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, UserControl> _controlCache = new Dictionary<string, UserControl>();

        public MainWindow()
        {
            InitializeComponent();
            _controlCache["Home"] = new HomeControl();
            MainContentArea.Content = _controlCache["Home"];
            HeaderTextBlock.Text = "Ana Sayfa";
        }

        private void ShowControl<T>(string controlName, string headerText) where T : UserControl, new()
        {
            if (!_controlCache.ContainsKey(controlName))
            {
                _controlCache[controlName] = new T();
            }
            MainContentArea.Content = _controlCache[controlName];
            HeaderTextBlock.Text = headerText;
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowControl<HomeControl>("Home", "Ana Sayfa");
        }

        private void SystemCleanButton_Click(object sender, RoutedEventArgs e)
        {
            ShowControl<SystemCleanUserControl>("SystemClean", "Sistem Temizliği");
        }

        private void AppManagerButton_Click(object sender, RoutedEventArgs e)
        {
            ShowControl<AppManagerUserControl>("AppManager", "Uygulama Yönetimi");
        }

        private void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            ShowControl<BackupControl>("Backup", "Yedekleme");
        }

        private void ImageBackupButton_Click(object sender, RoutedEventArgs e)
        {
            ShowControl<DismPlusPlusControl>("ImageBackup", "İmaj & ISO Oluşturucu");
        }

        private void OptimizationManagerButton_Click(object sender, RoutedEventArgs e)
        {
            ShowControl<OptimizationManagerControl>("OptimizationManager", "Optimizasyon Yöneticisi");
        }

        private void AffinityManagerButton_Click(object sender, RoutedEventArgs e)
        {
            ShowControl<AffinityManagerControl>("AffinityManager", "Affinity Yöneticisi");
        }

private void IrqToolButton_Click(object sender, RoutedEventArgs e)
{
    HeaderTextBlock.Text = "Interrupt Affinity Policy Tool";
    MainContentArea.Content = new Controls.InterruptAffinityToolControl();
}

        private void UpdateManagerButton_Click(object sender, RoutedEventArgs e)
        {
            ShowControl<UpdateManagerControl>("UpdateManager", "Windows Güncellemesi");
        }

        private void BloatwareButton_Click(object sender, RoutedEventArgs e)
{
    if (!_controlCache.ContainsKey("Bloatware"))
    {
        var bloatwareControl = new BloatwareCleanerUserControl
        {
            LogMessageToMain = LogMessageToMainWindow
        };
        _controlCache["Bloatware"] = bloatwareControl;
    }
    MainContentArea.Content = _controlCache["Bloatware"];
    HeaderTextBlock.Text = "Bloatware";
}


        private void ServiceManagementButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_controlCache.ContainsKey("ServiceManagement"))
            {
                var serviceControl = new ServiceManagementControl
                {
                    LogMessageToMain = LogMessageToMainWindow
                };
                _controlCache["ServiceManagement"] = serviceControl;
            }
            MainContentArea.Content = _controlCache["ServiceManagement"];
            HeaderTextBlock.Text = "Servis Yönetimi";
        }

        public void LogMessageToMainWindow(string message)
        {
            if (LogTextBox != null)
            {
                Dispatcher.Invoke(() =>
                {
                    LogTextBox.Text = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}{LogTextBox.Text}";
                });
            }
        }

        private async Task UpdateProgressBar(int progress, string status)
        {
            await Task.CompletedTask;
        }
    }
}