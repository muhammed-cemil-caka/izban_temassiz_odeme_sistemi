@echo off
title IZBAN Kiosk - Termal Yazici Teshisi
cd /d "%~dp0"
echo.
echo  Termal yazici teshis raporu olusturuluyor...
echo  ------------------------------------------------------------------
echo.
Bridge\IzbanKiosk.LegacyHardwareBridge.exe --printer-diagnose
echo.
echo  ------------------------------------------------------------------
echo  Cikis kodu: %ERRORLEVEL%   (0 = yazici hazir)
echo.
echo  Alan anlamlari:
echo    IsInstalled=false               -^> yapilandirilan ad kurulu degil
echo    DefaultPrinterRoutingApplied=false -^> varsayilan yazici degistirilemedi
echo    SpoolerStatusFlags 16           -^> kagit bitti
echo    SpoolerStatusFlags 128          -^> yazici cevrimdisi
echo    SpoolerStatusFlags 4194304      -^> kapak acik
echo    VendorQueuedJobCount ^> 3        -^> kuyrukta is birikmis
echo.
pause
