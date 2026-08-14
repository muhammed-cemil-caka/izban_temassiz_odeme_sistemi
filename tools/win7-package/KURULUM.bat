@echo off
title IZBAN Kiosk - Otomat Kurulumu
mode con: cols=78 lines=52
cls

net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo   Bu dosyayi YONETICI OLARAK calistirin.
  echo   Sag tik -^> "Yonetici olarak calistir"
  echo.
  for %%f in (%*) do if /i "%%f"=="/S" exit /b 1
  pause
  exit /b 1
)

cd /d "%~dp0"
set HATA=0

REM Toplu dagitim icin sessiz mod. Saha ekibi yuzlerce otomatin basinda
REM oturamaz; /S hicbir soru sormaz, sonucu ekran yerine gunluge yazar ve
REM cikis koduyla bildirir.
REM   /S        soru sorma, test fisi basma, yeniden baslatmayi sorma
REM   /RESTART  /S ile birlikte: bitince otomati kendisi yeniden baslatir
REM   /KAPALIAG otomat internete cikmiyor: GitHub adimini atlar ve otomatik
REM             guncellemeyi ayar dosyasinda kapatir
REM   /FILTREATLA yazma filtresi kontrolunu atla (kapali oldugu dogrulandiysa)
REM   /KABUK-EXPLORER  Windows kabugunu explorer.exe yapar (masaustu gelir,
REM                    uygulamamiz acilis kaydindan uzerine acilir)
REM   /KABUK-KIOSK     Windows kabugunu bizim uygulamamiz yapar (otomat kilitli
REM                    kalir, masaustu hic gorunmez)
REM   Eski kabuk degeri her durumda yedeklenir.
set SESSIZ=0
set OTOBASLAT=0
set KAPALIAG=0
set FILTREATLA=0
set "KABUKAYAR="
for %%f in (%*) do (
  if /i "%%f"=="/S" set SESSIZ=1
  if /i "%%f"=="/RESTART" set OTOBASLAT=1
  if /i "%%f"=="/KAPALIAG" set KAPALIAG=1
  if /i "%%f"=="/FILTREATLA" set FILTREATLA=1
  if /i "%%f"=="/KABUK-EXPLORER" set KABUKAYAR=explorer.exe
  if /i "%%f"=="/KABUK-KIOSK" set KABUKAYAR=KIOSK
)
set "GUNLUK=%~dp0kurulum-gunlugu.txt"
if "%SESSIZ%"=="1" echo [%DATE% %TIME%] KURULUM BASLADI>"%GUNLUK%"
set SP1=1
set "KIOSKEXE=%~dp0IZBAN-Kiosk.exe"
set "KOPRU=%~dp0Bridge\IzbanKiosk.LegacyHardwareBridge.exe"

REM Adim basina ayri bayrak. Ekran 100 satirdan uzun akiyor ve ilk adimlar
REM yukari kayip gozden kaciyor; sonda hangi adimin eksik kaldigi tek tek
REM tekrar yazilsin diye tutuluyor.
set H1=0
set H2=0
set H4=0
set H5=0
set H6=0
set H7=0
set H8=0
set H9=0

echo.
echo  ==============================================================
echo    IZBAN KIOSK - YENI OTOMAT KURULUMU
echo  ==============================================================
echo.
echo   Bu betik otomatin on gereksinimlerini hazirlar ve donanimi
echo   kendisi bulur. Bir kez calistirilir; sonrasinda uygulama
echo   kendini gunceller.
echo.

echo  --------------------------------------------------------------
echo   1/9  Windows surumu ve Service Pack
echo  --------------------------------------------------------------
for /f "tokens=2 delims=[]" %%a in ('ver') do echo        %%a

set BUILD=
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v CurrentBuildNumber 2^>nul ^| find "CurrentBuildNumber"') do set BUILD=%%a
set CSD=
for /f "tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v CSDVersion 2^>nul ^| find "CSDVersion"') do set CSD=%%b

if not "%CSD%"=="" echo        %CSD%

if "%BUILD%"=="" goto :sp_bilinmiyor
if "%BUILD%"=="7600" goto :sp_yok
echo        [TAMAM] Yapi %BUILD% - Service Pack gereksinimi karsilaniyor.
goto :sp_bitti

:sp_bilinmiyor
echo        [UYARI] Yapi numarasi okunamadi, Service Pack kontrolu atlandi.
goto :sp_bitti

