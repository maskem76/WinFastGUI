#Requires -RunAsAdministrator
param (
    [Parameter(Mandatory=$true)]
    [ValidateSet(
        "Disable-Telemetry",
        "Optimize-MicrosoftEdge", 
        "Optimize-SystemPerformance",
        "Optimize-TelemetryAndPrivacy",
        "Restore-MicrosoftEdge",
        "Test",
        "Optimize-SearchSettings",
        "Optimize-ExplorerSettings", 
        "Optimize-AdvancedNetworkTweaks",
        "Optimize-BackgroundApps",
        "Optimize-VisualEffects",
        "Optimize-GameMode",
        "Optimize-MMCSS_Profiles",
        "Optimize-Input_Optimizations",
        "Disable-WindowsDefender",
        "Optimize-StartupPrograms",
        "Optimize-Services",
        "Optimize-RegistryTweaks",
        "Optimize-PowerSettings", 
        "Optimize-GPU_Settings",
        "Optimize-RAM_Management",
        "Optimize-Storage_Performance",
        "Optimize-Audio_Settings",
        "Disable-Cortana",
        "Optimize-Privacy_Settings",
        "Optimize-Update_Settings",
        "Optimize-Firewall_Settings",
        "Optimize-Display_Settings",
        "Optimize-USB_Performance",
        "Optimize-CPU_Performance",
        "Manage-VirtualMemory",
        "Optimize-DiskCleanup",
        "Optimize-Updates_CompleteDisable",
        "Optimize-WindowsDefender",
        "Optimize-CoreIsolation",
        "Optimize-MPO_Optimization",
        "Invoke-AutomaticOptimization",
        "Optimize-GeneralCleanup",
        "Optimize-CleanRegistry",
        "Optimize-Network",
        "Show-CS2Recommendations",
        "Disable-EventLogging",
        "Manage-MemoryOptimizations",
        "Manage-StorageOptimizations",
        "Manage-GpuOptimizations",
        "Disable-SpecificDevices",
        "Optimize-WindowsUpdates"
    )]
    [string]$OptimizationCommand,
    [Parameter(Mandatory=$false)]
    [string]$ExecutingScriptPath 
)

# JSON dosyasını oku
$jsonPath = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath "..\..\..\..") -ChildPath "Data\optimization.json"
if (-not (Test-Path $jsonPath)) {
    Write-Error "optimization.json dosyası bulunamadı: $jsonPath"
    exit 1
}
$optimizations = Get-Content -Path $jsonPath -Raw | ConvertFrom-Json

# LOG FONKSİYONU
function Write-Log {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,
        [ValidateSet("INFO", "SUCCESS", "WARNING", "ERROR")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp][$Level] $Message"
    $logPath = "$env:LOCALAPPDATA\Temp\WinFastGUI.log"
    Write-Host $logEntry
    Add-Content -Path $logPath -Value $logEntry -ErrorAction SilentlyContinue
}

# Helper Functions
function Set-RegistryValue {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Value,
        [string]$Type = 'DWord'
    )
    try {
        if (!(Test-Path $Path)) {
            Write-Log "Creating registry key: $Path"
            New-Item -Path $Path -Force | Out-Null
        }
        $current = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
        if ($null -ne $current.$Name -and "$($current.$Name)" -eq "$Value") {
            Write-Log "Registry value already set: $Path\$Name = $Value"
            return
        }
        Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $Type -Force
        Write-Log "Registry value set: $Path\$Name = $Value" -Level "SUCCESS"
    }
    catch {
        Write-Log "Error setting registry value $Path\$Name - $($_.Exception.Message)" -Level "ERROR"
        throw
    }
}

function Disable-ServiceSafely {
    param([string]$ServiceName)
    try {
        $svc = Get-Service $ServiceName -ErrorAction SilentlyContinue
        if ($null -eq $svc) {
            Write-Log "Service not found: $ServiceName" -Level "INFO"
            return
        }
        Write-Log "Service status: $ServiceName (Status: $($svc.Status), StartType: $($svc.StartType))"
        if ($svc.Status -ne 'Stopped') {
            Stop-Service $ServiceName -Force -ErrorAction Stop
            Write-Log "Service stopped: $ServiceName" -Level "SUCCESS"
        }
        if ($svc.StartType -ne 'Manual') {
            Set-Service $ServiceName -StartupType Manual -ErrorAction Stop
            Write-Log "Service set to Manual: $ServiceName" -Level "SUCCESS"
        }
    }
    catch {
        Write-Log "Error processing service $ServiceName - $($_.Exception.Message)" -Level "ERROR"
        throw
    }
}

function Confirm-Action {
    param([string]$Message)
    return $true
}

function Remove-FileWithRetry {
    param(
        [string]$Path,
        [int]$RetryCount = 3,
        [int]$Delay = 1000
    )
    for ($i = 1; $i -le $RetryCount; $i++) {
        try {
            if (Test-Path $Path) {
                Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
                Write-Log "File/folder deleted: $Path" -Level "SUCCESS"
                return $true
            }
            Write-Log "File/folder not found: $Path" -Level "INFO"
            return $false
        }
        catch {
            Write-Log "Deletion attempt $i/$RetryCount failed for $Path - $($_.Exception.Message)" -Level "WARNING"
            Start-Sleep -Milliseconds $Delay
        }
    }
    Write-Log "Could not delete file/folder: $Path" -Level "ERROR"
    return $false
}

function Remove-UWPApp {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$AppxPackages
    )
    foreach ($package in $AppxPackages) {
        try {
            Write-Log "Attempting to remove package: $package"
            $appx = Get-AppxPackage -AllUsers -Name $package -ErrorAction SilentlyContinue
            if ($appx) {
                $appx | Remove-AppxPackage -AllUsers -ErrorAction Stop
                Write-Log "Removed package with Remove-AppxPackage: $package" -Level "SUCCESS"
            } else {
                Write-Log "Package not found: $package" -Level "INFO"
            }
            $provisioned = Get-AppxProvisionedPackage -Online | Where-Object { $_.PackageName -like "*$package*" }
            if ($provisioned) {
                Write-Log "Attempting to remove provisioned package: $package"
                Start-Process -FilePath "DISM.exe" -ArgumentList "/Online /Remove-ProvisionedAppxPackage /PackageName:$($provisioned.PackageName)" -Wait -NoNewWindow -ErrorAction Stop
                Write-Log "Removed provisioned package: $package" -Level "SUCCESS"
            }
            $appxPath = "C:\Program Files\WindowsApps\$package*"
            if (Test-Path $appxPath) {
                Write-Log "Attempting to remove Appx folder: $appxPath"
                if (Remove-FileWithRetry -Path $appxPath) {
                    Write-Log "Appx folder removed: $appxPath" -Level "SUCCESS"
                } else {
                    Write-Log "Failed to remove Appx folder: $appxPath" -Level "ERROR"
                }
            }
        }
        catch {
            if ($package -like "*MicrosoftEdgeDevToolsClient*") {
                Write-Log "MicrosoftEdgeDevToolsClient is system-protected, ignoring..." -Level "WARNING"
            } else {
                Write-Log "Failed to remove package $package - $($_.Exception.Message)" -Level "ERROR"
            }
        }
    }
}

