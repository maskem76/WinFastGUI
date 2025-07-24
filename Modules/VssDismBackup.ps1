# Modules\VssDismBackup.ps1
param(
    [string]$BackupDir = "D:\Backups"
)
function Add-Log {
    param ([string]$Message, [string]$Level = "INFO", [string]$LogPath)
    $LogMessage = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'): [$Level] $Message"
    Add-Content -Path $LogPath -Value $LogMessage -Force
}

function Start-BulletproofVSSBackup {
    $LogPath = Join-Path -Path $BackupDir -ChildPath "backup_log_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
    $shadowObj = $null; $driveLetter = "X"
    try {
        if (-not (Test-Path $BackupDir)) { New-Item $BackupDir -ItemType Directory -Force | Out-Null }
        Add-Log "Sağlamlaştırılmış VSS + DISM yedekleme başlatıldı." "ACTION" $LogPath
        $class = Get-CimClass -ClassName Win32_ShadowCopy
        $result = Invoke-CimMethod -InputObject $class -MethodName Create -Arguments @{Volume = 'C:\'}
        if ($result.ReturnValue -ne 0) { throw "Shadow Copy oluşturulamadı! Hata Kodu: $($result.ReturnValue)" }
        $shadowID = $result.ShadowID
        $shadowObj = Get-CimInstance -ClassName Win32_ShadowCopy | Where-Object { $_.ID -eq $shadowID }
        while (Test-Path -Path "$driveLetter`:") {
            $driveLetter = [char]([int][char]$driveLetter + 1)
            if ($driveLetter -eq 'C') { $driveLetter = 'A' }
            if ($driveLetter -gt 'Z') { throw "Atanacak boş sürücü harfi bulunamadı!" }
        }
        mountvol "$driveLetter`:" "$($shadowObj.DeviceObject)\" | Out-Null
        $DateTime = Get-Date -Format "yyyyMMdd_HHmmss"
        $BackupPath = Join-Path $BackupDir "SystemBackup_VSS_$DateTime.wim"
        $ArgumentList = "/Capture-Image /ImageFile:`"$BackupPath`" /CaptureDir:$driveLetter`:\ /Name:`"VSS_Backup`" /Description:`"VSS Backup $DateTime`" /Compress:maximum /CheckIntegrity /Verify /NoRpFix"
        $process = Start-Process dism.exe -ArgumentList $ArgumentList -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -eq 0) {
            Add-Log "DISM ile yedek başarıyla alındı: $BackupPath" "SUCCESS" $LogPath
        } else {
            throw "DISM hata verdi. Çıkış Kodu: $($process.ExitCode). Detaylar için CBS.log dosyasını kontrol edin."
        }
    } catch {
        Add-Log "KRİTİK HATA: $($_.Exception.Message)" "ERROR" $LogPath
    } finally {
        if (Test-Path -Path "$driveLetter`:") { mountvol "$driveLetter`:" /D | Out-Null }
        if ($shadowObj) { $shadowObj | Remove-CimInstance }
    }
}
Start-BulletproofVSSBackup
