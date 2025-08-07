function Invoke-AppUninstallMsi {
    param ([string]$ProductCode, [string]$AppName, [string]$Vendor)
    Write-Host "MSI kaldırma başlatılıyor: $AppName, ProductCode: $ProductCode"
    if ([string]::IsNullOrEmpty($ProductCode) -or $ProductCode -notmatch '^{[0-9A-Fa-f\-]{36}}$') {
        Write-Host "Hata: Geçersiz ProductCode - $AppName için MSI kaldırma başarısız"
        return
    }
    $args = "/x $ProductCode /qn"
    try {
        $process = Start-Process "msiexec.exe" -ArgumentList $args -Wait -NoNewWindow -PassThru -ErrorAction Stop
        if ($process.ExitCode -eq 0) {
            Write-Host "MSI kaldırma tamamlandı: $AppName"
        } else {
            Write-Host "Hata: MSI kaldırma başarısız, ExitCode: $($process.ExitCode) - $AppName"
        }
    } catch {
        Write-Host "Hata: MSI kaldırma sırasında hata oluştu - $_"
    }
}

function Invoke-AppUninstallExe {
    param ([string]$UninstallString, [string]$AppName, [string]$Vendor)
    Write-Output "EXE kaldırma başlatılıyor: $AppName, UninstallString: $UninstallString"
    if ([string]::IsNullOrEmpty($UninstallString)) {
        Write-Output "Hata: UninstallString boş veya geçersiz - $AppName için kaldırma başarısız"
        return
    }
    try {
        $process = Start-Process -FilePath "cmd.exe" -ArgumentList "/c $UninstallString" -Wait -NoNewWindow -PassThru -ErrorAction Stop
        if ($process.ExitCode -eq 0) {
            Write-Output "EXE kaldırma tamamlandı: $AppName"
        } else {
            Write-Output "Hata: EXE kaldırma başarısız, ExitCode: $($process.ExitCode) - $AppName"
        }
    } catch {
        Write-Output "Hata: EXE kaldırma sırasında hata oluştu - $_"
    }
}

