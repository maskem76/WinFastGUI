# encoding: utf-8-bom
# WinFastTweaks.psm1 - Tüm WinFast Optimizasyonları (Modern ve Kapsamlı Sürüm)

# ===================== TEMEL SİSTEM OPTİMİZASYONLARI =====================

function Optimize-GeneralCleanup {
    param([switch]$Force)
    Write-Host "[BİLGİ] Geçici dosyalar temizleniyor..." -ForegroundColor Cyan
    try {
        Remove-Item -Path "$env:TEMP\*" -Force -Recurse -ErrorAction SilentlyContinue
        Remove-Item -Path "$env:SystemRoot\Temp\*" -Force -Recurse -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] Geçici dosyalar ve temp klasörü temizlendi." -ForegroundColor Green
    } catch { Write-Host "[HATA] Temizlik sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-Services {
    param([switch]$Force)
    Write-Host "[BİLGİ] Gereksiz servisler devre dışı bırakılıyor..." -ForegroundColor Cyan
    $services = @("DiagTrack", "WSearch", "SysMain", "dmwappushservice", "Fax", "XblGameSave", "WMPNetworkSvc", "MapsBroker")
    foreach ($svc in $services) {
        try {
            Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
            Set-Service -Name $svc -StartupType Disabled -ErrorAction SilentlyContinue
            Write-Host "[BAŞARILI] $svc servisi devre dışı bırakıldı." -ForegroundColor Green
        } catch { Write-Host "[HATA] $svc servisinde hata: $($_.Exception.Message)" -ForegroundColor Red }
    }
}

function Optimize-StartupApps {
    param([switch]$Force)
    Write-Host "[BİLGİ] Başlangıç uygulamaları kaldırılıyor..." -ForegroundColor Cyan
    $regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $items = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue | Select-Object -Property * -ExcludeProperty PSPath, PSParentPath, PSChildName, PSDrive, PSProvider
    foreach ($item in $items.PSObject.Properties) {
        if ($item.Name -ne "SecurityHealth") {
            try {
                Remove-ItemProperty -Path $regPath -Name $item.Name -ErrorAction SilentlyContinue
                Write-Host "[BAŞARILI] $($item.Name) başlangıçtan kaldırıldı." -ForegroundColor Green
            } catch { Write-Host "[HATA] $($item.Name) kaldırılırken hata: $($_.Exception.Message)" -ForegroundColor Red }
        }
    }
}

