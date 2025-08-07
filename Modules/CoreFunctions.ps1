function Invoke-AppUninstallMsi {
    param ([string]$ProductCode, [string]$AppName, [string]$Vendor)
    $args = "/x `"$ProductCode`" /qn"
    Start-Process "msiexec.exe" -ArgumentList $args -Wait -NoNewWindow
    Write-Host "MSI kaldırma tamamlandı: $AppName"
}

function Invoke-AppUninstallExe {
    param ([string]$UninstallString, [string]$AppName, [string]$Vendor)
    if (-not [string]::IsNullOrWhiteSpace($UninstallString)) {
        $safeArgs = "/c `"$UninstallString`""
        Start-Process -FilePath "cmd.exe" -ArgumentList $safeArgs -Wait -NoNewWindow
        Write-Host "EXE kaldırma tamamlandı: $AppName"
    } else {
        Write-Host "Geçersiz UninstallString: $AppName"
    }
}

function Remove-AppRemnants {
    param ([string]$AppName, [string]$Vendor)
    Write-Host "Artıklar temizleniyor: $AppName"
    switch ($AppName.ToLower()) {
        "yandex" {
            $yandexTasks = @("\Yandex\YandexBrowserUpdateTaskMachineCore", "\Yandex\YandexBrowserUpdateTaskMachineUA")
            foreach ($task in $yandexTasks) {
                schtasks /Delete /TN $task /F 2>$null
                Write-Host "🗑️ Silindi: $task"
            }
            Stop-Service -Name YandexBrowserService -Force -ErrorAction SilentlyContinue
            Set-Service -Name YandexBrowserService -StartupType Disabled -ErrorAction SilentlyContinue
            $browserUpdateExe = "C:\Program Files (x86)\Yandex\YandexBrowser\browser_update.exe"
            if (Test-Path $browserUpdateExe) {
                Rename-Item $browserUpdateExe -NewName "browser_update_disabled.exe" -ErrorAction SilentlyContinue
                Write-Host "🔒 browser_update.exe yeniden adlandırıldı."
            }
            $yandexPath = "C:\Program Files (x86)\Yandex\YandexBrowser"
            if (Test-Path $yandexPath) {
                Remove-Item $yandexPath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "Yandex klasörü silindi."
            }
        }
        "driver booster" {
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
                Get-ChildItem $path | ForEach-Object {
                    try {
                        $prop = Get-ItemProperty $_.PsPath
                        if ($prop.DisplayName -like "*Driver Booster*") {
                            Remove-Item $_.PsPath -Recurse -Force -ErrorAction SilentlyContinue
                        }
                    } catch {}
                }
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
        try {
            $appInfo = Get-ChildItem $path | ForEach-Object { Get-ItemProperty $_.PsPath } | Where-Object { $_.DisplayName -like "*$AppName*" }
            if ($appInfo) { break }
        } catch {}
    }

    if ($appInfo) {
        $vendor = $appInfo.Publisher
        if ($appInfo.UninstallString.ToLower().Contains("msiexec.exe")) {
            $productCode = ($appInfo.UninstallString -replace '.*(\{{8\}}-\{{4\}}-\{{4\}}-\{{4\}}-\{{12\}}).*', '$1')
            Invoke-AppUninstallMsi -ProductCode $productCode -AppName $AppName -Vendor $vendor
        }
        elseif ($appInfo.UninstallString) {
            Invoke-AppUninstallExe -UninstallString $appInfo.UninstallString -AppName $AppName -Vendor $vendor
        } else {
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
            Set-Service -Name YandexBrowserService -StartupType Automatic -ErrorAction SilentlyContinue
            Start-Service -Name YandexBrowserService -ErrorAction SilentlyContinue
            Write-Host "YandexBrowserService geri yüklendi."
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
