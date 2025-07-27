// BackupManager.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WinFastGUI.Controls
{
    public class BackupManager
    {
        private readonly Action<string> _logAction;
        private readonly Action<double, string> _progressAction;

        public BackupManager(Action<string> logAction, Action<double, string> progressAction)
        {
            _logAction = logAction;
            _progressAction = progressAction;
        }

        public async Task<(string DeviceObject, string ShadowId)> CreateSnapshotAsync()
        {
            return await Task.Run(() =>
            {
                using var mngClass = new ManagementClass(@"\\.\root\cimv2", "Win32_ShadowCopy", null!);
                using var inParams = mngClass.GetMethodParameters("Create");
                inParams["Volume"] = "C:\\";
                inParams["Context"] = "ClientAccessible";
                using var outParams = mngClass.InvokeMethod("Create", inParams, null!);

                if ((uint)outParams["ReturnValue"] != 0)
                    throw new Exception($"Snapshot oluşturma başarısız! WMI Kodu: {outParams["ReturnValue"]}");

                string shadowId = outParams["ShadowID"]?.ToString() ?? throw new Exception("Geçerli bir Snapshot ID alınamadı.");
                
                using var searcher = new ManagementObjectSearcher($@"\\.\root\cimv2", $"SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID = '{shadowId}'");
                var obj = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                string deviceObject = obj?["DeviceObject"]?.ToString() ?? throw new Exception("DeviceObject bulunamadı.");
                
                return (deviceObject, shadowId);
            });
        }

        public async Task<bool> DeleteAllSnapshotsAsync()
        {
            return await RunProcessAsync(Path.Combine(Environment.SystemDirectory, "vssadmin.exe"), "delete shadows /for=C: /all /quiet");
        }

        public async Task CreateWimImageAsync(string snapshotDevicePath, string wimTargetPath)
        {
            string mountPath = Path.Combine(Path.GetTempPath(), "WinFastMount");
            try
            {
                await Task.Run(() => MountSnapshot(snapshotDevicePath, mountPath));
                await Task.Run(() => CaptureWim(wimTargetPath, mountPath));
                await FixFilePermissionsAsync(wimTargetPath);
            }
            finally
            {
                await Task.Run(() => UnmountSnapshot(mountPath));
            }
        }
        
        public async Task CreateIsoFromWimAsync(string sourceWimPath, string outputIsoPath, string templateZipName)
        {
             string modulesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
             string templateZipPath = Path.Combine(modulesPath, templateZipName);
             string scriptPath = Path.Combine(modulesPath, "Create-CustomISO.ps1");

             if (!File.Exists(scriptPath)) throw new FileNotFoundException("PowerShell scripti bulunamadı!", scriptPath);
             if (!File.Exists(templateZipPath)) throw new FileNotFoundException("Seçilen şablon dosyası bulunamadı!", templateZipPath);

             string command = $"& '{scriptPath}' -TemplateZipPath '{templateZipPath}' -SourceWimPath '{sourceWimPath}' -OutputIsoPath '{outputIsoPath}' -ModulesPath '{modulesPath}'";
             
             bool success = await RunPowerShellWithNsudoAsync(command);
             if(!success)
             {
                 throw new Exception("ISO oluşturma işlemi başarısız oldu. Detaylar için logları kontrol edin.");
             }
        }

        private void MountSnapshot(string devicePath, string mountPath)
        {
            if (Directory.Exists(mountPath)) UnmountSnapshot(mountPath);
            RunProcess("cmd.exe", $"/c mklink /d \"{mountPath}\" \"{devicePath.TrimEnd('\\')}\\\"");
        }

        private void UnmountSnapshot(string mountPath)
        {
            if (!Directory.Exists(mountPath)) return;
            RunProcess("cmd.exe", $"/c rmdir \"{mountPath}\"");
        }

        private void CaptureWim(string wimPath, string captureDir)
        {
            string exclusionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wimscript.ini");
            string args = $"/Capture-Image /ImageFile:\"{wimPath}\" /CaptureDir:\"{captureDir.TrimEnd('\\')}\" /Name:\"WinFastYedek\" /Compress:Max /CheckIntegrity";
            if (File.Exists(exclusionFile)) args += $" /ConfigFile:\"{exclusionFile}\"";

            RunProcess(Path.Combine(Environment.SystemDirectory, "dism.exe"), args, true);
        }
        
        private async Task<bool> FixFilePermissionsAsync(string filePath)
        {
            _logAction($"'{filePath}' dosyasının izinleri ayarlanıyor...");
            string modulesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
            string scriptPath = Path.Combine(modulesDir, "Fix-Permissions.ps1");
            if (!File.Exists(scriptPath))
            {
                _logAction("HATA: Fix-Permissions.ps1 scripti bulunamadı.");
                return false;
            }
            string command = $"& '{scriptPath}' -FilePath '{filePath}'";
            return await RunPowerShellWithNsudoAsync(command);
        }

        private bool RunProcess(string fileName, string arguments, bool trackProgress = false)
        {
            var psi = new ProcessStartInfo { FileName = fileName, Arguments = arguments, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, Verb = "runas" };
            using var proc = Process.Start(psi) ?? throw new Exception($"{fileName} işlemi başlatılamadı.");
            
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) { if (trackProgress) ParseProgress(e.Data); _logAction(e.Data); } };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) _logAction($"HATA: {e.Data}"); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }

        private async Task<bool> RunPowerShellWithNsudoAsync(string command)
        {
            string modulesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
            string nsudoFolder = Path.Combine(modulesDir, "nsudo");
            string nsudoPath = Path.Combine(nsudoFolder, "NSudoLC.exe");
            if (!File.Exists(nsudoPath)) nsudoPath = Path.Combine(nsudoFolder, "Nsudo.exe");

            string fileName;
            string arguments;
            if (File.Exists(nsudoPath))
            {
                _logAction("NSudo bulundu. İşlem TrustedInstaller yetkisiyle çalıştırılacak.");
                fileName = nsudoPath;
                arguments = $"-U:T -P:E -Wait powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{command}\"";
            }
            else
            {
                _logAction("UYARI: NSudo bulunamadı. Standart Yönetici yetkisiyle denenecek.");
                fileName = "powershell.exe";
                arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"";
            }
            
            var psi = new ProcessStartInfo { FileName = fileName, Arguments = arguments, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using var process = Process.Start(psi);
            if (process == null) return false;
            
            process.OutputDataReceived += (sender, args) => { if(args.Data != null) _logAction(args.Data); };
            process.ErrorDataReceived += (sender, args) => { if(args.Data != null) _logAction($"HATA: {args.Data}"); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
		
		private async Task<bool> RunProcessAsync(string fileName, string arguments, bool trackProgress = false)
{
    var psi = new ProcessStartInfo 
    { 
        FileName = fileName, 
        Arguments = arguments, 
        UseShellExecute = false, 
        RedirectStandardOutput = true, 
        RedirectStandardError = true, 
        CreateNoWindow = true, 
        Verb = "runas" 
    };
    
    using var proc = Process.Start(psi);
    if (proc == null) 
    {
        _logAction($"{fileName} işlemi başlatılamadı.");
        return false;
    }
    
    proc.OutputDataReceived += (s, e) => 
    { 
        if (e.Data != null) 
        { 
            if (trackProgress) ParseProgress(e.Data); 
            _logAction(e.Data); 
        } 
    };
    proc.ErrorDataReceived += (s, e) => { if (e.Data != null) _logAction($"HATA: {e.Data}"); };
    
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    
    await proc.WaitForExitAsync();
    
    return proc.ExitCode == 0;
}

        private void ParseProgress(string line)
        {
            var match = Regex.Match(line, @"\[\s*\=*\s*([\d\.,]+)%\s*\=*\s*\]");
            if (match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                _progressAction(val, $"İlerleme: %{Math.Round(val)}");
            }
        }
    }
}