using System.Globalization;
using System.Text;

namespace MaarifPlatform.Application.Visuals;

/// <summary>§6 Görsel Soru Motoru'nun render ucu — <see cref="VisualSpec"/>'i (LLM'in ürettiği
/// TARİF) SVG'ye çevirir. Tamamen deterministik, LLM'e hiç geri gitmez. Geçersiz/tutarsız
/// spec'lerde (boş fonksiyon listesi, xMin&gt;=xMax, eksik köşe/daire vb.)
/// <see cref="InvalidOperationException"/> fırlatır — GenerationOrchestrationService bunu
/// regenerate sinyali olarak kullanır (§15 fail-safe: render edilemeyen bir görsel asla
/// sessizce atlanmaz veya sahte bir şeyle değiştirilmez).</summary>
public static class VisualSpecRenderer
{
    private const int CanvasWidth = 640;
    private const int CanvasHeight = 440;
    private const int Margin = 48;
    private const int SampleCount = 300;

    private static readonly string[] SeriesColors = ["#c6242a", "#1a56db", "#0f9d58", "#8b5cf6", "#ea580c"];

    public static string RenderToSvg(VisualSpec spec) => spec.Type switch
    {
        VisualSpecTypes.FunctionGraph => RenderFunctionGraph(spec),
        VisualSpecTypes.CoordinateSystem => RenderCoordinateSystem(spec),
        VisualSpecTypes.GeometricShape => RenderGeometricShape(spec),
        VisualSpecTypes.Table => RenderTable(spec),
        _ => throw new InvalidOperationException(
            $"Bilinmeyen görsel türü: '{spec.Type}'. Desteklenen türler: {VisualSpecTypes.FunctionGraph}, " +
            $"{VisualSpecTypes.CoordinateSystem}, {VisualSpecTypes.GeometricShape}, {VisualSpecTypes.Table}.")
    };

    // ============================== FUNCTION GRAPH ==============================

    private static string RenderFunctionGraph(VisualSpec spec)
    {
        if (spec.Functions is null || spec.Functions.Count == 0)
        {
            throw new InvalidOperationException("function_graph için en az bir fonksiyon gerekli.");
        }

        var xMin = spec.XMin ?? -10;
        var xMax = spec.XMax ?? 10;
        if (xMax <= xMin)
        {
            throw new InvalidOperationException($"Geçersiz x aralığı: x_min={xMin} >= x_max={xMax}.");
        }

        var compiled = spec.Functions
            .Select(f => (f.Label, Fn: MathExpressionEvaluator.Compile(f.Expression))) // sözdizimi hatası burada fırlar
            .ToList();

        var samples = new List<(double X, double Y)>[compiled.Count];
        for (var i = 0; i < compiled.Count; i++)
        {
            samples[i] = SampleFunction(compiled[i].Fn, xMin, xMax);
        }

        var (yMin, yMax) = ResolveYRange(spec, samples);

        var sb = new StringBuilder();
        BeginSvg(sb);
        DrawAxesAndGrid(sb, xMin, xMax, yMin, yMax, spec.XLabel, spec.YLabel);

        for (var i = 0; i < compiled.Count; i++)
        {
            var color = SeriesColors[i % SeriesColors.Length];
            DrawFunctionPath(sb, samples[i], xMin, xMax, yMin, yMax, color);
        }

        if (spec.Points is { Count: > 0 })
        {
            DrawPoints(sb, spec.Points, xMin, xMax, yMin, yMax);
        }

        DrawLegend(sb, compiled.Select((c, i) => (c.Label ?? $"f{i + 1}(x)", SeriesColors[i % SeriesColors.Length])).ToList());

        EndSvg(sb);
        return sb.ToString();
    }

    private static List<(double X, double Y)> SampleFunction(Func<double, double> fn, double xMin, double xMax)
    {
        var result = new List<(double X, double Y)>(SampleCount);
        for (var i = 0; i < SampleCount; i++)
        {
            var x = xMin + (xMax - xMin) * i / (SampleCount - 1);
            result.Add((x, fn(x)));
        }
        return result;
    }

