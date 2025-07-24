function Remove-BloatwareApp {
    param ([string]$AppName)

    try {
        $app = Get-AppxPackage -Name $AppName -AllUsers -ErrorAction Stop
        if ($app) {
            Remove-AppxPackage -Package $app.PackageFullName -ErrorAction Stop
            Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like "*$AppName*" } | Remove-AppxProvisionedPackage -Online -ErrorAction Stop
            Write-Output "✅ Kaldırıldı: $AppName"
        } else {
            Write-Output "⚠️ $AppName bulunamadı."
        }
    } catch {
        Write-Output "❌ Kaldırma hatası: $AppName - $($_.Exception.Message)"
    }
}

function Restore-BloatwareApp {
    param ([string]$AppName)
    try {
        $manifestPath = (Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like "*$AppName*" }).PackageName
        if ($manifestPath) {
            $installPath = "$env:ProgramFiles\WindowsApps\$manifestPath"
            if (Test-Path $installPath) {
                Add-AppxPackage -DisableDevelopmentMode -Register "$installPath\AppXManifest.xml" -ErrorAction Stop
                Write-Output "✅ Geri yüklendi: $AppName"
            } else {
                Write-Output "⚠️ $AppName için manifest yolu bulunamadı."
            }
        } else {
            Write-Output "⚠️ $AppName geri yükleme için uygun değil."
        }
    } catch {
        Write-Output "❌ Geri yükleme hatası: $AppName - $($_.Exception.Message)"
    }
}

# ZARARSIZLAR HARİÇ HER ŞEYİ KALDIR (whitelist sadece sistem/çekirdek uygulamalar olmalı)
[regex]$WhitelistedApps = 'Microsoft.WindowsCalculator|Microsoft.WindowsStore|Microsoft.Windows.Photos|Microsoft.WindowsNotepad|Microsoft.WindowsCamera|Microsoft.MSPaint|Microsoft.MicrosoftStickyNotes|Microsoft.WindowsTerminal|Microsoft.XboxGameCallableUI'

function Remove-AllBloatware {
    Get-AppxPackage -AllUsers | Where-Object { $_.Name -NotMatch $WhitelistedApps } | Remove-AppxPackage -ErrorAction SilentlyContinue
    Get-AppxProvisionedPackage -Online | Where-Object { $_.PackageName -NotMatch $WhitelistedApps } | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue
    Write-Output "✅ Tüm bloatware (whitelist hariç) kaldırıldı."
}

Export-ModuleMember -Function Remove-BloatwareApp, Restore-BloatwareApp, Remove-AllBloatware
