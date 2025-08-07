# Write-Log fonksiyonu
function Write-Log {
    param (
        [string]$Message,
        [string]$Level = "INFO"
    )

    $logFilePath = Join-Path $PSScriptRoot "WinFastGUI.log"
    
    $logLine = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    
    try {
        [System.IO.File]::AppendAllText($logFilePath, "$logLine`r`n", [System.Text.Encoding]::UTF8)
    } catch {
        Write-Output "Log yazma hatası: $_"
    }
    
    Write-Output $logLine
}

# Generate-RegFile fonksiyonu
function Generate-RegFile {
    param (
        [string]$Category,
        [array]$Actions
    )
    try {
        $regFilePath = Join-Path -Path $PSScriptRoot -ChildPath "RegFiles\$Category.reg"
        $regContent = "Windows Registry Editor Version 5.00`r`n`r`n"
        
        foreach ($action in $Actions) {
            $path = $action.Path
            $name = $action.Name
            $value = $action.Value
            $type = $action.Type

            $regContent += "[$path]`r`n"
            if ($action.Action -eq "RemoveKey") {
                $regContent += "[-$path]`r`n"
            } elseif ($action.Action -eq "RemoveValue") {
                $regContent += "`"$name`"=-`r`n"
            } else {
                switch ($type) {
                    "DWord" {
                        $regContent += "`"$name`"=dword:$($value.ToString('X8'))`r`n"
                    }
                    "String" {
                        $regContent += "`"$name`"=`"$value`"`r`n"
                    }
                    "Binary" {
                        $hexValue = ($value | ForEach-Object { $_.ToString("X2") }) -join ","
                        $regContent += "`"$name`"=hex:$hexValue`r`n"
                    }
                }
            }
        }

        $regDir = Join-Path -Path $PSScriptRoot -ChildPath "RegFiles"
        if (-not (Test-Path $regDir)) {
            New-Item -ItemType Directory -Path $regDir | Out-Null
        }
        [System.IO.File]::WriteAllText($regFilePath, $regContent, [System.Text.Encoding]::Unicode)
        Write-Log ".reg dosyası oluşturuldu: $regFilePath" "INFO"
        Write-Output ".reg dosyası oluşturuldu: $regFilePath"
    } catch {
        Write-Log ".reg dosyası oluşturma hatası ($Category): $_" "ERROR"
        throw $_
    }
}

# Apply-RegistryAction fonksiyonu
function Apply-RegistryAction {
    param (
        [hashtable]$Action
    )
    try {
        $path = $Action.Path
        $name = $Action.Name
        $value = $Action.Value
        $type = $Action.Type

        if (-not (Test-Path $path)) {
            New-Item -Path $path -Force | Out-Null
            Write-Log "Registry yolu oluşturuldu: $path" "INFO"
        }

        if ($Action.Action -eq "RemoveKey") {
            if (Test-Path $path) {
                try {
                    Remove-Item -Path $path -Force -Recurse -ErrorAction Stop
                    Write-Log "Registry anahtarı kaldırıldı: $path" "INFO"
                } catch {
                    Write-Log "Registry anahtarı kaldırma hatası: $path, Hata: $_" "WARNING"
                }
            } else {
                Write-Log "Registry anahtarı zaten yok: $path" "INFO"
            }
        } elseif ($Action.Action -eq "RemoveValue") {
            if (Get-ItemProperty -Path $path -Name $name -ErrorAction SilentlyContinue) {
                Remove-ItemProperty -Path $path -Name $name -Force
                Write-Log "Registry değeri kaldırıldı: $path\$name" "INFO"
            } else {
                Write-Log "Registry değeri zaten yok: $path\$name" "INFO"
            }
        } else {
            if ($type -eq "DWord") {
                Set-ItemProperty -Path $path -Name $name -Value ([int]$value) -Force
                Write-Log "DWord değeri ayarlandı: $path\$name = $value" "INFO"
                Write-Output "DWord değeri ayarlandı: $path\$name = $value"
            } elseif ($type -eq "String") {
                Set-ItemProperty -Path $path -Name $name -Value ([string]$value) -Force
                Write-Log "String değeri ayarlandı: $path\$name = $value" "INFO"
                Write-Output "String değeri ayarlandı: $path\$name = $value"
            } elseif ($type -eq "Binary") {
                Set-ItemProperty -Path $path -Name $name -Value ([byte[]]$value) -Force
                Write-Log "Binary değeri ayarlandı: $path\$name" "INFO"
                Write-Output "Binary değeri ayarlandı: $path\$name"
            }
        }
    } catch {
        Write-Log "Registry işlemi hatası ($path\$name): $_" "ERROR"
        throw $_
    }
}
function Invoke-NSudo {
    param (
        [string]$Command
    )
    try {
        Write-Log "Invoke-NSudo çalıştırılıyor: $Command" "INFO"
        $advancedRunPath = Join-Path -Path $PSScriptRoot -ChildPath "Modules\advanced\AdvancedRun.exe"
        if (-not (Test-Path $advancedRunPath)) {
            Write-Log "HATA: AdvancedRun.exe bulunamadı: $advancedRunPath" "ERROR"
            throw "AdvancedRun.exe bulunamadı: $advancedRunPath"
        }
        $arguments = "/U:S /P:E /M:S /UseCurrentConsole powershell.exe -NoProfile -Command `"$Command`""
        $process = Start-Process -FilePath $advancedRunPath -ArgumentList $arguments -Wait -NoNewWindow -PassThru
        if ($process.ExitCode -eq 0) {
            Write-Log "BAŞARILI: Komut çalıştırıldı: $Command" "INFO"
            return $true
        } else {
            Write-Log "HATA: Komut başarısız oldu (ExitCode: $($process.ExitCode)): $Command" "ERROR"
            return $false
        }
    } catch {
        Write-Log "HATA: Invoke-NSudo hatası: $_" "ERROR"
        throw $_
    }
}

# Invoke-WindowsPowerShell fonksiyonu
function Invoke-WindowsPowerShell {
    param($ScriptBlock)
    try {
        $output = powershell.exe -NoProfile -Command $ScriptBlock 2>&1
        Write-Log "Windows PowerShell çıktısı: $output" "INFO"
        return $true
    } catch {
        Write-Log "Windows PowerShell hatası: $_" "ERROR"
        return $false
    }
}

# Optimize-MicrosoftEdge fonksiyonu (Güncellendi)
function Optimize-MicrosoftEdge {
         try {
             Write-Log "Optimize-MicrosoftEdge başlatılıyor: MicrosoftEdge ve ilgili bileşenler kaldırılıyor" "INFO"
             
             # 0. NSudo’nun çalıştığını doğrula
             Write-Log "0. Adım: NSudo testi yapılıyor..." "INFO"
             if (-not (Invoke-NSudo -Command "Write-Output 'NSudo testi başarılı'")) {
                 Write-Log "HATA: Invoke-NSudo çalışmıyor, işlem sonlandırılıyor" "ERROR"
                 throw "Invoke-NSudo çalışmıyor"
             }
             Write-Log "BAŞARILI: NSudo testi geçti" "INFO"

             # 1. Edge süreçlerini sonlandır
             Write-Log "1. Adım: Edge süreçleri sonlandırılıyor..." "INFO"
             $edgeProcesses = @("MicrosoftEdge", "MicrosoftEdgeCP", "MicrosoftEdgeSH", "msedge", "edgeupdate", "edgeupdatem", "MicrosoftEdgeDevToolsClient")
             foreach ($process in $edgeProcesses) {
                 Write-Log "Süreç kontrol ediliyor: $process" "INFO"
                 if (Get-Process -Name $process -ErrorAction SilentlyContinue) {
                     $command = "Stop-Process -Name '$process' -Force -ErrorAction Stop"
                     Write-Log "Komut çalıştırılıyor: $command" "INFO"
                     if (Invoke-NSudo -Command $command) {
                         Write-Log "BAŞARILI: $process sonlandırıldı" "INFO"
                     } else {
                         Write-Log "HATA: $process sonlandırılamadı" "ERROR"
                     }
                 } else {
                     Write-Log "UYARI: $process zaten çalışmıyor" "WARNING"
                 }
             }

             # 2. Edge hizmetlerini durdur ve devre dışı bırak
             Write-Log "2. Adım: Edge hizmetleri durduruluyor..." "INFO"
             $services = @("edgeupdate", "edgeupdatem", "MicrosoftEdgeElevationService")
             foreach ($service in $services) {
                 Write-Log "Hizmet kontrol ediliyor: $service" "INFO"
                 if (Get-Service -Name $service -ErrorAction SilentlyContinue) {
                     $command = "Stop-Service -Name '$service' -Force -ErrorAction Stop; Set-Service -Name '$service' -StartupType Disabled -ErrorAction Stop"
                     Write-Log "Komut çalıştırılıyor: $command" "INFO"
                     if (Invoke-NSudo -Command $command) {
                         Write-Log "BAŞARILI: $service durduruldu ve devre dışı bırakıldı" "INFO"
                     } else {
                         Write-Log "HATA: $service durdurulamadı veya devre dışı bırakılamadı" "ERROR"
                     }
                 } else {
                     Write-Log "UYARI: $service hizmeti bulunamadı" "WARNING"
                 }
             }

             # 3. Appx paketlerini kaldır
             Write-Log "3. Adım: Appx paketleri kaldırılıyor..." "INFO"
             $packages = @("Microsoft.MicrosoftEdge", "Microsoft.MicrosoftEdgeDevToolsClient", "MicrosoftEdge")
             foreach ($package in $packages) {
                 Write-Log "Paket kontrol ediliyor: $package" "INFO"
                 try {
                     $pkgs = Get-AppxPackage -AllUsers -Name $package -ErrorAction Stop
                     if ($pkgs) {
                         foreach ($pkg in $pkgs) {
                             Write-Log "Paket kaldırılıyor: $($pkg.PackageFullName)" "INFO"
                             $command = "Remove-AppxPackage -Package '$($pkg.PackageFullName)' -AllUsers -ErrorAction Stop"
                             Write-Log "Komut çalıştırılıyor: $command" "INFO"
                             if (Invoke-NSudo -Command $command) {
                                 Write-Log "BAŞARILI: $($pkg.PackageFullName) kaldırıldı" "INFO"
                             } else {
                                 Write-Log "HATA: $($pkg.PackageFullName) kaldırılamadı" "ERROR"
                             }
                         }
                     } else {
                         Write-Log "UYARI: $package paketi bulunamadı" "WARNING"
                     }
                 } catch {
                     Write-Log "HATA: $package paketi kaldırılırken hata oluştu: $_" "ERROR"
                 }

                 try {
                     $provPkgs = Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq $package } -ErrorAction Stop
                     if ($provPkgs) {
                         foreach ($provPkg in $provPkgs) {
                             Write-Log "Provisioned paket kaldırılıyor: $($provPkg.PackageName)" "INFO"
                             $command = "Remove-AppxProvisionedPackage -Online -PackageName '$($provPkg.PackageName)' -ErrorAction Stop"
                             Write-Log "Komut çalıştırılıyor: $command" "INFO"
                             if (Invoke-NSudo -Command $command) {
                                 Write-Log "BAŞARILI: Provisioned $($provPkg.PackageName) kaldırıldı" "INFO"
                             } else {
                                 Write-Log "HATA: Provisioned $($provPkg.PackageName) kaldırılamadı" "ERROR"
                             }
                         }
                     } else {
                         Write-Log "UYARI: Provisioned $package paketi bulunamadı" "WARNING"
                     }
                 } catch {
                     Write-Log "HATA: Provisioned $package paketi kaldırılırken hata oluştu: $_" "ERROR"
                 }
             }

             # 4. Dosya sistemi temizliği
             Write-Log "4. Adım: Dosya sistemi temizliği yapılıyor..." "INFO"
             $filePaths = @(
                 "C:\Windows\SystemApps\Microsoft.MicrosoftEdge_*",
                 "C:\Program Files (x86)\Microsoft\Edge*",
                 "C:\Program Files\WindowsApps\Microsoft.MicrosoftEdge_*",
                 "$env:LOCALAPPDATA\Microsoft\Edge*",
                 "$env:PROGRAMDATA\Microsoft\Edge*"
             )
             foreach ($path in $filePaths) {
                 try {
                     if (Test-Path $path -ErrorAction Stop) {
                         Write-Log "Dosyalar siliniyor: $path" "INFO"
                         $command = "takeown /f `"$path`" /r /d y; icacls `"$path`" /grant administrators:F /t; Remove-Item -Path `"$path`" -Recurse -Force -ErrorAction Stop"
                         Write-Log "Komut çalıştırılıyor: $command" "INFO"
                         if (Invoke-NSudo -Command $command) {
                             Write-Log "BAŞARILI: $path silindi" "INFO"
                         } else {
                             Write-Log "HATA: $path silinemedi" "ERROR"
                         }
                     } else {
                         Write-Log "UYARI: Dosya yolu zaten yok: $path" "WARNING"
                     }
                 } catch {
                     Write-Log "HATA: $path silinirken hata oluştu: $_" "ERROR"
                 }
             }

             # 5. Kayıt defteri temizliği
             Write-Log "5. Adım: Kayıt defteri temizliği yapılıyor..." "INFO"
             $regActions = @(
                 @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\*Edge*"; Action = "RemoveKey" },
                 @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*Edge*"; Action = "RemoveKey" },
                 @{ Path = "HKLM:\SOFTWARE\Microsoft\EdgeUpdate"; Action = "RemoveKey" },
                 @{ Path = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate"; Action = "RemoveKey" },
                 @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Deprovisioned\*Edge*"; Action = "RemoveKey" }
             )
             Generate-RegFile -Category "EdgeRegistryCleanup" -Actions $regActions
             foreach ($action in $regActions) {
                 try {
                     Write-Log "Kayıt defteri anahtarı işleniyor: $($action.Path)" "INFO"
                     if (Test-Path $action.Path) {
                         Apply-RegistryAction -Action $action
                         Write-Log "BAŞARILI: Kayıt defteri anahtarı işlendi: $($action.Path)" "INFO"
                     } else {
                         Write-Log "UYARI: Kayıt defteri anahtarı bulunamadı: $($action.Path)" "WARNING"
                     }
                 } catch {
                     Write-Log "HATA: Kayıt defteri temizleme hatası ($($action.Path)): $_" "ERROR"
                 }
             }

             # 6. Edge’in tekrar yüklenmesini engelle
             Write-Log "6. Adım: Edge’in tekrar yüklenmesi engelleniyor..." "INFO"
             $blockReg = @(
                 @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate"; Name = "DoNotUpdateToEdgeWithChromium"; Value = 1; Type = "DWord"; Action = "Set" },
                 @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate"; Name = "InstallDefault"; Value = 0; Type = "DWord"; Action = "Set" },
                 @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate"; Name = "InstallMicrosoftEdge"; Value = 0; Type = "DWord"; Action = "Set" },
                 @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate"; Name = "UpdateDefault"; Value = 0; Type = "DWord"; Action = "Set" }
             )
             Generate-RegFile -Category "EdgeBlock" -Actions $blockReg
             foreach ($reg in $blockReg) {
                 try {
                     Write-Log "Kayıt defteri ayarı işleniyor: $($reg.Path)\$($reg.Name)" "INFO"
                     if (-not (Test-Path $reg.Path)) {
                         New-Item -Path $reg.Path -Force | Out-Null
                         Write-Log "Kayıt defteri yolu oluşturuldu: $($reg.Path)" "INFO"
                     }
                     Apply-RegistryAction -Action $reg
                     Write-Log "BAŞARILI: Kayıt defteri ayarı uygulandı: $($reg.Path)\$($reg.Name)" "INFO"
                 } catch {
                     Write-Log "HATA: $($reg.Path)\$($reg.Name) engellenirken hata oluştu: $_" "ERROR"
                 }
             }

             # 7. Appx önbelleğini temizle ve son kontrol
             Write-Log "7. Adım: Appx önbelleği temizleniyor ve son kontrol yapılıyor..." "INFO"
             try {
                 $command = "Start-Process -FilePath 'WSReset.exe' -NoNewWindow -Wait -ErrorAction Stop"
                 Write-Log "Komut çalıştırılıyor: $command" "INFO"
                 if (Invoke-NSudo -Command $command) {
                     Write-Log "BAŞARILI: Appx önbelleği temizlendi" "INFO"
                 } else {
                     Write-Log "HATA: Appx önbelleği temizlenemedi" "ERROR"
                 }
             } catch {
                 Write-Log "HATA: Appx önbelleği temizlenirken hata oluştu: $_" "ERROR"
             }

             # Son kontrol: Kalan Edge paketleri
             Write-Log "Son kontrol: Kalan Edge paketleri kontrol ediliyor..." "INFO"
             $remainingPkgs = Get-AppxPackage -AllUsers | Where-Object { $_.Name -like "*Edge*" } | Select-Object -Property Name, PackageFullName
             if ($remainingPkgs) {
                 Write-Log "UYARI: Hala mevcut Edge paketleri: $($remainingPkgs | ForEach-Object { $_.PackageFullName })" "WARNING"
             } else {
                 Write-Log "BAŞARILI: Hiçbir Edge paketi bulunamadı" "INFO"
             }

             Write-Log "Optimize-MicrosoftEdge tamamlandı" "SUCCESS"
             Write-Output "TAMAMLANDI: Microsoft Edge optimizasyonu tamamlandı"
         } catch {
             Write-Log "HATA: Optimize-MicrosoftEdge genel hatası: $_" "ERROR"
             throw $_
         }
     }

# Invoke-AutomaticOptimization fonksiyonu
function Invoke-AutomaticOptimization {
    try {
        Write-Log "Invoke-AutomaticOptimization başlatılıyor" "INFO"
        Optimize-SystemPerformance
        Optimize-BackgroundApps
        Optimize-ExplorerSettings
        Optimize-VisualEffects
        Optimize-SearchSettings
        Optimize-GameMode
        Optimize-MMCSS_Profiles
        Optimize-TelemetryAndPrivacy
        Optimize-WindowsDefender
        Optimize-WindowsUpdates
        Optimize-Updates_CompleteDisable
        Optimize-MicrosoftEdge
        Optimize-Input_Optimizations
        Optimize-MPO_Optimization
        Optimize-Network
        Optimize-SystemCleanup
        Disable-EventLogging
        Manage-MemoryOptimizations
        Manage-StorageOptimizations
        Manage-GpuOptimizations
        Manage-VirtualMemory
        Disable-SpecificDevices
        Write-Log "Invoke-AutomaticOptimization tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Otomatik optimizasyonlar uygulandı"
    } catch {
        Write-Log "Invoke-AutomaticOptimization hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-SystemCleanup fonksiyonu
function Optimize-SystemCleanup {
    try {
        Write-Log "Optimize-SystemCleanup başlatılıyor" "INFO"
        
        # Geçici dosyaları temizle
        Write-Log "Geçici dosyalar temizleniyor..." "INFO"
        $tempPaths = @(
            "$env:TEMP\*",
            "$env:SYSTEMROOT\Temp\*",
            "$env:SYSTEMROOT\Prefetch\*"
        )
        foreach ($path in $tempPaths) {
            try {
                if (Test-Path $path -ErrorAction Stop) {
                    $command = "Remove-Item -Path '$path' -Force -Recurse -ErrorAction Stop"
                    if (Invoke-NSudo -Command $command) {
                        Write-Log "Temizlendi: $path" "INFO"
                    } else {
                        Write-Log "HATA: $path temizlenemedi" "ERROR"
                    }
                } else {
                    Write-Log "Dosya yolu zaten yok: $path" "INFO"
                }
            } catch {
                Write-Log "HATA: $path temizlenirken hata oluştu: $_" "ERROR"
            }
        }

        # Çöp kutusunu temizle
        Write-Log "Çöp kutusu temizleniyor..." "INFO"
        try {
            $command = "Clear-RecycleBin -Force -ErrorAction Stop"
            if (Invoke-NSudo -Command $command) {
                Write-Log "Çöp kutusu temizlendi" "INFO"
            } else {
                Write-Log "HATA: Çöp kutusu temizlenemedi" "ERROR"
            }
        } catch {
            Write-Log "HATA: Çöp kutusu temizlenirken hata oluştu: $_" "ERROR"
        }

        # Registry temizliği
        Write-Log "Registry temizleniyor..." "INFO"
        $regActions = @(
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs"; Action = "RemoveKey" },
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU"; Action = "RemoveKey" }
        )
        Generate-RegFile -Category "SystemCleanupRegistry" -Actions $regActions
        foreach ($action in $regActions) {
            try {
                if (Test-Path $action.Path) {
                    Apply-RegistryAction -Action $action
                    Write-Log "Registry verileri temizlendi: $($action.Path)" "INFO"
                } else {
                    Write-Log "Registry yolu zaten yok: $($action.Path)" "INFO"
                }
            } catch {
                Write-Log "HATA: $($action.Path) temizlenirken hata oluştu: $_" "ERROR"
            }
        }

        # Windows Update önbelleğini temizle
        Write-Log "Windows Update önbelleği temizleniyor..." "INFO"
        try {
            $command = "Stop-Service -Name wuauserv -Force -ErrorAction Stop; Remove-Item -Path '$env:SYSTEMROOT\SoftwareDistribution' -Recurse -Force -ErrorAction Stop; Start-Service -Name wuauserv -ErrorAction Stop"
            if (Invoke-NSudo -Command $command) {
                Write-Log "Windows Update önbelleği temizlendi" "INFO"
            } else {
                Write-Log "HATA: Windows Update önbelleği temizlenemedi" "ERROR"
            }
        } catch {
            Write-Log "HATA: Windows Update önbelleği temizlenirken hata oluştu: $_" "ERROR"
        }

        # cleanmgr.exe ile Disk Temizleme çalıştır
        Write-Log "cleanmgr.exe ile Disk Temizleme başlatılıyor..." "INFO"
        try {
            $cleanMgrSettings = @(
                @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Active Setup Temp Folders"; Name = "StateFlags0001"; Value = 2; Type = "DWord"; Action = "Set" },
                @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Temporary Files"; Name = "StateFlags0001"; Value = 2; Type = "DWord"; Action = "Set" },
                @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Recycle Bin"; Name = "StateFlags0001"; Value = 2; Type = "DWord"; Action = "Set" },
                @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Windows Upgrade Log Files"; Name = "StateFlags0001"; Value = 2; Type = "DWord"; Action = "Set" },
                @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Windows Error Reporting Files"; Name = "StateFlags0001"; Value = 2; Type = "DWord"; Action = "Set" }
            )
            Generate-RegFile -Category "DiskCleanup" -Actions $cleanMgrSettings
            foreach ($action in $cleanMgrSettings) {
                try {
                    if (-not (Test-Path $action.Path)) {
                        New-Item -Path $action.Path -Force | Out-Null
                        Write-Log "Registry yolu oluşturuldu: $($action.Path)" "INFO"
                    }
                    Apply-RegistryAction -Action $action
                    Write-Log "Disk Temizleme ayarı yapılandı: $($action.Path)" "INFO"
                } catch {
                    Write-Log "HATA: Disk Temizleme ayarı yapılandırılamadı ($($action.Path)): $_" "ERROR"
                }
            }

            $command = "Start-Process -FilePath 'cleanmgr.exe' -ArgumentList '/sagerun:1' -Wait -NoNewWindow -ErrorAction Stop"
            if (Invoke-NSudo -Command $command) {
                Write-Log "cleanmgr.exe başarıyla çalıştırıldı" "INFO"
            } else {
                Write-Log "HATA: cleanmgr.exe çalıştırılamadı" "ERROR"
            }
        } catch {
            Write-Log "HATA: cleanmgr.exe çalıştırılırken hata oluştu: $_" "ERROR"
        }

        Write-Log "Optimize-SystemCleanup tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Sistem temizliği tamamlandı"
    } catch {
        Write-Log "Optimize-SystemCleanup hatası: $_" "ERROR"
        throw $_
    }
}

        # Windows Update önbelleğini temizle
        Write-Log "Windows Update önbelleği temizleniyor..." "INFO"
        try {
            $command = "Stop-Service -Name wuauserv -Force -ErrorAction SilentlyContinue; Remove-Item -Path '$env:SYSTEMROOT\SoftwareDistribution' -Recurse -Force -ErrorAction SilentlyContinue; Start-Service -Name wuauserv -ErrorAction SilentlyContinue"
            if (Invoke-NSudo -Command $command) {
                Write-Log "Windows Update önbelleği temizlendi" "INFO"
            } else {
                Write-Log "HATA: Windows Update önbelleği temizlenemedi" "ERROR"
            }
        } catch {
            Write-Log "Hata: Windows Update önbelleği temizlenemedi, Hata: $_" "ERROR"
        }

        # cleanmgr.exe ile Disk Temizleme çalıştır
        Write-Log "cleanmgr.exe ile Disk Temizleme başlatılıyor..." "INFO"
        try {
            $cleanMgrSettings = @{
                Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
                Actions = @(
                    @{ Name = "Active Setup Temp Folders"; Value = 2; Type = "DWord"; Action = "Set" },
                    @{ Name = "Temporary Files"; Value = 2; Type = "DWord"; Action = "Set" },
                    @{ Name = "Recycle Bin"; Value = 2; Type = "DWord"; Action = "Set" },
                    @{ Name = "Windows Upgrade Log Files"; Value = 2; Type = "DWord"; Action = "Set" },
                    @{ Name = "Windows Error Reporting Files"; Value = 2; Type = "DWord"; Action = "Set" }
                )
            }
            foreach ($action in $cleanMgrSettings.Actions) {
                $fullPath = "$($cleanMgrSettings.Path)\$($action.Name)"
                if (-not (Test-Path $fullPath)) {
                    New-Item -Path $fullPath -Force | Out-Null
                }
                Set-ItemProperty -Path $fullPath -Name "StateFlags0001" -Value $action.Value -Type $action.Type -Force
                Write-Log "Disk Temizleme ayarı yapılandı: $fullPath" "INFO"
            }

            Start-Process -FilePath "cleanmgr.exe" -ArgumentList "/sagerun:1" -Wait -NoNewWindow
            Write-Log "cleanmgr.exe başarıyla çalıştırıldı" "INFO"
        } catch {
            Write-Log "Hata: cleanmgr.exe çalıştırılamadı, Hata: $_" "ERROR"
        }

        Write-Log "Optimize-SystemCleanup tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Sistem temizliği tamamlandı"
    } catch {
        Write-Log "Optimize-SystemCleanup hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-Network fonksiyonu
function Optimize-Network {
    try {
        Write-Log "Optimize-Network başlatılıyor" "INFO"
        $commands = @(
            "netsh int tcp set global autotuninglevel=disabled",
            "netsh int tcp set global rss=enabled"
        )
        foreach ($cmd in $commands) {
            if (Invoke-NSudo -Command $cmd) {
                Write-Log "Komut başarıyla çalıştırıldı: $cmd" "INFO"
            } else {
                Write-Log "HATA: Komut çalıştırılamadı: $cmd" "ERROR"
            }
        }
        Write-Log "Optimize-Network tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Ağ ayarları optimize edildi"
    } catch {
        Write-Log "Optimize-Network hatası: $_" "ERROR"
        throw $_
    }
}

# Disable-EventLogging fonksiyonu
function Disable-EventLogging {
    try {
        Write-Log "Disable-EventLogging başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\WMI\Autologger\EventLog-System"; Name = "Start"; Value = 0; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\WMI\Autologger\EventLog-Application"; Name = "Start"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "EventLogging" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Disable-EventLogging tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Olay günlükleri devre dışı bırakıldı"
    } catch {
        Write-Log "Disable-EventLogging hatası: $_" "ERROR"
        throw $_
    }
}

# Manage-MemoryOptimizations fonksiyonu
function Manage-MemoryOptimizations {
    try {
        Write-Log "Manage-MemoryOptimizations başlatılıyor" "INFO"
        Optimize-MemoryLow
        Optimize-MemoryHigh
        Write-Log "Manage-MemoryOptimizations tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Bellek optimizasyonları uygulandı"
    } catch {
        Write-Log "Manage-MemoryOptimizations hatası: $_" "ERROR"
        throw $_
    }
}

# Manage-StorageOptimizations fonksiyonu
function Manage-StorageOptimizations {
    try {
        Write-Log "Manage-StorageOptimizations başlatılıyor" "INFO"
        Optimize-StorageHdd
        Optimize-StorageSsd
        Write-Log "Manage-StorageOptimizations tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Depolama optimizasyonları uygulandı"
    } catch {
        Write-Log "Manage-StorageOptimizations hatası: $_" "ERROR"
        throw $_
    }
}

# Manage-GpuOptimizations fonksiyonu
function Manage-GpuOptimizations {
    try {
        Write-Log "Manage-GpuOptimizations başlatılıyor" "INFO"
        Optimize-GpuNvidia
        Optimize-GpuAmd
        Optimize-GpuIntel
        Write-Log "Manage-GpuOptimizations tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: GPU optimizasyonları uygulandı"
    } catch {
        Write-Log "Manage-GpuOptimizations hatası: $_" "ERROR"
        throw $_
    }
}

# Manage-VirtualMemory fonksiyonu
function Manage-VirtualMemory {
    try {
        Write-Log "Manage-VirtualMemory başlatılıyor" "INFO"
        $ramSizeMB = [math]::Round((Get-CimInstance -ClassName Win32_ComputerSystem).TotalPhysicalMemory / 1MB)
        $pagefileSizeMB = [math]::Round($ramSizeMB * 1.5)
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"; Name = "PagingFiles"; Value = "C:\pagefile.sys $pagefileSizeMB $pagefileSizeMB"; Type = "MultiString"; Action = "Set" }
        )
        Generate-RegFile -Category "VirtualMemory" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Manage-VirtualMemory tamamlandı (Pagefile: $pagefileSizeMB MB)" "SUCCESS"
        Write-Output "TAMAMLANDI: Sanal bellek $pagefileSizeMB MB olarak ayarlandı"
    } catch {
        Write-Log "Manage-VirtualMemory hatası: $_" "ERROR"
        throw $_
    }
}

# Disable-SpecificDevices fonksiyonu
function Disable-SpecificDevices {
    try {
        Write-Log "Disable-SpecificDevices başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\xboxgip"; Name = "Start"; Value = 4; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\XblAuthManager"; Name = "Start"; Value = 4; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "SpecificDevices" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Disable-SpecificDevices tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Belirli cihazlar devre dışı bırakıldı"
    } catch {
        Write-Log "Disable-SpecificDevices hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-TelemetryAndPrivacy fonksiyonu
function Optimize-TelemetryAndPrivacy {
    try {
        Write-Log "Optimize-TelemetryAndPrivacy başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection"; Name = "AllowTelemetry"; Value = 0; Type = "DWord"; Action = "Set" },
            @{ Path = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"; Name = "SubscribedContent-338393Enabled"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "TelemetryAndPrivacy" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-TelemetryAndPrivacy tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Telemetri ve gizlilik ayarları devre dışı bırakıldı"
    } catch {
        Write-Log "Optimize-TelemetryAndPrivacy hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-WindowsDefender fonksiyonu
function Optimize-WindowsDefender {
    try {
        Write-Log "Optimize-WindowsDefender başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender"; Name = "DisableAntiSpyware"; Value = 1; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender"; Name = "DisableRealtimeMonitoring"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "WindowsDefender" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-WindowsDefender tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Windows Defender devre dışı bırakıldı"
    } catch {
        Write-Log "Optimize-WindowsDefender hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-WindowsUpdates fonksiyonu
function Optimize-WindowsUpdates {
    try {
        Write-Log "Optimize-WindowsUpdates başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"; Name = "NoAutoUpdate"; Value = 1; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"; Name = "AUOptions"; Value = 2; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "WindowsUpdates" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-WindowsUpdates tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Windows Güncellemeleri manuel moda alındı"
    } catch {
        Write-Log "Optimize-WindowsUpdates hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-Updates_CompleteDisable fonksiyonu
function Optimize-Updates_CompleteDisable {
    try {
        Write-Log "Optimize-Updates_CompleteDisable başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate"; Name = "DoNotConnectToWindowsUpdateInternetLocations"; Value = 1; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\wuauserv"; Name = "Start"; Value = 4; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "Updates_CompleteDisable" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-Updates_CompleteDisable tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Windows Update tamamen devre dışı bırakıldı"
    } catch {
        Write-Log "Optimize-Updates_CompleteDisable hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-SystemPerformance fonksiyonu
function Optimize-SystemPerformance {
    try {
        Write-Log "Optimize-SystemPerformance başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"; Name = "DisablePagingExecutive"; Value = 1; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl"; Name = "Win32PrioritySeparation"; Value = 38; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "SystemPerformance" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-SystemPerformance tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Sistem performansı optimize edildi"
    } catch {
        Write-Log "Optimize-SystemPerformance hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-MemoryLow fonksiyonu
function Optimize-MemoryLow {
    try {
        Write-Log "Optimize-MemoryLow başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"; Name = "LargeSystemCache"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "MemoryLow" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-MemoryLow tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Düşük bellek optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-MemoryLow hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-MemoryHigh fonksiyonu
function Optimize-MemoryHigh {
    try {
        Write-Log "Optimize-MemoryHigh başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"; Name = "LargeSystemCache"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "MemoryHigh" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-MemoryHigh tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Yüksek bellek optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-MemoryHigh hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-StorageHdd fonksiyonu
function Optimize-StorageHdd {
    try {
        Write-Log "Optimize-StorageHdd başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\SysMain"; Name = "Start"; Value = 3; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "StorageHdd" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-StorageHdd tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: HDD optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-StorageHdd hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-StorageSsd fonksiyonu
function Optimize-StorageSsd {
    try {
        Write-Log "Optimize-StorageSsd başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\SysMain"; Name = "Start"; Value = 4; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Storage"; Name = "EnableDefrag"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "StorageSsd" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-StorageSsd tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: SSD optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-StorageSsd hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-GpuNvidia fonksiyonu
function Optimize-GpuNvidia {
    try {
        Write-Log "Optimize-GpuNvidia başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\NVIDIA Corporation\Global\NVTweak"; Name = "PowerMizerEnable"; Value = 0; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"; Name = "EnableMclkSlowdown"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "GpuNvidia" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-GpuNvidia tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: NVIDIA GPU optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-GpuNvidia hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-GpuAmd fonksiyonu (Düzeltildi)
function Optimize-GpuAmd {
    try {
        Write-Log "Optimize-GpuAmd başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\AMD\CN"; Name = "PowerSaving"; Value = 0; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"; Name = "DisableDynamicGpuPower"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "GpuAmd" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-GpuAmd tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: AMD GPU optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-GpuAmd hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-GpuIntel fonksiyonu
function Optimize-GpuIntel {
    try {
        Write-Log "Optimize-GpuIntel başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Intel\GMM"; Name = "EnablePowerSaving"; Value = 0; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"; Name = "DisablePowerSaving"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "GpuIntel" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-GpuIntel tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Intel GPU optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-GpuIntel hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-ExplorerSettings fonksiyonu
function Optimize-ExplorerSettings {
    try {
        Write-Log "Optimize-ExplorerSettings başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"; Name = "Hidden"; Value = 1; Type = "DWord"; Action = "Set" },
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"; Name = "HideFileExt"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "ExplorerSettings" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-ExplorerSettings tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Dosya Gezgini ayarları optimize edildi"
    } catch {
        Write-Log "Optimize-ExplorerSettings hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-AdvancedNetworkTweaks fonksiyonu
function Optimize-AdvancedNetworkTweaks {
    param (
        [string]$DnsOption
    )
    try {
        Write-Log "Optimize-AdvancedNetworkTweaks başlatılıyor (DNS: $DnsOption)" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"; Name = "DisableTaskOffload"; Value = 1; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"; Name = "EnableTCPA"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "AdvancedNetworkTweaks" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        if ($DnsOption -eq "Google") {
            Write-Log "Google DNS (8.8.8.8, 8.8.4.4) uygulanıyor" "INFO"
            if (Invoke-NSudo -Command "netsh interface ip set dns name='Ethernet' source=static address=8.8.8.8") {
                Write-Log "Google DNS (8.8.8.8) ayarlandı" "INFO"
            }
            if (Invoke-NSudo -Command "netsh interface ip add dns name='Ethernet' address=8.8.4.4 index=2") {
                Write-Log "Google DNS (8.8.4.4) eklendi" "INFO"
            }
        } elseif ($DnsOption -eq "Cloudflare") {
            Write-Log "Cloudflare DNS (1.1.1.1, 1.0.0.1) uygulanıyor" "INFO"
            if (Invoke-NSudo -Command "netsh interface ip set dns name='Ethernet' source=static address=1.1.1.1") {
                Write-Log "Cloudflare DNS (1.1.1.1) ayarlandı" "INFO"
            }
            if (Invoke-NSudo -Command "netsh interface ip add dns name='Ethernet' address=1.0.0.1 index=2") {
                Write-Log "Cloudflare DNS (1.0.0.1) eklendi" "INFO"
            }
        } else {
            Write-Log "Otomatik DNS (DHCP) uygulanıyor" "INFO"
            if (Invoke-NSudo -Command "netsh interface ip set dns name='Ethernet' source=dhcp") {
                Write-Log "Otomatik DNS ayarlandı" "INFO"
            }
        }
        Write-Log "Optimize-AdvancedNetworkTweaks tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Gelişmiş ağ optimizasyonları uygulandı"
    } catch {
        Write-Log "Optimize-AdvancedNetworkTweaks hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-SearchSettings fonksiyonu
function Optimize-SearchSettings {
    try {
        Write-Log "Optimize-SearchSettings başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Search"; Name = "SearchboxTaskbarMode"; Value = 0; Type = "DWord"; Action = "Set" },
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Search"; Name = "BingSearchEnabled"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "SearchSettings" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-SearchSettings tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Arama ayarları optimize edildi"
    } catch {
        Write-Log "Optimize-SearchSettings hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-Input_Optimizations fonksiyonu
function Optimize-Input_Optimizations {
    try {
        Write-Log "Optimize-Input_Optimizations başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Control Panel\Mouse"; Name = "MouseSensitivity"; Value = 10; Type = "DWord"; Action = "Set" },
            @{ Path = "HKCU:\Control Panel\Keyboard"; Name = "KeyboardDelay"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "Input_Optimizations" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-Input_Optimizations tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Giriş aygıtı optimizasyonları uygulandı"
    } catch {
        Write-Log "Optimize-Input_Optimizations hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-MMCSS_Profiles fonksiyonu
function Optimize-MMCSS_Profiles {
    try {
        Write-Log "Optimize-MMCSS_Profiles başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"; Name = "SystemResponsiveness"; Value = 0; Type = "DWord"; Action = "Set" },
            @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"; Name = "Priority"; Value = 6; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "MMCSS_Profiles" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-MMCSS_Profiles tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: MMCSS profilleri optimize edildi"
    } catch {
        Write-Log "Optimize-MMCSS_Profiles hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-GameMode fonksiyonu
function Optimize-GameMode {
    try {
        Write-Log "Optimize-GameMode başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\GameBar"; Name = "AllowAutoGameMode"; Value = 0; Type = "DWord"; Action = "Set" },
            @{ Path = "HKCU:\Software\Microsoft\GameBar"; Name = "AutoGameModeEnabled"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "GameMode" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-GameMode tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Oyun modu optimize edildi"
    } catch {
        Write-Log "Optimize-GameMode hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-CoreIsolation fonksiyonu
function Optimize-CoreIsolation {
    try {
        Write-Log "Optimize-CoreIsolation başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"; Name = "Enabled"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "CoreIsolation" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-CoreIsolation tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Çekirdek yalıtımı devre dışı bırakıldı"
    } catch {
        Write-Log "Optimize-CoreIsolation hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-BackgroundApps fonksiyonu
function Optimize-BackgroundApps {
    try {
        Write-Log "Optimize-BackgroundApps başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications"; Name = "GlobalUserDisabled"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "BackgroundApps" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-BackgroundApps tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Arka plan uygulamaları devre dışı bırakıldı"
    } catch {
        Write-Log "Optimize-BackgroundApps hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-MPO_Optimization fonksiyonu
function Optimize-MPO_Optimization {
    try {
        Write-Log "Optimize-MPO_Optimization başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\Dwm"; Name = "OverlayTestMode"; Value = 5; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "MPO_Optimization" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-MPO_Optimization tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: MPO optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-MPO_Optimization hatası: $_" "ERROR"
        throw $_
    }
}

# Optimize-VisualEffects fonksiyonu
function Optimize-VisualEffects {
    try {
        Write-Log "Optimize-VisualEffects başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"; Name = "VisualFXSetting"; Value = 2; Type = "DWord"; Action = "Set" },
            @{ Path = "HKCU:\Control Panel\Desktop"; Name = "UserPreferencesMask"; Value = ([byte[]](0x90,0x12,0x03,0x80,0x10,0x00,0x00,0x00)); Type = "Binary"; Action = "Set" }
        )
        Generate-RegFile -Category "VisualEffects" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-VisualEffects tamamlandı" "SUCCESS"
        Write-Output "TAMAMLANDI: Görsel efektler optimize edildi"
    } catch {
        Write-Log "Optimize-VisualEffects hatası: $_" "ERROR"
        throw $_
    }
}

Export-ModuleMember -Function Optimize-*, Invoke-*, Disable-*, Manage-*, Generate-RegFile, Apply-RegistryAction