:sp_yok
echo        Windows 7 RTM ^(yapi 7600^) - Service Pack 1 YOK.
echo        .NET Framework 4.8 SP1 olmadan KURULMAZ.
echo.
if not exist "windows6.1-KB976932-X86.exe" goto :sp_dosya_yok

echo        SP1 kurulum dosyasi bulundu, baslatiliyor...
echo        Bu islem 30 dakikaya kadar surebilir; ekran hareketsiz
echo        gorunebilir. Otomatin fisini CEKMEYIN.
echo.
start /wait "" "windows6.1-KB976932-X86.exe" /quiet /norestart
set SPRC=%errorlevel%
if "%SPRC%"=="0" goto :sp_kuruldu
if "%SPRC%"=="3010" goto :sp_kuruldu
echo        [HATA] SP1 kurulumu basarisiz oldu. Cikis kodu: %SPRC%
set HATA=1
set H1=1
set SP1=0
goto :sp_bitti

:sp_kuruldu
echo        [TAMAM] Service Pack 1 kuruldu.
echo.
echo        SP1 ANCAK YENIDEN BASLATMADAN SONRA GECERLI OLUR.
echo        Otomati yeniden baslatin ve BU BETIGI TEKRAR CALISTIRIN.
echo.
call :yeniden_baslat_sor
exit /b 0

:sp_dosya_yok
echo        [HATA] windows6.1-KB976932-X86.exe bu klasorde yok.
echo               Once Windows 7 SP1 kurun, sonra bu betigi tekrar
echo               calistirin. Dosyayi bu klasore koyarsaniz betik
echo               kendisi kurar.
set HATA=1
set H1=1
set SP1=0

:sp_bitti
echo.

echo  --------------------------------------------------------------
echo   2/9  Yazma filtresi ^(write filter^)
echo  --------------------------------------------------------------
set WF=
if "%FILTREATLA%"=="1" (
  echo        [ATLANDI] Yazma filtresi kontrolu bayrakla atlandi.
  goto :wf_bitti
)

REM Once hangi filtrenin KURULU oldugunu bul. Servisin calisiyor olmasi
REM filtrenin ACIK oldugu anlamina GELMEZ: FBWF kapatildiktan sonra da surucu
REM yuklu kalir ve servis RUNNING gorunur. Bu yuzden servis yalnizca hangi
REM aracin sorulacagini secmek icin kullaniliyor; gercek durum araca soruluyor.
sc query ewfsrv 2>nul | find "RUNNING" >nul && set WF=EWF
sc query fbwf 2>nul | find "RUNNING" >nul && set WF=FBWF
sc query uwfservicingsvc 2>nul | find "RUNNING" >nul && set WF=UWF

if "%WF%"=="" (
  echo        [TAMAM] Yazma filtresi kurulu degil.
  goto :wf_bitti
)

if not "%WF%"=="FBWF" goto :wf_durum_bilinmiyor

REM fbwfmgr iki bolum yazar: once bu oturum, sonra sonraki oturum. Bizi
REM ilgilendiren ilki - su anda diske gercekten yazilip yazilmadigi.
set "FBWFDURUM="
for /f "tokens=2 delims=:" %%a in ('fbwfmgr /displayconfig 2^>nul ^| find /i "filter state"') do (
  if not defined FBWFDURUM set "FBWFDURUM=%%a"
)
echo %FBWFDURUM% | find /i "disabled" >nul
if not errorlevel 1 (
  echo        [TAMAM] FBWF kurulu ama filtre KAPALI, diske yazilabiliyor.
  set WF=
  goto :wf_bitti
)
goto :wf_acik

:wf_durum_bilinmiyor
REM EWF/UWF icin aracin cikti bicimi dogrulanmadi; acik varsaymak guvenli
REM olan yon. Operator kapali oldugunu biliyorsa /FILTREATLA ile gecebilir.
echo        %WF% kurulu. Durumu su komutla dogrulayabilirsiniz:
if "%WF%"=="EWF" echo           ewfmgr c:
if "%WF%"=="UWF" echo           uwfmgr filter get-config
echo        Kapali oldugundan eminseniz: KURULUM.bat /FILTREATLA

:wf_acik

