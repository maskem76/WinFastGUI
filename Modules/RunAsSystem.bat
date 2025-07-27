@echo off
set ArchDir=x64

REM Bu script, kendisine gonderilen tum parametreleri (%*)
REM dogrudan NSudoLC.exe'ye yonlendirir.
REM Boylece C#'tan gelen PowerShell komutu calistirilabilir.

"%~dp0\nsudo\%ArchDir%\NSudoLC.exe" %*