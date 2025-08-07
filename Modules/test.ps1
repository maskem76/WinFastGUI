<#
.SYNOPSIS
    WinFastGUI için test scripti
.DESCRIPTION
    NSudo ile SYSTEM yetkisi altında dosya oluşturma, silme, whoami ve optimizasyon testlerini yapar.
#>

param (
    [Parameter(Mandatory=$true)]
    [string]$TempLogPath,
    [string]$NSudoPath = "C:\Users\pc\source\repos\WinFastGUI\Modules\nsudo\NSudo.exe",
    [string]$ExecutorPath = "C:\Users\pc\source\repos\WinFastGUI\Modules\Executor.ps1"
)

function Write-Log {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,
        [ValidateSet("INFO","SUCCESS","WARNING","ERROR")]
        [string]$Level = "INFO"
    )
    
    try {
        $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        $logEntry = "[$timestamp][$Level] $Message"
        
        switch ($Level) {
            "ERROR"   { Write-Host $logEntry -ForegroundColor Red }
            "WARNING" { Write-Host $logEntry -ForegroundColor Yellow }
            "SUCCESS" { Write-Host $logEntry -ForegroundColor Green }
            default   { Write-Host $logEntry }
        }
        
        # Dosya kilidini kontrol et ve yeniden dene
        $retryCount = 3
        $retryDelay = 500
        for ($i = 1; $i -le $retryCount; $i++) {
            try {
                $logEntry | Out-File -FilePath $TempLogPath -Append -Encoding UTF8 -ErrorAction Stop
                break
            } catch {
                if ($i -eq $retryCount) {
                    Write-Host "Log yazma hatası (son deneme): $_" -ForegroundColor Red
                    return
                }
                Write-Host "Log yazma hatası (deneme $i/$retryCount): $_, tekrar deneniyor..." -ForegroundColor Yellow
                Start-Sleep -Milliseconds $retryDelay
            }
        }
    } catch {
        Write-Host "Kritik log yazma hatası: $_" -ForegroundColor Red
    }
}

Write-Log "NSudo testi başladı"

# Test 1: System32'de dosya oluşturma
Write-Log "Test 1: System32'de dosya oluşturma"
try {
    $command = "cmd /c echo WinFastGUI Test > C:\Windows\System32\test.log"
    $encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($command))
    $psi = Start-Process -FilePath $NSudoPath -ArgumentList "-U:S -P:E -M:S -ShowWindowMode:Show -Wait powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand" -PassThru -NoNewWindow -RedirectStandardOutput "$TempLogPath" -RedirectStandardError "$TempLogPath" -ErrorAction Stop
    $psi.WaitForExit()
    
    if ($psi.ExitCode -eq 0 -and (Test-Path "C:\Windows\System32\test.log")) {
        Write-Log "Başarı: Dosya oluşturuldu" -Level "SUCCESS"
        Write-Log "test.log içeriği: $(Get-Content -Path 'C:\Windows\System32\test.log' -ErrorAction SilentlyContinue)" -Level "INFO"
    } else {
        Write-Log "Hata: Dosya oluşturma başarısız, çıkış kodu: $($psi.ExitCode)" -Level "ERROR"
    }
} catch {
    Write-Log "Hata: Dosya oluşturma başarısız: $_" -Level "ERROR"
}

# Test 2: System32'de dosya silme
Write-Log "Test 2: System32'de dosya silme"
try {
    $command = "cmd /c del C:\Windows\System32\test.log"
    $encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($command))
    $psi = Start-Process -FilePath $NSudoPath -ArgumentList "-U:S -P:E -M:S -ShowWindowMode:Show -Wait powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand" -PassThru -NoNewWindow -RedirectStandardOutput "$TempLogPath" -RedirectStandardError "$TempLogPath" -ErrorAction Stop
    $psi.WaitForExit()
    
    if ($psi.ExitCode -eq 0 -and -not (Test-Path "C:\Windows\System32\test.log")) {
        Write-Log "Başarı: Dosya silindi" -Level "SUCCESS"
    } else {
        Write-Log "Hata: Dosya silme başarısız, çıkış kodu: $($psi.ExitCode)" -Level "ERROR"
    }
} catch {
    Write-Log "Hata: Dosya silme başarısız: $_" -Level "ERROR"
}

# Test 3: whoami testi
Write-Log "Test 3: whoami testi"
try {
    $command = "whoami | Out-File -FilePath '$TempLogPath' -Append -Encoding utf8"
    $encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($command))
    $psi = Start-Process -FilePath $NSudoPath -ArgumentList "-U:S -P:E -M:S -ShowWindowMode:Show -Wait powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand" -PassThru -NoNewWindow -RedirectStandardOutput "$TempLogPath" -RedirectStandardError "$TempLogPath" -ErrorAction Stop
    $psi.WaitForExit()
    
    if ($psi.ExitCode -eq 0) {
        Write-Log "Başarı: whoami tamamlandı" -Level "SUCCESS"
        Write-Log "whoami çıktısı: $(Get-Content -Path '$TempLogPath' -Tail 1 -ErrorAction SilentlyContinue)" -Level "INFO"
    } else {
        Write-Log "Hata: whoami başarısız, çıkış kodu: $($psi.ExitCode)" -Level "ERROR"
    }
} catch {
    Write-Log "Hata: whoami başarısız: $_" -Level "ERROR"
}

# Test 4: Disable-Telemetry testi
Write-Log "Test 4: Disable-Telemetry testi"
try {
    $command = "& { & '$ExecutorPath' -OptimizationCommand 'Disable-Telemetry' -TempLogPath '$TempLogPath' } *>&1"
    $encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($command))
    $psi = Start-Process -FilePath $NSudoPath -ArgumentList "-U:S -P:E -M:S -ShowWindowMode:Show -Wait powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand" -PassThru -NoNewWindow -RedirectStandardOutput "$TempLogPath" -RedirectStandardError "$TempLogPath" -ErrorAction Stop
    $psi.WaitForExit()
    
    if ($psi.ExitCode -eq 0) {
        Write-Log "Başarı: Disable-Telemetry tamamlandı" -Level "SUCCESS"
    } else {
        Write-Log "Hata: Disable-Telemetry başarısız, çıkış kodu: $($psi.ExitCode)" -Level "ERROR"
    }
} catch {
    Write-Log "Hata: Disable-Telemetry başarısız: $_" -Level "ERROR"
}

# Test 5: Optimize-SystemPerformance testi
Write-Log "Test 5: Optimize-SystemPerformance testi"
try {
    $command = "& { & '$ExecutorPath' -OptimizationCommand 'Optimize-SystemPerformance' -TempLogPath '$TempLogPath' } *>&1"
    $encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($command))
    $psi = Start-Process -FilePath $NSudoPath -ArgumentList "-U:S -P:E -M:S -ShowWindowMode:Show -Wait powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand" -PassThru -NoNewWindow -RedirectStandardOutput "$TempLogPath" -RedirectStandardError "$TempLogPath" -ErrorAction Stop
    $psi.WaitForExit()
    
    if ($psi.ExitCode -eq 0) {
        Write-Log "Başarı: Optimize-SystemPerformance tamamlandı" -Level "SUCCESS"
    } else {
        Write-Log "Hata: Optimize-SystemPerformance başarısız, çıkış kodu: $($psi.ExitCode)" -Level "ERROR"
    }
} catch {
    Write-Log "Hata: Optimize-SystemPerformance başarısız: $_" -Level "ERROR"
}

Write-Log "Test tamamlandı, loglar kontrol ediliyor"