echo        %WF% yazma filtresi calisiyor, devre disi birakiliyor...
echo.
echo        Filtre acikken bu betigin diske yazdigi HER SEY ^(TLS
echo        kayitlari, kok sertifika, yazici ayari, otomatik baslatma^)
echo        ilk yeniden baslatmada geri alinir.
echo.

if "%WF%"=="EWF"  ewfmgr c: -disable
if "%WF%"=="FBWF" fbwfmgr /disable
if "%WF%"=="UWF"  uwfmgr filter disable
set WFRC=%errorlevel%

if not "%WFRC%"=="0" (
  echo.
  echo        [HATA] Filtre devre disi birakilamadi. Cikis kodu: %WFRC%
  echo               Filtreyi elle kapatin, otomati yeniden baslatin ve
  echo               bu betigi tekrar calistirin. Aksi halde kurulum
  echo               kalici OLMAZ.
  set HATA=1
  set H2=1
  goto :wf_bitti
)

echo.
echo        [TAMAM] %WF% devre disi birakildi.
echo.
echo        BU ANCAK YENIDEN BASLATMADAN SONRA GECERLI OLUR.
echo        Otomati yeniden baslatin ve BU BETIGI TEKRAR CALISTIRIN.
echo        Kurulum bittikten sonra filtreyi geri acmayi unutmayin.
echo.
call :yeniden_baslat_sor
exit /b 0

:wf_bitti
echo.

echo  --------------------------------------------------------------
echo   3/9  TLS 1.2
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
echo   4/9  Kok sertifika
echo  --------------------------------------------------------------
if not exist "ISRG-Root-X1.crt" (
  echo        [HATA] ISRG-Root-X1.crt bu klasorde yok.
  set HATA=1
  set H4=1
  goto :net
)
certutil -addstore -f root "ISRG-Root-X1.crt" >nul 2>&1
if errorlevel 1 (
  echo        [HATA] Sertifika eklenemedi.
  set HATA=1
  set H4=1
) else (
  echo        [TAMAM] ISRG Root X1 guvenilen koke eklendi.
  echo               GitHub'in dosya sunucusu bu sertifikayi kullanir;
  echo               olmadan guncelleme indirilemez.
)
echo.

:net
echo  --------------------------------------------------------------
echo   5/9  .NET Framework
echo  --------------------------------------------------------------
call :netsurum
if %NETVAL% GEQ 378389 (
  echo        [TAMAM] .NET Framework 4.5 veya ustu kurulu. ^(Release %NETVAL%^)
  goto :cakisma
)

echo        .NET Framework 4.5+ kurulu degil. TLS 1.2 icin gereklidir.
echo.

if "%SP1%"=="0" (
  echo        [ATLANDI] Service Pack 1 olmadan .NET 4.8 kurulamaz.
  echo                  Once SP1, sonra bu betik.
  set HATA=1
  set H5=1
  goto :cakisma
)

if not exist "ndp48-x86-x64-allos-enu.exe" (
  echo        [EKSIK] ndp48-x86-x64-allos-enu.exe bu klasorde yok.
  echo                Dosyayi bu klasore koyup betigi tekrar calistirin.
  echo                Indirme: dotnet.microsoft.com/download/dotnet-framework/net48
  echo                ^(Web installer degil, OFFLINE installer - yaklasik 110 MB^)
  set HATA=1
  set H5=1
  goto :cakisma
)

echo        Kurulum dosyasi bulundu, baslatiliyor...
echo        Bu islem 10-20 dakika surebilir; ekran hareketsiz gorunebilir.
echo.
start /wait "" "ndp48-x86-x64-allos-enu.exe" /passive /norestart
set NETRC=%errorlevel%

if "%NETRC%"=="0" goto :netdogrula
if "%NETRC%"=="3010" goto :netdogrula
if "%NETRC%"=="1641" goto :netdogrula

if "%NETRC%"=="5100" (
  echo        [HATA] Kurulum "on gereksinim karsilanmiyor" dedi ^(kod 5100^).
  echo               Neredeyse her zaman Windows 7 SP1 eksikligi demektir.
) else (
  echo        [HATA] .NET kurulumu basarisiz oldu. Cikis kodu: %NETRC%
  echo               Ayrinti icin: %%TEMP%%\Microsoft .NET Framework 4.8 Setup_*.html
)
set HATA=1
set H5=1
goto :cakisma