    private static (double YMin, double YMax) ResolveYRange(VisualSpec spec, List<(double X, double Y)>[] samples)
    {
        if (spec.YMin is not null && spec.YMax is not null && spec.YMax > spec.YMin)
        {
            return (spec.YMin.Value, spec.YMax.Value);
        }

        var finiteValues = samples.SelectMany(s => s).Select(p => p.Y).Where(double.IsFinite).ToList();
        if (finiteValues.Count == 0)
        {
            throw new InvalidOperationException("Fonksiyon(lar) verilen x aralığında hiçbir noktada tanımlı değil.");
        }

        var min = finiteValues.Min();
        var max = finiteValues.Max();
        if (Math.Abs(max - min) < 1e-9)
        {
            min -= 1;
            max += 1;
        }
        var pad = (max - min) * 0.1;
        return (min - pad, max + pad);
    }

    private static void DrawFunctionPath(
        StringBuilder sb, List<(double X, double Y)> samples, double xMin, double xMax, double yMin, double yMax, string color)
    {
        // Asimptot/tanımsızlık noktalarında path'i böler (sahte dikey çizgi çizmez) —
        // görünür aralığın epey dışına taşan veya sonlu olmayan değerler "boşluk" sayılır.
        var yRange = yMax - yMin;
        var lowerBound = yMin - yRange * 2;
        var upperBound = yMax + yRange * 2;

        var path = new StringBuilder();
        var drawing = false;
        foreach (var (x, y) in samples)
        {
            var visible = double.IsFinite(y) && y >= lowerBound && y <= upperBound;
            if (!visible)
            {
                drawing = false;
                continue;
            }

            var (px, py) = ToPixel(x, y, xMin, xMax, yMin, yMax);
            path.Append(drawing ? " L " : " M ").Append(Fmt(px)).Append(' ').Append(Fmt(py));
            drawing = true;
        }

        if (path.Length > 0)
        {
            sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2.5\" />\n");
        }
    }

    // ============================== COORDINATE SYSTEM ==============================

    private static string RenderCoordinateSystem(VisualSpec spec)
    {
        var hasContent = (spec.Points?.Count ?? 0) > 0 || (spec.Segments?.Count ?? 0) > 0 || (spec.Vectors?.Count ?? 0) > 0;
        if (!hasContent)
        {
            throw new InvalidOperationException("coordinate_system için en az bir nokta, doğru parçası veya vektör gerekli.");
        }

        var (xMin, xMax, yMin, yMax) = ResolveBounds(spec);

        var sb = new StringBuilder();
        BeginSvg(sb);
        DrawAxesAndGrid(sb, xMin, xMax, yMin, yMax, spec.XLabel, spec.YLabel);

        var pointsByLabel = (spec.Points ?? []).Where(p => p.Label is not null).ToDictionary(p => p.Label!, p => p);

        if (spec.Segments is { Count: > 0 })
        {
            foreach (var seg in spec.Segments)
            {
                var (x1, y1) = ResolveEndpoint(seg.From, seg.X1, seg.Y1, pointsByLabel, "from");
                var (x2, y2) = ResolveEndpoint(seg.To, seg.X2, seg.Y2, pointsByLabel, "to");
                var (px1, py1) = ToPixel(x1, y1, xMin, xMax, yMin, yMax);
                var (px2, py2) = ToPixel(x2, y2, xMin, xMax, yMin, yMax);
                sb.Append($"<line x1=\"{Fmt(px1)}\" y1=\"{Fmt(py1)}\" x2=\"{Fmt(px2)}\" y2=\"{Fmt(py2)}\" stroke=\"#1a56db\" stroke-width=\"2\" />\n");
                if (seg.Label is not null)
                {
                    DrawText(sb, (px1 + px2) / 2, (py1 + py2) / 2 - 6, Escape(seg.Label), "#1a56db");
                }
            }
        }

        if (spec.Vectors is { Count: > 0 })
        {
            foreach (var v in spec.Vectors)
            {
                DrawVector(sb, v, xMin, xMax, yMin, yMax);
            }
        }

        if (spec.Points is { Count: > 0 })
        {
            DrawPoints(sb, spec.Points, xMin, xMax, yMin, yMax);
        }

        EndSvg(sb);
        return sb.ToString();
    }

    private static (double X, double Y) ResolveEndpoint(
        string? label, double? x, double? y, IReadOnlyDictionary<string, PlotPoint> pointsByLabel, string role)
    {
        if (label is not null && pointsByLabel.TryGetValue(label, out var point))
        {
            return (point.X, point.Y);
        }
        if (x is not null && y is not null)
        {
            return (x.Value, y.Value);
        }
        throw new InvalidOperationException($"Doğru parçasının '{role}' ucu çözülemedi (ne tanımlı bir nokta etiketi ne de x/y koordinatı var).");
    }

    private static void DrawVector(StringBuilder sb, PlotVector v, double xMin, double xMax, double yMin, double yMax)
    {
        var (px1, py1) = ToPixel(v.X1, v.Y1, xMin, xMax, yMin, yMax);
        var (px2, py2) = ToPixel(v.X2, v.Y2, xMin, xMax, yMin, yMax);
        sb.Append($"<line x1=\"{Fmt(px1)}\" y1=\"{Fmt(py1)}\" x2=\"{Fmt(px2)}\" y2=\"{Fmt(py2)}\" stroke=\"#0f9d58\" stroke-width=\"2.5\" marker-end=\"url(#arrow)\" />\n");
        if (v.Label is not null)
        {
            DrawText(sb, (px1 + px2) / 2 + 8, (py1 + py2) / 2, Escape(v.Label), "#0f9d58");
        }
    }

    // ============================== GEOMETRIC SHAPE ==============================

    private static string RenderGeometricShape(VisualSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Shape))
        {
            throw new InvalidOperationException("geometric_shape için 'shape' alanı gerekli (triangle/rectangle/polygon/circle).");
        }

        var isCircle = spec.Shape.Equals("circle", StringComparison.OrdinalIgnoreCase);
        if (isCircle && spec.Circle is null)
        {
            throw new InvalidOperationException("shape=circle için 'circle' (center_x/center_y/radius) gerekli.");
        }
        if (!isCircle && (spec.Vertices is null || spec.Vertices.Count < 3))
        {
            throw new InvalidOperationException($"shape={spec.Shape} için en az 3 köşe (vertices) gerekli.");
        }
        if (isCircle && spec.Circle!.Radius <= 0)
        {
            throw new InvalidOperationException("Dairenin yarıçapı (radius) pozitif olmalı.");
        }

        var (xMin, xMax, yMin, yMax) = ResolveBounds(spec);

        var sb = new StringBuilder();
        BeginSvg(sb);

        if (isCircle)
        {
            var (cx, cy) = ToPixel(spec.Circle!.CenterX, spec.Circle.CenterY, xMin, xMax, yMin, yMax);
            var scale = (CanvasWidth - 2.0 * Margin) / (xMax - xMin);
            var r = spec.Circle.Radius * scale;
            sb.Append($"<circle cx=\"{Fmt(cx)}\" cy=\"{Fmt(cy)}\" r=\"{Fmt(r)}\" fill=\"none\" stroke=\"#c6242a\" stroke-width=\"2.5\" />\n");
        }
        else
        {
            var pixelPoints = spec.Vertices!.Select(v => ToPixel(v.X, v.Y, xMin, xMax, yMin, yMax)).ToList();
            var pointsAttr = string.Join(" ", pixelPoints.Select(p => $"{Fmt(p.Item1)},{Fmt(p.Item2)}"));
            sb.Append($"<polygon points=\"{pointsAttr}\" fill=\"#c6242a1a\" stroke=\"#c6242a\" stroke-width=\"2.5\" />\n");

            foreach (var v in spec.Vertices!)
            {
                if (v.Label is null)
                {
                    continue;
                }
                var (px, py) = ToPixel(v.X, v.Y, xMin, xMax, yMin, yMax);
                DrawText(sb, px, py - 10, Escape(v.Label), "#111827");
            }
        }

        if (spec.SideLabels is { Count: > 0 } && spec.Vertices is not null)
        {
            var byLabel = spec.Vertices.Where(v => v.Label is not null).ToDictionary(v => v.Label!, v => v);
            foreach (var side in spec.SideLabels)
            {
                if (!byLabel.TryGetValue(side.From, out var from) || !byLabel.TryGetValue(side.To, out var to))
                {
                    throw new InvalidOperationException($"side_labels: '{side.From}' veya '{side.To}' etiketli bir köşe bulunamadı.");
                }
                var (px1, py1) = ToPixel(from.X, from.Y, xMin, xMax, yMin, yMax);
                var (px2, py2) = ToPixel(to.X, to.Y, xMin, xMax, yMin, yMax);
                DrawText(sb, (px1 + px2) / 2, (py1 + py2) / 2, Escape(side.Label), "#1a56db");
            }
        }

        if (spec.AngleLabels is { Count: > 0 } && spec.Vertices is not null)
        {
            var byLabel = spec.Vertices.Where(v => v.Label is not null).ToDictionary(v => v.Label!, v => v);
            foreach (var angle in spec.AngleLabels)
            {
                if (!byLabel.TryGetValue(angle.Vertex, out var vertex))
                {
                    throw new InvalidOperationException($"angle_labels: '{angle.Vertex}' etiketli bir köşe bulunamadı.");
                }
                var (px, py) = ToPixel(vertex.X, vertex.Y, xMin, xMax, yMin, yMax);
                DrawText(sb, px + 12, py + 12, Escape(angle.Label), "#0f9d58");
            }
        }

        EndSvg(sb);
        return sb.ToString();
    }

    // ============================== TABLE ==============================

    private static string RenderTable(VisualSpec spec)
    {
        if (spec.Headers is null || spec.Headers.Count == 0)
        {
            throw new InvalidOperationException("table için en az bir başlık (headers) gerekli.");
        }
        if (spec.Rows is null || spec.Rows.Count == 0)
        {
            throw new InvalidOperationException("table için en az bir satır (rows) gerekli.");
        }
        foreach (var row in spec.Rows)
        {
            if (row.Count != spec.Headers.Count)
            {
                throw new InvalidOperationException(
                    $"Tablo satırı {row.Count} hücre içeriyor ama {spec.Headers.Count} başlık var — sayılar eşleşmeli.");
            }
        }

        const int cellPaddingX = 16;
        const int rowHeight = 36;
        var colWidths = new int[spec.Headers.Count];
        for (var c = 0; c < spec.Headers.Count; c++)
        {
            var maxLen = Math.Max(spec.Headers[c].Length, spec.Rows.Max(r => r[c].Length));
            colWidths[c] = Math.Max(80, maxLen * 9 + cellPaddingX * 2);
        }

        var tableWidth = colWidths.Sum();
        var tableHeight = rowHeight * (spec.Rows.Count + 1);
        var width = tableWidth + 2 * Margin;
        var height = tableHeight + 2 * Margin;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">\n");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"white\" />\n");

        var y = Margin;
        var x = Margin;
        DrawTableRow(sb, spec.Headers, x, y, colWidths, rowHeight, isHeader: true);
        y += rowHeight;
        foreach (var row in spec.Rows)
        {
            DrawTableRow(sb, row, x, y, colWidths, rowHeight, isHeader: false);
            y += rowHeight;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void DrawTableRow(StringBuilder sb, IReadOnlyList<string> cells, int x, int y, int[] colWidths, int rowHeight, bool isHeader)
    {
        var cx = x;
        for (var c = 0; c < cells.Count; c++)
        {
            var fill = isHeader ? "#f3f4f6" : "white";
            sb.Append($"<rect x=\"{cx}\" y=\"{y}\" width=\"{colWidths[c]}\" height=\"{rowHeight}\" fill=\"{fill}\" stroke=\"#d1d5db\" stroke-width=\"1\" />\n");
            var textWeight = isHeader ? "700" : "400";
            sb.Append($"<text x=\"{Fmt(cx + colWidths[c] / 2.0)}\" y=\"{Fmt(y + rowHeight / 2.0 + 5)}\" font-size=\"14\" font-weight=\"{textWeight}\" " +
                      $"text-anchor=\"middle\" fill=\"#111827\">{Escape(cells[c])}</text>\n");
            cx += colWidths[c];
        }
    }

    // ============================== ORTAK ÇİZİM YARDIMCILARI ==============================

    private static (double XMin, double XMax, double YMin, double YMax) ResolveBounds(VisualSpec spec)
    {
        if (spec.XMin is not null && spec.XMax is not null && spec.YMin is not null && spec.YMax is not null
            && spec.XMax > spec.XMin && spec.YMax > spec.YMin)
        {
            return (spec.XMin.Value, spec.XMax.Value, spec.YMin.Value, spec.YMax.Value);
        }

        var allX = new List<double>();
        var allY = new List<double>();
        void Collect(double x, double y) { allX.Add(x); allY.Add(y); }

        foreach (var p in (spec.Points ?? []).Concat(spec.Vertices ?? []))
        {
            Collect(p.X, p.Y);
        }
        foreach (var s in spec.Segments ?? [])
        {
            if (s.X1 is not null && s.Y1 is not null) Collect(s.X1.Value, s.Y1.Value);
            if (s.X2 is not null && s.Y2 is not null) Collect(s.X2.Value, s.Y2.Value);
        }
        foreach (var v in spec.Vectors ?? [])
        {
            Collect(v.X1, v.Y1);
            Collect(v.X2, v.Y2);
        }
        if (spec.Circle is { } circle)
        {
            Collect(circle.CenterX - circle.Radius, circle.CenterY - circle.Radius);
            Collect(circle.CenterX + circle.Radius, circle.CenterY + circle.Radius);
        }

        if (allX.Count == 0)
        {
            // Etiketli uçlar (segment from/to) dışında hiçbir koordinat verilmemiş — makul bir varsayılan.
            return (-10, 10, -10, 10);
        }

        var xMin = allX.Min();
        var xMax = allX.Max();
        var yMin = allY.Min();
        var yMax = allY.Max();
        var xPad = Math.Max((xMax - xMin) * 0.2, 1);
        var yPad = Math.Max((yMax - yMin) * 0.2, 1);
        return (xMin - xPad, xMax + xPad, yMin - yPad, yMax + yPad);
    }

    private static void BeginSvg(StringBuilder sb)
    {
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{CanvasWidth}\" height=\"{CanvasHeight}\" viewBox=\"0 0 {CanvasWidth} {CanvasHeight}\">\n");
        sb.Append($"<defs><marker id=\"arrow\" markerWidth=\"10\" markerHeight=\"10\" refX=\"8\" refY=\"3\" orient=\"auto\">" +
                  "<path d=\"M0,0 L0,6 L9,3 z\" fill=\"#0f9d58\" /></marker></defs>\n");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{CanvasWidth}\" height=\"{CanvasHeight}\" fill=\"white\" />\n");
    }

    private static void EndSvg(StringBuilder sb) => sb.Append("</svg>");

    private static void DrawAxesAndGrid(StringBuilder sb, double xMin, double xMax, double yMin, double yMax, string? xLabel, string? yLabel)
    {
        const int gridLines = 10;

        for (var i = 0; i <= gridLines; i++)
        {
            var x = xMin + (xMax - xMin) * i / gridLines;
            var (px, _) = ToPixel(x, yMin, xMin, xMax, yMin, yMax);
            sb.Append($"<line x1=\"{Fmt(px)}\" y1=\"{Margin}\" x2=\"{Fmt(px)}\" y2=\"{CanvasHeight - Margin}\" stroke=\"#e5e7eb\" stroke-width=\"1\" />\n");
            DrawText(sb, px, CanvasHeight - Margin + 18, FmtLabel(x), "#6b7280", fontSize: 11);

            var y = yMin + (yMax - yMin) * i / gridLines;
            var (_, py) = ToPixel(xMin, y, xMin, xMax, yMin, yMax);
            sb.Append($"<line x1=\"{Margin}\" y1=\"{Fmt(py)}\" x2=\"{CanvasWidth - Margin}\" y2=\"{Fmt(py)}\" stroke=\"#e5e7eb\" stroke-width=\"1\" />\n");
            DrawText(sb, Margin - 22, py + 4, FmtLabel(y), "#6b7280", fontSize: 11);
        }

        // Eksenler (x=0/y=0 görünür aralıktaysa vurgulu çizilir).
        if (xMin <= 0 && xMax >= 0)
        {
            var (px, _) = ToPixel(0, yMin, xMin, xMax, yMin, yMax);
            sb.Append($"<line x1=\"{Fmt(px)}\" y1=\"{Margin}\" x2=\"{Fmt(px)}\" y2=\"{CanvasHeight - Margin}\" stroke=\"#111827\" stroke-width=\"1.5\" />\n");
        }
        if (yMin <= 0 && yMax >= 0)
        {
            var (_, py) = ToPixel(xMin, 0, xMin, xMax, yMin, yMax);
            sb.Append($"<line x1=\"{Margin}\" y1=\"{Fmt(py)}\" x2=\"{CanvasWidth - Margin}\" y2=\"{Fmt(py)}\" stroke=\"#111827\" stroke-width=\"1.5\" />\n");
        }

        sb.Append($"<rect x=\"{Margin}\" y=\"{Margin}\" width=\"{CanvasWidth - 2 * Margin}\" height=\"{CanvasHeight - 2 * Margin}\" fill=\"none\" stroke=\"#9ca3af\" stroke-width=\"1\" />\n");

        if (!string.IsNullOrWhiteSpace(xLabel))
        {
            DrawText(sb, CanvasWidth / 2.0, CanvasHeight - 8, Escape(xLabel), "#374151", fontSize: 13);
        }
        if (!string.IsNullOrWhiteSpace(yLabel))
        {
            sb.Append($"<text x=\"14\" y=\"{Fmt(CanvasHeight / 2.0)}\" font-size=\"13\" fill=\"#374151\" text-anchor=\"middle\" " +
                      $"transform=\"rotate(-90 14 {Fmt(CanvasHeight / 2.0)})\">{Escape(yLabel)}</text>\n");
        }
    }

    private static void DrawPoints(StringBuilder sb, IReadOnlyList<PlotPoint> points, double xMin, double xMax, double yMin, double yMax)
    {
        foreach (var p in points)
        {
            var (px, py) = ToPixel(p.X, p.Y, xMin, xMax, yMin, yMax);
            sb.Append($"<circle cx=\"{Fmt(px)}\" cy=\"{Fmt(py)}\" r=\"4\" fill=\"#c6242a\" />\n");
            if (p.Label is not null)
            {
                DrawText(sb, px + 8, py - 8, Escape(p.Label), "#111827");
            }
        }
    }

    private static void DrawLegend(StringBuilder sb, IReadOnlyList<(string Label, string Color)> series)
    {
        var y = Margin - 26;
        var x = Margin;
        foreach (var (label, color) in series)
        {
            sb.Append($"<line x1=\"{x}\" y1=\"{y}\" x2=\"{x + 18}\" y2=\"{y}\" stroke=\"{color}\" stroke-width=\"3\" />\n");
            DrawText(sb, x + 24, y + 4, Escape(label), "#374151", fontSize: 12, anchor: "start");
            x += 24 + label.Length * 7 + 20;
        }
    }

    private static void DrawText(StringBuilder sb, double x, double y, string escapedText, string color, int fontSize = 12, string anchor = "middle") =>
        sb.Append($"<text x=\"{Fmt(x)}\" y=\"{Fmt(y)}\" font-size=\"{fontSize}\" fill=\"{color}\" text-anchor=\"{anchor}\">{escapedText}</text>\n");

    private static (double, double) ToPixel(double x, double y, double xMin, double xMax, double yMin, double yMax)
    {
        var px = Margin + (x - xMin) / (xMax - xMin) * (CanvasWidth - 2.0 * Margin);
        var py = CanvasHeight - Margin - (y - yMin) / (yMax - yMin) * (CanvasHeight - 2.0 * Margin);
        return (px, py);
    }

    private static string Fmt(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string FmtLabel(double value) => Math.Abs(value - Math.Round(value)) < 1e-9
        ? Math.Round(value).ToString(CultureInfo.InvariantCulture)
        : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
