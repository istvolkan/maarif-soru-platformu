# Maarif Soru Dönüşüm Platformu

Türkiye Yüzyılı Maarif Modeli'ne uyumlu soru analiz, dönüşüm ve üretim platformu.
Mimari tasarımın tam metni: proje kickoff'unda paylaşılan Artifact (A–O tasarım dokümanı).

## Çözüm yapısı

```
src/
  MaarifPlatform.Domain          — entity'ler, enum'lar (dış bağımlılık yok, Pgvector.Vector hariç)
  MaarifPlatform.Application     — ILLMProvider + Extraction/Storage/Rag sözleşmeleri, RubricEngine (§E)
  MaarifPlatform.Infrastructure  — EF Core (Npgsql + pgvector), DbContext, migration'lar,
                                    PDF extraction (Docnet.Core + heuristic segmenter),
                                    RAG ingestion + retrieval, Analysis pipeline (Anthropic SDK + mock sağlayıcı)
  MaarifPlatform.Api             — ASP.NET Core Web API (composition root), Books/ReferenceDocuments/Questions controller'ları
tests/
  MaarifPlatform.Tests           — xUnit (segmenter, chunker, embedding provider, rubric engine testleri)
docker-compose.yml                — lokal PostgreSQL + pgvector
```

Sprint 1 (§O Faz 1): çözüm iskeleti + veritabanı şeması.
Sprint 2 (§O Faz 1 devamı / §K madde 2-4): PDF → sayfa → soru bloğu → Question DNA pipeline'ı,
`POST /api/books`, `POST /api/books/{id}/extract`, `GET /api/books/{id}/questions`.
Sprint 3 (§O Faz 1 devamı / §K madde 1, §G RAG mimarisi): MEB referans dokümanı yükleme,
chunking, embedding, pgvector retrieval — `POST /api/reference-documents`,
`POST /api/reference-documents/{id}/ingest`, `GET /api/reference-documents/search`.
Sprint 4 (§O Faz 2 / §4+§8 Analysis AI): RAG grounding çekme, ILLMProvider.AnalyzeQuestionAsync
(gerçek Anthropic sağlayıcısı + anahtar gerektirmeyen mock), deterministik RubricEngine (§E),
AlignmentScore kayıtları — `POST /api/questions/{id}/analyze`, `GET /api/questions/{id}`.
Sprint 5 (Multimodal/Vision Question Processing, Phase 1): sayfa rasterizasyonu (Docnet.Core),
ücretsiz heuristic VisionRouter, IVisionProvider (Gemini + anahtar gerektirmeyen mock),
QuestionDna görsel alanları + `question_visual_assets` tablosu, Analysis pipeline'ının
vision-aware hale getirilmesi.
Sprint 6: ikinci Vision sağlayıcı (Anthropic) + provider disagreement/consensus.
Sprint 7 (Auth/RBAC): JWT bearer authentication + rol tabanlı authorization
(`Admin, Editor, Teacher, Reviewer`), mevcut `AppUser`/`users` tablosu üzerine kurulu.
Açık self-registration yok — ilk Admin uygulama açılışında `Auth:BootstrapAdmin`
config'inden otomatik seed edilir, sonraki kullanıcılar `POST /api/users` (Admin-only)
ile oluşturulur. `POST /api/auth/login` JWT döner; Books/ReferenceDocuments/Questions
controller'larında GET uçları herhangi bir role, mutasyon uçları (`Create`, `Extract`,
`Ingest`, `Analyze`) `Admin,Editor` rolüne kısıtlıdır.
Transformation/Quality Judge sonraki bir sprintte eklenecek.

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
- Embedding sağlayıcısı `appsettings.json`'daki `Embeddings:Provider` ile seçilir. Varsayılan
  **"Local"** — `LocalDeterministicEmbeddingProvider`, dış API anahtarı gerektirmez ama
  semantik olarak ANLAMLI DEĞİLDİR (yalnızca aynı metin → aynı vektör garantisi verir, RAG
  borusunun mekaniğini doğrulamak içindir). Gerçek semantik retrieval için
  `Embeddings:Provider=OpenAI` + `Embeddings:OpenAI:ApiKey` (user-secrets ile) gerekir.
- Aynı referans dokümanının/kitabın tekrar yüklenmesi, dosya içeriğinin SHA-256 hash'i
  üzerinden 409 Conflict ile engellenir (§9); tekrar ingest/extract denemesi de aynı şekilde reddedilir.
- Analysis AI sağlayıcısı `appsettings.json`'daki `Ai:Provider` ile seçilir. Varsayılan
  **"Local"** — `LocalHeuristicLLMProvider`, dış API anahtarı gerektirmez ama yapısal
  sinyallere (grounding/şık sayısı/gövde uzunluğu) dayanan bir MOCK'tur, gerçek matematiksel/
  pedagojik değerlendirme yapmaz. Gerçek değerlendirme için `Ai:Provider=Anthropic` +
  `Ai:Anthropic:ApiKey` (resmi `Anthropic` NuGet paketi, tool-use ile zorunlu yapılandırılmış
  çıktı) gerekir. LLM yalnızca kriter başına ham puan döner; ağırlıklandırma ve nihai
  TransformationLevel kararı `RubricEngine`'de (Application/Rubric) deterministik hesaplanır —
  bu ayrım §A tasarım kararıdır.
- RAG'de hiç grounding chunk'ı bulunamazsa (§elestiri madde 1/9) skor ne olursa olsun soru
  `ManualReviewRequired` durumuna düşer; kazanım/beceri iddiası hiçbir zaman kaynaksız
  "Analyzed" sayılmaz.
- Vision sağlayıcısı `appsettings.json`'daki `Vision:Provider` ile seçilir. Varsayılan
  **"Local"** — `LocalMockVisionProvider`, dış API anahtarı gerektirmez ama her zaman
  confidence=0 + MOCK uyarısı döner, bu yüzden görsel gerektiren bir soru asla otomatik
  "Analyzed" olmaz. Gerçek görsel analiz için `Vision:Provider=Gemini` +
  `Vision:Gemini:ApiKey` gerekir (ham HTTP ile Google'ın Generative Language API'sine bağlanır,
  yeni bir NuGet bağımlılığı eklenmedi — bkz. PDF kütüphanesi notu). `GeminiOptions.Model`
  varsayılanı doğrulanmadan güvenilmemeli; devreye almadan önce güncel bir model ID ile
  appsettings üzerinden teyit edilmelidir.
- `HeuristicVisionRouter` her soruyu Vision API'sine göndermez — önce ücretsiz anahtar kelime
  taraması yapar (Türkçe noktalı biçim + ASCII'ye katlanmış yedek, eski/OCR'lı metinler için).
  Yalnızca `requires_visual=true` işaretlenen sorularda sayfa render edilir ve Vision
  sağlayıcısı çağrılır (§9 maliyet ilkesi).
- Vision analizi mevcut "Analyzed" `QuestionVersion` satırına eklenir, yeni bir versiyon
  ÜRETMEZ — vision, analysis'in girdisidir, ayrı bir pipeline aşaması değil.
- `Auth:Jwt:SigningKey` ve `Auth:BootstrapAdmin:Email/Password`, `appsettings.Development.json`
  içindeki dev-only değerlerdir (aynı DB bağlantı dizesi kuralı); gerçek ortamlarda
  user-secrets/key vault kullanılmalıdır.
- Bu sprintte refresh token YOK — bilinçli MVP sınırı. JWT `Auth:Jwt:ExpiryMinutes` sonunda
  süresi dolar, istemci tekrar `POST /api/auth/login` yapmalıdır.
