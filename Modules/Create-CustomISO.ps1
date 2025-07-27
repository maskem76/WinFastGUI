param (
    [Parameter(Mandatory=$true)]
    [string]$TemplateZipPath,
    [Parameter(Mandatory=$true)]
    [string]$SourceWimPath,
    [Parameter(Mandatory=$true)]
    [string]$OutputIsoPath,
    [Parameter(Mandatory=$true)]
    [string]$ModulesPath,
    [string]$UnattendPath
)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try {
    Write-Output "ISO oluşturma işlemi başlatılıyor..."
    $tempDir = Join-Path $env:TEMP "WinFastISO_$(Get-Random)"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    Write-Output "Şablon ZIP dosyası çıkarılıyor: $TemplateZipPath"
    Expand-Archive -Path $TemplateZipPath -DestinationPath $tempDir -Force

    # sources klasörü oluştur
    $sourcesDir = Join-Path $tempDir "sources"
    if (-not (Test-Path $sourcesDir)) {
        New-Item -ItemType Directory -Path $sourcesDir -Force | Out-Null
    }

    Write-Output "WIM dosyası kopyalanıyor: $SourceWimPath"
    Copy-Item -Path $SourceWimPath -Destination (Join-Path $sourcesDir "install.wim") -Force

    # === Unattend.xml EKLEME OTOMATİĞİ ===
    if ($UnattendPath -and (Test-Path $UnattendPath)) {
        Copy-Item -Path $UnattendPath -Destination (Join-Path $sourcesDir "unattend.xml") -Force
        Write-Output "unattend.xml başarıyla ISO'ya eklendi."
    } else {
        Write-Output "unattend.xml dosyası yok/yolu yanlış, eklenmedi."
    }

    Write-Output "ISO dosyası oluşturuluyor: $OutputIsoPath"
    $oscdimgPath = Join-Path $ModulesPath "oscdimg.exe"
    if (-not (Test-Path $oscdimgPath)) {
        Write-Error "HATA: oscdimg.exe bulunamadı: $oscdimgPath"
        exit 1
    }

    $arguments = "-m -o -u2 -udfver102 -bootdata:2#p0,e,b$(Join-Path $tempDir 'boot\etfsboot.com')#pEF,e,b$(Join-Path $tempDir 'efi\microsoft\boot\efisys.bin') -lWinFastISO `"$tempDir`" `"$OutputIsoPath`""
    Start-Process -FilePath $oscdimgPath -ArgumentList $arguments -Wait -NoNewWindow

    Write-Output "ISO başarıyla oluşturuldu: $OutputIsoPath"
    Remove-Item -Path $tempDir -Recurse -Force
}
catch {
    Write-Error "HATA: ISO oluşturma başarısız: $($_.Exception.Message)"
    if ($tempDir -and (Test-Path $tempDir)) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    exit 1
}
