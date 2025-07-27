$scriptContent = @'
# PowerShell script for writing ISO to USB using Ventoy
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
param (
    [string]$IsoPath,
    [string]$UsbDrive
)
try {
    Write-Output "Ventoy ile USB hazırlanıyor: $UsbDrive"
    $ventoyPath = Join-Path $PSScriptRoot "ventoy\Ventoy2Disk.exe"
    if (-not (Test-Path $ventoyPath)) {
        Write-Error "Ventoy2Disk.exe bulunamadı: $ventoyPath"
        exit 1
    }
    $tempVentoyPath = "$env:TEMP\Ventoy2Disk.exe"
    Copy-Item -Path $ventoyPath -Destination $tempVentoyPath -Force
    Write-Output "Ventoy kurulumu başlatılıyor..."
    $process = Start-Process -FilePath $tempVentoyPath -ArgumentList "-i ${UsbDrive}: -y -g" -NoNewWindow -RedirectStandardOutput "$env:TEMP\ventoy_out.txt" -RedirectStandardError "$env:TEMP\ventoy_err.txt" -PassThru -Verb RunAs
    $process | Wait-Process -Timeout 300
    if ($process.HasExited -eq $false) {
        Write-Output "Ventoy işlemi zaman aşımına uğradı, zorla kapatılıyor..."
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    if ($process.ExitCode -ne 0) {
        $errorContent = Get-Content "$env:TEMP\ventoy_err.txt" -ErrorAction SilentlyContinue
        Write-Error "Ventoy USB hazırlama başarısız! Hata: $errorContent"
        exit 1
    }
    Write-Output "Ventoy kurulumu başarılı."
    Write-Output "ISO kopyalanıyor: $IsoPath -> $UsbDrive"
    $targetPath = Join-Path "$($UsbDrive):\" (Split-Path $IsoPath -Leaf)
    Copy-Item -Path $IsoPath -Destination $targetPath -Force
    Write-Output "ISO USB'ye başarıyla yazıldı!"
}
catch {
    Write-Error "Hata oluştu: $($_.Exception.Message)"
    exit 1
}
finally {
    # Temizlik
    Remove-Item -Path $tempVentoyPath -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "$env:TEMP\ventoy_out.txt", "$env:TEMP\ventoy_err.txt" -Force -ErrorAction SilentlyContinue
}
'@
Set-Content -Path "C:\Users\pc\source\repos\WinFastGUI\bin\Debug\net8.0-windows\Modules\Write-IsoToUsb.ps1" -Value $scriptContent -Encoding UTF8