# Maarif Soru Dönüşüm Platformu

Türkiye Yüzyılı Maarif Modeli'ne uyumlu soru analiz, dönüşüm ve üretim platformu.
Mimari tasarımın tam metni: proje kickoff'unda paylaşılan Artifact (A–O tasarım dokümanı).

## Çözüm yapısı

```
src/
  MaarifPlatform.Domain          — entity'ler, enum'lar (dış bağımlılık yok, Pgvector.Vector hariç)
  MaarifPlatform.Application     — ILLMProvider sözleşmesi ve DTO'lar (§11 provider-agnostic mimari)
  MaarifPlatform.Infrastructure  — EF Core (Npgsql + pgvector), DbContext, migration'lar
  MaarifPlatform.Api             — ASP.NET Core Web API (composition root)
tests/
  MaarifPlatform.Tests           — xUnit
docker-compose.yml                — lokal PostgreSQL + pgvector
```

Bu, Sprint 1 kapsamıdır (§O Faz 1 / MVP Sprint 1): çözüm iskeleti + veritabanı şeması.
Auth/RBAC, extraction pipeline, AI sağlayıcı implementasyonları sonraki sprintlerde eklenecek.

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
