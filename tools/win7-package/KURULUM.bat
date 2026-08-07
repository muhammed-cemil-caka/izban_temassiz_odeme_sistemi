@echo off
title IZBAN Kiosk - Otomat Kurulumu
mode con: cols=78 lines=44
cls

net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo   Bu dosyayi YONETICI OLARAK calistirin.
  echo   Sag tik -^> "Yonetici olarak calistir"
  echo.
  pause
  exit /b 1
)

cd /d "%~dp0"
set HATA=0

echo.
echo  ==============================================================
echo    IZBAN KIOSK - YENI OTOMAT KURULUMU
echo  ==============================================================
echo.
echo   Bu betik otomatin on gereksinimlerini hazirlar. Bir kez
echo   calistirilir; sonrasinda uygulama kendini gunceller.
echo.

echo  --------------------------------------------------------------
echo   1/4  Windows surumu
echo  --------------------------------------------------------------
for /f "tokens=2 delims=[]" %%a in ('ver') do echo        %%a
wmic os get ServicePackMajorVersion /value 2>nul | find "ServicePackMajorVersion"
echo        .NET 4.8 icin Service Pack 1 gereklidir.
echo.

echo  --------------------------------------------------------------
echo   2/4  TLS 1.2
echo  --------------------------------------------------------------
reg add "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client" /v Enabled /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client" /v DisabledByDefault /t REG_DWORD /d 0 /f >nul
reg add "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server" /v Enabled /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server" /v DisabledByDefault /t REG_DWORD /d 0 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319" /v SchUseStrongCrypto /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319" /v SystemDefaultTlsVersions /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Wow6432Node\Microsoft\.NETFramework\v4.0.30319" /v SchUseStrongCrypto /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Wow6432Node\Microsoft\.NETFramework\v4.0.30319" /v SystemDefaultTlsVersions /t REG_DWORD /d 1 /f >nul
echo        [TAMAM] Windows'ta TLS 1.2 acildi.
echo.

echo  --------------------------------------------------------------
echo   3/4  Kok sertifika
echo  --------------------------------------------------------------
if not exist "ISRG-Root-X1.crt" (
  echo        [HATA] ISRG-Root-X1.crt bu klasorde yok.
  set HATA=1
  goto :net
)
certutil -addstore -f root "ISRG-Root-X1.crt" >nul 2>&1
if errorlevel 1 (
  echo        [HATA] Sertifika eklenemedi.
  set HATA=1
) else (
  echo        [TAMAM] ISRG Root X1 guvenilen koke eklendi.
  echo               GitHub'in dosya sunucusu bu sertifikayi kullanir;
  echo               olmadan guncelleme indirilemez.
)
echo.

:net
echo  --------------------------------------------------------------
echo   4/4  .NET Framework
echo  --------------------------------------------------------------
set NETOK=
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find "Release"') do set NETOK=%%a

if not "%NETOK%"=="" (
  set /a DEC=%NETOK%
  echo        [TAMAM] .NET Framework 4.5 veya ustu kurulu.
  goto :sonuc
)

echo        .NET Framework 4.5+ kurulu degil. TLS 1.2 icin gereklidir.
echo.
if exist "ndp48-x86-x64-allos-enu.exe" (
  echo        Kurulum dosyasi bulundu, baslatiliyor...
  echo        Bu islem 10-20 dakika surebilir.
  echo.
  start /wait "" "ndp48-x86-x64-allos-enu.exe" /passive /norestart
  echo        [TAMAM] .NET kurulumu bitti.
) else (
  echo        [EKSIK] ndp48-x86-x64-allos-enu.exe bu klasorde yok.
  echo                Dosyayi bu klasore koyup betigi tekrar calistirin.
  echo                Indirme: dotnet.microsoft.com/download/dotnet-framework/net48
  echo                ^(Web installer degil, OFFLINE installer - yaklasik 110 MB^)
  set HATA=1
)
echo.

:sonuc
echo  ==============================================================
if "%HATA%"=="1" (
  echo    EKSIK ADIM VAR - yukaridaki [HATA] / [EKSIK] satirlarina bakin
) else (
  echo    KURULUM TAMAM
)
echo  ==============================================================
echo.
echo   SIRADAKI ADIMLAR
echo.
echo    1. Otomati YENIDEN BASLATIN.
echo    2. 1-Yazicilari-Listele.bat ile yazici adini ogrenip
echo       KioskHardware.config.json icine yazin.
echo    3. IZBAN-Kiosk.exe -^> SISTEM TANILA -^> SIMDI KONTROL ET
echo       "GitHub'a erisim: BASARILI" gormeniz gerekir.
echo.
echo   Bundan sonra uygulama kendini gunceller; bu otomata bir daha
echo   gelmeniz gerekmez.
echo.
pause
