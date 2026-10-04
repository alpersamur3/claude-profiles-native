# Changelog

## 0.7.0 · 2026-10-05

<details open>
<summary><strong>English</strong></summary>

- Automatically find the installed x64 Microsoft Store Claude when no manual application path is selected.
- Match versioned WindowsApps package folders and compare versions numerically.
- Refresh automatic paths after Store updates; preserve manually selected paths and settings from previous versions.
- Add localized discovery status messages and startup verification.

</details>

<details>
<summary><strong>Türkçe</strong></summary>

- Elle uygulama yolu seçilmemişse kurulu x64 Microsoft Store Claude otomatik bulunur.
- Sürümlü WindowsApps paket klasörleri eşleştirilir; sürümler sayısal karşılaştırılır.
- Store güncellemelerinde otomatik yollar yenilenir; elle seçilen yollar ve önceki sürümlerin ayarları korunur.
- İki dilde bulma durum mesajları ve açılış doğrulaması eklendi.

</details>

## 0.6.0 · 2026-10-05

<details open>
<summary><strong>English</strong></summary>

- Added live English/Turkish language switching with a persistent preference.
- Localized the interface, browser sign-in chooser, status messages and application errors.
- Simplified the launcher layout and grouped browser sign-in and Code history controls.
- Added access to per-profile history backups.
- Updated Google sign-in instructions following successful user confirmation.
- Prepared one bilingual README with expandable sections and screenshots.
- Kept existing profile IDs, names, sign-ins and session paths compatible.

</details>

<details>
<summary><strong>Türkçe</strong></summary>

- Kalıcı tercihle birlikte anında İngilizce/Türkçe dil değişimi eklendi.
- Arayüz, tarayıcı girişindeki profil seçimi, durum mesajları ve uygulama hataları çevrildi.
- Başlatıcı sadeleştirildi; tarayıcı girişi ve Code geçmişi kontrolleri gruplandı.
- Profil bazında geçmiş yedeklerine erişim eklendi.
- Google girişinin çalıştığı doğrulandıktan sonra kullanım talimatları güncellendi.
- Açılır bölümler ve ekran görüntüleriyle tek bir iki dilli README hazırlandı.
- Mevcut profil kimlikleri, adlar, girişler ve oturum yollarıyla uyumluluk korundu.

</details>

## 0.5.0 · 2026-10-03

History imports also update existing records when the source conversation’s last activity is newer. Replaced records are backed up atomically; more advanced native-profile records remain unchanged.

## 0.4.0 · 2026-10-03

Added same-account import of missing Claude Code list records from existing Desktop stores.

## 0.3.0 · 2026-10-03

Introduced persistent profiles using the installed Claude Desktop executable, native user-data directories and a browser sign-in profile chooser.
