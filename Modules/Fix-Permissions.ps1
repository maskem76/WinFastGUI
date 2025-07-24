param (
    [Parameter(Mandatory=$true)]
    [string]$FilePath
)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try {
    Write-Output "Dosya izinleri ayarlanıyor: $FilePath"
    $acl = Get-Acl -Path $FilePath
    $everyone = New-Object System.Security.Principal.SecurityIdentifier("S-1-1-0")
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($everyone, "Read", "Allow")
    $acl.AddAccessRule($accessRule)
    Set-Acl -Path $FilePath -AclObject $acl
    Write-Output "Herkes için okuma izni eklendi."

    # Ekstra güvenlik için icacls ile izinleri sıfırla ve herkese okuma izni ver
    $icaclsCommand = "icacls `"$FilePath`" /reset"
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c $icaclsCommand" -Wait -NoNewWindow
    $icaclsCommand = "icacls `"$FilePath`" /grant *S-1-1-0:R /T"
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c $icaclsCommand" -Wait -NoNewWindow
    Write-Output "Dosya izinleri icacls ile sıfırlandı ve herkes için okuma izni ayarlandı."
}
catch {
    Write-Error "HATA: Dosya izinleri ayarlanamadı: $($_.Exception.Message)"
    exit 1
}