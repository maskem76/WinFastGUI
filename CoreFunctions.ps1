function Invoke-AppUninstallMsi {
    param ([string]$ProductCode, [string]$AppName, [string]$Vendor)
    $args = "/x $ProductCode /qn"
    Start-Process "msiexec.exe" -ArgumentList $args -Wait -NoNewWindow
}

function Invoke-AppUninstallExe {
    param ([string]$UninstallString, [string]$AppName, [string]$Vendor)
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c $UninstallString" -Wait -NoNewWindow
}

function Remove-AppRemnants {
    param ([string]$appName, [string]$vendor)
    # Artık temizleme mantığını buraya ekle (örneğin, registry ve dosya temizliği)
}