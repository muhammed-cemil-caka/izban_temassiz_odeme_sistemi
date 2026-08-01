# Windows 11 IoT Enterprise Kiosk Hardening Guide

Bu doküman, İzmirim Kart Temassız Ödeme Kiosklarının saha kurulumlarında Windows 11 IoT Enterprise işletim sisteminin finansal güvenlik standartlarına uygun şekilde sertleştirilmesi (hardening) için gerekli yapılandırma adımlarını içerir.

## 1. Unified Write Filter (UWF) Yapılandırması
UWF, kiosk işletim sisteminin disk yazmalarını RAM üzerinde sanal bir katmana yönlendirerek dosya sisteminin bozulmasını engeller ve her yeniden başlatmada temiz bir sistem açılmasını sağlar.

### Etkinleştirme
Yönetici yetkileriyle PowerShell üzerinden çalıştırın:
```powershell
# UWF Özelliğini Kurun
Enable-WindowsOptionalFeature -Online -FeatureName "Client-DeviceLockdown" -All
Enable-WindowsOptionalFeature -Online -FeatureName "Client-UnifiedWriteFilter" -All

# UWF Servisini Aktif Edin
uwfmgr filter enable
```

### Dışlama (Exception) Kuralları
Kiosk uygulamasının çalışması ve yerel SQLite veritabanının silinmemesi için aşağıdaki dışlamaları yapılandırın:
```powershell
# SQLite Veritabanı ve Günlük Dışlaması
uwfmgr file add-exclusion "C:\IzbanKiosk\transactions.db"
uwfmgr file add-exclusion "C:\IzbanKiosk\transactions.db-journal"
uwfmgr file add-exclusion "C:\IzbanKiosk\logs"

# Windows Olay Günlükleri (Opsiyonel)
uwfmgr file add-exclusion "C:\Windows\System32\Winevt\Logs"
```

---

## 2. Windows Assigned Access (Kiosk Modu)
Kiosk uygulamasının (`IzbanKioskApp.exe`) sistem açılışında Shell (Explorer.exe) yerine doğrudan tam ekran başlatılmasını sağlar. Bu sayede kullanıcının işletim sistemine veya masaüstüne erişmesi engellenir.

### XML Yapılandırma Profili (`AssignedAccessConfig.xml`)
Aşağıdaki yapılandırmayı kaydedin:
```xml
<?xml version="1.0" encoding="utf-8"?>
<KioskConfiguration xmlns="http://schemas.microsoft.com/AssignedAccess/2017/config">
  <Profiles>
    <Profile Id="{E1D3B512-E538-41F6-B2FA-8D05AC82E94A}">
      <AllAppsList>
        <AllowedApp AppUserModelId="C:\IzbanKiosk\IzbanKioskApp.exe" />
      </AllAppsList>
      <StartLayout>
        <![CDATA[
        <LayoutModificationTemplate xmlns:defaultlayout="http://schemas.microsoft.com/Start/2014/FullDefaultLayout" xmlns:start="http://schemas.microsoft.com/Start/2014/StartLayout" Version="1" xmlns="http://schemas.microsoft.com/Start/2014/LayoutModification">
          <DefaultLayoutOverride>
            <StartLayoutCollection>
              <defaultlayout:StartLayout GroupCellWidth="6" />
            </StartLayoutCollection>
          </DefaultLayoutOverride>
        </LayoutModificationTemplate>
        ]]>
      </StartLayout>
      <Taskbar ShowTaskbar="false" />
    </Profile>
  </Profiles>
  <Configs>
    <Config>
      <Account>KioskUser</Account>
      <Profile Id="{E1D3B512-E538-41F6-B2FA-8D05AC82E94A}" />
    </Config>
  </Configs>
</KioskConfiguration>
```

### PowerShell ile Uygulama
```powershell
Set-AssignedAccess -ConfigFile "C:\IzbanKiosk\AssignedAccessConfig.xml"
```

---

## 3. USB Yetkilendirme Değerleri ve Erişimi Sınırlandırma
Finansal veri güvenliği kapsamında, kiosk içi PC'ye yetkisiz USB bellek veya klavye takılması engellenmelidir. Yalnızca kart okuyucu (NFC) ve entegre yazıcı / POS cihazlarına izin verilmelidir.

### Grup Politikaları (Group Policy - gpedit.msc) Yapılandırması:
1. **Yol:** `Bilgisayar Yapılandırması -> Yönetim Şablonları -> Sistem -> Cihaz Yükleme -> Cihaz Yükleme Sınırlamaları`
2. **Kural:** `Diğer İlkeler Tarafından Tanımlanmayan Cihazların Yüklenmesini Engelle` özelliğini **Etkin** olarak işaretleyin.
3. **Kural:** `Bu Cihaz Kurulum Kimlikleriyle Eşleşen Cihazların Yüklenmesine İzin Ver` özelliğini **Etkin** yapıp, NFC Reader ve POS Terminal Vendor ID/Product ID (GUID/Hardware ID) değerlerini ekleyin.

PowerShell ile USB Depolama Sınıfını engelleme:
```powershell
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR" -Name "Start" -Value 4
```

---

## 4. Grup Politikaları (GPO) Listesi
Kiosk güvenliği için aktif edilmesi önerilen kritik GPO kuralları:

| Grup Politikası Adı | Önerilen Değer | Amacı |
| :--- | :--- | :--- |
| **Ctrl+Alt+Del Seçeneklerini Kaldır** | Etkin (Tüm Seçenekler: Kilit, Şifre Değiştir, Görev Yöneticisi) | Kullanıcının işletim sistemine müdahalesini önler. |
| **Windows Logo Tuşu Kombinasyonlarını Engelle** | Etkin | Win+R, Win+X, Win+E gibi tuş kombinasyonlarını devre dışı bırakır. |
| **Otomatik Oturum Açma (Auto Log-on)** | Etkin | Kiosk PC açıldığında doğrudan şifresiz KioskUser hesabı ile açılır. |
| **USB Depolama Aygıtları Erişimi** | Devre Dışı (Sor/Yazma Engelli) | Dışarıdan kiosk yazılımının çalınmasını veya zararlı yazılım bulaşmasını engeller. |
| **Windows Update Otomatik Yeniden Başlatma** | Devre Dışı | Aktif ödeme alma saatlerinde kioskun otomatik güncellenerek kapanmasını engeller. |

---

## 5. Donanım Ağacı ve Port Sınırlandırmaları
* **Güvenlik Duvarı (Windows Defender Firewall):** Tüm gelen (inbound) trafik varsayılan olarak engellenmelidir. Yalnızca İzmirim Kart yükleme API adresi ve POS Banka provizyon API adresine giden (outbound) trafiğe izin verilmelidir.
* **Port Kısıtlama:** Yerel yönetim arayüzleri dış dünyaya kapalı olmalı, kiosk üzerinde kullanılmayan tüm fiziksel ethernet portları BIOS üzerinden devre dışı bırakılmalıdır.
