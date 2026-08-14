@echo off
title IZBAN Kiosk - Saat ve Tarih Duzeltme
cd /d "%~dp0"

REM ---------------------------------------------------------------
REM Yanlis saat, otomatin sessizce guncelleme almayi birakmasinin en
REM sik sebebi. GitHub'in sertifikasi sistem saatine gore dogrulanir;
REM 2010'da oldugunu sanan bir makine 2026'da uretilmis sertifikayi
REM reddeder ve ekranda sadece "baglanti kurulamadi" yazar. Ne tarihi
REM ne de cozumu soyleyen bir hatadir, o yuzden ayri bir betik.
REM ---------------------------------------------------------------

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
echo   1. BOLUM - SIMDIKI DURUM
echo  ============================================================
echo.
echo   Otomatin saati : %DATE% %TIME%
for /f "tokens=2,*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\TimeZoneInformation" /v TimeZoneKeyName 2^>nul ^| find "TimeZoneKeyName"') do echo   Saat dilimi    : %%b
echo.
echo   Yukaridaki tarih BUGUNUN tarihi degilse devam edin.
echo.

echo  ============================================================
echo   2. BOLUM - SAAT DILIMI
echo  ============================================================
echo.
REM Turkiye 2016'da kalici UTC+3'e gecti. Yamalanmamis bir Windows 7
REM bunu bilmez ve "Turkey Standard Time" dilimi makinede yoktur; o
REM zaman eski GTB dilimine dusup yaz saati kaydirmasini kapatiyoruz,
REM yoksa saat yilda iki kez kendiliginden bir saat kayar.
tzutil /s "Turkey Standard Time" >nul 2>&1
if errorlevel 1 (
  echo   [BILGI] Bu Windows "Turkey Standard Time" dilimini tanimiyor.
  echo           Eski dilim kullanilacak ve yaz saati kaydirmasi
  echo           kapatilacak.
  tzutil /s "GTB Standard Time" >nul 2>&1
  reg add "HKLM\SYSTEM\CurrentControlSet\Control\TimeZoneInformation" /v DynamicDaylightTimeDisabled /t REG_DWORD /d 1 /f >nul 2>&1
  echo   [TAMAM] GTB Standard Time, otomatik yaz saati KAPALI.
) else (
  echo   [TAMAM] Saat dilimi: Turkey Standard Time ^(UTC+3^).
)
echo.

echo  ============================================================
echo   3. BOLUM - WINDOWS ZAMAN SERVISI
echo  ============================================================
echo.
REM w32time varsayilan olarak buyuk duzeltmeleri REDDEDER. Pili bitmis
REM bir makinede duzeltme her zaman buyuktur, yani bu iki kayit
REM olmadan servis dogru saati alir ve uygulamayi reddeder.
reg add "HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Config" /v MaxPosPhaseCorrection /t REG_DWORD /d 0xFFFFFFFF /f >nul 2>&1
reg add "HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Config" /v MaxNegPhaseCorrection /t REG_DWORD /d 0xFFFFFFFF /f >nul 2>&1
echo   [TAMAM] Buyuk saat duzeltmelerine izin verildi.

sc config w32time start= auto >nul 2>&1
net start w32time >nul 2>&1
echo   [TAMAM] Zaman servisi acilista kendiliginden baslayacak.

w32tm /config /manualpeerlist:"tr.pool.ntp.org,0x8 time.google.com,0x8 time.windows.com,0x8" /syncfromflags:manual /update >nul 2>&1
echo   [TAMAM] Zaman sunuculari tanimlandi.
echo.
echo   Sunucuya baglaniliyor, bekleyin...
w32tm /resync /force >nul 2>&1
if errorlevel 1 goto :ntp_yok
REM %DATE% parantezli blogun ICINDE blok okunurken cozulur, yani
REM resync'ten ONCEKI saati basardi. Duzeltmenin sonucunu gostermesi
REM gereken satirin eski saati yazmasi, en cok yanlis anlasilacak yer.
echo   [TAMAM] Saat sunucudan alindi: %DATE% %TIME%
goto :ntp_bitti
:ntp_yok
echo   [BILGI] Zaman sunucusuna ULASILAMADI.
echo           Kapali agda bu normaldir; asagida uygulamanin kendi
echo           denemesi ve elle giris secenegi var.
:ntp_bitti
echo.

echo  ============================================================
echo   4. BOLUM - UYGULAMANIN KENDI DENEMESI
echo  ============================================================
echo.
REM Uygulama NTP'yi ve o da kapaliysa duz HTTP yanitinin Date basligini
REM dener. Kapali aglarda UDP 123 sik kapali olur ama 80 acik kalir,
REM bu yuzden ikinci yol w32tm'in basaramadigi yerde ise yarayabiliyor.
if not exist "IZBAN-Kiosk.exe" goto :uygulama_yok
"IZBAN-Kiosk.exe" --sync-clock
goto :uygulama_bitti
:uygulama_yok
echo   [ATLANDI] IZBAN-Kiosk.exe bu klasorde yok.
:uygulama_bitti
echo.

echo  ============================================================
echo   5. BOLUM - SONUC
echo  ============================================================
echo.
echo   Otomatin saati simdi : %DATE% %TIME%
echo.
echo   Bu tarih DOGRU ise yapacak bir sey kalmadi; bu pencereyi
echo   kapatabilirsiniz.
echo.
echo   Hala YANLIS ise saati elle girmeniz gerekiyor.
echo.
set ELLE=
set /p ELLE=  Saati elle girmek istiyor musunuz? (E/H):
if /i not "%ELLE%"=="E" goto :bitti

echo.
echo   Tarihi asagidaki ornekteki BICIMDE yazin.
echo   Bu makinenin bekledigi bicim:
date /t
echo.
date
echo.
echo   Saati asagidaki ornekteki bicimde yazin (ornek 15:30:00).
echo   Bu makinenin bekledigi bicim:
time /t
echo.
time
echo.
echo   Yeni saat: %DATE% %TIME%

:bitti
echo.
echo  ============================================================
echo   SIRADAKI ADIMLAR
echo  ============================================================
echo.
echo   1. IZBAN-Kiosk.exe -^> SISTEM TANILA -^> SIMDI KONTROL ET
echo      "GitHub'a erisim: BASARILI" gormeniz gerekir.
echo.
echo   2. Otomati kapatip acin ve tarihi TEKRAR kontrol edin.
echo      Tarih her acilista yeniden bozuluyorsa yazilimla
echo      cozulmez: ANAKART PILI (CR2032) bitmistir, degistirin.
echo      BIOS'a girip oradaki saate de bakin; orada da yanlissa
echo      pil kesin bitmistir.
echo.
pause
