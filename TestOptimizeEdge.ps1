# TestOptimizeEdge.ps1
# Doğrudan kilitli Edge dosyalarını ve paketlerini kaldırmayı dener

function Force-TerminateProcesses {
    param([string[]]$Names)
    foreach($n in $Names) {
        Write-Output "[INFO] taskkill /f /im $n"
        taskkill /f /im $n /t 2>$null
    }
}

function Force-DeleteServices {
    param([string[]]$Names)
    foreach($s in $Names) {
        Write-Output "[INFO] sc.exe delete $s"
        sc.exe delete $s 2>$null
    }
}

function Force-RemoveAppx {
    $pkgs = Get-AppxPackage -AllUsers "*MicrosoftEdge*" | Select-Object -ExpandProperty PackageFullName
    foreach($pkg in $pkgs) {
        Write-Output "[INFO] Remove-AppxPackage -AllUsers -Package $pkg"
        try {
            Remove-AppxPackage -AllUsers -Package $pkg -ErrorAction Stop
            Write-Output "[SUCCESS] Package removed: $pkg"
        } catch {
            Write-Warning "[WARN] Paket kaldırılamadı: $pkg"
        }
    }
}

function Force-DeleteFolders {
    param([string[]]$Paths)
    foreach($p in $Paths) {
        if(Test-Path $p) {
            Write-Output "[INFO] takeown /f `"$p`" /r /d Y"
            takeown /f "$p" /r /d Y 2>$null
            Write-Output "[INFO] icacls `"$p`" /grant Administrators:F /t"
            icacls "$p" /grant Administrators:F /t 2>$null
            Write-Output "[INFO] rd /s /q `"$p`""
            rd /s /q "$p" 2>$null
            if(Test-Path $p) {
                Write-Warning "[WARN] Klasör kalıntı: $p"
            } else {
                Write-Output "[SUCCESS] Klasör silindi: $p"
            }
        }
    }
}

# 1) Süreçleri öldür
Force-TerminateProcesses -Names @("msedge.exe","msedgewebview2.exe","MicrosoftEdgeUpdate.exe","edgeupdate.exe")

# 2) Hizmetleri sil
Force-DeleteServices -Names @("edgeupdate","edgeupdatem","MicrosoftEdgeElevationService")

# 3) AppX paketlerini kaldır
Force-RemoveAppx

# 4) Dosya sistemi klasörlerini sil
Force-DeleteFolders -Paths @(
    "C:\Program Files (x86)\Microsoft\Edge",
    "C:\Program Files (x86)\Microsoft\EdgeWebView"
)

Write-Output "[INFO] Test tamamlandı. Çıkış kodu: $LASTEXITCODE"