:netdogrula
REM Cikis kodu basari dese bile kayit defterinden dogrulanir: kurulum
REM "bitti" deyip .NET'i gercekte kurmamis olabilir ve otomattan
REM ayrildiktan sonra bunu kimse fark etmez.
call :netsurum
if %NETVAL% GEQ 378389 (
  echo        [TAMAM] .NET Framework kuruldu. ^(Release %NETVAL%^)
) else (
  echo        [HATA] Kurulum bitti ama kayit defterinde .NET 4.5+ gorunmuyor.
  echo               Otomati yeniden baslatip betigi tekrar calistirin.
  set HATA=1
  set H5=1
)
echo.

:cakisma
echo  --------------------------------------------------------------
echo   6/9  Cakisan eski kurulum
echo  --------------------------------------------------------------

REM Eski bir IZBAN Kiosk kurulumunun donanim servisi, isimli kanali tek
REM ornekle acar ve yenisinin yerini kapar. Yeni surum acilir, kanali
REM alamaz ve "surum uyusmazligi" verip durur. Once eskisi kapatilir.
tasklist /FI "IMAGENAME eq IzbanKiosk.LegacyHardwareBridge.exe" 2>nul | find /i "IzbanKiosk.LegacyHardwareBridge.exe" >nul
if errorlevel 1 goto :eskikiosk
echo        Eski donanim servisi calisiyor, kapatiliyor...
taskkill /F /IM IzbanKiosk.LegacyHardwareBridge.exe >nul 2>&1
if errorlevel 1 (
  echo        [UYARI] Donanim servisi kapatilamadi.
) else (
  echo        [TAMAM] Eski donanim servisi kapatildi.
)

:eskikiosk
tasklist /FI "IMAGENAME eq IZBAN-Kiosk.exe" 2>nul | find /i "IZBAN-Kiosk.exe" >nul
if errorlevel 1 goto :auskiosk
echo        Calisan IZBAN-Kiosk.exe kapatiliyor...
taskkill /F /IM IZBAN-Kiosk.exe >nul 2>&1
if errorlevel 1 (
  echo        [UYARI] IZBAN-Kiosk.exe kapatilamadi.
) else (
  echo        [TAMAM] IZBAN-Kiosk.exe kapatildi.
)

:auskiosk
tasklist /FI "IMAGENAME eq AUSKiosk.exe" 2>nul | find /i "AUSKiosk.exe" >nul
if errorlevel 1 (
  echo        [TAMAM] AUSKiosk.exe calismiyor.
  goto :eskiklasor
)

echo        AUSKiosk.exe calisiyor ve NFC okuyucunun COM portunu
echo        mesgul tutuyor. Kapatiliyor...
taskkill /F /IM AUSKiosk.exe >nul 2>&1
if errorlevel 1 (
  echo        [UYARI] Kapatilamadi. Kart okuma calismayabilir.
) else (
  echo        [TAMAM] AUSKiosk.exe kapatildi.
)

:eskiklasor
REM Acilis kaydi baska bir klasoru gosteriyorsa otomatta ikinci bir
REM kurulum var demektir. 8/9 bu kaydin uzerine yazar, ama eski klasor
REM yerinde durur; elle silinmesi gerektigini burada soyluyoruz.
set "ESKIYOL="
for /f "tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "IZBAN-Kiosk" 2^>nul ^| find /i "IZBAN-Kiosk"') do set "ESKIYOL=%%b"
if not defined ESKIYOL goto :eskihkcu
echo %ESKIYOL% | find /i "%~dp0" >nul
if not errorlevel 1 goto :eskihkcu
echo        [UYARI] Acilis kaydi BASKA bir klasoru gosteriyor:
echo                %ESKIYOL%
echo                Otomatta ikinci bir IZBAN Kiosk kurulumu var.
echo                Kayit birazdan bu klasore cevrilecek, ama eski
echo                klasoru elle SILIN; yoksa eski surum yine acilip
echo                donanim servisinin yerini kapabilir.

:eskihkcu
set ESKIACILIS=
reg query "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" 2>nul | find /i "IZBAN-Kiosk" >nul && set ESKIACILIS=HKCU\...\Run
if not "%ESKIACILIS%"=="" (
  echo        [UYARI] IZBAN Kiosk kullanici acilis kaydinda da var:
  echo                %ESKIACILIS%
  echo                Bu kaydi elle kaldirin; iki kopya birbirinin
  echo                yerini kapar.
)

