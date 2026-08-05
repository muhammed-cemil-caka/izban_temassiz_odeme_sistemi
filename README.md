# İZBAN Kiosk — İzmirim Kart (Windows 7 Embedded / x86)

Otomatın üzerinde çalışan kiosk yazılımı. Hedef donanım **Windows 7 Embedded, x86** olduğu
için tüm ürün **.NET Framework 4.0 Client Profile / x86** üzerinde durur.

## Projeler

| Proje | Hedef | Görevi |
|---|---|---|
| `src/IzbanKiosk.Win7Prototype` | net40 / x86 / WPF | Yolcu arayüzü (`IZBAN-Kiosk.exe`). Donanıma doğrudan dokunmaz. |
| `src/IzbanKiosk.LegacyHardwareBridge` | net40 / x86 | Donanım süreci. Vendor DLL'lerini yükler, named pipe ile hizmet verir. |
| `src/IzbanKiosk.LegacyHardware.Contracts` | (kaynak) | İki taraf arasındaki mesaj modelleri ve pipe çerçeveleme. `.Net40` projesi bunları derler. |
| `tests/IzbanKiosk.Tests` | net8.0 | Yalnızca geliştirici makinesinde koşar, otomata **gitmez**. net40 kaynaklarını link ederek test eder. |

Arayüz ve donanım ayrı süreçlerde: vendor DLL'lerinden biri kilitlenirse veya çökerse
yolcu arayüzü ayakta kalır.

## Donanım

| Aygıt | Kütüphane | Durum |
|---|---|---|
| NFC okuyucu + SAM (COM4) | `EMVRdr35Lib.dll` | Çalışıyor — gerçek kart kimliği ve bakiye |
| Termal yazıcı | `KioskPrint.dll` | Çalışıyor — test fişi ve gerçek bakiye fişi |
| POS | — | **Entegre değil.** `IPosTerminal` arayüzü hazır, banka SDK'sı bekleniyor. |
| Karta yükleme | — | **Kapalı.** İzmirim Kart yazma yetkisi/lisansı gerekiyor. |

Vendor DLL'leri repoya **konmaz**; paketleme sırasında mevcut AUSKiosk kurulumundan
whitelist + SHA-256 manifest ile alınır (`tools/Import-LegacyVendorFiles.ps1`).

`KioskPrint.dll` yazıcı adı parametresi almaz; her zaman **Windows varsayılan yazıcısına**
basar. Ayrıntı ve sorun giderme: [docs/04](docs/04-win7-nfc-printer-hardware-test.md).

## Derleme ve paketleme

```bash
dotnet build src/IzbanKiosk.LegacyHardwareBridge/IzbanKiosk.LegacyHardwareBridge.csproj -c Release -p:Platform=x86
```

```bash
dotnet build src/IzbanKiosk.Win7Prototype/IzbanKiosk.Win7Prototype.csproj -c Release -p:Platform=x86
```

```bash
python3 tools/Prepare-Win7HardwareTestPackage.py --vendor-source ~/Desktop/AUSKiosk --zip-path ~/Desktop/IZBAN-Kiosk-R9-Win7.zip
```

Windows'ta `tools/Prepare-Win7HardwareTestPackage.ps1` aynı işi MSBuild ile yapar.
Paket kökündeki `.bat` dosyaları otomatta çift tıklanarak çalıştırılır; anlatım
`OKU-BENI.txt` içinde.

## Yapılandırma

`KioskHardware.config.json` — çalıştırılabilirin yanında durur, dağıtımın sahibidir:

```json
{ "NfcComPort": "COM4", "ThermalPrinterName": "<Windows yazıcı kuyruğu adı>" }
```

`ThermalPrinterName` **sürücü adı değil, kuyruk adı** olmalıdır. Otomattaki gerçek adı
`IzbanKiosk.LegacyHardwareBridge.exe --list-printers` verir.

## Sınırlar

Bilinen engeller, doğrulanmış binary gerçekleri ve güvenlik sınırı: [BLOCKERS.md](BLOCKERS.md).
