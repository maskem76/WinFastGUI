function Write-Log {
    param (
        [string]$Message,
        [string]$Level = "INFO"
    )

    $logFilePath = "C:\Temp\WinFast_PS_Log.txt"
    
    if (-not (Test-Path "C:\Temp" -PathType Container)) {
        try {
            New-Item -Path "C:\Temp" -ItemType Directory -Force -ErrorAction Stop | Out-Null
        } catch {}
    }

    $logLine = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    
    try {
        Add-Content -Path $logFilePath -Value $logLine -Encoding UTF8 -Force
    } catch {}

    Write-Output $logLine
}

function RunAsAdmin {
    param (
        [string]$Command
    )
    Write-Log "RunAsAdmin çalıştırılıyor: $Command" "INFO"
    try {
        $tempPath = [System.IO.Path]::GetTempPath()
        $stdoutFile = Join-Path -Path $tempPath -ChildPath "stdout.txt"
        $stderrFile = Join-Path -Path $tempPath -ChildPath "stderr.txt"

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "powershell.exe"
        $psi.Arguments = "-NoProfile -Command `"$Command`""
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
        $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

        $process = [System.Diagnostics.Process]::Start($psi)
        $output = $process.StandardOutput.ReadToEnd()
        $errorOutput = $process.StandardError.ReadToEnd()
        $process.WaitForExit()

        if ($output) {
            foreach ($line in ($output -split "`r`n")) {
                if ($line -match "\[BILGI\]|\[BASARILI\]|\[UYARI\]|\[HATA\]") {
                    if ($line -match "\[HATA\]") {
                        Write-Log $line "ERROR"
                    } else {
                        Write-Log $line "INFO"
                    }
                }
            }
        }
        if ($errorOutput) {
            Write-Log "Hata: $errorOutput" "ERROR"
        }
        if ($process.ExitCode -ne 0) {
            Write-Log "Komut başarısız oldu (ExitCode: $($process.ExitCode)): $Command" "ERROR"
            throw "Komut başarısız: $Command"
        }
        Write-Log "Komut başarıyla çalıştırıldı: $Command" "INFO"
    } catch {
        Write-Log "RunAsAdmin hatası: $_" "ERROR"
        throw $_
    }
}

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
        Set-Content -Path $regFilePath -Value $regContent -Encoding Unicode
        Write-Log ".reg dosyası oluşturuldu: $regFilePath" "INFO"
        Write-Output ".reg dosyası oluşturuldu: $regFilePath"
    } catch {
        Write-Log ".reg dosyası oluşturma hatası ($Category): $_" "ERROR"
        throw $_
    }
}

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
                Remove-Item -Path $path -Force -Recurse
                Write-Log "Registry anahtarı kaldırıldı: $path" "INFO"
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
        Optimize-CoreIsolation
        Optimize-WindowsUpdates
        Optimize-Updates_CompleteDisable
        Optimize-MicrosoftEdge
        Optimize-Input_Optimizations
        Optimize-MPO_Optimization
        Optimize-Network
        Optimize-GeneralCleanup
        Optimize-CleanRegistry
        Disable-EventLogging
        Manage-MemoryOptimizations
        Manage-StorageOptimizations
        Manage-GpuOptimizations
        Manage-VirtualMemory
        Disable-SpecificDevices
        Write-Log "Invoke-AutomaticOptimization tamamlandı" "SUCCESS"
        Write-Output "Invoke-AutomaticOptimization tamamlandı"
    } catch {
        Write-Log "Invoke-AutomaticOptimization hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-GeneralCleanup {
    try {
        Write-Log "Optimize-GeneralCleanup başlatılıyor" "INFO"
        Remove-Item -Path "$env:TEMP\*" -Force -Recurse -ErrorAction SilentlyContinue
        Remove-Item -Path "C:\Windows\Temp\*" -Force -Recurse -ErrorAction SilentlyContinue
        Start-Process -FilePath "cleanmgr.exe" -ArgumentList "/sagerun:1" -Wait -NoNewWindow
        Write-Log "Optimize-GeneralCleanup tamamlandı" "SUCCESS"
        Write-Output "Optimize-GeneralCleanup tamamlandı, geçici dosyalar temizlendi"
    } catch {
        Write-Log "Optimize-GeneralCleanup hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-CleanRegistry {
    try {
        Write-Log "Optimize-CleanRegistry başlatılıyor" "INFO"
        # Yer tutucu yerine basit bir registry temizleme komutu eklendi
        $regPaths = @("HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU")
        foreach ($path in $regPaths) {
            if (Test-Path $path) {
                Remove-Item -Path $path -Force -Recurse -ErrorAction SilentlyContinue
                Write-Log "Eski registry verileri temizlendi: $path" "INFO"
            }
        }
        Write-Log "Optimize-CleanRegistry tamamlandı" "SUCCESS"
        Write-Output "Optimize-CleanRegistry tamamlandı, eski registry verileri temizlendi"
    } catch {
        Write-Log "Optimize-CleanRegistry hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-Network {
    try {
        Write-Log "Optimize-Network başlatılıyor" "INFO"
        netsh int tcp set global autotuninglevel=disabled
        netsh int tcp set global rss=enabled
        Write-Log "Optimize-Network tamamlandı" "SUCCESS"
        Write-Output "Optimize-Network tamamlandı, ağ ayarları optimize edildi"
    } catch {
        Write-Log "Optimize-Network hatası: $_" "ERROR"
        throw $_
    }
}

function Show-CS2Recommendations {
    try {
        Write-Log "Show-CS2Recommendations başlatılıyor" "INFO"
        Write-Log "CS2 için öneriler: -novid -nojoy -high -freq 240 -tickrate 128" "INFO"
        Write-Output "CS2 için öneriler: -novid -nojoy -high -freq 240 -tickrate 128"
        Write-Log "Show-CS2Recommendations tamamlandı" "SUCCESS"
    } catch {
        Write-Log "Show-CS2Recommendations hatası: $_" "ERROR"
        throw $_
    }
}

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
        Write-Output "Disable-EventLogging tamamlandı, olay günlüğü devre dışı bırakıldı"
    } catch {
        Write-Log "Disable-EventLogging hatası: $_" "ERROR"
        throw $_
    }
}

function Manage-MemoryOptimizations {
    try {
        Write-Log "Manage-MemoryOptimizations başlatılıyor" "INFO"
        Optimize-MemoryLow
        Optimize-MemoryHigh
        Write-Log "Manage-MemoryOptimizations tamamlandı" "SUCCESS"
        Write-Output "Manage-MemoryOptimizations tamamlandı"
    } catch {
        Write-Log "Manage-MemoryOptimizations hatası: $_" "ERROR"
        throw $_
    }
}

function Manage-StorageOptimizations {
    try {
        Write-Log "Manage-StorageOptimizations başlatılıyor" "INFO"
        Optimize-StorageHdd
        Optimize-StorageSsd
        Write-Log "Manage-StorageOptimizations tamamlandı" "SUCCESS"
        Write-Output "Manage-StorageOptimizations tamamlandı"
    } catch {
        Write-Log "Manage-StorageOptimizations hatası: $_" "ERROR"
        throw $_
    }
}

function Manage-GpuOptimizations {
    try {
        Write-Log "Manage-GpuOptimizations başlatılıyor" "INFO"
        Optimize-GpuNvidia
        Optimize-GpuAmd
        Optimize-GpuIntel
        Write-Log "Manage-GpuOptimizations tamamlandı" "SUCCESS"
        Write-Output "Manage-GpuOptimizations tamamlandı"
    } catch {
        Write-Log "Manage-GpuOptimizations hatası: $_" "ERROR"
        throw $_
    }
}

function Manage-VirtualMemory {
    try {
        Write-Log "Manage-VirtualMemory başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"; Name = "PagingFiles"; Value = "C:\pagefile.sys 4096 4096"; Type = "MultiString"; Action = "Set" }
        )
        Generate-RegFile -Category "VirtualMemory" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Manage-VirtualMemory tamamlandı" "SUCCESS"
        Write-Output "Manage-VirtualMemory tamamlandı, sanal bellek ayarlandı"
    } catch {
        Write-Log "Manage-VirtualMemory hatası: $_" "ERROR"
        throw $_
    }
}

function Disable-SpecificDevices {
    try {
        Write-Log "Disable-SpecificDevices başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\xboxgip"; Name = "Start"; Value = 4; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "SpecificDevices" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Disable-SpecificDevices tamamlandı" "SUCCESS"
        Write-Output "Disable-SpecificDevices tamamlandı, belirli cihazlar devre dışı bırakıldı"
    } catch {
        Write-Log "Disable-SpecificDevices hatası: $_" "ERROR"
        throw $_
    }
}

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
        Write-Output "Optimize-TelemetryAndPrivacy tamamlandı, telemetri devre dışı bırakıldı"
    } catch {
        Write-Log "Optimize-TelemetryAndPrivacy hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-WindowsDefender {
    try {
        Write-Log "Optimize-WindowsDefender başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender"; Name = "DisableAntiSpyware"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "WindowsDefender" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-WindowsDefender tamamlandı" "SUCCESS"
        Write-Output "Optimize-WindowsDefender tamamlandı, savunucu devre dışı bırakıldı"
    } catch {
        Write-Log "Optimize-WindowsDefender hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-MicrosoftEdge {
    try {
        Write-Log "Optimize-MicrosoftEdge başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Edge"; Name = "StartupBoostEnabled"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "MicrosoftEdge" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-MicrosoftEdge tamamlandı" "SUCCESS"
        Write-Output "Optimize-MicrosoftEdge tamamlandı, başlatma optimizasyonu devre dışı"
    } catch {
        Write-Log "Optimize-MicrosoftEdge hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-WindowsUpdates {
    try {
        Write-Log "Optimize-WindowsUpdates başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"; Name = "NoAutoUpdate"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "WindowsUpdates" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-WindowsUpdates tamamlandı" "SUCCESS"
        Write-Output "Optimize-WindowsUpdates tamamlandı, otomatik güncelleme devre dışı"
    } catch {
        Write-Log "Optimize-WindowsUpdates hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-Updates_CompleteDisable {
    try {
        Write-Log "Optimize-Updates_CompleteDisable başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate"; Name = "DoNotConnectToWindowsUpdateInternetLocations"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "Updates_CompleteDisable" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-Updates_CompleteDisable tamamlandı" "SUCCESS"
        Write-Output "Optimize-Updates_CompleteDisable tamamlandı, güncellemeler tamamen devre dışı"
    } catch {
        Write-Log "Optimize-Updates_CompleteDisable hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-SystemPerformance {
    try {
        Write-Log "Optimize-SystemPerformance başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"; Name = "DisablePagingExecutive"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "SystemPerformance" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-SystemPerformance tamamlandı" "SUCCESS"
        Write-Output "Optimize-SystemPerformance çalıştı, registry değiştirildi: [Memory Management]"
    } catch {
        Write-Log "Optimize-SystemPerformance hatası: $_" "ERROR"
        throw $_
    }
}

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
        Write-Output "Optimize-MemoryLow tamamlandı, düşük bellek optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-MemoryLow hatası: $_" "ERROR"
        throw $_
    }
}

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
        Write-Output "Optimize-MemoryHigh tamamlandı, yüksek bellek optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-MemoryHigh hatası: $_" "ERROR"
        throw $_
    }
}

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
        Write-Output "Optimize-StorageHdd tamamlandı, HDD optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-StorageHdd hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-StorageSsd {
    try {
        Write-Log "Optimize-StorageSsd başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\SysMain"; Name = "Start"; Value = 4; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "StorageSsd" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-StorageSsd tamamlandı" "SUCCESS"
        Write-Output "Optimize-StorageSsd tamamlandı, SSD optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-StorageSsd hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-GpuNvidia {
    try {
        Write-Log "Optimize-GpuNvidia başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\NVIDIA Corporation\Global\NVTweak"; Name = "PowerMizerEnable"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "GpuNvidia" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-GpuNvidia tamamlandı" "SUCCESS"
        Write-Output "Optimize-GpuNvidia tamamlandı, NVIDIA optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-GpuNvidia hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-GpuAmd {
    try {
        Write-Log "Optimize-GpuAmd başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\AMD\CN"; Name = "PowerSaving"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "GpuAmd" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-GpuAmd tamamlandı" "SUCCESS"
        Write-Output "Optimize-GpuAmd tamamlandı, AMD optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-GpuAmd hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-GpuIntel {
    try {
        Write-Log "Optimize-GpuIntel başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Intel\GMM"; Name = "EnablePowerSaving"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "GpuIntel" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-GpuIntel tamamlandı" "SUCCESS"
        Write-Output "Optimize-GpuIntel tamamlandı, Intel optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-GpuIntel hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-ExplorerSettings {
    try {
        Write-Log "Optimize-ExplorerSettings başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"; Name = "Hidden"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "ExplorerSettings" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-ExplorerSettings tamamlandı" "SUCCESS"
        Write-Output "Optimize-ExplorerSettings tamamlandı, explorer ayarları optimize edildi"
    } catch {
        Write-Log "Optimize-ExplorerSettings hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-AdvancedNetworkTweaks {
    param (
        [string]$DnsOption
    )
    try {
        Write-Log "Optimize-AdvancedNetworkTweaks başlatılıyor (DNS: $DnsOption)" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"; Name = "DisableTaskOffload"; Value = 1; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "AdvancedNetworkTweaks" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        if ($DnsOption -eq "Google") {
            Write-Log "Google DNS (8.8.8.8, 8.8.4.4) uygulanıyor" "INFO"
            netsh interface ip set dns name="Ethernet" source=static address=8.8.8.8
            netsh interface ip add dns name="Ethernet" address=8.8.4.4 index=2
        } elseif ($DnsOption -eq "Cloudflare") {
            Write-Log "Cloudflare DNS (1.1.1.1, 1.0.0.1) uygulanıyor" "INFO"
            netsh interface ip set dns name="Ethernet" source=static address=1.1.1.1
            netsh interface ip add dns name="Ethernet" address=1.0.0.1 index=2
        } else {
            Write-Log "Otomatik DNS (DHCP) uygulanıyor" "INFO"
            netsh interface ip set dns name="Ethernet" source=dhcp
        }
        Write-Log "Optimize-AdvancedNetworkTweaks tamamlandı" "SUCCESS"
        Write-Output "Optimize-AdvancedNetworkTweaks tamamlandı, ağ optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-AdvancedNetworkTweaks hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-SearchSettings {
    try {
        Write-Log "Optimize-SearchSettings başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Search"; Name = "SearchboxTaskbarMode"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "SearchSettings" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-SearchSettings tamamlandı" "SUCCESS"
        Write-Output "Optimize-SearchSettings tamamlandı, arama ayarları optimize edildi"
    } catch {
        Write-Log "Optimize-SearchSettings hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-Input_Optimizations {
    try {
        Write-Log "Optimize-Input_Optimizations başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Control Panel\Mouse"; Name = "MouseSensitivity"; Value = 10; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "Input_Optimizations" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-Input_Optimizations tamamlandı" "SUCCESS"
        Write-Output "Optimize-Input_Optimizations tamamlandı, giriş optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-Input_Optimizations hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-MMCSS_Profiles {
    try {
        Write-Log "Optimize-MMCSS_Profiles başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"; Name = "SystemResponsiveness"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "MMCSS_Profiles" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-MMCSS_Profiles tamamlandı" "SUCCESS"
        Write-Output "Optimize-MMCSS_Profiles tamamlandı, MMCSS ayarları optimize edildi"
    } catch {
        Write-Log "Optimize-MMCSS_Profiles hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-GameMode {
    try {
        Write-Log "Optimize-GameMode başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\GameBar"; Name = "AllowAutoGameMode"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "GameMode" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-GameMode tamamlandı" "SUCCESS"
        Write-Output "Optimize-GameMode tamamlandı, oyun modu optimize edildi"
    } catch {
        Write-Log "Optimize-GameMode hatası: $_" "ERROR"
        throw $_
    }
}

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
        Write-Output "Optimize-CoreIsolation tamamlandı, çekirdek yalıtımı devre dışı"
    } catch {
        Write-Log "Optimize-CoreIsolation hatası: $_" "ERROR"
        throw $_
    }
}

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
        Write-Output "Optimize-BackgroundApps tamamlandı, arka plan uygulamalar devre dışı"
    } catch {
        Write-Log "Optimize-BackgroundApps hatası: $_" "ERROR"
        throw $_
    }
}

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
        Write-Output "Optimize-MPO_Optimization tamamlandı, MPO optimizasyonu uygulandı"
    } catch {
        Write-Log "Optimize-MPO_Optimization hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-VisualEffects {
    try {
        Write-Log "Optimize-VisualEffects başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"; Name = "VisualFXSetting"; Value = 2; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "VisualEffects" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-VisualEffects tamamlandı" "SUCCESS"
        Write-Output "Optimize-VisualEffects tamamlandı, görsel efektler optimize edildi"
    } catch {
        Write-Log "Optimize-VisualEffects hatası: $_" "ERROR"
        throw $_
    }
}

function Optimize-LegacyComponents_Remove {
    try {
        Write-Log "Optimize-LegacyComponents_Remove başlatılıyor" "INFO"
        $actions = @(
            @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"; Name = "LegacyComponents"; Value = 0; Type = "DWord"; Action = "Set" }
        )
        Generate-RegFile -Category "LegacyComponents_Remove" -Actions $actions
        foreach ($action in $actions) {
            Apply-RegistryAction -Action $action
        }
        Write-Log "Optimize-LegacyComponents_Remove tamamlandı" "SUCCESS"
        Write-Output "Optimize-LegacyComponents_Remove tamamlandı, eski bileşenler devre dışı"
    } catch {
        Write-Log "Optimize-LegacyComponents_Remove hatası: $_" "ERROR"
        throw $_
    }
}

Export-ModuleMember -Function Optimize-*, Invoke-*, Disable-*, Manage-*, Show-*