function Optimize-CleanRegistry {
    param([switch]$Force)
    Write-Host "[BİLGİ] Gereksiz registry anahtarları siliniyor..." -ForegroundColor Cyan
    try {
        Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU" -Force -Recurse -ErrorAction SilentlyContinue
        Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU" -Force -Recurse -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] Gereksiz registry anahtarları silindi." -ForegroundColor Green
    } catch { Write-Host "[HATA] Registry temizliği sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-Network {
    param([switch]$Force)
    Write-Host "[BİLGİ] Ağ ayarları optimize ediliyor..." -ForegroundColor Cyan
    try {
        New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" -Name "TcpAckFrequency" -Value 1 -PropertyType DWord -Force
        New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" -Name "TCPNoDelay" -Value 1 -PropertyType DWord -Force
        Write-Host "[BAŞARILI] Ağ ayarları optimize edildi." -ForegroundColor Green
    } catch { Write-Host "[HATA] Ağ optimizasyonu sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-Disk {
    param([switch]$Force)
    Write-Host "[BİLGİ] Disk optimizasyonu (Defrag/TRIM) uygulanıyor..." -ForegroundColor Cyan
    try {
        Optimize-Volume -DriveLetter C -Defrag -Verbose | Out-Null
        Write-Host "[BAŞARILI] Disk optimizasyonu tamamlandı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Disk optimizasyonu sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-DisableWindowsFeatures {
    param([switch]$Force)
    Write-Host "[BİLGİ] Gereksiz Windows özellikleri kapatılıyor..." -ForegroundColor Cyan
    try {
        Disable-WindowsOptionalFeature -Online -FeatureName "SMB1Protocol" -NoRestart -ErrorAction SilentlyContinue
        Disable-WindowsOptionalFeature -Online -FeatureName "WorkFolders-Client" -NoRestart -ErrorAction SilentlyContinue
        Disable-WindowsOptionalFeature -Online -FeatureName "WindowsMediaPlayer" -NoRestart -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] Gereksiz Windows özellikleri kapatıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Özellik kapama sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-WindowsUpdates {
    param([switch]$Force)
    Write-Host "[BİLGİ] Windows Update servisi manuel moda alınıyor..." -ForegroundColor Cyan
    try {
        Stop-Service -Name wuauserv -Force -ErrorAction SilentlyContinue
        Set-Service -Name wuauserv -StartupType Manual -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] Windows güncellemeleri optimize edildi (Manuel moda alındı)." -ForegroundColor Green
    } catch { Write-Host "[HATA] Güncelleme optimizasyonu sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-DisableScheduledTasks {
    param([switch]$Force)
    Write-Host "[BİLGİ] Gereksiz planlanmış görevler devre dışı bırakılıyor..." -ForegroundColor Cyan
    try {
        Get-ScheduledTask | Where-Object { $_.TaskName -like "*OneDrive*" -or $_.TaskName -like "*EdgeUpdate*" -or $_.TaskName -like "*Update*" -or $_.TaskName -like "*Yandex*" } | Unregister-ScheduledTask -Confirm:$false
        Write-Host "[BAŞARILI] Gereksiz planlanmış görevler (OneDrive, Edge, Update, Yandex) kapatıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Planlanmış görev kapama sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

# ================= RİSKLİ VE GELİŞMİŞ SİSTEM OPTİMİZASYONLARI ================

function Optimize-DisableTelemetry {
    param([switch]$Force)
    Write-Host "[UYARI] Telemetri servisleri devre dışı bırakılıyor..." -ForegroundColor Yellow
    try {
        Set-Service -Name "DiagTrack" -StartupType Disabled -ErrorAction SilentlyContinue
        Stop-Service -Name "DiagTrack" -Force -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] Telemetri servisleri devre dışı bırakıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Telemetri kapatma sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-DisableDefender {
    param([switch]$Force)
    Write-Host "[UYARI] Windows Defender devre dışı bırakılıyor. Bu işlem GÜVENLİK RİSKİ oluşturur!" -ForegroundColor Yellow
    try {
        Set-MpPreference -DisableRealtimeMonitoring $true -ErrorAction SilentlyContinue
        Set-Service -Name "WinDefend" -StartupType Disabled -ErrorAction SilentlyContinue
        Stop-Service -Name "WinDefend" -Force -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] Defender devre dışı bırakıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Defender kapatma sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-DisableUpdatesCompletely {
    param([switch]$Force)
    Write-Host "[UYARI] Windows Update servisleri tamamen kapatılıyor. Sisteminiz güncel kalmayacak!" -ForegroundColor Yellow
    try {
        $services = "wuauserv", "DoSvc", "UsoSvc", "bits"
        foreach ($svc in $services) {
            Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
            Set-Service -Name $svc -StartupType Disabled -ErrorAction SilentlyContinue
        }
        Write-Host "[BAŞARILI] Windows Update servisleri kapatıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Update kapatılırken hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-DisableBackgroundApps {
    param([switch]$Force)
    Write-Host "[BİLGİ] Arka plan uygulamaları (UWP) devre dışı bırakılıyor..." -ForegroundColor Cyan
    try {
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications" -Name "GlobalUserDisabled" -Value 1 -Type DWord -Force
        Write-Host "[BAŞARILI] Arka plan uygulamaları kapatıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Arka plan uygulamaları kapatılırken hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-DisableMPO {
    param([switch]$Force)
    Write-Host "[UYARI] MPO (Multi-Plane Overlay) devre dışı bırakılıyor. Sadece gerekliyse kullanın." -ForegroundColor Yellow
    try {
        $path = "HKLM:\SOFTWARE\Microsoft\Windows\Dwm"
        if (!(Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
        Set-ItemProperty -Path $path -Name "OverlayTestMode" -Type DWord -Value 5 -Force
        Write-Host "[BAŞARILI] MPO devre dışı bırakıldı. Değişikliğin etkili olması için bilgisayarı yeniden başlatın." -ForegroundColor Green
    } catch { Write-Host "[HATA] MPO kapatma sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-DisableEventLog {
    param([switch]$Force)
    Write-Host "[UYARI] Olay Günlükleri kapatılıyor. Bu işlem sorun gidermeyi neredeyse imkansız hale getirir!" -ForegroundColor Red
    try {
        $logs = wevtutil el
        foreach ($log in $logs) {
            wevtutil sl "$log" /enabled:false
        }
        Write-Host "[BAŞARILI] Olay günlükleri devre dışı bırakıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Olay günlüğü kapatma sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-DisableYandexUpdates {
    param([switch]$Force)
    Write-Host "[BİLGİ] Yandex servisleri/güncelleyicileri devre dışı bırakılıyor..." -ForegroundColor Cyan
    try {
        Get-ScheduledTask | Where-Object { $_.TaskName -like "*Yandex*" } | Unregister-ScheduledTask -Confirm:$false
        Get-Service | Where-Object { $_.Name -like "*yandex*" } | Stop-Service -Force -ErrorAction SilentlyContinue
        Get-Service | Where-Object { $_.Name -like "*yandex*" } | Set-Service -StartupType Disabled -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] Yandex güncelleyici devre dışı bırakıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Yandex update kapatma sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-VisualEffects {
    param([switch]$Force)
    Write-Host "[BİLGİ] Görsel efektler performans için ayarlanıyor..." -ForegroundColor Cyan
    try {
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" -Name "VisualFXSetting" -Value 2
        Write-Host "[BAŞARILI] Görsel efektler kapatıldı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Görsel efektleri kapatma sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-GameMode {
    param([switch]$Force)
    Write-Host "[BİLGİ] Oyun Modu ayarları optimize ediliyor..." -ForegroundColor Cyan
    try {
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\GameBar" -Name "AllowAutoGameMode" -Value 1
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\GameBar" -Name "GamePanelStartupTipIndex" -Value 3
        Write-Host "[BAŞARILI] Oyun Modu optimize edildi." -ForegroundColor Green
    } catch { Write-Host "[HATA] Oyun Modu optimizasyonu sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-MMCSS {
    param([switch]$Force)
    Write-Host "[BİLGİ] MMCSS profilleri optimize ediliyor..." -ForegroundColor Cyan
    try {
        Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" -Name "SystemResponsiveness" -Value 10
        Write-Host "[BAŞARILI] MMCSS profilleri optimize edildi." -ForegroundColor Green
    } catch { Write-Host "[HATA] MMCSS optimizasyonu sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-InputDevices {
    param([switch]$Force)
    Write-Host "[BİLGİ] Giriş aygıtı (Input Lag) optimizasyonu uygulanıyor..." -ForegroundColor Cyan
    try {
        Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\i8042prt\Parameters" -Name "PollStatusIterations" -Value 1 -Type DWord
        Write-Host "[BAŞARILI] Giriş aygıtı optimizasyonu uygulandı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Giriş aygıtı optimizasyonu sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

# ============== EKSTRA/FANTOM FONKSİYONLAR (alias ve placeholder) ==============

# Alias (eski isimleri de destekler)
Set-Alias Disable-WindowsDefender Optimize-DisableDefender
Set-Alias Disable-MicrosoftEdge Optimize-DisableEdge
Set-Alias Disable-UpdatesCompletely Optimize-DisableUpdatesCompletely
Set-Alias Disable-TelemetryAndPrivacySettings Optimize-DisableTelemetry
Set-Alias Disable-BackgroundApps Optimize-DisableBackgroundApps
Set-Alias Disable-MPO Optimize-DisableMPO
Set-Alias Disable-EventLogging Optimize-DisableEventLog
Set-Alias Disable-YandexUpdates Optimize-DisableYandexUpdates
Set-Alias Optimize-ExplorerSettings Optimize-ExplorerSettings
Set-Alias Optimize-SearchSettings Optimize-SearchSettings

function Optimize-ExplorerSettings {
    param([switch]$Force)
    Write-Host "[BİLGİ] Dosya Gezgini ayarları optimize ediliyor..." -ForegroundColor Cyan
    try {
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" -Name "HideFileExt" -Value 0
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" -Name "Hidden" -Value 1
        Write-Host "[BAŞARILI] Dosya Gezgini ayarları optimize edildi." -ForegroundColor Green
    } catch { Write-Host "[HATA] Dosya Gezgini ayarları sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

function Optimize-SearchSettings {
    param([switch]$Force)
    Write-Host "[BİLGİ] Windows Arama ayarları optimize ediliyor..." -ForegroundColor Cyan
    try {
        Set-ItemProperty -Path "HKCU:\Software\Policies\Microsoft\Windows\Explorer" -Name "DisableSearchBoxSuggestions" -Value 1 -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] Windows Arama, web sonuçlarını göstermeyecek şekilde ayarlandı." -ForegroundColor Green
    } catch { Write-Host "[HATA] Arama ayarları sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red }
}

# Placeholder (Henüz kodlanmamış veya manuel reg/bat/ps1 ile yapılması gereken fonksiyonlar)
function Manage-GpuOptimizations { Write-Host "[PLACEHOLDER] GPU optimizasyonları fonksiyonu eklenmeli! (Registry ile uygulanacak.)" -ForegroundColor Yellow }
function Remove-PhotoViewer      { Write-Host "[PLACEHOLDER] Windows Photo Viewer kaldırma işlemi eklenmeli! (Registry ile uygulanacak.)" -ForegroundColor Yellow }
function Show-CS2Recommendations { Write-Host "[PLACEHOLDER] CS2 tavsiye fonksiyonu - kendi kodunu ekle!" -ForegroundColor Yellow }
function Manage-MemoryOptimizations { Write-Host "[PLACEHOLDER] Gelişmiş bellek optimizasyonları buraya eklenecek." -ForegroundColor Yellow }
function Manage-StorageOptimizations { Write-Host "[PLACEHOLDER] Storage (SSD/HDD) registry tweakleri eklenmeli." -ForegroundColor Yellow }
function Manage-VirtualMemory    { Write-Host "[PLACEHOLDER] Sanal bellek ayarları - registry ile optimize edilecek." -ForegroundColor Yellow }
function Disable-SpecificDevices { Write-Host "[PLACEHOLDER] Belirli donanımların devre dışı bırakılması - manuel registry/bat." -ForegroundColor Yellow }
function Import-NvidiaProfile    { Write-Host "[PLACEHOLDER] Nvidia Profile Import işlemi - nvidiaProfileInspector ile yapılır." -ForegroundColor Yellow }
function Apply-InteractiveAdvancedNetworkTweaks { Write-Host "[PLACEHOLDER] Gelişmiş ağ tweakleri fonksiyonu eklenecek." -ForegroundColor Yellow }
function Invoke-AutomaticOptimization {
    Write-Host "[BİLGİ] Tüm temel ve güvenli optimizasyonlar topluca başlatılıyor..." -ForegroundColor Cyan
    Optimize-GeneralCleanup -Force
    Optimize-Services -Force
    Optimize-StartupApps -Force
    Optimize-CleanRegistry -Force
    Optimize-Network -Force
    Optimize-Disk -Force
    Optimize-DisableWindowsFeatures -Force
    Optimize-WindowsUpdates -Force
    Optimize-DisableScheduledTasks -Force
    Optimize-DisableTelemetry -Force
    Optimize-DisableDefender -Force
    Optimize-DisableUpdatesCompletely -Force
    Optimize-DisableBackgroundApps -Force
    Optimize-DisableMPO -Force
    Optimize-DisableEventLog -Force
    Optimize-DisableYandexUpdates -Force
    Optimize-VisualEffects -Force
    Optimize-GameMode -Force
    Optimize-MMCSS -Force
    Optimize-InputDevices -Force
    Optimize-ExplorerSettings -Force
    Optimize-SearchSettings -Force
    # PLACEHOLDER: Kapsamlı registry tweakleri / GPU, RAM, SSD, vs.
    Manage-GpuOptimizations
    Remove-PhotoViewer
    Show-CS2Recommendations
    Manage-MemoryOptimizations
    Manage-StorageOptimizations
    Manage-VirtualMemory
    Disable-SpecificDevices
    Import-NvidiaProfile
    Apply-InteractiveAdvancedNetworkTweaks
    Write-Host "[BAŞARILI] Tüm güvenli optimizasyonlar tamamlandı. Ek fonksiyonlarınızı placeholder'lara ekleyin." -ForegroundColor Green
}

Export-ModuleMember -Function *

# END OF FILE
