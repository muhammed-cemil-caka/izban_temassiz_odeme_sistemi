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
  pause
  exit /b 1
)

cd /d "%~dp0"
set HATA=0
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
echo   1/8  Windows surumu ve Service Pack
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
echo   2/8  Yazma filtresi ^(write filter^)
echo  --------------------------------------------------------------
set WF=
set "WFKOMUT="
sc query ewfsrv 2>nul | find "RUNNING" >nul && set WF=EWF
sc query fbwf 2>nul | find "RUNNING" >nul && set WF=FBWF
sc query uwfservicingsvc 2>nul | find "RUNNING" >nul && set WF=UWF

if "%WF%"=="" (
  echo        [TAMAM] Calisan bir yazma filtresi bulunamadi.
  goto :wf_bitti
)

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
echo   3/8  TLS 1.2
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
echo   4/8  Kok sertifika
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
echo   5/8  .NET Framework
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
echo   6/8  Cakisan eski kurulum
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
REM kurulum var demektir. 8/8 bu kaydin uzerine yazar, ama eski klasor
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

set ESKIAUS=
reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" 2>nul | find /i "AUSKiosk" >nul && set ESKIAUS=HKLM\...\Run
reg query "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" 2>nul | find /i "AUSKiosk" >nul && set ESKIAUS=HKCU\...\Run
if not "%ESKIAUS%"=="" (
  echo        [UYARI] AUSKiosk acilista da baslatiliyor: %ESKIAUS%
  echo                Yeniden baslatmadan sonra COM portunu yine kapar.
  echo                Bu kaydi kaldirmaya siz karar verin.
)
echo.

echo  --------------------------------------------------------------
echo   7/8  Termal yazici ve NFC portu
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
"%KOPRU%" --autoconfigure
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
echo               Uygulama acildiktan sonra SISTEM TANILA ekranindan
echo               dogru kuyrugu elle secebilirsiniz.
set HATA=1
set H7=1

:testfisi
if not "%ACRC%"=="0" goto :baslangic
echo.
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
echo   8/8  Otomatik baslatma
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

:sonuc
echo  ==============================================================
if "%HATA%"=="1" (
  echo    EKSIK ADIM VAR - OTOMATTAN AYRILMADAN ONCE GIDERIN
) else (
  echo    KURULUM TAMAM
)
echo  ==============================================================
echo.
if "%H1%"=="1" echo    - 1/8  Service Pack 1 yok. .NET 4.8 kurulamaz.
if "%H2%"=="1" echo    - 2/8  Yazma filtresi kapatilamadi. Hicbir sey kalici degil.
if "%H4%"=="1" echo    - 4/8  Kok sertifika kurulamadi. Guncelleme indirilemez.
if "%H5%"=="1" echo    - 5/8  .NET Framework 4.5+ yok. Guncelleme calismaz.
if "%H7%"=="1" echo    - 7/8  Yazici veya NFC portu secilemedi.
if "%H8%"=="1" echo    - 8/8  Otomatik baslatma kaydi yok. Acilista uygulama acilmaz.
if "%HATA%"=="1" echo.
echo   SIRADAKI ADIMLAR
echo.
echo    1. Otomati YENIDEN BASLATIN.
echo    2. Uygulama kendiliginden acilmali.
echo    3. SISTEM TANILA -^> SIMDI KONTROL ET
echo       "GitHub'a erisim: BASARILI" gormeniz gerekir.
echo    4. Kart okutup fis basin ve KAGIDIN CIKTIGINI GOZLE
echo       DOGRULAYIN.
if not "%WF%"=="" echo    5. Yazma filtresini ^(%WF%^) geri acin.
echo.
echo   Bunlar tamamsa otomata bir daha gelmeniz gerekmez;
echo   uygulama kendini gunceller.
echo.
call :yeniden_baslat_sor
exit /b 0

REM ---------------------------------------------------------------
REM Yeniden baslatmayi teklif eder. Kurulumun yarisi ancak yeniden
REM baslatmadan sonra gecerli oldugu icin, bunu operatorun hatirlamasina
REM birakmak kurulumun en sik atlanan adimi oluyordu.
REM ---------------------------------------------------------------
:yeniden_baslat_sor
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