REM AUSKiosk'un kendisi SILINMIYOR - setup.ini otomat numarasini, klasoru de
REM vendor DLL'lerini sagliyor. Kaldirilan tek sey ACILISTA BASLAMASI: ikisi
REM ayni anda calisamaz, NFC okuyucunun COM portunu tek biri tutabilir.
REM Silinen her kayit once yedeklenir; geri almak tek komut olmali.
set "ACILISYEDEK=%~dp0auskiosk-acilis-yedek.txt"
set ESKIAUS=0

for %%K in ("HKLM" "HKCU") do (
  for /f "tokens=1,2,*" %%a in ('reg query "%%~K\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" 2^>nul ^| find /i "AUSKiosk"') do (
    echo %%~K\Run^|%%a^|%%c>>"%ACILISYEDEK%"
    reg delete "%%~K\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "%%a" /f >nul 2>&1
    echo        [TAMAM] Acilis kaydi kaldirildi: %%~K\Run -^> %%a
    set ESKIAUS=1
  )
)

REM Baslangic klasorundeki kisayollar. Silinmiyor, yedek klasore tasiniyor.
for %%D in ("%ALLUSERSPROFILE%\Start Menu\Programs\Startup" "%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup" "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup") do (
  if exist "%%~D\*AUSKiosk*" (
    if not exist "%~dp0auskiosk-acilis-yedek" md "%~dp0auskiosk-acilis-yedek" >nul 2>&1
    move /Y "%%~D\*AUSKiosk*" "%~dp0auskiosk-acilis-yedek\" >nul 2>&1
    echo        [TAMAM] Baslangic kisayolu tasindi: %%~D
    set ESKIAUS=1
  )
)

if "%ESKIAUS%"=="0" echo        [TAMAM] AUSKiosk acilista baslatilmiyor.
if "%ESKIAUS%"=="1" echo               Yedek: %ACILISYEDEK%

REM Kiosk makinelerinde uygulama cogu zaman Run anahtarindan degil, Windows
REM KABUGU olarak baslatilir. Oyleyse Run kaydini silmek hicbir sey degistirmez
REM ve bizim uygulama da acilmaz. Bu deger DEGISTIRILMIYOR: yanlis bir kabuk
REM makineyi bos ekrana acar ve sahadaki bir otomatta geri donusu yoktur.
set KABUK=
for /f "tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell 2^>nul ^| find /i "Shell"') do set "KABUK=%%b"
echo %KABUK% | find /i "AUSKiosk" >nul
if not errorlevel 1 (
  echo.
  echo        [UYARI] AUSKiosk Windows KABUGU olarak ayarli:
  echo                %KABUK%
  echo                Bu haliyle bizim uygulama acilista BASLAMAZ. Kabugu
  echo                degistirmek bilerek verilecek bir karardir ve yanlis
  echo                deger makineyi bos ekrana acar; betik dokunmuyor.
  echo                Karar verirseniz kabugu explorer.exe yapin, acilis
  echo                kaydimiz uygulamayi baslatir.
  set HATA=1
  set H6=1
)

REM Kabugu degistirmek, ancak operator bilerek istediginde yapiliyor. Yanlis bir
REM deger makineyi bos ekrana acar ve sahadaki bir otomatta geri donusu yoktur;
REM bu yuzden varsayilan davranis dokunmamak. Eski deger her zaman once
REM yedekleniyor, geri almak tek komut olsun diye.
if not defined KABUKAYAR goto :kabuk_bitti
if not defined KABUK goto :kabuk_bitti

REM Winlogon kabuk degerini bosluktan boler, o yuzden yol TIRNAK ICINDE
REM kaydedilmeli: C:\IZBAN KIOSK\IZBAN-Kiosk.exe tirnaksiz yazilirsa Windows
REM "C:\IZBAN" calistirmaya calisir ve ekran bos acilir.
REM
REM Tirnak reg.exe'ye \" olarak gecirilir. Duz tirnak yazmak /d ""C:\..."" 
REM uretir; reg.exe bunu ayristiramaz ve "Kabuk degistirilemedi" der - ilk
REM denemede tam olarak bu oldu.
set "YENIKABUK=%KABUKAYAR%"
if /i "%KABUKAYAR%"=="KIOSK" set "YENIKABUK=\"%KIOSKEXE%\""