function Remove-AppRemnants {
    param ([string]$AppName, [string]$Vendor)
    Write-Host "Artıklar temizleniyor: $AppName"

    switch -Wildcard ($AppName.ToLower()) {
        "yandex*" {
            # Planlanmış görevleri sil
            $yandexTasks = @(
                "\Yandex\YandexBrowserUpdateTaskMachineCore",
                "\Yandex\YandexBrowserUpdateTaskMachineUA"
            )
            foreach ($task in $yandexTasks) {
                schtasks /Delete /TN $task /F 2>$null
                Write-Host "🗑️ Silindi: $task"
            }

            # YandexBrowserService servisini durdur ve devre dışı bırak
            Stop-Service -Name YandexBrowserService -Force -ErrorAction SilentlyContinue
            Set-Service -Name YandexBrowserService -StartupType Disabled -ErrorAction SilentlyContinue
            Write-Host "YandexBrowserService devre dışı bırakıldı"

            # browser_update.exe adını değiştir
            $browserUpdateExe = "C:\Program Files (x86)\Yandex\YandexBrowser\browser_update.exe"
            if (Test-Path $browserUpdateExe) {
                try {
                    Rename-Item $browserUpdateExe -NewName "browser_update_disabled.exe" -ErrorAction Stop
                    Write-Host "🔒 browser_update.exe yeniden adlandırıldı: $browserUpdateExe"
                } catch {
                    Write-Host "Hata: $browserUpdateExe yeniden adlandırılamadı - $_"
                }
            } else {
                Write-Host "Dosya bulunamadı: $browserUpdateExe"
            }
        }

        "edge*" {
            # Önce süreçleri sonlandır
            $processes = @("msedge", "msedgewebview2", "MicrosoftEdgeUpdate")
            foreach ($processName in $processes) {
                Get-Process -Name $processName -ErrorAction SilentlyContinue | ForEach-Object {
                    try {
                        Stop-Process -Id $_.Id -Force -ErrorAction Stop
                        Write-Host "Süreç sonlandırıldı: $processName (PID: $($_.Id))"
                    } catch {
                        Write-Host "Hata: $processName süreci sonlandırılamadı - $_"
                    }
                }
            }

            # MicrosoftEdgeUpdate.exe adını değiştir
            $updateExePath = "C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe"
            if (Test-Path $updateExePath) {
                try {
                    Rename-Item $updateExePath -NewName "MicrosoftEdgeUpdate_disabled.exe" -ErrorAction Stop
                    Write-Host "🔒 MicrosoftEdgeUpdate.exe yeniden adlandırıldı: $updateExePath"
                } catch {
                    Write-Host "Hata: MicrosoftEdgeUpdate.exe yeniden adlandırılamadı - $_"
                }
            }

            # Edge ve WebView2 bileşenlerini engelle
            $edgePath = "C:\Program Files (x86)\Microsoft\Edge"
            if (Test-Path $edgePath) {
                Remove-Item $edgePath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "Microsoft Edge klasörü silindi: $edgePath"
            }

            # WebView2 için dinamik yol tespiti
            $webviewBasePath = "C:\Program Files (x86)\Microsoft\EdgeWebView\Application"
            if (Test-Path $webviewBasePath) {
                $latestVersions = Get-ChildItem -Path $webviewBasePath -Directory
                foreach ($version in $latestVersions) {
                    $webviewPath = Join-Path $webviewBasePath $version.Name
                    if (Test-Path $webviewPath) {
                        Remove-Item $webviewPath -Recurse -Force -ErrorAction SilentlyContinue
                        Write-Host "Microsoft Edge WebView klasörü silindi: $webviewPath"
                    }
                }
            }

            # MicrosoftEdgeUpdate servisini devre dışı bırak
            $updateService = Get-Service -Name "edgeupdate" -ErrorAction SilentlyContinue
            if ($updateService) {
                Stop-Service -Name "edgeupdate" -Force -ErrorAction SilentlyContinue
                Set-Service -Name "edgeupdate" -StartupType Disabled -ErrorAction SilentlyContinue
                Write-Host "MicrosoftEdgeUpdate servisi devre dışı bırakıldı"
            }

            # Edge’in kayıt defteri girdilerini temizle
            $regPaths = @("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM:\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU:\Software\Microsoft\Edge", "HKCU:\Software\Microsoft\EdgeWebView", "HKLM:\SOFTWARE\Microsoft\EdgeUpdate")
            foreach ($path in $regPaths) {
                if (Test-Path $path) {
                    Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Host "Kayıt defteri temizlendi: $path"
                }
            }

            # Edge’in otomatik yüklenmesini engelleyen kayıt defteri ayarı
            $policyPath = "HKLM:\SOFTWARE\Policies\Microsoft\Edge"
            if (-not (Test-Path $policyPath)) {
                New-Item -Path $policyPath -Force | Out-Null
            }
            Set-ItemProperty -Path $policyPath -Name "HideMicrosoftEdgeDownloads" -Value 1 -Type DWord -ErrorAction SilentlyContinue
            Write-Host "Microsoft Edge otomatik yükleme engellendi"
        }

        "msedgewebview2*" {
            # Önce süreçleri sonlandır
            $processes = @("msedgewebview2", "MicrosoftEdgeUpdate")
            foreach ($processName in $processes) {
                Get-Process -Name $processName -ErrorAction SilentlyContinue | ForEach-Object {
                    try {
                        Stop-Process -Id $_.Id -Force -ErrorAction Stop
                        Write-Host "Süreç sonlandırıldı: $processName (PID: $($_.Id))"
                    } catch {
                        Write-Host "Hata: $processName süreci sonlandırılamadı - $_"
                    }
                }
            }

            # MicrosoftEdgeUpdate.exe adını değiştir
            $updateExePath = "C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe"
            if (Test-Path $updateExePath) {
                try {
                    Rename-Item $updateExePath -NewName "MicrosoftEdgeUpdate_disabled.exe" -ErrorAction Stop
                    Write-Host "🔒 MicrosoftEdgeUpdate.exe yeniden adlandırıldı: $updateExePath"
                } catch {
                    Write-Host "Hata: MicrosoftEdgeUpdate.exe yeniden adlandırılamadı - $_"
                }
            }

            # WebView2 için dinamik yol tespiti
            $webviewBasePath = "C:\Program Files (x86)\Microsoft\EdgeWebView\Application"
            if (Test-Path $webviewBasePath) {
                $latestVersions = Get-ChildItem -Path $webviewBasePath -Directory
                foreach ($version in $latestVersions) {
                    $webviewPath = Join-Path $webviewBasePath $version.Name
                    if (Test-Path $webviewPath) {
                        Remove-Item $webviewPath -Recurse -Force -ErrorAction SilentlyContinue
                        Write-Host "Microsoft Edge WebView klasörü silindi: $webviewPath"
                    }
                }
            }

            # WebView2 kayıt defteri temizliği
            $regPaths = @("HKCU:\Software\Microsoft\EdgeWebView")
            foreach ($path in $regPaths) {
                if (Test-Path $path) {
                    Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Host "Kayıt defteri temizlendi: $path"
                }
            }

            # MicrosoftEdgeUpdate servisini devre dışı bırak
            $updateService = Get-Service -Name "edgeupdate" -ErrorAction SilentlyContinue
            if ($updateService) {
                Stop-Service -Name "edgeupdate" -Force -ErrorAction SilentlyContinue
                Set-Service -Name "edgeupdate" -StartupType Disabled -ErrorAction SilentlyContinue
                Write-Host "MicrosoftEdgeUpdate servisi devre dışı bırakıldı"
            }
        }

        "driver booster*" {
            $uninstallPath = "C:\Program Files\IObit\Driver Booster"
            if (Test-Path $uninstallPath) {
                Remove-Item $uninstallPath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "Driver Booster klasörü silindi."
            }

            $regPaths = @(
                "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
                "HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            )

            foreach ($path in $regPaths) {
                Get-ChildItem -Path $path | Where-Object {
                    $_.GetValue("DisplayName") -like "*Driver Booster*"
                } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        default {
            Write-Host "Desteklenmeyen program için artık temizleme: $AppName"
        }
    }

    Write-Host "Artık temizleme tamamlandı: $AppName"
}

function Remove-InatciProgram {
    param ([string]$AppName)
    Write-Host "Kaldırılıyor: $AppName"

    $regPaths = @(
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    )

    $appInfo = $null
    foreach ($path in $regPaths) {
        $appInfo = Get-ItemProperty -Path "$path\*" -ErrorAction SilentlyContinue | Where-Object {
            $_.DisplayName -like "*$AppName*"
        }
        if ($appInfo) { break }
    }

    if ($appInfo) {
        $vendor = $appInfo.Publisher
        $uninstallString = $appInfo.UninstallString
        Write-Host "Bulunan UninstallString: $uninstallString"
        if ($uninstallString -match "msiexec\.exe") {
            $productCode = $uninstallString -replace '.*({[0-9A-Fa-f\-]+}).*', '$1'
            Write-Host "Çıkarılan ProductCode: $productCode"
            Invoke-AppUninstallMsi -ProductCode $productCode -AppName $AppName -Vendor $vendor
        }
        elseif ($uninstallString) {
            Invoke-AppUninstallExe -UninstallString $uninstallString -AppName $AppName -Vendor $vendor
        }
        else {
            Write-Host "UninstallString bulunamadı: $AppName"
        }

        Remove-AppRemnants -AppName $AppName -Vendor $vendor
    }
    else {
        Write-Host "Uygulama registry'de bulunamadı: $AppName"
        Remove-AppRemnants -AppName $AppName -Vendor $null
    }

    Write-Host "Kaldırma tamamlandı: $AppName"
}

function Restore-InatciProgram {
    param ([string]$AppName)
    Write-Host "Geri yükleniyor: $AppName"

    switch ($AppName.ToLower()) {
        "yandex" {
            $browserUpdateExe = "C:\Program Files (x86)\Yandex\YandexBrowser\browser_update_disabled.exe"
            if (Test-Path $browserUpdateExe) {
                Rename-Item $browserUpdateExe -NewName "browser_update.exe" -ErrorAction SilentlyContinue
                Write-Host "🔓 browser_update.exe geri adlandırıldı."
            }

            $yandexService = Get-Service -Name "YandexBrowserService" -ErrorAction SilentlyContinue
            if ($yandexService) {
                Set-Service -Name "YandexBrowserService" -StartupType Automatic -ErrorAction SilentlyContinue
                Start-Service -Name "YandexBrowserService" -ErrorAction SilentlyContinue
                Write-Host "YandexBrowserService geri yüklendi"
            }

            # Planlanmış görevleri geri yüklemek için manuel kontrol
            $yandexTasks = @("\Yandex\YandexBrowserUpdateTaskMachineCore", "\Yandex\YandexBrowserUpdateTaskMachineUA")
            foreach ($task in $yandexTasks) {
                if (-not (schtasks /Query /TN $task 2>$null)) {
                    Write-Host "Planlanmış görev geri yüklenmedi: $task (manuel olarak ekleyin)"
                }
            }
        }

        "edge" {
            $policyPath = "HKLM:\SOFTWARE\Policies\Microsoft\Edge"
            if (Test-Path $policyPath) {
                Remove-ItemProperty -Path $policyPath -Name "HideMicrosoftEdgeDownloads" -ErrorAction SilentlyContinue
                Write-Host "Microsoft Edge engelleme kaldırıldı"
            }

            # WebView2 ve MicrosoftEdgeUpdate engellemesini kaldır
            $webviewRegPath = "HKCU:\Software\Microsoft\EdgeWebView"
            if (Test-Path $webviewRegPath) {
                Remove-Item -Path $webviewRegPath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "Microsoft Edge WebView kayıt defteri kaldırıldı"
            }
            $updateExePath = "C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate_disabled.exe"
            if (Test-Path $updateExePath) {
                Rename-Item $updateExePath -NewName "MicrosoftEdgeUpdate.exe" -ErrorAction SilentlyContinue
                Write-Host "🔓 MicrosoftEdgeUpdate.exe geri adlandırıldı"
            }
            $updateService = Get-Service -Name "edgeupdate" -ErrorAction SilentlyContinue
            if ($updateService) {
                Set-Service -Name "edgeupdate" -StartupType Automatic -ErrorAction SilentlyContinue
                Start-Service -Name "edgeupdate" -ErrorAction SilentlyContinue
                Write-Host "MicrosoftEdgeUpdate servisi geri yüklendi"
            }
        }

        "driver booster" {
            Write-Host "Driver Booster geri yükleme desteklenmiyor."
        }

        default {
            Write-Host "Desteklenmeyen program: $AppName"
        }
    }

    Write-Host "Geri yükleme tamamlandı: $AppName"
}

Export-ModuleMember -Function Remove-InatciProgram, Restore-InatciProgram, Invoke-AppUninstallMsi, Invoke-AppUninstallExe, Remove-AppRemnants