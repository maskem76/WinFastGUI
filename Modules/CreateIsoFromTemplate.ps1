[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$TemplateZipPath,
    [Parameter(Mandatory=$true)][string]$SourceWimPath,
    [Parameter(Mandatory=$true)][string]$OutputIsoPath,
    [Parameter(Mandatory=$true)][string]$ModulesPath
)

$arch = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
$oscdimgFolder = Join-Path $ModulesPath "x64" # Gerekirse dinamik değiştir
$oscdimgExe = Join-Path $oscdimgFolder "oscdimg.exe"
if (-not (Test-Path $oscdimgExe)) { throw "oscdimg.exe bulunamadı: $oscdimgExe" }
$workspace = Join-Path $env:TEMP "ISO_Workspace"
if (Test-Path $workspace) { Remove-Item -Path $workspace -Recurse -Force }
New-Item -Path $workspace -ItemType Directory | Out-Null
Expand-Archive -Path $TemplateZipPath -DestinationPath $workspace -Force
$targetWimPath = Join-Path $workspace "sources\install.wim"
Copy-Item -Path $SourceWimPath -Destination $targetWimPath -Force
$bootData = Join-Path $workspace "boot\etfsboot.com"
$efiData = Join-Path $workspace "efi\Microsoft\boot\efisys.bin"
Push-Location $oscdimgFolder
& $oscdimgExe -b"$bootData" -u2 -udfver102 -bootdata:2#p0,e,b"$bootData"#pEF,e,b"$efiData" "$workspace" "$OutputIsoPath"
Pop-Location
Remove-Item -Path $workspace -Recurse -Force
