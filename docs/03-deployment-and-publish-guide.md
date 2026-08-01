# Deployment and Publish Guide

Bu rehber, C# Avalonia tabanlı `IzbanKioskApp` uygulamasının sahada Windows 11 IoT Enterprise terminallerine dağıtımı için gerekli derleme ve yayınlama (publish) işlemlerini açıklar.

## 1. Yayınlama Profilleri (Publish Profiles)

Projede iki adet MSBuild `.pubxml` yayınlama profili tanımlanmıştır:
1. **Self-Contained (Bağımsız Dağıtım):** Hedef kioskta .NET 8 Runtime kurulu olmak zorunda değildir; tüm DLL'ler tek bir çalıştırılabilir dosya (`IzbanKioskApp.exe`) içerisine gömülür.
2. **Framework-Dependent (Bağımlı Dağıtım):** Hedef sistemde .NET 8 Runtime kuruludur. Dosya boyutu daha küçüktür, ancak .NET 8 Runtime bağımlılığı bulunur.

---

## 2. Derleme ve Yayınlama Komutları

Yayın paketi oluşturmak için projenin kök dizininde terminali açıp aşağıdaki CLI komutlarını çalıştırabilirsiniz:

### komut 1: Self-Contained (Önerilen Saha Kurulumu)
```bash
dotnet publish src/IzbanKioskApp/IzbanKioskApp.csproj -c Release -p:PublishProfile=win-x64-self-contained
```
* **Çıktı Dizini:** `src/IzbanKioskApp/bin/Release/net8.0/publish/win-x64-self-contained/`
* **Avantajları:** Tek dosya (Single File), ReadyToRun (Derleme optimizasyonu), .NET Runtime gereksinimi yok, tamamen fail-closed izole çalışma.

### Komut 2: Framework-Dependent
```bash
dotnet publish src/IzbanKioskApp/IzbanKioskApp.csproj -c Release -p:PublishProfile=win-x64-framework-dependent
```
* **Çıktı Dizini:** `src/IzbanKioskApp/bin/Release/net8.0/publish/win-x64-framework-dependent/`
* **Gereksinim:** Hedef işletim sisteminde `.NET 8 Desktop Runtime (x64)` kurulu olmalıdır.

---

## 3. Dağıtım ve Saha Kurulum Adımları

Yayınlama çıktılarınızı hedef sahada kurmak için aşağıdaki sırayı takip edin:

### Adım 1: Uygulama Dizinlerin Oluşturulması
Kiosk pc üzerinde ana çalışma dizinlerini oluşturun:
- Ana klasör: `C:\IzbanKiosk\`
- SQLite Veritabanı dizini: `C:\IzbanKiosk\database\`
- Günlük dosyaları dizini: `C:\IzbanKiosk\logs\`
- Güncelleme geçici depolama dizini: `C:\IzbanKiosk\updates\`

### Adım 2: Yayın Dosyalarının Kopyalanması
`win-x64-self-contained` klasörü içeriğini (veya tek dosya `IzbanKioskApp.exe` ve `appsettings.json` dosyasını) `C:\IzbanKiosk\` dizinine kopyalayın.

### Adım 3: appsettings.json Yapılandırması
Saha profilini etkinleştirin:
* Eşlenik gerçek POS bağlanacaksa `"UseMockHardware": false` yapın.
* SQLite veritabanı yolunu ayarlayın: `"Database": { "Path": "database/transactions.db" }`.
* COM portlarını donanım portlarına göre düzenleyin.

### Adım 4: Güvenli Güncelleme Kurulumu
Uygulama otomatik güncelleme hizmetinin çalışabilmesi için:
* `UpdateManager.cs` içerisinde tanımlanan ECDsa Public Key ile eşlenen Private Key kullanılarak imzalanmış ZIP dosyalarını sunucunuzda / GitHub sürümlerinde dağıtın.
* İmza doğrulaması doğrulanmayan güncellemeler kiosk tarafından otomatik olarak reddedilecektir.
