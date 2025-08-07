[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$OutputIsoPath, # Kullanıcının nereye kaydedeceği

    [Parameter(Mandatory=$true)]
    [string]$ModulesPath
)

# Değişkenler
$workspace = Join-Path $env:TEMP "ISO_Workspace"
$tempWimPath = Join-Path $workspace "install.wim" # wim dosyasını geçici olarak buraya oluşturacağız
$isoSourcePath = Join-Path $workspace "ISO_Source"
$oscdimgExe = Join-Path $ModulesPath "oscdimg\x64\oscdimg.exe" # 64-bit varsayıyoruz

Write-Host "Tüm işlemler başlıyor... Çalışma alanı: $workspace"

try {
    # 0. Çalışma Alanını Temizle
    if (Test-Path $workspace) { Remove-Item -Path $workspace -Recurse -Force }
    New-Item -ItemType Directory -Path $workspace, $isoSourcePath | Out-Null

    # 1. VSS Snapshot Oluştur
    Write-Host "VSS Snapshot oluşturuluyor..."
    $shadow = (Get-WmiObject -List Win32_ShadowCopy).Create("C:\", "ClientAccessible")
    if ($shadow.ReturnValue -ne 0) { throw "VSS Snapshot oluşturulamadı. Kod: $($shadow.ReturnValue)" }
    $shadowId = $shadow.ShadowID
    $shadowPath = (Get-WmiObject Win32_ShadowCopy | Where-Object { $_.ID -eq $shadowId }).DeviceObject
    Write-Host "Snapshot hazır: $shadowPath"

    # 2. DISM ile İmajı AL (Geçici Konuma)
    Write-Host "DISM ile imaj alınıyor..."
    $dismExe = Join-Path $env:SystemRoot "System32\dism.exe"
    $exclusionFile = Join-Path $ModulesPath "wimscript.ini" # Ana dizindeki wimscript.ini'yi kullanır
    $dismArgs = "/Capture-Image /ImageFile:`"$tempWimPath`" /CaptureDir:`"$($shadowPath)\`" /Name:`"WinFastYedek`" /Compress:Max /CheckIntegrity"
    if(Test-Path $exclusionFile) { $dismArgs += " /ConfigFile:`"$exclusionFile`"" }
    
    Start-Process -FilePath $dismExe -ArgumentList $dismArgs -Wait -NoNewWindow

    # 3. WIM Dosyasının İzinlerini Düzelt (Kendi içinde)
    Write-Host "Oluşturulan WIM dosyasının izinleri düzeltiliyor..."
    takeown.exe /F $tempWimPath /A
    icacls.exe $tempWimPath /grant "Users:(F)"

    # 4. Sürümü Tespit Et ve Doğru Template'i Aç
    Write-Host "Windows sürümü tespit ediliyor..."
    $imageInfo = Dism /Get-ImageInfo /ImageFile:$tempWimPath
    $winVersion = if ($imageInfo -match "Windows 11") { "11" } else { "10" }
    Write-Host "Tespit edilen sürüm: Windows $winVersion"
    $templateZip = Join-Path $ModulesPath "MinimalISO_Template_$($winVersion).zip"
    Expand-Archive -Path $templateZip -DestinationPath $isoSourcePath -Force

    # 5. İzinleri Düzeltilmiş WIM'i Kopyala
    Write-Host "WIM dosyası ISO kaynak klasörüne taşınıyor..."
    Move-Item -Path $tempWimPath -Destination (Join-Path $isoSourcePath "sources\install.wim") -Force

    # 6. ISO Oluştur
    Write-Host "Boot edilebilir ISO dosyası oluşturuluyor..."
    $bootData = Join-Path $isoSourcePath "boot\etfsboot.com"
    $oscdimgArgs = "-h", "-m", "-o", "-u2", "-b`"$bootData`"", "`"$isoSourcePath`"", "`"$OutputIsoPath`""
    & $oscdimgExe $oscdimgArgs

    Write-Host "TÜM İŞLEMLER BAŞARIYLA TAMAMLANDI!"
    Write-Host "ISO dosyanız burada: $OutputIsoPath"

} catch {
    Write-Error "İşlem sırasında kritik bir hata oluştu: $_"
    exit 1
} finally {
    # 7. Temizlik
    Write-Host "Tüm geçici dosyalar ve VSS kopyaları temizleniyor..."
    if ($shadowId) { vssadmin.exe delete shadows /shadow={$shadowId} /quiet }
    if (Test-Path $workspace) { Remove-Item -Path $workspace -Recurse -Force }
}