echo.
echo        Windows kabugu degistiriliyor.
echo           Eski: %KABUK%
if /i "%KABUKAYAR%"=="KIOSK" (echo           Yeni: "%KIOSKEXE%") else (echo           Yeni: %KABUKAYAR%)
echo Eski kabuk: %KABUK%>>"%ACILISYEDEK%"
REM Once exe'nin gercekten orada oldugu dogrulaniyor. Var olmayan bir kabuk
REM yolu makineyi bos ekrana acar; yazmadan once bakmak bunu tamamen onler.
if /i not "%KABUKAYAR%"=="explorer.exe" if not exist "%KIOSKEXE%" (
  echo        [HATA] %KIOSKEXE% bulunamadi, kabuk DEGISTIRILMEDI.
  set HATA=1
  set H6=1
  goto :kabuk_bitti
)

reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d "%YENIKABUK%" /f >nul 2>&1
if errorlevel 1 (
  echo        [HATA] Kabuk degistirilemedi.
  set HATA=1
  set H6=1
) else (
  echo        [TAMAM] Kabuk yazildi. Yedek: %ACILISYEDEK%
  set H6=0
)

:kabuk_bitti
echo.

echo  --------------------------------------------------------------
echo   7/9  Termal yazici ve NFC portu
echo  --------------------------------------------------------------
if not exist "%KOPRU%" (
  echo        [HATA] Bridge\IzbanKiosk.LegacyHardwareBridge.exe yok.
  echo               Paketin TAMAMINI ayni klasore kopyalayin.
  set HATA=1
  set H7=1
  goto :baslangic
)

echo        Kurulu yazicilar ve seri portlar taraniyor...
echo.
"%KOPRU%" --autoconfigure --interactive
set ACRC=%errorlevel%
echo.

if "%ACRC%"=="0" (
  echo        [TAMAM] Yazici ve NFC portu ayar dosyasina yazildi.
  goto :testfisi
)
if "%ACRC%"=="2" echo        [EKSIK] Yazici secilemedi. Yukaridaki satira bakin.
if "%ACRC%"=="3" echo        [EKSIK] NFC portu secilemedi. Yukaridaki satira bakin.
if "%ACRC%"=="4" echo        [EKSIK] Yazici ve NFC portu secilemedi.
if "%ACRC%"=="5" echo        [HATA]  Otomatik yapilandirma hata verdi.
echo               Ayar dosyasini elle duzeltmeniz gerekir:
echo               KioskHardware.config.json -^> ThermalPrinterName / NfcComPort
set HATA=1
set H7=1

:testfisi
if not "%ACRC%"=="0" goto :baslangic
echo.
if "%SESSIZ%"=="1" goto :baslangic
set TEST=
set /p TEST=       Simdi bir test fisi basilsin mi? (E/H):
if /i not "%TEST%"=="E" goto :baslangic
echo.
"%KOPRU%" --print-test
set TESTRC=%errorlevel%
echo.
if "%TESTRC%"=="0" (
  echo        Is kuyruga verildi. YAZICIDAN KAGIT CIKTI MI, GOZLE
  echo        KONTROL EDIN. API basarisi tek basina kagit ciktigini
  echo        KANITLAMAZ.
) else (
  echo        [UYARI] Test fisi basilamadi. Cikis kodu: %TESTRC%
  echo                Teshis icin 2-Yazici-Teshis.bat calistirin.
)

:baslangic
echo.
echo  --------------------------------------------------------------
echo   8/9  Otomatik baslatma
echo  --------------------------------------------------------------
if not exist "%KIOSKEXE%" (
  echo        [HATA] IZBAN-Kiosk.exe bu klasorde yok, kayit yapilmadi.
  echo               Paketin TAMAMINI ayni klasore kopyalayin ve betigi
  echo               o klasorden calistirin.
  set HATA=1
  set H8=1
  goto :sonuc
)

reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "IZBAN-Kiosk" /t REG_SZ /d "\"%KIOSKEXE%\"" /f >nul
if errorlevel 1 (
  echo        [HATA] Otomatik baslatma kaydi yazilamadi.
  set HATA=1
  set H8=1
) else (
  echo        [TAMAM] Otomat her acilista uygulamayi kendisi baslatacak.
  echo               Yazilan kayit:
  for /f "tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "IZBAN-Kiosk" 2^>nul ^| find "IZBAN-Kiosk"') do echo               %%b
)
echo.

