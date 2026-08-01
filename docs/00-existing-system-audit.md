# Mevcut Sistem Denetim Raporu (docs/00-existing-system-audit.md)

Mevcut İzmirim Kart yükleme Kiosk yazılımı incelenmiş, mimari, güvenlik, veri bütünlüğü ve işlem bütünlüğü açısından tespit edilen risk ve eksiklikler aşağıda önem derecelerine göre sınıflandırılmıştır.

## 🚨 Kritik Riskler (Critical)

1. **İşlem Compensating (Saga/Reversal) Eksikliği**
   - **Sorun:** POS ödemesi onaylandıktan sonra İzmirim Kart'a bakiye yüklemesi (`WriteBalanceAsync`) başarısız olduğunda veya kart erkenden çekildiğinde banka işlemine yönelik otomatik bir iptal/iade (reversal/void) işlemi yapılmamaktadır.
   - **Etki:** Kullanıcının banka kartından para çekilmesine rağmen İzmirim Kartına yükleme yapılmaz. Bu durum doğrudan mutabakatsızlığa ve müşteri mağduriyetine yol açar.

2. **Gerçek Servislerde Sahte/Mock Veri Fallback'leri**
   - **Sorun:** `RealPosTerminalService` sınıfı gerçek bir ödeme protokolü uygulamak yerine arka planda statik `PROV_XXXXXX` şeklinde rastgele onay kodları üretmektedir. Benzer şekilde `RealNfcReaderService` sınıfı seri port bağlantısı açsa bile gerçek kart okumak yerine `"35-IZM-REAL-9921"` UID değerini ve sabit `62.50m` bakiyeyi döndürmektedir.
   - **Etki:** Production/Staging ortamında gerçek donanım varmış gibi davranan ancak tamamen sahte işlem yürüten güvensiz bir yapı söz konusudur.

3. **Veritabanı Tablosunun Doğrudan Silinmesi (Potential Data Loss)**
   - **Sorun:** `DatabaseService.InitializeDatabaseAsync` metodu, eski şemayı (INTEGER Primary Key) tespit ettiğinde `DROP TABLE Transactions;` komutuyla tabloyu tamamen silmektedir.
   - **Etki:** Kiosk üzerinde bekleyen veya geçmişe dönük finansal işlem kayıtları uygulama güncellemesi sırasında tamamen silinir. Bu durum PCI-DSS ve BDDK mevzuatlarına tamamen aykırıdır.

4. **Güvensiz ve İmzasız Otomatik Güncelleme (RCE Riski)**
   - **Sorun:** `UpdateManager.CheckAndPerformUpdateAsync` metodu doğrudan GitHub API'sine bağlanıp en son ZIP paketini indirmekte ve herhangi bir imza doğrulama, hash kontrolü veya Authenticode denetimi yapmadan `Updater.exe` aracılığıyla kurmaktadır.
   - **Etki:** Ortadaki adam (MITM) saldırıları veya GitHub hesabı ele geçirme durumlarında cihaza zararlı kod enjekte edilebilir (Remote Code Execution).

5. **Double-Tap ve Eşzamanlılık Koruması Eksikliği**
   - **Sorun:** Kullanıcı tutar butonlarına (Örn: 20 TL, 50 TL) veya nümerik klavyedeki "Onayla" butonuna arka arkaya hızlıca dokunduğunda paralel asenkron `StartPaymentFlow` görevleri başlatılabilmektedir. UI'da buton kilitleme veya aktif işlem kilidi bulunmamaktadır.
   - **Etki:** Aynı kart için tek seferde birden fazla POS ödemesi çekilebilir veya iki kez yükleme denenebilir.

---

## ⚡ Yüksek Riskler (High)

1. **Göreli SQLite Dosya Yolu**
   - **Sorun:** SQLite bağlantı dizesi `Data Source=kiosk_transactions.db` olarak tanımlanmıştır.
   - **Etki:** Veritabanı dosyası kiosk uygulamasının o anki çalışma dizininde (Working Directory) oluşturulur. Kiosk farklı bir servisle başlatıldığında veritabanına erişilemez veya yeni boş bir veritabanı oluşturulur.

2. **CancellationToken Kullanım Eksiği**
   - **Sorun:** `ProcessPaymentAsync` gibi kritik POS işlemlerine `CancellationToken` aktarılmamakta, asenkron akış yarıda kesildiğinde veya kart çekildiğinde POS tarafındaki işlem iptal edilememektedir.

3. **Konfigürasyon Değerlerinin Kod İçinde Sabitlenmesi (Hardcoded Configuration)**
   - **Sorun:** `UseMockHardware = true`, COM port numarası `"COM3"`, POS IP adresi `"192.168.1.100:5000"`, güncelleme havuzu adresleri program kodunda hardcoded olarak yazılmıştır.
   - **Etki:** Farklı sahalarda farklı donanım portları veya IP yapılandırmaları için uygulamanın yeniden derlenmesi gerekmektedir.

4. **Gerçek Bakiye Sorgulama ve Doğrulama Yokluğu**
   - **Sorun:** Bakiye sorguları kart üzerinden okunurken veya yazılırken yetkili bakiye kaynağı (Authoritative Balance Source) doğrulanmamaktadır. Kaynaklar arası (Card vs. Backend) uyuşmazlık denetlenmemekte ve yükleme sonrası bakiye teyidi (Read-back verification) yapılmamaktadır.

---

## 🔍 Orta Riskler (Medium)

1. **UI Kodlarında İş Mantığının Bulunması**
   - **Sorun:** `MainWindow.cs` sınıfı doğrudan donanım servis çağrılarını, asenkron ödeme akışını, veritabanı loglama işlemlerini ve durum geçişlerini yönetmektedir.
   - **Etki:** UI katmanı birim testlere tabi tutulamaz. Arayüz değişiklikleri finansal işlem motorunun bozulmasına yol açabilir.

2. **Simülasyon Paneli Ayrımının Olmaması**
   - **Sorun:** Simülatör kontrolünü sağlayan "KART YAKLAŞTIR" / "KARTI ÇEK" butonları ve olayları doğrudan ana görünümün ve kod akışının bir parçasıdır. Production modunda derlenmesini engelleyecek bir mekanizma yoktur.

3. **Merkezi İzleme ve Mutabakat Altyapısı Yokluğu**
   - **Sorun:** Cihazların sağlık durumunu raporlayan Heartbeat, yerel veritabanı kayıtlarının sunucuya iletilmesi (Outbox) ve batch mutabakat operasyonları bulunmamaktadır.

---

## ℹ️ Düşük Riskler (Low)

1. **Console.WriteLine Kullanımı**
   - **Sorun:** Loglama işlemleri `Console.WriteLine` ile yapılmaktadır. Yapılandırılmış loglama (Structured Logging) ve log rotasyonu / disk kotası yönetimi bulunmamaktadır.
2. **Platform Bağımlı Metotlar**
   - **Sorun:** macOS ve Linux platformlarında `Console.Beep` çağrıları platform uyuşmazlığı uyarıları üretmektedir.