function Set-ScheduledTaskState {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$ScheduledTasks,
        [Parameter(Mandatory=$true)]
        [ValidateSet("Enabled", "Disabled")]
        [string]$State
    )
    foreach ($task in $ScheduledTasks) {
        try {
            $taskInfo = Get-ScheduledTask -TaskPath "\" -TaskName $task -ErrorAction SilentlyContinue
            if ($taskInfo) {
                if ($State -eq "Disabled") {
                    Disable-ScheduledTask -TaskPath "\" -TaskName $task -ErrorAction Stop
                    Write-Log "Task disabled: $task" -Level "SUCCESS"
                } elseif ($State -eq "Enabled") {
                    Enable-ScheduledTask -TaskPath "\" -TaskName $task -ErrorAction Stop
                    Write-Log "Task enabled: $task" -Level "SUCCESS"
                }
            } else {
                Write-Log "Task not found: $task" -Level "INFO"
            }
        }
        catch {
            Write-Log "Failed to set task state for $task - $($_.Exception.Message)" -Level "ERROR"
        }
    }
}

function Set-ServiceStartup {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$Services,
        [Parameter(Mandatory=$true)]
        [ValidateSet("Automatic", "Manual", "Disabled")]
        [string]$State
    )
    foreach ($service in $Services) {
        try {
            $svc = Get-Service -Name $service -ErrorAction SilentlyContinue
            if ($svc) {
                Stop-Service $service -Force -ErrorAction SilentlyContinue
                Set-Service -Name $service -StartupType $State -ErrorAction Stop
                Write-Log "Service $service set to $State" -Level "SUCCESS"
            } else {
                Write-Log "Service not found: $service" -Level "INFO"
            }
        }
        catch {
            Write-Log "Failed to set service state for $service - $($_.Exception.Message)" -Level "ERROR"
        }
    }
}

function Check-OptimizationPrerequisites {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Command
    )
    $opt = $optimizations | Where-Object { $_.Name -eq $Command }
    if (-not $opt) {
        Write-Log "Optimizasyon bulunamadı: $Command" -Level "ERROR"
        throw "Bilinmeyen optimizasyon komutu: $Command"
    }

    # Çakışma kontrolü
    if ($opt.ConflictsWith) {
        foreach ($conflict in $opt.ConflictsWith) {
            if ($OptimizationCommand -eq $conflict) {
                Write-Log "Çakışma tespit edildi: $Command ile $conflict çakışıyor." -Level "ERROR"
                throw "Çakışma nedeniyle işlem durduruldu."
            }
        }
    }

    # Bağımlılık kontrolü
if ($opt.Dependencies) {
    foreach ($dep in $opt.Dependencies) {
        Write-Log "Bağımlılık kontrol ediliyor: $dep" -Level "INFO"
        try {
            # Script yolunu yeni parametreden veya fallback olarak al
            $scriptPath = $ExecutingScriptPath
            if ([string]::IsNullOrEmpty($scriptPath)) {
                $scriptPath = $MyInvocation.MyCommand.Path # Doğrudan çalıştırma için yedek
            }
            if ([string]::IsNullOrEmpty($scriptPath)) {
                throw "Script dosya yolu belirlenemedi!"
            }

            # Argümanları dizi olarak oluştur
            $argumentArray = @(
                "-ExecutionPolicy", "Bypass",
                "-File", $scriptPath,
                "-OptimizationCommand", $dep,
                "-ExecutingScriptPath", $scriptPath # Yolu bir sonraki çağrıya da aktar
            )
            Start-Process -FilePath "powershell.exe" -ArgumentList $argumentArray -Wait -NoNewWindow -ErrorAction Stop
            Write-Log "Bağımlılık tamamlandı: $dep" -Level "SUCCESS"
        }
        catch {
            Write-Log "Bağımlılık hatası: $dep - $($_.Exception.Message)" -Level "ERROR"
            throw # <-- ÖNEMLİ: Hata durumunda işlemi tamamen durdurmak için throw ekleyin.
        }
    }
}

    # Uyarıyı göster
    if ($opt.Warning) {
        Write-Log "Uyarı: $($opt.Warning)" -Level "WARNING"
    }

    # Güvenli olmayan işlem için onay
    if (-not $opt.IsSafe) {
        Write-Log "Bu işlem güvenli değil, lütfen dikkatli olun: $Command" -Level "WARNING"
    }

    return $opt
}

