param($savePath, $format)
try {
    $wimlibPath = Join-Path $PSScriptRoot "wimlib-imagex.exe"
    $vssInfo = (vssadmin create shadow /for=C:) | Out-String
    $shadowLine = ($vssInfo -split "`n" | Where-Object {$_ -like "*Shadow Copy Volume*"})
    $shadowDev = $shadowLine -replace '.*Volume: ', '' -replace '\s',''
    mountvol X: $shadowDev

    $compress = ($format -eq "ESD") ? "--compress=LZMS" : "--compress=LZX"
    & "$wimlibPath" capture X:\ "$savePath" "VSS_WimlibBackup" --snapshot $compress --check

    mountvol X: /D
    Write-Host "İmaj alma tamamlandı!"
} catch {
    Write-Host "[HATA]: $($_.Exception.Message)"
    mountvol X: /D
}
