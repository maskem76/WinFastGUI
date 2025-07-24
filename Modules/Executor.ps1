param (
    [string][Parameter(Mandatory=$true)]
    $OptimizationCommand
)

$logFilePath = Join-Path $PSScriptRoot "PS1_CALIŞTI.txt"
Import-Module (Join-Path $PSScriptRoot "OptimizationTweaks.psm1") -Force -ErrorAction Stop -DisableNameChecking
"BAŞLADI: $OptimizationCommand $(Get-Date)" | Out-File -FilePath $logFilePath -Append

# Komutun çıktısını al ve hata koduna bak
$result = & (Get-Command $OptimizationCommand -ErrorAction Stop) 2>&1 | Out-String
if ($LASTEXITCODE -eq 0) {
    "$OptimizationCommand - Çıktı: $result $(Get-Date)" | Out-File -FilePath $logFilePath -Append
} else {
    "$OptimizationCommand - HATA: $result $(Get-Date)" | Out-File -FilePath $logFilePath -Append
}