# Optimization Commands
try {
    $optInfo = Check-OptimizationPrerequisites -Command $OptimizationCommand
    Write-Log "Optimizasyon başlatılıyor: $($optInfo.Title)" -Level "INFO"

    switch ($OptimizationCommand) {
"Manage-VirtualMemory" {
    $currentUser = $(whoami)
    if ($currentUser -ne "nt authority\system") {
        $nsudoPath = Join-Path -Path $PSScriptRoot -ChildPath "..\Modules\nsudo\NSudo.exe"
        
        # Script yolunu yeni parametreden al
        $scriptPath = $ExecutingScriptPath
        if ([string]::IsNullOrEmpty($scriptPath)) {
            $scriptPath = $MyInvocation.MyCommand.Path
        }
        if ([string]::IsNullOrEmpty($scriptPath)) {
            throw "Script dosya yolu belirlenemedi!"
        }

        # Argümanları düzelt
        $arguments = "-U:S -P:E -ShowWindowMode:Hide -Wait PowerShell -ExecutionPolicy Bypass -File `'$scriptPath`' -OptimizationCommand Manage-VirtualMemory -ExecutingScriptPath `'$scriptPath`'"
        
        Write-Log "SYSTEM yetkisi yok, NSudo ile tekrar başlatılıyor..." -Level "WARNING"
        Start-Process -FilePath $nsudoPath -ArgumentList $arguments -Wait
        return
    }

            Write-Log "Sanal bellek yönetimi optimize ediliyor..." -Level "INFO"
            try {
                $totalRAMGB = [math]::Round((Get-CimInstance Win32_PhysicalMemory | Measure-Object -Property Capacity -Sum).Sum / 1GB, 2)
                Write-Log "Sistem RAM'i: ${totalRAMGB} GB" -Level "INFO"
                $minPageFileMB = [uint32][math]::Max(4096, [math]::Round($totalRAMGB * 1.5 * 1024))
                $maxPageFileMB = [uint32][math]::Max($minPageFileMB, [math]::Min(32768, [math]::Round($totalRAMGB * 2 * 1024)))
                Write-Log "Önerilen Pagefile: ${minPageFileMB}-${maxPageFileMB} MB" -Level "INFO"

                try {
                    $old = Get-CimInstance -ClassName Win32_PageFileSetting -ErrorAction SilentlyContinue
                    if ($old) { foreach ($item in $old) { Remove-CimInstance -InputObject $item -ErrorAction SilentlyContinue } }
                    Write-Log "Eski pagefile ayarları silindi." -Level "INFO"
                } catch {
                    Write-Log "Eski pagefile silinemedi: $($_.Exception.Message)" -Level "WARNING"
                }

                Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "AutomaticManagedPagefile" -Value 0 -Type DWord -Force
                $pagingFilesValue = "C:\pagefile.sys $minPageFileMB $maxPageFileMB"
                Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "PagingFiles" -Value @($pagingFilesValue) -Force
                Write-Log "C için pagefile ayarlandı: $pagingFilesValue" -Level "SUCCESS"
            } catch {
                Write-Log "KRİTİK HATA: $($_.Exception.Message)" -Level "ERROR"
                throw
            }
        }

        "Disable-Telemetry" {
            Write-Log "Checking TPM status before optimization..." -Level "INFO"
            try {
                $tpm = Get-Tpm
                if ($tpm.TpmPresent -and $tpm.TpmReady) {
                    Write-Log "TPM present and ready" -Level "INFO"
                } else {
                    Write-Log "TPM not ready or not present, skipping sensitive operations" -Level "WARNING"
                    return
                }
            } catch {
                Write-Log "TPM check failed: $($_.Exception.Message)" -Level "ERROR"
                return
            }
            Write-Log "Telemetri bileşenleri devre dışı bırakılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name='AllowTelemetry'; Value=0},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Diagnostics\DiagTrack'; Name='Disabled'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\DiagTrack'; Name='Start'; Value=4},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat'; Name='AITEnable'; Value=0},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat'; Name='DisableUAR'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat'; Name='DisableInventory'; Value=1}
            )
            $regSettings | ForEach-Object { 
                try { Set-RegistryValue @_ } catch { Write-Log "Registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $services = @('DiagTrack', 'dmwappushservice', 'diagnosticshub.standardcollector.service', 'DcpSvc')
            $services | ForEach-Object { 
                try { Disable-ServiceSafely $_ } catch { Write-Log "Servis devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $telemetryFolders = @(
                "$env:ProgramData\Microsoft\Diagnosis",
                "$env:LOCALAPPDATA\Microsoft\Telemetry",
                "$env:ProgramData\Microsoft\Windows\WER"
            )
            $telemetryFolders | ForEach-Object {
                try {
                    if (Test-Path $_) {
                        Write-Log "Removing telemetry folder: $_"
                        if (Remove-FileWithRetry -Path $_) { Write-Log "Telemetry folder removed: $_" -Level "SUCCESS" }
                        else { Write-Log "Telemetry folder could not be removed: $_" -Level "ERROR" }
                    } else { Write-Log "Telemetry folder not found: $_" -Level "INFO" }
                } catch { Write-Log "Error processing telemetry folder $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-MicrosoftEdge" {
            Write-Log "Removing Microsoft Edge components..." -Level "INFO"
            $edgeProcesses = @("msedge.exe", "msedgewebview2.exe", "MicrosoftEdgeUpdate.exe", "edgeupdate.exe")
            $edgeProcesses | ForEach-Object {
                try {
                    Get-Process $_ -ErrorAction SilentlyContinue | ForEach-Object {
                        $_.Kill()
                        Write-Log "Process stopped: $($_.Name) (PID: $($_.Id))" -Level "SUCCESS"
                    }
                } catch { Write-Log "Failed to stop process $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
            $edgeServices = @("edgeupdate", "edgeupdatem", "MicrosoftEdgeElevationService")
            $edgeServices | ForEach-Object {
                try {
                    $svc = Get-Service $_ -ErrorAction SilentlyContinue
                    if ($svc) {
                        Stop-Service $_ -Force -ErrorAction SilentlyContinue
                        Start-Process -FilePath "sc.exe" -ArgumentList "delete $_" -Wait -NoNewWindow -ErrorAction Stop
                        Write-Log "Service deleted: $_" -Level "SUCCESS"
                    } else { Write-Log "Service not found: $_" -Level "INFO" }
                } catch { Write-Log "Failed to delete service $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
            $edgePackages = @(
                "Microsoft.MicrosoftEdge",
                "Microsoft.MicrosoftEdgeDevToolsClient",
                "Microsoft.Edge",
                "Microsoft.Edge.GameAssist"
            )
            Remove-UWPApp -AppxPackages $edgePackages
            $edgeFolders = @(
                "C:\Program Files (x86)\Microsoft\Edge",
                "C:\Program Files (x86)\Microsoft\EdgeCore",
                "C:\Program Files (x86)\Microsoft\EdgeWebView",
                "C:\Program Files\WindowsApps\Microsoft.Edge.GameAssist_1.0.3423.0_x64__8wekyb3d8bbwe",
                "C:\Program Files (x86)\Microsoft\EdgeUpdate",
                "C:\ProgramData\Microsoft\Windows\AppRepository\Packages\Microsoft.MicrosoftEdgeDevToolsClient_1000.19041.3636.0_neutral_neutral_8wekyb3d8bbwe",
                "C:\ProgramData\Packages\Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe",
                "$env:LOCALAPPDATA\Microsoft\Edge",
                "C:\Program Files (x86)\Microsoft\Temp",
                "$env:PROGRAMDATA\Microsoft\Windows\Start Menu\Programs\Microsoft Edge.lnk"
            )
            $edgeFolders | ForEach-Object {
                try {
                    if (Test-Path $_) {
                        Write-Log "Removing folder: $_"
                        if (Remove-FileWithRetry -Path $_) { Write-Log "Folder removed: $_" -Level "SUCCESS" }
                        else { Write-Log "Folder could not be removed: $_" -Level "ERROR" }
                    } else { Write-Log "Folder not found: $_" -Level "INFO" }
                } catch { Write-Log "Error processing folder $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
            $edgeRegistryPaths = @(
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge',
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge Update',
                'HKLM:\SOFTWARE\Microsoft\EdgeUpdate',
                'HKCU:\SOFTWARE\Microsoft\Edge',
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge Update',
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications\Microsoft.MicrosoftEdgeDevToolsClient*',
                'HKLM:\SOFTWARE\Microsoft\Edge',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Edge',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate'
            )
            $edgeRegistryPaths | ForEach-Object {
                try {
                    if (Test-Path $_) {
                        Remove-Item -Path $_ -Recurse -Force -ErrorAction Stop
                        Write-Log "Registry key removed: $_" -Level "SUCCESS"
                    }
                } catch { Write-Log "Failed to remove registry key $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
            try {
                Get-ScheduledTask -TaskPath "\Microsoft\Windows\EdgeUpdate\" -ErrorAction SilentlyContinue | 
                    Disable-ScheduledTask -ErrorAction SilentlyContinue | Out-Null
                Write-Log "Edge tasks disabled" -Level "SUCCESS"
            } catch { Write-Log "Failed to disable Edge tasks - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Restore-MicrosoftEdge" {
            Write-Log "Restoring Microsoft Edge..." -Level "INFO"
            try {
                Start-Process -FilePath "winget" -ArgumentList "install Microsoft.Edge --silent --accept-source-agreements" -Wait -NoNewWindow -ErrorAction Stop
                Write-Log "Microsoft Edge restored successfully" -Level "SUCCESS"
            }
            catch {
                Write-Log "Failed to restore Microsoft Edge - $($_.Exception.Message)" -Level "ERROR"
                throw
            }
        }

        "Test" {
            Write-Log "Running system tests..." -Level "INFO"
            try {
                Write-Log "Test log yazılıyor..."
                Write-Log "NSudo yetki testi yapılıyor" -Level "INFO"
                whoami /groups | Out-Null
                Write-Log "NSudo yetki testi başarılı" -Level "SUCCESS"
                $testPath = "$env:TEMP\WinFastGUI_Test_$(New-Guid).txt"
                Set-Content -Path $testPath -Value "Test file" -ErrorAction Stop
                Write-Log "Test file created: $testPath" -Level "SUCCESS"
                Remove-Item -Path $testPath -Force -ErrorAction Stop
                Write-Log "Test file removed: $testPath" -Level "SUCCESS"
                $telemetryService = Get-Service "DiagTrack" -ErrorAction SilentlyContinue
                if ($telemetryService -and $telemetryService.Status -eq "Stopped" -and $telemetryService.StartType -eq "Manual") {
                    Write-Log "Telemetry service (DiagTrack) is disabled" -Level "SUCCESS"
                } else {
                    Write-Log "Telemetry service (DiagTrack) is not disabled" -Level "WARNING"
                }
            }
            catch {
                Write-Log "System tests failed - $($_.Exception.Message)" -Level "ERROR"
                throw
            }
        }

        "Optimize-SystemPerformance" {
            Write-Log "Sistem performansı optimizasyonu başlatılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='DisablePagingExecutive'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='LargeSystemCache'; Value=1},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'; Name='SystemResponsiveness'; Value=0},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer'; Name='AlwaysUnloadDLL'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Power'; Name='HibernateEnabled'; Value=0}
            )
            $regSettings | ForEach-Object { 
                try { Set-RegistryValue @_ } catch { Write-Log "Registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $services = @('SysMain', 'WSearch')
            $services | ForEach-Object { 
                try { Disable-ServiceSafely $_ } catch { Write-Log "Servis devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try {
                powercfg -setactive SCHEME_MIN
                Write-Log "Power plan set to High Performance" -Level "SUCCESS"
            }
            catch { Write-Log "Power plan ayarlanamadı - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Optimize-TelemetryAndPrivacy" {
            Write-Log "Checking TPM status before optimization..." -Level "INFO"
            try {
                $tpm = Get-Tpm
                if ($tpm.TpmPresent -and $tpm.TpmReady) {
                    Write-Log "TPM present and ready" -Level "INFO"
                } else {
                    Write-Log "TPM not ready or not present, skipping sensitive operations" -Level "WARNING"
                    return
                }
            } catch {
                Write-Log "TPM check failed: $($_.Exception.Message)" -Level "ERROR"
                return
            }
            Write-Log "Optimizing telemetry and privacy settings..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name='AllowTelemetry'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy'; Name='TailoredExperiencesWithDiagnosticDataEnabled'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo'; Name='Enabled'; Value=0},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name='DisableWindowsConsumerFeatures'; Value=1},
                @{Path='HKCU:\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy'; Name='HasAccepted'; Value=0}
            )
            $regSettings | ForEach-Object { 
                try { Set-RegistryValue @_ } catch { Write-Log "Registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $services = @('DiagTrack', 'dmwappushservice', 'diagnosticshub.standardcollector.service', 'DcpSvc', 'WMPNetworkSvc')
            $services | ForEach-Object { 
                try { Disable-ServiceSafely $_ } catch { Write-Log "Servis devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $telemetryFolders = @(
                "$env:ProgramData\Microsoft\Diagnosis",
                "$env:LOCALAPPDATA\Microsoft\Telemetry",
                "$env:ProgramData\Microsoft\Windows\WER"
            )
            $telemetryFolders | ForEach-Object {
                try {
                    if (Test-Path $_) {
                        Write-Log "Removing telemetry folder: $_"
                        if (Remove-FileWithRetry -Path $_) { Write-Log "Telemetry folder removed: $_" -Level "SUCCESS" }
                        else { Write-Log "Telemetry folder could not be removed: $_" -Level "ERROR" }
                    } else { Write-Log "Telemetry folder not found: $_" -Level "INFO" }
                } catch { Write-Log "Error processing telemetry folder $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
            try {
                Get-ScheduledTask -TaskPath "\Microsoft\Windows\Customer Experience Improvement Program\" -ErrorAction SilentlyContinue | 
                    Disable-ScheduledTask -ErrorAction SilentlyContinue | Out-Null
                Write-Log "CEIP tasks disabled" -Level "SUCCESS"
            } catch { Write-Log "CEIP tasks could not be disabled - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Optimize-SearchSettings" {
            Write-Log "Optimizing search settings..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name='AllowCortana'; Value=0},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name='DisableWebSearch'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\WSearch'; Name='Start'; Value=4}
            )
            $regSettings | ForEach-Object { 
                try { Set-RegistryValue @_ } catch { Write-Log "Registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try { Disable-ServiceSafely "WSearch" } catch { Write-Log "WSearch servisi devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Optimize-ExplorerSettings" {
            Write-Log "Explorer ayarları optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='HideFileExt'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='Hidden'; Value=1},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='ShowSuperHidden'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer'; Name='ShowRecent'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Explorer için registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-AdvancedNetworkTweaks" {
            Write-Log "Gelişmiş ağ ayarları optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'; Name='NetworkThrottlingIndex'; Value=-1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='MaxUserPort'; Value=65534},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='TcpTimedWaitDelay'; Value=30}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Ağ için registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-BackgroundApps" {
            Write-Log "Arka plan uygulamaları devre dışı bırakılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications'; Name='GlobalUserDisabled'; Value=1},
                @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'; Name='BackgroundAppGlobalToggle'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Arka plan uygulamaları için registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-VisualEffects" {
            Write-Log "Görsel efektler performans için optimize ediliyor..." -Level "INFO"
            Set-RegistryValue -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' -Name 'VisualFXSetting' -Value 2
            $regSettings = @(
                @{Path='HKCU:\Control Panel\Desktop'; Name='DragFullWindows'; Value="0"; Type='String'},
                @{Path='HKCU:\Control Panel\Desktop\WindowMetrics'; Name='MinAnimate'; Value="0"; Type='String'},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='ListviewAlphaSelect'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='TaskbarAnimations'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\DWM'; Name='EnableAeroPeek'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\DWM'; Name='AlwaysHwndPlacemen'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Görsel efekt için registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-GameMode" {
            Write-Log "Oyun modu optimizasyonu başlatılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKCU:\SOFTWARE\Microsoft\GameBar'; Name='AllowAutoGameMode'; Value=1},
                @{Path='HKCU:\SOFTWARE\Microsoft\GameBar'; Name='AutoGameModeEnabled'; Value=1},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='GPU Priority'; Value=8},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='Priority'; Value=6},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='Scheduling Category'; Value='High'; Type='String'},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='SFIO Priority'; Value='High'; Type='String'}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Oyun modu için registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $gamingServices = @('XblAuthManager', 'XblGameSave', 'XboxNetApiSvc', 'XboxGipSvc')
            $gamingServices | ForEach-Object {
                try { 
                    Set-Service -Name $_ -StartupType Manual -ErrorAction SilentlyContinue
                    Write-Log "Gaming service optimized: $_" -Level "SUCCESS"
                } catch { Write-Log "Gaming service optimization failed: $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-MMCSS_Profiles" {
            Write-Log "MMCSS profilleri optimizasyonu başlatılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'; Name='SystemResponsiveness'; Value=0},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'; Name='NetworkThrottlingIndex'; Value=-1},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio'; Name='GPU Priority'; Value=8},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio'; Name='Priority'; Value=6},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio'; Name='Scheduling Category'; Value='Medium'; Type='String'},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio'; Name='GPU Priority'; Value=8},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio'; Name='Priority'; Value=7},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio'; Name='Scheduling Category'; Value='High'; Type='String'}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "MMCSS için registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-Input_Optimizations" {
            Write-Log "Giriş cihazları optimizasyonu başlatılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKCU:\Control Panel\Mouse'; Name='MouseHoverTime'; Value='10'; Type='String'},
                @{Path='HKCU:\Control Panel\Mouse'; Name='MouseSpeed'; Value='0'; Type='String'},
                @{Path='HKCU:\Control Panel\Mouse'; Name='MouseThreshold1'; Value='0'; Type='String'},
                @{Path='HKCU:\Control Panel\Mouse'; Name='MouseThreshold2'; Value='0'; Type='String'},
                @{Path='HKCU:\Control Panel\Keyboard'; Name='KeyboardDelay'; Value='0'; Type='String'},
                @{Path='HKCU:\Control Panel\Keyboard'; Name='KeyboardSpeed'; Value='31'; Type='String'},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\mouclass\Parameters'; Name='MouseDataQueueSize'; Value=100},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters'; Name='KeyboardDataQueueSize'; Value=100}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Giriş optimizasyonu için registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try {
                Set-RegistryValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\usbhub\Parameters' -Name 'DisableSelectiveSuspend' -Value 1
                Write-Log "USB selective suspend devre dışı bırakıldı" -Level "SUCCESS"
            } catch { Write-Log "USB optimizasyonu başarısız - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Disable-WindowsDefender" {
            Write-Log "Windows Defender devre dışı bırakılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender'; Name='DisableAntiSpyware'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection'; Name='DisableRealtimeMonitoring'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection'; Name='DisableIOAVProtection'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection'; Name='DisableOnAccessProtection'; Value=1}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Defender registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $defenderServices = @('WinDefend', 'WdNisSvc', 'Sense', 'WdBoot', 'WdFilter', 'WdNisDrv')
            $defenderServices | ForEach-Object {
                try { Disable-ServiceSafely $_ } catch { Write-Log "Defender servisi devre dışı bırakılamadı: $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
            $defenderTasks = @("Windows Defender Cache Maintenance", "Windows Defender Cleanup", "Windows Defender Scheduled Scan", "Windows Defender Verification")
            Set-ScheduledTaskState -ScheduledTasks $defenderTasks -State "Disabled"
        }

        "Optimize-StartupPrograms" {
            Write-Log "Başlangıç programları optimize ediliyor..." -Level "INFO"
            $startupPaths = @(
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run',
                'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
            )
            $commonStartupItems = @('Adobe', 'Java', 'Office', 'Skype', 'Spotify', 'Steam')
            foreach ($path in $startupPaths) {
                if (Test-Path $path) {
                    $items = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue
                    foreach ($property in $items.PSObject.Properties) {
                        foreach ($startup in $commonStartupItems) {
                            if ($property.Name -like "*$startup*" -and $property.Name -ne "PSPath" -and $property.Name -ne "PSParentPath" -and $property.Name -ne "PSChildName" -and $property.Name -ne "PSProvider") {
                                try {
                                    Remove-ItemProperty -Path $path -Name $property.Name -ErrorAction Stop
                                    Write-Log "Startup item removed: $($property.Name)" -Level "SUCCESS"
                                } catch { Write-Log "Failed to remove startup item: $($property.Name) - $($_.Exception.Message)" -Level "ERROR" }
                            }
                        }
                    }
                }
            }

            # Görev Zamanlayıcı hizmetini kontrol et ve yönet
            $taskService = Get-Service -Name 'Schedule' -ErrorAction SilentlyContinue
            if (-not $taskService) {
                Write-Log "Görev Zamanlayıcı hizmeti (Schedule) bulunamadı. Görev optimizasyonu atlanıyor." -Level "WARNING"
            } else {
                $wasServiceStopped = $false
                # Hizmetin çalışıp çalışmadığını kontrol et
                if ($taskService.Status -ne 'Running') {
                    try {
                        Write-Log "Görev Zamanlayıcı hizmeti çalışmıyor. Geçici olarak başlatılıyor..." -Level "INFO"
                        Start-Service -Name 'Schedule'
                        # Hizmetin başlaması için 15 saniyeye kadar bekle
                        $taskService.WaitForStatus('Running', [TimeSpan]::FromSeconds(15))
                        $wasServiceStopped = $true
                    } catch {
                        Write-Log "Görev Zamanlayıcı hizmeti başlatılamadı: $($_.Exception.Message). Görev optimizasyonu atlanıyor." -Level "ERROR"
                    }
                }

                # Hizmetin artık çalıştığından emin olduktan sonra işlemleri yap
                if ((Get-Service -Name 'Schedule').Status -eq 'Running') {
                    try {
                        Get-ScheduledTask | Where-Object { $_.TaskName -like "*Adobe*" -or $_.TaskName -like "*Java*" } | ForEach-Object {
                            try {
                                Disable-ScheduledTask -TaskName $_.TaskName -ErrorAction Stop
                                Write-Log "Startup task disabled: $($_.TaskName)" -Level "SUCCESS"
                            } catch { Write-Log "Failed to disable startup task: $($_.TaskName) - $($_.Exception.Message)" -Level "ERROR" }
                        }
                    } catch {
                        Write-Log "Zamanlanmış görevler işlenirken bir hata oluştu: $($_.Exception.Message)" -Level "ERROR"
                    } finally {
                        # Eğer hizmeti biz başlattıysak, işlem bittiğinde tekrar durdur
                        if ($wasServiceStopped) {
                            Write-Log "Görev Zamanlayıcı hizmeti orijinal durdurulmuş durumuna geri getiriliyor." -Level "INFO"
                            Stop-Service -Name 'Schedule' -Force -ErrorAction SilentlyContinue
                        }
                    }
                }
            }
        }
			

        "Optimize-Services" {
            Write-Log "Sistem servisleri optimize ediliyor..." -Level "INFO"
            $unnecessaryServices = @('Fax', 'Spooler', 'Themes', 'TabletInputService', 'WMPNetworkSvc', 'RemoteRegistry', 'RemoteAccess', 'SharedAccess')
            Set-ServiceStartup -Services $unnecessaryServices -State "Manual"
            $performanceServices = @('Dhcp', 'Dnscache', 'LanmanWorkstation', 'LanmanServer', 'RpcSs', 'RpcEptMapper')
            Set-ServiceStartup -Services $performanceServices -State "Automatic"
        }

        "Optimize-RegistryTweaks" {
            Write-Log "Gelişmiş registry tweaks uygulanıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer'; Name='AlwaysUnloadDLL'; Value=1},
                @{Path='HKCU:\Control Panel\Desktop'; Name='MenuShowDelay'; Value='0'; Type='String'},
                @{Path='HKCU:\Control Panel\Desktop'; Name='WaitToKillAppTimeout'; Value='2000'; Type='String'},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control'; Name='WaitToKillServiceTimeout'; Value='2000'; Type='String'},
                @{Path='HKCU:\Control Panel\Mouse'; Name='MouseHoverTime'; Value='8'; Type='String'},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='ClearPageFileAtShutdown'; Value=0},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='DisablePagingExecutive'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='LargeSystemCache'; Value=0},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'; Name='NetworkThrottlingIndex'; Value=-1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='DefaultTTL'; Value=64},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='MaxConnectionsPerServer'; Value=16}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Registry tweak uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-PowerSettings" {
            Write-Log "Güç ayarları optimize ediliyor..." -Level "INFO"
            try {
                powercfg -setactive SCHEME_MIN
                powercfg -setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0
                powercfg -setdcvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0
                powercfg -setacvalueindex SCHEME_CURRENT 0012ee47-9041-4b5d-9b77-535fba8b1442 6738e2c4-e8a5-4a42-b16a-e040e769756e 0
                powercfg -setdcvalueindex SCHEME_CURRENT 0012ee47-9041-4b5d-9b77-535fba8b1442 6738e2c4-e8a5-4a42-b16a-e040e769756e 0
                powercfg -setactive SCHEME_CURRENT
                Write-Log "Güç ayarları optimize edildi" -Level "SUCCESS"
            }
            catch {
                Write-Log "Güç ayarları optimize edilirken hata - $($_.Exception.Message)" -Level "ERROR"
            }
        }

        "Optimize-GPU_Settings" {
            Write-Log "GPU ayarları optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='GPU Priority'; Value=8},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='Priority'; Value=6},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='Scheduling Category'; Value='High'; Type='String'},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'; Name='HwSchMode'; Value=2},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'; Name='TdrLevel'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "GPU registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-RAM_Management" {
            Write-Log "RAM yönetimi optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='LargeSystemCache'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='DisablePagingExecutive'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='IoPageLockLimit'; Value=983040},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='NonPagedPoolSize'; Value=0},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='PagedPoolSize'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "RAM yönetimi registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try { Disable-ServiceSafely "SysMain" } catch { Write-Log "SysMain devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Optimize-Storage_Performance" {
            Write-Log "Depolama performansı optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'; Name='LongPathsEnabled'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'; Name='NtfsDisableLastAccessUpdate'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'; Name='NtfsDisable8dot3NameCreation'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'; Name='ContigFileAllocSize'; Value=1536}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Depolama registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try { Disable-ServiceSafely "WSearch" } catch { Write-Log "Windows Search devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Optimize-Audio_Settings" {
            Write-Log "Ses ayarları optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio'; Name='GPU Priority'; Value=8},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio'; Name='Priority'; Value=6},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio'; Name='Scheduling Category'; Value='Medium'; Type='String'},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio'; Name='GPU Priority'; Value=8},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio'; Name='Priority'; Value=7},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Pro Audio'; Name='Scheduling Category'; Value='High'; Type='String'}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Ses registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Disable-Cortana" {
            Write-Log "Cortana devre dışı bırakılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name='AllowCortana'; Value=0},
                @{Path='HKLM:\SOFTWARE\Microsoft\PolicyManager\default\Experience\AllowCortana'; Name='value'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search'; Name='CortanaEnabled'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search'; Name='CanCortanaBeEnabled'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Cortana registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-Privacy_Settings" {
            Write-Log "Gizlilik ayarları optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy'; Name='TailoredExperiencesWithDiagnosticDataEnabled'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Personalization\Settings'; Name='AcceptedPrivacyPolicy'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\InputPersonalization'; Name='RestrictImplicitInkCollection'; Value=1},
                @{Path='HKCU:\SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore'; Name='HarvestContacts'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Diagnostics\DiagTrack'; Name='ShowedToastAtLevel'; Value=1}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Gizlilik registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-Update_Settings" {
            Write-Log "Güncelleme ayarları optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'; Name='NoAutoUpdate'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'; Name='AUOptions'; Value=2},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config'; Name='DODownloadMode'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Güncelleme registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try { Set-Service -Name "wuauserv" -StartupType Manual } catch {}
        }

        "Optimize-WindowsUpdates" {
            Write-Log "Windows güncellemeleri manuel moda alınıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'; Name='NoAutoUpdate'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'; Name='AUOptions'; Value=2}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Güncelleme registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try { Set-Service -Name "wuauserv" -StartupType Manual } catch {}
        }

        "Optimize-Firewall_Settings" {
            Write-Log "Güvenlik duvarı optimize ediliyor..." -Level "INFO"
            try {
                netsh advfirewall set allprofiles state off
                Set-Service -Name "MpsSvc" -StartupType Manual
                Write-Log "Güvenlik duvarı optimize edildi" -Level "SUCCESS"
            }
            catch {
                Write-Log "Güvenlik duvarı optimize edilirken hata - $($_.Exception.Message)" -Level "ERROR"
            }
        }

        "Optimize-Display_Settings" {
            Write-Log "Ekran ayarları optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\DWM'; Name='EnableAeroPeek'; Value=0},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\DWM'; Name='AlwaysHibernateThumbnails'; Value=0},
                @{Path='HKCU:\Control Panel\Desktop'; Name='DragFullWindows'; Value="0"; Type='String'},
                @{Path='HKCU:\Control Panel\Desktop\WindowMetrics'; Name='MinAnimate'; Value="0"; Type='String'},
                @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='ListviewAlphaSelect'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Ekran registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-USB_Performance" {
            Write-Log "USB performansı optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\usbhub\Parameters'; Name='DisableSelectiveSuspend'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\USB'; Name='DisableSelectiveSuspend'; Value=1}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "USB registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-CPU_Performance" {
            Write-Log "CPU performansı optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'; Name='SystemResponsiveness'; Value=0},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl'; Name='Win32PrioritySeparation'; Value=38},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\kernel'; Name='DisableTSX'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "CPU registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-DiskCleanup" {
            Write-Log "Disk temizleme başlatılıyor..." -Level "INFO"
            $tempPaths = @(
                "$env:TEMP\*",
                "$env:WINDIR\Temp\*",
                "$env:WINDIR\Prefetch\*",
                "$env:LOCALAPPDATA\Microsoft\Windows\Temporary Internet Files\*"
            )
            foreach ($path in $tempPaths) {
                try {
                    Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Log "Temp files cleaned: $path" -Level "SUCCESS"
                } catch { Write-Log "Temp cleanup failed: $path - $($_.Exception.Message)" -Level "ERROR" }
            }
            Start-Process -FilePath "cleanmgr.exe" -ArgumentList "/sagerun:1" -NoNewWindow -Wait -ErrorAction SilentlyContinue
        }

        "Optimize-Updates_CompleteDisable" {
            Write-Log "Windows Update tamamen devre dışı bırakılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'; Name='NoAutoUpdate'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'; Name='DoNotConnectToWindowsUpdateInternetLocations'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'; Name='DisableWindowsUpdateAccess'; Value=1}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Update registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $updateServices = @('wuauserv', 'UsoSvc', 'WaaSMedicSvc')
            Set-ServiceStartup -Services $updateServices -State "Disabled"
        }

        "Optimize-WindowsDefender" {
            Write-Log "Windows Defender optimizasyonu başlatılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender'; Name='DisableAntiSpyware'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection'; Name='DisableRealtimeMonitoring'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection'; Name='DisableIOAVProtection'; Value=1},
                @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection'; Name='DisableOnAccessProtection'; Value=1}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Windows Defender registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $defenderServices = @('WinDefend', 'WdNisSvc', 'Sense', 'WdBoot', 'WdFilter', 'WdNisDrv')
            $defenderServices | ForEach-Object {
                try { Disable-ServiceSafely $_ } catch { Write-Log "Windows Defender servisi devre dışı bırakılamadı: $_ - $($_.Exception.Message)" -Level "ERROR" }
            }
            $defenderTasks = @("Windows Defender Cache Maintenance", "Windows Defender Cleanup", "Windows Defender Scheduled Scan", "Windows Defender Verification")
            Set-ScheduledTaskState -ScheduledTasks $defenderTasks -State "Disabled"
        }

        "Optimize-CoreIsolation" {
            Write-Log "Çekirdek yalıtımı optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity'; Name='Enabled'; Value=0},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard'; Name='EnableVirtualizationBasedSecurity'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Çekirdek yalıtımı registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-MPO_Optimization" {
            Write-Log "Multi-Plane Overlay (MPO) optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows\DWM'; Name='OverlayTestMode'; Value=5}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "MPO registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Invoke-AutomaticOptimization" {
            Write-Log "Otomatik optimizasyon başlatılıyor..." -Level "INFO"
            
            # Bu liste, JSON dosyanızdaki 'IsSafe' olarak işaretlenmiş optimizasyonları
            # veya sizin belirlediğiniz güvenli listeyi yansıtmalıdır.
            $safeOptimizations = @(
                "Disable-Telemetry",
                "Optimize-BackgroundApps",
                "Optimize-VisualEffects",
                "Optimize-GameMode",
                "Optimize-MMCSS_Profiles",
                "Optimize-Input_Optimizations",
                "Optimize-GeneralCleanup",
                "Optimize-Network",
                "Manage-MemoryOptimizations",
                "Manage-StorageOptimizations",
                "Manage-GpuOptimizations"
            )

            # Script yolunu güvenilir bir şekilde al
            $scriptPath = $ExecutingScriptPath
            if ([string]::IsNullOrEmpty($scriptPath)) {
                $scriptPath = $MyInvocation.MyCommand.Path
            }
            if ([string]::IsNullOrEmpty($scriptPath)) {
                throw "Invoke-AutomaticOptimization içinde script dosya yolu belirlenemedi!"
            }

            foreach ($opt in $safeOptimizations) {
                Write-Log "Otomatik olarak çalıştırılıyor: $opt" -Level "INFO"
                try {
                    # Argümanları dizi olarak ve ExecutingScriptPath ile birlikte oluştur
                    $argumentArray = @(
                        "-ExecutionPolicy", "Bypass",
                        "-File", $scriptPath,
                        "-OptimizationCommand", $opt,
                        "-ExecutingScriptPath", $scriptPath # <-- EN ÖNEMLİ DÜZELTME
                    )
                    Start-Process -FilePath "powershell.exe" -ArgumentList $argumentArray -Wait -NoNewWindow -ErrorAction Stop
                    Write-Log "$opt optimizasyonu tamamlandı" -Level "SUCCESS"
                }
                catch {
                    # Bir optimizasyon hata verirse logla ve devam et
                    Write-Log "$opt optimizasyonu hatası: $($_.Exception.Message)" -Level "ERROR"
                }
            }
        }

        "Optimize-GeneralCleanup" {
            Write-Log "Genel sistem temizliği başlatılıyor..." -Level "INFO"
            $tempPaths = @(
                "$env:TEMP\*",
                "$env:WINDIR\Temp\*",
                "$env:WINDIR\Prefetch\*",
                "$env:LOCALAPPDATA\Microsoft\Windows\Temporary Internet Files\*",
                "$env:APPDATA\Microsoft\Windows\Recent\*"
            )
            foreach ($path in $tempPaths) {
                try {
                    Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Log "Temizlendi: $path" -Level "SUCCESS"
                } catch { Write-Log "Temizleme başarısız: $path - $($_.Exception.Message)" -Level "ERROR" }
            }
            Start-Process -FilePath "cleanmgr.exe" -ArgumentList "/sagerun:1" -NoNewWindow -Wait -ErrorAction SilentlyContinue
        }

        "Optimize-CleanRegistry" {
            Write-Log "Kayıt defteri temizliği başlatılıyor..." -Level "INFO"
            $registryPaths = @(
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
                'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\*'
            )
            foreach ($path in $registryPaths) {
                try {
                    $items = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue
                    foreach ($item in $items.PSObject.Properties) {
                        if ($item.Name -like "*Temp*" -or $item.Name -like "*Uninstall*") {
                            Remove-ItemProperty -Path $path -Name $item.Name -ErrorAction SilentlyContinue
                            Write-Log "Registry key removed: $path\$($item.Name)" -Level "SUCCESS"
                        }
                    }
                } catch { Write-Log "Registry temizliği başarısız: $path - $($_.Exception.Message)" -Level "ERROR" }
            }
        }

        "Optimize-Network" {
            Write-Log "Ağ ayarları optimize ediliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='MaxUserPort'; Value=65534},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='TcpTimedWaitDelay'; Value=30},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='DefaultTTL'; Value=64},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='EnablePMTUDiscovery'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='EnablePMTUBHDetect'; Value=0},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='TcpMaxDataRetransmissions'; Value=5},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'; Name='EnableWsd'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Ağ registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try {
                netsh int tcp set global autotuninglevel=normal | Out-Null
                netsh int tcp set global congestionprovider=ctcp | Out-Null
                netsh int tcp set global ecncapability=disabled | Out-Null
                netsh int tcp set global rss=enabled | Out-Null
                Write-Log "TCP/IP ayarları optimize edildi" -Level "SUCCESS"
            } catch {
                Write-Log "TCP/IP ayarları uygulanamadı - $($_.Exception.Message)" -Level "ERROR"
            }
            try { Disable-ServiceSafely "WwanSvc" } catch { Write-Log "WwanSvc devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
            try { Disable-ServiceSafely "WlanSvc" } catch { Write-Log "WlanSvc devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Show-CS2Recommendations" {
            Write-Log "CS2 (Counter-Strike 2) için optimizasyon önerileri gösteriliyor..." -Level "INFO"
            $recommendations = @(
                "1. Oyun Modu'nu etkinleştirin: Optimize-GameMode komutunu çalıştırın.",
                "2. Arka plan uygulamalarını devre dışı bırakın: Optimize-BackgroundApps komutunu çalıştırın.",
                "3. Görsel efektleri optimize edin: Optimize-VisualEffects komutunu çalıştırın.",
                "4. Ağ ayarlarını optimize edin: Optimize-Network komutunu çalıştırın.",
                "5. Windows Defender'ı devre dışı bırakın (isteğe bağlı): Optimize-WindowsDefender komutunu çalıştırın.",
                "6. Güç ayarlarını yüksek performansa ayarlayın: Optimize-PowerSettings komutunu çalıştırın.",
                "7. Giriş cihazlarını optimize edin: Optimize-Input_Optimizations komutunu çalıştırın.",
                "8. Sistemde gereksiz servisleri kapatın: Optimize-Services komutunu çalıştırın."
            )
            foreach ($rec in $recommendations) {
                Write-Log $rec -Level "INFO"
            }
        }

        "Disable-EventLogging" {
            Write-Log "Olay günlüğü devre dışı bırakılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\WMI\Autologger\EventLog-System'; Name='Start'; Value=0},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\WMI\Autologger\EventLog-Application'; Name='Start'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Olay günlüğü registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            $eventServices = @('EventLog')
            Set-ServiceStartup -Services $eventServices -State "Manual"
        }

        "Manage-MemoryOptimizations" {
            Write-Log "Bellek optimizasyonları yönetiliyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='DisablePagingExecutive'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='LargeSystemCache'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='IoPageLockLimit'; Value=983040},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='NonPagedPoolSize'; Value=0},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'; Name='PagedPoolSize'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Bellek registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try { Disable-ServiceSafely "SysMain" } catch { Write-Log "SysMain devre dışı bırakılamadı - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Manage-StorageOptimizations" {
            Write-Log "Depolama (SSD/HDD) optimizasyonları başlatılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'; Name='NtfsDisableLastAccessUpdate'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'; Name='NtfsDisable8dot3NameCreation'; Value=1},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'; Name='ContigFileAllocSize'; Value=1536}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "Depolama registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try {
                Start-Process -FilePath "defrag.exe" -ArgumentList "/C /O" -NoNewWindow -Wait -ErrorAction SilentlyContinue
                Write-Log "Disk birleştirme ve optimizasyon yapıldı" -Level "SUCCESS"
            } catch { Write-Log "Disk birleştirme başarısız - $($_.Exception.Message)" -Level "ERROR" }
        }

        "Manage-GpuOptimizations" {
            Write-Log "GPU optimizasyonları başlatılıyor..." -Level "INFO"
            $regSettings = @(
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='GPU Priority'; Value=8},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='Priority'; Value=6},
                @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'; Name='Scheduling Category'; Value='High'; Type='String'},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'; Name='HwSchMode'; Value=2},
                @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'; Name='TdrLevel'; Value=0}
            )
            $regSettings | ForEach-Object {
                try { Set-RegistryValue @_ } catch { Write-Log "GPU registry ayarı uygulanamadı - $($_.Exception.Message)" -Level "ERROR" }
            }
            try {
                $nvidiaKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm'
                if (Test-Path $nvidiaKey) {
                    Set-RegistryValue -Path $nvidiaKey -Name 'RmGpsPsEnablePerCpuCoreDpc' -Value 1
                    Write-Log "NVIDIA GPU optimizasyonu uygulandı" -Level "SUCCESS"
                }
                $amdKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\amdkmdag'
                if (Test-Path $amdKey) {
                    Set-RegistryValue -Path $amdKey -Name 'EnableHighPerformanceMode' -Value 1
                    Write-Log "AMD GPU optimizasyonu uygulandı" -Level "SUCCESS"
                }
                Write-Log "GPU optimizasyonları tamamlandı" -Level "SUCCESS"
            } catch {
                Write-Log "GPU optimizasyonu başarısız - $($_.Exception.Message)" -Level "ERROR"
            }
        }

        "Disable-SpecificDevices" {
            Write-Log "Belirtilen cihazlar devre dışı bırakılıyor..." -Level "INFO"
            $devicesToDisable = @(
                "Microsoft Virtual Drive Enumerator",
                "Microsoft GS Wavetable Synth",
                "High Precision Event Timer"
            )
            foreach ($device in $devicesToDisable) {
                try {
                    $dev = Get-PnpDevice -FriendlyName "*$device*" -ErrorAction SilentlyContinue
                    if ($dev) {
                        $dev | Disable-PnpDevice -Confirm:$false -ErrorAction Stop
                        Write-Log "Cihaz devre dışı bırakıldı: $device" -Level "SUCCESS"
                    } else {
                        Write-Log "Cihaz bulunamadı: $device" -Level "INFO"
                    }
                } catch {
                    Write-Log "Cihaz devre dışı bırakılamadı: $device - $($_.Exception.Message)" -Level "ERROR"
                }
            }
        }
    }
}
catch {
    Write-Log "Optimizasyon sırasında hata oluştu: $($_.Exception.Message)" -Level "ERROR"
    throw
}
finally {
    Write-Log "Optimizasyon işlemi tamamlandı: $OptimizationCommand" -Level "INFO"
}