echo  --------------------------------------------------------------
echo   9/9  Saat ve GitHub erisimi
echo  --------------------------------------------------------------
REM Saat once geliyor, cunku yanlis saat GitHub testini adi ne tarihi
REM ne de cozumu anan bir TLS hatasiyla dusurur. O sirayi tersine
REM cevirmek, teknisyene var olmayan bir ag arizasi arattiriyor.
REM Buyuk duzeltmelere izin verilmezse w32time pili bitmis bir
REM makinede dogru saati alir ve uygulamayi reddeder - duzeltme orada
REM her zaman buyuktur.
reg add "HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Config" /v MaxPosPhaseCorrection /t REG_DWORD /d 0xFFFFFFFF /f >nul 2>&1
reg add "HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Config" /v MaxNegPhaseCorrection /t REG_DWORD /d 0xFFFFFFFF /f >nul 2>&1
sc config w32time start= auto >nul 2>&1
net start w32time >nul 2>&1
tzutil /s "Turkey Standard Time" >nul 2>&1
if errorlevel 1 (
  tzutil /s "GTB Standard Time" >nul 2>&1
  reg add "HKLM\SYSTEM\CurrentControlSet\Control\TimeZoneInformation" /v DynamicDaylightTimeDisabled /t REG_DWORD /d 1 /f >nul 2>&1
)

REM errorlevel parantezli blogun icinden dogru okunmaz; bu dosyanin her
REM yerinde oldugu gibi blok disinda yakalaniyor.
set SAATRC=0
if not exist "%KIOSKEXE%" goto :saat_bitti
"%KIOSKEXE%" --sync-clock
set SAATRC=%errorlevel%
:saat_bitti
echo.
if "%SAATRC%"=="6" (
  echo        [HATA] OTOMATIN SAATI YANLIS ve duzeltilemedi.
  echo               Bu hâliyle otomat GUNCELLEME ALAMAZ.
  echo               6-Saat-Duzelt.bat dosyasini yonetici olarak
  echo               calistirip saati elle girin. Her acilista
  echo               bozuluyorsa anakart pilini (CR2032) degistirin.
  set HATA=1
  set H9=1
)
if "%SAATRC%"=="5" (
  echo        [HATA] Saat yanlis ve Windows duzeltmeyi kabul etmedi.
  echo               6-Saat-Duzelt.bat dosyasini YONETICI olarak
  echo               calistirin.
  set HATA=1
  set H9=1
)
echo.
REM Sahadaki otomatlarda tanilama ekrani yok; bu, erisimin calistigini
REM kimsenin ogrenebilecegi TEK an. Erisemeyen bir otomat yolcuya normal
REM hizmet vermeye devam eder, bu yuzden hicbir sey dikkat cekmez - sadece
REM bir daha hic guncelleme almaz. Ping yetmez: TLS 1.2, kok sertifika ve
REM depo adresinin ucu birden ancak gercek istekle sinanir.
if not exist "%KIOSKEXE%" (
  echo        [HATA] IZBAN-Kiosk.exe yok, erisim test edilemedi.
  set HATA=1
  set H9=1
  goto :sonuc
)

REM Kapali agdaki otomatta GitHub'a ulasilamamasi bir ariza degil, tasarim.
REM Adimi [HATA] saymak, saha ekibine her kurulumda gercek olmayan bir sorun
REM bildirmek olurdu. Otomatik guncelleme ayari degistirilmiyor: gunde bir
REM basarisiz denemenin zarari yok, ayar dosyasini burada duzenlemek ise
REM WES7'de bulunmayabilecek araclara bagimlilik getirirdi.
if "%KAPALIAG%"=="1" (
  echo        [ATLANDI] Otomat kapali agda; guncellemeler elle dagitiliyor.
  goto :sonuc
)

echo        GitHub'a baglaniliyor, bekleyin...
echo.
"%KIOSKEXE%" --check-update
set NETTEST=%errorlevel%
echo.

