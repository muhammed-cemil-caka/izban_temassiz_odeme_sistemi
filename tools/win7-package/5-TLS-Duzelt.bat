@echo off
title IZBAN Kiosk - TLS 1.2 Tanilama ve Duzeltme
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo  Bu dosyayi YONETICI OLARAK calistirin.
  echo  Sag tik -^> "Yonetici olarak calistir"
  echo.
  pause
  exit /b 1
)

echo.
echo  ============================================================
echo   1. BOLUM - TANILAMA
echo  ============================================================
echo.
echo  Kurulu .NET Framework 4.x surumu:
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2>nul
if errorlevel 1 (
  echo.
  echo    ^>^>^> .NET Framework 4.5 veya ustu KURULU DEGIL.
  echo    ^>^>^> Otomatik guncelleme icin .NET Framework 4.8 kurulmalidir.
  echo    ^>^>^> https://dotnet.microsoft.com/download/dotnet-framework/net48
  echo.
) else (
  echo.
  echo    Release degeri 378389 veya uzeri ise 4.5+ kurulu demektir.
  echo    ^(4.8 = 528040 ve uzeri^)
  echo.
)

echo.
echo  ============================================================
echo   2. BOLUM - TLS 1.2'yi ETKINLESTIR
echo  ============================================================
echo.
echo  Windows 7'de TLS 1.2 desteklenir ama varsayilan olarak kapalidir.
echo  Asagidaki kayitlar onu acar. Guvenligi ZAYIFLATMAZ, guclendirir.
echo.

reg add "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client" /v Enabled /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client" /v DisabledByDefault /t REG_DWORD /d 0 /f >nul
reg add "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server" /v Enabled /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server" /v DisabledByDefault /t REG_DWORD /d 0 /f >nul
echo    [TAMAM] Windows SCHANNEL TLS 1.2 acildi.

reg add "HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319" /v SchUseStrongCrypto /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319" /v SystemDefaultTlsVersions /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Wow6432Node\Microsoft\.NETFramework\v4.0.30319" /v SchUseStrongCrypto /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Wow6432Node\Microsoft\.NETFramework\v4.0.30319" /v SystemDefaultTlsVersions /t REG_DWORD /d 1 /f >nul
echo    [TAMAM] .NET Framework guclu sifreleme acildi.

echo.
echo  ============================================================
echo   3. BOLUM - SIRADAKI ADIM
echo  ============================================================
echo.
echo   1. Otomati YENIDEN BASLATIN. Kayit degisiklikleri ancak
echo      yeniden baslatmadan sonra gecerli olur.
echo   2. IZBAN-Kiosk.exe -^> SISTEM TANILA -^> SIMDI KONTROL ET
echo   3. "GitHub'a erisim: BASARILI" gormeniz gerekir.
echo.
echo   Hala BASARISIZ ise: 1. bolumde .NET 4.5+ kurulu degil
echo   yaziyorsa once .NET Framework 4.8 kurun, sonra tekrar deneyin.
echo.
pause
