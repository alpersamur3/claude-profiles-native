# Validation · 0.7.0

2026-10-05 · Windows x64

<details open>
<summary><strong>English</strong></summary>

## Automated verification

- 234 checks cover persistent profile identity, path validation, callback arguments, host path preservation, history import and replacement, exact backups, translation coverage, matching format placeholders, saved language and compatibility with settings from older versions.
- The Windows release builds as a self-contained executable with no compiler warnings.
- English and Turkish launcher screens and callback choosers were rendered and visually inspected. Switching the running interface from English to Turkish and back was checked, including the saved language, edited profile name and unchanged profile IDs.

- Discovery checks cover numeric version ordering, x64 filtering, missing executables, Unicode/spaces, unavailable roots, manual-path preservation, automatic refresh after updates and migration of older settings.
- An empty-settings GUI check found the locally installed Claude application, populated and saved its path, and preserved profile IDs without launching Claude.

## Installation checks

- Two main Claude processes were launched from the installed Microsoft Store application with distinct native user-data directories and independent app settings and cookie databases.
- Existing Code history was imported and verified byte-for-byte. A more advanced record transferred with Session Mover replaced an older profile record, with a recoverable backup and a valid transcript reference.
- Email sign-in and Google sign-in were confirmed by the user. On 2026-10-05 Windows Default Apps showed Claude Profiles Native as the selected CLAUDE handler.
- The supplied screenshot shows concurrent Claude windows signed into different accounts.

These checks describe the tested installation, not every Claude or Windows version. Desktop updates, all tools in every account, and Cowork compatibility require separate verification. Code history updates are applied on launch or on demand; open windows may require a full quit and reopen.

</details>

<details>
<summary><strong>Türkçe</strong></summary>

## Otomatik doğrulama

- 234 kontrol kalıcı profil kimliğini, yol doğrulamasını, giriş bağlantısı argümanlarını, normal dosya yollarının korunmasını, geçmiş aktarımı ve güncellemelerini, birebir yedekleri, çeviri kapsamını, eşleşen yer tutucuları, kaydedilen dili ve eski ayarlarla uyumluluğu kapsar.
- Windows dağıtımı derleyici uyarısı olmadan, çalışma zamanı dahil tek dosya olarak derlenir.
- İngilizce ve Türkçe başlatıcı ekranları ve giriş profil seçicileri oluşturulup görsel olarak incelendi. Açık arayüzde iki dil arasında geçiş, kaydedilen dil, düzenlenen profil adı ve değişmeyen profil kimlikleri kontrol edildi.

- Otomatik bulma kontrolleri sayısal sürüm sıralamasını, x64 filtresini, eksik yürütülebilir dosyaları, Unicode/boşlukları, erişilemeyen kökleri, elle seçilen yolu korumayı, sürüm güncellemelerinde otomatik yenilemeyi ve eski ayarların taşınmasını kapsar.
- Boş ayarla başlayan arayüz kontrolü, kurulu Claude uygulamasını bulup yolunu doldurdu ve kaydetti; Claude’u başlatmadan profil kimliklerini korudu.

## Kurulum kontrolleri

- Kurulu Microsoft Store uygulamasından iki ana Claude süreci, farklı yerel kullanıcı verisi klasörleri ve ayrı ayar/çerez depolarıyla açıldı.
- Mevcut Code geçmişi aktarıldı ve dosyaları birebir doğrulandı. Session Mover ile taşınan ilerlemiş bir kayıt, eski profil kaydını geri alınabilir yedekle güncelledi; transkript referansı da kontrol edildi.
- Kullanıcı e-posta ve Google ile girişin çalıştığını doğruladı. 2026-10-05 tarihinde Windows Varsayılan Uygulamalar ekranında CLAUDE yönlendirmesi için Claude Profiles Native seçiliydi.
- Paylaşılan ekran görüntüsü farklı hesaplarla açık Claude pencerelerini gösteriyor.

Bu kontroller test edilen kurulumu anlatır; her Claude veya Windows sürümünü kapsamaz. Desktop güncellemeleri, her hesapta tüm araçlar ve Cowork uyumluluğu ayrıca doğrulanmalıdır. Code geçmişi açılışta veya elle güncellenir; açık pencerelerde tamamen çıkıp yeniden açmak gerekebilir.

</details>