if "%NETTEST%"=="0" (
  echo        [TAMAM] Otomat guncellemelerine erisebiliyor.
  goto :sonuc
)
if "%NETTEST%"=="2" (
  echo        [HATA] GitHub'a ERISILEMIYOR.
  echo               Bu otomat bir daha hic guncelleme ALAMAZ ve sahada
  echo               bunu fark edeceginiz bir ekran yok.
  echo               Otomatin ag baglantisini ve varsa proxy ayarlarini
  echo               kontrol edin, sonra bu betigi tekrar calistirin.
)
if "%NETTEST%"=="3" echo        [HATA] Ayar dosyasi okunamadi.
if "%NETTEST%"=="4" echo        [HATA] Otomatik guncelleme ayar dosyasinda KAPALI.
set HATA=1
set H9=1

:sonuc
echo  ==============================================================
if "%HATA%"=="1" (
  echo    EKSIK ADIM VAR - OTOMATTAN AYRILMADAN ONCE GIDERIN
) else (
  echo    KURULUM TAMAM
)
echo  ==============================================================
echo.
if "%H1%"=="1" echo    - 1/9  Service Pack 1 yok. .NET 4.8 kurulamaz.
if "%H2%"=="1" echo    - 2/9  Yazma filtresi kapatilamadi. Hicbir sey kalici degil.
if "%H4%"=="1" echo    - 4/9  Kok sertifika kurulamadi. Guncelleme indirilemez.
if "%H5%"=="1" echo    - 5/9  .NET Framework 4.5+ yok. Guncelleme calismaz.
if "%H6%"=="1" echo    - 6/9  AUSKiosk Windows kabugu; uygulamamiz acilista BASLAMAZ.
if "%H7%"=="1" echo    - 7/9  Yazici veya NFC portu secilemedi.
if "%H8%"=="1" echo    - 8/9  Otomatik baslatma kaydi yok. Acilista uygulama acilmaz.
if "%H9%"=="1" echo    - 9/9  GitHub'a erisilemiyor. Otomat guncelleme ALAMAZ.
if "%HATA%"=="1" echo.
echo   SIRADAKI ADIMLAR
echo.
echo    1. Otomati YENIDEN BASLATIN.
echo    2. Uygulama kendiliginden acilmali.
echo    3. Kart okutup fis basin ve KAGIDIN CIKTIGINI GOZLE
echo       DOGRULAYIN.
if not "%WF%"=="" echo    4. Yazma filtresini ^(%WF%^) geri acin.
echo.
echo   Bunlar tamamsa otomata bir daha gelmeniz gerekmez;
echo   uygulama kendini gunceller.
echo.
call :yeniden_baslat_sor
if "%HATA%"=="1" exit /b 1
exit /b 0

REM ---------------------------------------------------------------
REM Yeniden baslatmayi teklif eder. Kurulumun yarisi ancak yeniden
REM baslatmadan sonra gecerli oldugu icin, bunu operatorun hatirlamasina
REM birakmak kurulumun en sik atlanan adimi oluyordu.
REM ---------------------------------------------------------------
:yeniden_baslat_sor
if "%SESSIZ%"=="1" (
  if "%HATA%"=="1" echo [%DATE% %TIME%] EKSIK ADIM VAR>>"%GUNLUK%"
  if not "%HATA%"=="1" echo [%DATE% %TIME%] KURULUM TAMAM>>"%GUNLUK%"
  if "%OTOBASLAT%"=="1" shutdown /r /t 30 /c "IZBAN Kiosk kurulumu" >nul 2>&1
  goto :eof
)
set CEVAP=
set /p CEVAP=   Otomat simdi yeniden baslatilsin mi? (E/H):
if /i not "%CEVAP%"=="E" (
  echo.
  echo   Yeniden baslatilmadi. Degisikliklerin gecerli olmasi icin
  echo   otomati elle yeniden baslatmaniz gerekir.
  echo.
  pause
  goto :eof
)
echo.
echo   60 saniye icinde yeniden baslatilacak.
echo   Vazgecerseniz bu pencereye "shutdown /a" yazin.
shutdown /r /t 60 /c "IZBAN Kiosk kurulumu - yeniden baslatiliyor"
pause
goto :eof

REM ---------------------------------------------------------------
REM Kurulu .NET 4.x surumunu NETVAL'e ondalik olarak yazar.
REM reg query degeri onaltilik basar (ornek 0x81bf8); set /a bunu
REM oldugu gibi cozer. Kurulu degilse 0 doner.
REM ---------------------------------------------------------------
:netsurum
set NETREL=
set NETVAL=0
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find "Release"') do set NETREL=%%a
if not "%NETREL%"=="" set /a NETVAL=%NETREL% 2>nul
goto :eof
