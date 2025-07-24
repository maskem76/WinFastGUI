@echo off
echo Varsayılan Guc Planları Ekleniyor...

powercfg -import "%~dp0\Balanced.pow
powercfg -import "%~dp0\High Peformance.pow
powercfg -import "%~dp0\Power saver.pow
control powercfg.cpl
exit

