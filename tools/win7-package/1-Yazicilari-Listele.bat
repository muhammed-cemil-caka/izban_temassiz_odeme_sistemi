@echo off
title IZBAN Kiosk - Kurulu Yazicilar
cd /d "%~dp0"
echo.
echo  Windows'ta kurulu yazicilar ve mevcut varsayilan yazici listeleniyor...
echo  ------------------------------------------------------------------
echo.
Bridge\IzbanKiosk.LegacyHardwareBridge.exe --list-printers
echo.
echo  ------------------------------------------------------------------
echo  InstalledPrinters listesindeki adi birebir kopyalayip
echo  KioskHardware.config.json dosyasindaki ThermalPrinterName
echo  alanina yazin. Surucu adi degil, KUYRUK adi olmalidir.
echo.
pause
