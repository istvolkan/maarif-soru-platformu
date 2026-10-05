using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaarifPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeDuplicateCasedThemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CurriculumExtractionService eskiden StringComparer.OrdinalIgnoreCase ile tema adı
            // tekilleştirmesi yapıyordu; bu, Türkçe noktalı/noktasız I harflerini (İ/i/I/ı) yanlış
            // eşleyip ("Nicelikler ve Değişimler" != "NİCELİKLER VE DEĞİŞİMLER" ordinal karşılaştırmada)
            // aynı Grade+Subject+anlam için iki ayrı Theme satırı oluşturdu. Postgres'in lower()'ı
            // Türkçe harfleri doğru küçültüyor, bu yüzden lower("Name") ile grup bazlı tekilleştirme
            // güvenilir. Kanonik satır olarak tümü büyük harf OLMAYAN (orijinal/düzgün yazım) ve en
            // eski CreatedAt'e sahip satır seçilir; diğer kopyaya bağlı learning_outcomes satırları
            // kanoniğe yeniden bağlanıp kopya silinir. İdempotent — tekrar çalıştırılırsa hiçbir
            // grup birden fazla satır içermediğinden etkisi olmaz.
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT t.""Id"", t.""Grade"", t.""Subject"", t.""Name"", t.""CreatedAt"",
                           lower(t.""Name"") AS norm_name,
                           ROW_NUMBER() OVER (
                               PARTITION BY t.""Grade"", t.""Subject"", lower(t.""Name"")
                               ORDER BY (t.""Name"" = upper(t.""Name"")) ASC, t.""CreatedAt"" ASC
                           ) AS rn
                    FROM themes t
                ),
                canon AS (
                    SELECT dup.""Id"" AS dup_id, keep.""Id"" AS canonical_id
                    FROM ranked dup
                    JOIN ranked keep
                        ON dup.""Grade"" = keep.""Grade""
                       AND dup.""Subject"" = keep.""Subject""
                       AND dup.norm_name = keep.norm_name
                       AND keep.rn = 1
                    WHERE dup.rn > 1
                )
                UPDATE learning_outcomes lo
                SET ""ThemeId"" = canon.canonical_id
                FROM canon
                WHERE lo.""ThemeId"" = canon.dup_id;
            ");

            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT t.""Id"", t.""Grade"", t.""Subject"", t.""Name"", t.""CreatedAt"",
                           lower(t.""Name"") AS norm_name,
                           ROW_NUMBER() OVER (
                               PARTITION BY t.""Grade"", t.""Subject"", lower(t.""Name"")
                               ORDER BY (t.""Name"" = upper(t.""Name"")) ASC, t.""CreatedAt"" ASC
                           ) AS rn
                    FROM themes t
                ),
                canon AS (
                    SELECT dup.""Id"" AS dup_id
                    FROM ranked dup
                    JOIN ranked keep
                        ON dup.""Grade"" = keep.""Grade""
                       AND dup.""Subject"" = keep.""Subject""
                       AND dup.norm_name = keep.norm_name
                       AND keep.rn = 1
                    WHERE dup.rn > 1
                )
                DELETE FROM themes t
                USING canon
                WHERE t.""Id"" = canon.dup_id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alınamaz — birleştirilen kopya Theme satırları silindi, hangi learning_outcomes'un
            // hangi kopyaya ait olduğu bilgisi kalıcı olarak kayboldu (bilinçli kabul edilen kayıp,
            // iki satır da aynı anlamı taşıyordu).
        }
    }
}
