@echo off
setlocal enabledelayedexpansion

:: NSudo'nun tam yolunu belirle
set "NSudoPath=%~dp0nsudo\NSudo.exe"

:: Parametre kontrolü
if "%~1"=="" (
    echo Kullanım: %~nx0 "komut"
    exit /b 1
)

:: Komutu çalıştır
echo Komut çalıştırılıyor: %*
if exist "!NSudoPath!" (
    "!NSudoPath!" -U:S -P:E -Wait cmd /c %*
) else (
    echo Hata: NSudo.exe bulunamadı: !NSudoPath!
    exit /b 1
)

endlocal