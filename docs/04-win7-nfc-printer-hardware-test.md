# Windows 7 NFC/SAM ve Termal Yazıcı Donanım Testi

Bu aşama yalnızca gerçek kartın okunmasını, SAM doğrulamasını ve test makbuzunu doğrular. Kart yazma, para yükleme ve POS işlemleri kapalıdır.

## 1. Paketi Windows üzerinde oluştur

Windows 10/11 geliştirme bilgisayarında Visual Studio Build Tools ile .NET Framework 4.8 targeting pack kurulu olmalıdır. PowerShell'i proje kökünde aç:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Prepare-Win7HardwareTestPackage.ps1 `
  -VendorSourceDirectory "D:\AUSKiosk"
```

Çıktı `artifacts\win7-hardware-test` altında oluşur. `--self-contained` kullanılmaz; .NET Framework uygulamaları bu biçimde yayınlanmaz.

## 2. Kontrollü kiosk test penceresi aç

1. Mevcut otomattan geri alınabilir bir yedek al.
2. `AUSKiosk.exe` işlemini test süresince kapat. Aynı COM portunu iki uygulama kullanamaz.
3. Hazırlanan klasörü Windows 7 kioska kopyala.
4. Windows 7 üzerinde .NET Framework 4.8'in kurulu olduğunu doğrula.
5. Aygıt Yöneticisi'nde okuyucunun `COM4` olarak göründüğünü doğrula.
6. Windows yazıcılarında termal yazıcının adını ve hazır durumunu doğrula.

## 3. Tek tıklamalı test ekranını aç

Eski `AUSKiosk.exe` kapalıyken paket kökündeki aşağıdaki dosyaya çift tıkla:

```text
IZBAN-Donanim-Testi.exe
```

Test uygulaması read-only Bridge sürecini arka planda başlatır ve yalnızca o alt süreç için rastgele oturum anahtarı üretir. Arayüz kapanınca başlattığı Bridge sürecini de kapatır.

Arayüzde sırasıyla `Health Check`, `Read Card` ve `Print Test` adımlarını uygula. Bridge otomatik başlatılamazsa hata ayrıntısı Action Log bölümünde gösterilir.

## 4. İsteğe bağlı komut satırı donanım testleri

Tek tıklamalı ekran çalışmazsa ileri tanılama için CMD kullanılabilir. Kart ile ilgili
komutlar için önce `IZBAN_HMAC_SECRET` ortam değişkeni tanımlanmalıdır; üretimde test
anahtarı kullanılmaz. Yazıcı komutları kart verisine dokunmadığı için bu anahtarı
gerektirmez.

```powershell
.\Bridge\IzbanKiosk.LegacyHardwareBridge.exe --list-printers
.\Bridge\IzbanKiosk.LegacyHardwareBridge.exe --printer-diagnose
.\Bridge\IzbanKiosk.LegacyHardwareBridge.exe --printer-health
.\Bridge\IzbanKiosk.LegacyHardwareBridge.exe --print-test
.\Bridge\IzbanKiosk.LegacyHardwareBridge.exe --nfc-health --port COM4
.\Bridge\IzbanKiosk.LegacyHardwareBridge.exe --read-card-once --port COM4
```

Yazıcı adı verilmezse Bridge, kendi klasöründeki veya paket kökündeki
`KioskHardware.config.json` dosyasındaki `ThermalPrinterName` değerini kullanır.
Başka bir yazıcı denemek için `--printer "Yazıcı Adı"` eklenebilir.

Her başarılı komut `0` çıkış kodu üretmelidir. Kart okuma çıktısındaki kimlik
maskelenmiş/pseudonymized olmalıdır.

## 4.1 Termal yazıcıdan kâğıt çıkmıyorsa

`KioskPrint.dll` bir Delphi/VCL kütüphanesidir ve yazıcı adı parametresi almaz:
belgeyi **her zaman Windows varsayılan yazıcısına** gönderir. Windows Embedded
imajında varsayılan yazıcı çoğu zaman termal yazıcı değil (PDF/XPS/ağ yazıcısı)
olduğu için iş sessizce o kuyruğa gider ve kâğıt çıkmaz. Bridge bu yüzden
yapılandırılan termal yazıcıyı ilk vendor çağrısından önce Windows varsayılanı
yapar ve her makbuzdan önce bunu yeniden doğrular.

Sırayla şunları uygula:

1. `--list-printers` çıktısındaki `InstalledPrinters` listesiyle
   `KioskHardware.config.json` içindeki `ThermalPrinterName` değerini karşılaştır.
   Değer, **Windows yazıcı kuyruğu adı** olmalıdır; sürücü adı değil. Yanlışsa
   config dosyasını listedeki adla birebir güncelle.
2. `--printer-diagnose` çalıştır. Çıktıdaki alanlar:
   - `IsInstalled: false` → ad eşleşmiyor (adım 1).
   - `DefaultPrinterRoutingApplied: false` → Windows varsayılan yazıcı
     değiştirilemedi; kiosk kullanıcısının yazıcı üzerinde yetkisi yok veya bir
     grup ilkesi varsayılanı kilitliyor.
   - `SpoolerStatusFlags` → `0x10` kâğıt bitti, `0x80` çevrimdışı,
     `0x400000` kapak açık.
   - `VendorQueuedJobCount > 3` → kuyrukta biriken iş var; yazıcı işleri kabul
     edip basmıyor. Kuyruğu temizle, kâğıt/kablo/güç kontrol et.
3. `--print-test` çalıştır ve **fiziksel kâğıdı** kontrol et. API başarısı yalnızca
   işin kuyruğa verildiğini gösterir.
4. Bridge çalışırken Windows varsayılan yazıcısı dışarıdan değiştirilirse
   `KioskPrint.dll` ilk kullanımda yakaladığı kuyruğu bırakmaz. Bridge bu durumu
   tespit edip yazdırmayı durdurur ve yeniden başlatma ister; Bridge sürecini
   kapatıp yeniden başlat.
5. Windows Embedded write filter (EWF/FBWF/UWF) etkinse varsayılan yazıcı ayarı
   yeniden başlatmada geri alınır. Bridge her açılışta ayarı tekrar uyguladığı için
   çalışma kalıcıdır, ancak ayarın kalıcı olması isteniyorsa değişikliği filtre
   devre dışıyken yap.

## 5. Bakiye birimi doğrulaması

İlk testte uygulama `BalanceRaw` değerini gösterir. Bunu doğrudan TL olarak kabul etme. En az üç bilinen kart için eski çalışan kiosk bakiyesiyle karşılaştır:

| Kart | Eski kiosk bakiyesi | Bridge BalanceRaw | Beklenen ölçek | Sonuç |
|---|---:|---:|---:|---|
| 1 |  |  | 100 |  |
| 2 |  |  | 100 |  |
| 3 |  |  | 100 |  |

Üç sonuç tutarlı olmadan `IsBalanceScaleVerified` veya `IsAuthoritative` etkinleştirilmez.

## 6. WPF test ekranı

`IZBAN-Donanim-Testi.exe` Bridge'i otomatik başlatır ve donanımı initialize eder. Gerçek kartı okuyucu üzerinde sabit tutarak `Read Card` düğmesine bas ve sonuçları eski kiosk ile karşılaştır.

## 7. Testi kapat ve geri dön

Bridge'i `Ctrl+C` ile kapat. COM portunun serbest kaldığını doğruladıktan sonra eski `AUSKiosk.exe` uygulamasını yeniden başlat. Test sırasında eski veritabanına, sertifikalara veya yapılandırma dosyalarına yazılmaz.
