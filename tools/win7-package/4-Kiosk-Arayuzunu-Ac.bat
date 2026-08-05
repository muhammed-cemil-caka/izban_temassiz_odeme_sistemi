@echo off
title IZBAN Kiosk - Donanim Test Arayuzu
cd /d "%~dp0"
echo.
echo  DIKKAT: Bu arayuz NFC okuyucuyu da acar.
echo  Once AUSKiosk.exe uygulamasini kapatin, aksi halde
echo  COM portu mesgul olur ve okuyucu acilamaz.
echo.
pause
start "" "IZBAN-Kiosk.exe"
