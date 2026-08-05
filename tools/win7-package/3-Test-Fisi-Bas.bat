@echo off
title IZBAN Kiosk - Test Fisi
cd /d "%~dp0"
echo.
echo  DIKKAT: Bu islem yapilandirilan termal yaziciyi Windows
echo  varsayilan yazicisi yapar. KioskPrint.dll baska bir yaziciya
echo  gonderemez, bu yuzden zorunludur.
echo.
pause
echo.
Bridge\IzbanKiosk.LegacyHardwareBridge.exe --print-test
echo.
echo  ------------------------------------------------------------------
echo  Cikis kodu: %ERRORLEVEL%   (0 = is kuyruga verildi)
echo.
echo  API basarisi yalnizca isin kuyruga verildigini gosterir.
echo  YAZICIDAN FIZIKSEL KAGIT CIKTI MI, MUTLAKA KONTROL EDIN.
echo.
pause
