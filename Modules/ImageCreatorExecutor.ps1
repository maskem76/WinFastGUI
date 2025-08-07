param(
    [string][Parameter(Mandatory=$true)] $ImageCommand,
    [string]$SourcePath = "",
    [string]$TargetPath = "",
    [string]$TemplatePath = "",
    [string]$MountPath = "",
    [string]$LogFile = ""
)

function Write-Log($msg, $level="INFO") {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$level] $msg"
    if ($LogFile) { $line | Out-File -Append -FilePath $LogFile }
    Write-Host $line
}

try {
    switch ($ImageCommand) {
        "CreateSnapshot" {
            # VSS ile snapshot oluştur, device yolunu döndür
            $shadow = (Get-WmiObject -List Win32_ShadowCopy).Create("C:\", "ClientAccessible")
            if ($shadow.ReturnValue -eq 0) {
                $id = $shadow.ShadowID
                $obj = Get-WmiObject -Query "SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID = '$id'"
                $device = $obj.DeviceObject
                Write-Log "Snapshot oluşturuldu: $device" "SUCCESS"
                $device
            } else {
                throw "Snapshot alınamadı! Kod: $($shadow.ReturnValue)"
            }
        }
        "CaptureWim" {
            $excl = Join-Path $PSScriptRoot "wimscript.ini"
            $args = "/Capture-Image /ImageFile:`"$TargetPath`" /CaptureDir:`"$SourcePath`" /Name:WinFastYedek /Compress:Max /CheckIntegrity"
            if (Test-Path $excl) { $args += " /ConfigFile:`"$excl`"" }
            Write-Log "DISM başlatılıyor: $args"
            $proc = Start-Process -FilePath dism.exe -ArgumentList $args -Wait -NoNewWindow -PassThru
            if ($proc.ExitCode -eq 0) {
                Write-Log "WIM alındı: $TargetPath" "SUCCESS"
            } else {
                throw "DISM başarısız! ($($proc.ExitCode))"
            }
        }
        "CreateIso" {
            # ISO oluşturmak için scripti çağır (örnek)
            $ps1 = Join-Path $PSScriptRoot "Create-CustomISO.ps1"
            $cmd = "-TemplateZipPath `"$TemplatePath`" -SourceWimPath `"$SourcePath`" -OutputIsoPath `"$TargetPath`" -ModulesPath `"$PSScriptRoot`""
            Write-Log "ISO oluşturuluyor: $cmd"
            $proc = Start-Process -FilePath powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$ps1`" $cmd" -Wait -NoNewWindow -PassThru
            if ($proc.ExitCode -eq 0) {
                Write-Log "ISO oluşturuldu: $TargetPath" "SUCCESS"
            } else {
                throw "ISO oluşturma başarısız!"
            }
        }
        "DeleteAllSnapshots" {
            Write-Log "Tüm VSS snapshotlar siliniyor..." "INFO"
            Start-Process -FilePath vssadmin.exe -ArgumentList "delete shadows /for=C: /all /quiet" -Wait -NoNewWindow
            Write-Log "Tüm snapshotlar silindi." "SUCCESS"
        }
        "UnmountSnapshot" {
            if ($MountPath -and (Test-Path $MountPath)) {
                Remove-Item -Path $MountPath -Force -Recurse
                Write-Log "Snapshot mount kaldırıldı: $MountPath" "SUCCESS"
            } else {
                Write-Log "Kaldırılacak mount bulunamadı: $MountPath"
            }
        }
        default {
            Write-Log "Bilinmeyen komut: $ImageCommand" "ERROR"
            throw "Bilinmeyen komut: $ImageCommand"
        }
    }
} catch {
    Write-Log "Hata oluştu: $_" "ERROR"
    throw
}
