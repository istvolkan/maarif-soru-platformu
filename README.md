# Maarif Soru Dönüşüm Platformu

Türkiye Yüzyılı Maarif Modeli'ne uyumlu soru analiz, dönüşüm ve üretim platformu.
Mimari tasarımın tam metni: proje kickoff'unda paylaşılan Artifact (A–O tasarım dokümanı).

## Çözüm yapısı

```
src/
  MaarifPlatform.Domain          — entity'ler, enum'lar (dış bağımlılık yok, Pgvector.Vector hariç)
  MaarifPlatform.Application     — ILLMProvider + Extraction/Storage sözleşmeleri ve DTO'lar (§11)
  MaarifPlatform.Infrastructure  — EF Core (Npgsql + pgvector), DbContext, migration'lar,
                                    PDF extraction pipeline (Docnet.Core + heuristic segmenter)
  MaarifPlatform.Api             — ASP.NET Core Web API (composition root), BooksController
tests/
  MaarifPlatform.Tests           — xUnit (segmenter testleri)
docker-compose.yml                — lokal PostgreSQL + pgvector
```

Sprint 1 (§O Faz 1): çözüm iskeleti + veritabanı şeması.
Sprint 2 (§O Faz 1 devamı / §K madde 2-4): PDF → sayfa → soru bloğu → Question DNA pipeline'ı,
`POST /api/books`, `POST /api/books/{id}/extract`, `GET /api/books/{id}/questions`.
Auth/RBAC ve AI sağlayıcı implementasyonları sonraki sprintlerde eklenecek.

### PDF kütüphanesi seçimi hakkında not

Extraction için önce `UglyToad.PdfPig` denendi; nuget.org'daki paket kaydının **ele
geçirilmiş/el değiştirmiş** olduğuna dair güçlü belirtiler bulununca (tanınmayan sahip
"grinay", jenerik açıklama, `0.1.9-alpha001-patch1` → `1.7.0-custom-5` tutarsız sürüm
sıçraması) paket kaldırıldı, yerel NuGet önbelleğinden temizlendi ve yerine PDFium'u
saran, temiz sürüm geçmişine sahip **Docnet.Core** (MIT, GowenGit) kullanıldı.
Bağımlılık eklerken NuGet paket kaydını (sahip, açıklama, sürüm geçmişi) doğrulama
alışkanlığı bu projede sürdürülmelidir.

## Yerel geliştirme

Gereksinimler: .NET 8 SDK, Docker Desktop, `dotnet-ef` global tool (`dotnet tool install -g dotnet-ef`).

```bash
# 1. Veritabanını başlat
docker compose up -d

# 2. Şemayı uygula
dotnet ef database update \
  --project src/MaarifPlatform.Infrastructure \
  --startup-project src/MaarifPlatform.Infrastructure

# 3. API'yi çalıştır
dotnet run --project src/MaarifPlatform.Api

# 4. Sağlık kontrolü
curl http://localhost:5xxx/health
```

Bağlantı dizesi `src/MaarifPlatform.Api/appsettings.Development.json` içinde,
docker-compose.yml'deki kullanıcı/parola ile eşleşecek şekilde tanımlıdır
(yalnızca yerel geliştirme içindir — gerçek ortamlarda user-secrets / key vault kullanılmalı).
Host portu **5433**'tür (bu makinede 5432'yi kullanan başka bir proje — `fleetview` — ile
çakışmaması için); container içi Postgres portu standart 5432'de kalır.

## Yeni migration eklemek

```bash
dotnet ef migrations add <İsim> \
  --project src/MaarifPlatform.Infrastructure \
  --startup-project src/MaarifPlatform.Infrastructure \
  -o Persistence/Migrations
```

## Notlar

- `ReferenceChunk.Embedding` sütunu `vector(1536)` olarak tanımlıdır; gerçek embedding
  modeli pilot aşamasında seçilince boyut migration ile güncellenebilir (bkz. §elestiri madde 6/§M).
- `question_dna` tablosundaki `*Json` alanları `jsonb` sütunlardır; henüz olgunlaşmamış
  yeni DNA alanları önce `ExtensionsJson`'a eklenmelidir (§elestiri madde 12).
- `AuditLogEntry` uygulama katmanında yalnızca insert edilmeli, hiçbir zaman update edilmemelidir (§N).
- Soru sınırı tespiti (`HeuristicQuestionSegmenter`) tamamen deterministiktir, AI kullanmaz
  (§9 maliyet ilkesi: önce ücretsiz heuristic, LLM sadece düşük güvenli durumlarda). Bir soru
  bloğunun sayfa sınırını aşması desteklenmez; her sayfa bağımsız işlenir (bilinçli MVP sınırı).
- Yerel dosya deposu (`LocalFileStorage`) `src/MaarifPlatform.Api/data/` altına yazar,
  `.gitignore`'dadır; gerçek ortamda blob storage ile değiştirilecek (§L).
