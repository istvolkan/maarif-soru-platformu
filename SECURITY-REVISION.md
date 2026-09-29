# Güvenlik revizyonu — 29 Eylül 2026

Önceki kod sürümü: `backup/pre-security-20260928` etiketi (`1e46cb4`).
Bu etiket yalnızca kod yedeğidir; veritabanı ve yüklenen dosyaların yedeği değildir.

## Değişiklikler

- API ve Web aynı izin politikalarını kullanır. Soru üretimi ve referans belge işlemleri rolün güncel iznine bağlıdır. Soru okuma/indirme için `QuestionPoolAccess`, soru düzenleme için ayrıca Admin/Editor rolü gerekir. Kitap yönetimi Admin ile sınırlıdır.
- Web işlemleri, açık Blazor oturumunda da güncel oturum ve yetki kontrolü yapar. Cookie/JWT her istekte kullanıcı ve security stamp ile doğrulanır. Blazor kimliği ayrıca 30 saniyede bir yeniden doğrulanır. Başlatılmış bir işlemin ortasında iptal garantisi verilmez.
- Kullanıcı güncellemesi eski oturumları iptal eder; silinen kullanıcıların oturumları reddedilir. Bu revizyona geçişte eski cookie/JWT'ler yeniden giriş gerektirir.
- Ortak parola doğrulaması: beş başarısız denemede 15 dakika hesap kilidi. Sayaç PostgreSQL'de tutulur; eşzamanlı girişlerde concurrency kontrolü vardır. Başarılı giriş sayacı temizler.
- Giriş POST'larında uygulama başına IP başına dakikada 10, toplam dakikada 100 istek sınırı vardır. Proxy arkasında IP sınırı proxy adresine uygulanır; yalnızca güvenilir proxy listesiyle forwarded headers yapılandırılmalıdır. Dağıtık kurulumda ortak kenar hız limiti ayrıca uygulanabilir.
- Giriş dönüş adresi yalnızca yerel yollara izin verir. SignalR tek mesaj limiti 32 KB; InputFile'ın 200 MB toplam dosya limiti ayrı kalır.
- Kullanıcı oluşturma/güncelleme ortak servistedir. Yeni/değiştirilen parolalar 12–1024 karakter olmalıdır.
- Ayarlar tek SaveChanges/transaction ile kaydedilir, bir kez yenilenir. Diğer süreç ayarları 15 saniyelik aralıkla okur; embedding sağlayıcısı yeni servis scope'unda seçilir.
- `*:ApiKey` ayarları AES-256-GCM ile şifrelenir. Setting adı doğrulanan ek veridir. Mevcut düz metin anahtarlar ilk başarılı yüklemede atomik olarak şifrelenir.
- İlk ayar yüklemesinde yalnızca henüz oluşmamış tablo tolere edilir; bağlantı/izin/şifre çözme hataları başlatmayı durdurur. Sonraki yenileme hataları loglanır ve son başarılı ayarlar korunur.
- Yerel geliştirme sırları takip edilen JSON'lardan çıkarılmıştır. Yerel override dosyaları Git'e ve yeni publish çıktısına dahil edilmez.

## Kurulum ve mevcut ortamı yükseltme

1. Veritabanının ve yüklenen dosyaların yedeğini alın. API ve Web'i birlikte yükseltin.
2. Her iki uygulamaya aynı `Security:SettingsEncryptionKey` değerini verin: Base64 kodlanmış rastgele 32 bayt. Anahtarı veritabanından ayrı, erişimi kısıtlı secret store/ortam değişkeninde saklayın ve güvenli biçimde yedekleyin. Kaybı, şifreli API anahtarlarının okunamaması demektir. Bu revizyon otomatik şifreleme anahtarı rotasyonu içermez.
3. Bağlantı, JWT ve ilk yönetici değerlerini ortam değişkenleriyle sağlayın:

   ```text
   ConnectionStrings__MaarifDb
   Security__SettingsEncryptionKey
   Auth__Jwt__SigningKey
   Auth__BootstrapAdmin__Email
   Auth__BootstrapAdmin__Password
   ```

   Bootstrap değerleri yalnızca kullanıcı tablosu boşsa kullanılır. Mevcut kullanıcı parolalarını değiştirmez. Git geçmişindeki geliştirme JWT anahtarı ve yönetici parolası başka ortamda kullanıldıysa bunları değiştirin; eski kod etiketi geçmişteki değerleri korur.

4. Yerel geliştirmede her host'un dizinindeki `appsettings.Development.local.json` kullanılabilir; bu dosyalar yalnızca Development ortamında okunur. Ortam değişkenleri bunların önüne geçer. Bu çalışma sırasında mevcut yerel bağlantı/başlangıç yönetici ayarları bu dosyalara taşındı, ortak yeni JWT ve şifreleme anahtarı üretildi. Dosyaları paylaşmayın. Yeni makinede aşağıdaki yapıyı gerçek yerel değerlerle oluşturun:

   ```json
   {
     "ConnectionStrings": { "MaarifDb": "<yerel PostgreSQL bağlantısı>" },
     "Security": { "SettingsEncryptionKey": "<Base64 32 rastgele bayt>" },
     "Auth": {
       "Jwt": { "SigningKey": "<en az 32 bayt rastgele sır>" },
       "BootstrapAdmin": { "Email": "<e-posta>", "Password": "<benzersiz güçlü parola>" }
     }
   }
   ```

5. `MAARIF_DB_CONNECTION` değişkenini hedef bağlantıya ayarlayın ve migration'ı uygulayın:

   ```powershell
   dotnet ef database update --project src/MaarifPlatform.Infrastructure --startup-project src/MaarifPlatform.Infrastructure
   ```

   Yeni migration: `20260928141650_AddSessionSecurity`. Kullanıcılara benzersiz security stamp ve kilit alanları ekler. Bu çalışma sırasında mevcut uygulama veritabanına uygulanmadı; geçici PostgreSQL veritabanında doğrulandı.

6. API/Web'i yeniden başlatın ve yeniden giriş yapın. Önceden oluşturulmuş publish klasörlerini tekrar kullanmak yerine temiz çıktı üretin; eski çıktılarda yerel JSON kopyaları bulunabilir.

## Doğrulama

```powershell
dotnet build src/MaarifPlatform.Api -c Release
dotnet build src/MaarifPlatform.Web -c Release
dotnet test tests/MaarifPlatform.Tests -c Release
```

PostgreSQL entegrasyon testi için `MAARIF_TEST_DB` değişkeni bir test sunucusuna bağlanmalıdır. Kullanıcının CREATE DATABASE yetkisi gerekir. Test benzersiz `maarif_security_test_*` veritabanını oluşturur, migration/şifreleme/atomiklik testlerini yürütür ve kendi veritabanını kaldırır. Değişken yoksa bu test atlanır. Ücretli AI sağlayıcılarına istek gönderilmez.

## Geri dönüş

Eski kod, yeni şifreli ayarları okuyamaz. Yalnızca Git etiketine dönmek yeterli değildir: eski kodla birlikte yükseltmeden önce alınmış veritabanı yedeğini de geri yükleyin. Şifreleme anahtarını ve dosya yedeklerini koruyun.
