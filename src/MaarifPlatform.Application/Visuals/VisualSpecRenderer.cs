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
        VisualSpecTypes.Diagram => RenderDiagram(spec),
        VisualSpecTypes.Infographic => RenderInfographic(spec),
        VisualSpecTypes.VisualScenario => RenderVisualScenario(spec),
        VisualSpecTypes.MixedVisual => RenderMixedVisual(spec),
        _ => throw new InvalidOperationException(
            $"Bilinmeyen görsel türü: '{spec.Type}'. Desteklenen türler: {VisualSpecTypes.FunctionGraph}, " +
            $"{VisualSpecTypes.CoordinateSystem}, {VisualSpecTypes.GeometricShape}, {VisualSpecTypes.Table}, " +
            $"{VisualSpecTypes.Diagram}, {VisualSpecTypes.Infographic}, {VisualSpecTypes.VisualScenario}, " +
            $"{VisualSpecTypes.MixedVisual}.")
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

    // ============================== DIAGRAM (Faz 2b) ==============================

    private const double DiagramBoxWidth = 140, DiagramBoxHeight = 56, DiagramHGap = 60, DiagramVGap = 40;

    private static string RenderDiagram(VisualSpec spec)
    {
        if (spec.DiagramNodes is null || spec.DiagramNodes.Count == 0)
        {
            throw new InvalidOperationException("diagram için en az bir düğüm (diagram_nodes) gerekli.");
        }

        var nodeIds = spec.DiagramNodes.Select(n => n.Id).ToHashSet();
        foreach (var edge in spec.DiagramEdges ?? [])
        {
            if (!nodeIds.Contains(edge.From) || !nodeIds.Contains(edge.To))
            {
                throw new InvalidOperationException($"diagram_edges: '{edge.From}' veya '{edge.To}' id'li bir düğüm bulunamadı.");
            }
        }

        // LLM piksel koordinatı uydurmak zorunda değil: X/Y verilmemiş düğümler basit bir
        // ızgaraya otomatik yerleştirilir (sqrt(n) sütun).
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(spec.DiagramNodes.Count)));
        var positioned = new Dictionary<string, (double X, double Y)>();
        for (var i = 0; i < spec.DiagramNodes.Count; i++)
        {
            var node = spec.DiagramNodes[i];
            if (node.X is not null && node.Y is not null)
            {
                positioned[node.Id] = (node.X.Value, node.Y.Value);
            }
            else
            {
                positioned[node.Id] = (
                    (i % columns) * (DiagramBoxWidth + DiagramHGap),
                    (i / columns) * (DiagramBoxHeight + DiagramVGap));
            }
        }

        var minX = positioned.Values.Min(p => p.X);
        var minY = positioned.Values.Min(p => p.Y);
        var maxX = positioned.Values.Max(p => p.X);
        var maxY = positioned.Values.Max(p => p.Y);
        var offsetX = Margin - minX;
        var offsetY = Margin - minY;
        var width = maxX - minX + DiagramBoxWidth + 2.0 * Margin;
        var height = maxY - minY + DiagramBoxHeight + 2.0 * Margin;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\">\n");
        sb.Append("<defs><marker id=\"diagram-arrow\" markerWidth=\"10\" markerHeight=\"10\" refX=\"9\" refY=\"3\" orient=\"auto\">" +
                   "<path d=\"M0,0 L0,6 L9,3 z\" fill=\"#374151\" /></marker></defs>\n");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" fill=\"white\" />\n");

        foreach (var edge in spec.DiagramEdges ?? [])
        {
            var (fx, fy) = positioned[edge.From];
            var (tx, ty) = positioned[edge.To];
            var x1 = fx + offsetX + DiagramBoxWidth / 2;
            var y1 = fy + offsetY + DiagramBoxHeight / 2;
            var x2 = tx + offsetX + DiagramBoxWidth / 2;
            var y2 = ty + offsetY + DiagramBoxHeight / 2;
            var marker = edge.Directed ? " marker-end=\"url(#diagram-arrow)\"" : "";
            sb.Append($"<line x1=\"{Fmt(x1)}\" y1=\"{Fmt(y1)}\" x2=\"{Fmt(x2)}\" y2=\"{Fmt(y2)}\" stroke=\"#374151\" stroke-width=\"2\"{marker} />\n");
            if (edge.Label is not null)
            {
                DrawText(sb, (x1 + x2) / 2, (y1 + y2) / 2 - 6, Escape(edge.Label), "#374151", fontSize: 11);
            }
        }

        foreach (var node in spec.DiagramNodes)
        {
            var (nx, ny) = positioned[node.Id];
            var px = nx + offsetX;
            var py = ny + offsetY;
            sb.Append($"<rect x=\"{Fmt(px)}\" y=\"{Fmt(py)}\" width=\"{Fmt(DiagramBoxWidth)}\" height=\"{Fmt(DiagramBoxHeight)}\" " +
                      "rx=\"8\" fill=\"#eef2ff\" stroke=\"#4338ca\" stroke-width=\"2\" />\n");
            DrawText(sb, px + DiagramBoxWidth / 2, py + DiagramBoxHeight / 2 + 5, Escape(node.Label), "#1e1b4b", fontSize: 13);
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    // ============================== INFOGRAPHIC (Faz 2b) ==============================

    private static string RenderInfographic(VisualSpec spec)
    {
        if (spec.Headers is null || spec.Headers.Count == 0)
        {
            throw new InvalidOperationException("infographic için kategori adları (headers) gerekli.");
        }
        if (spec.Rows is null || spec.Rows.Count == 0)
        {
            throw new InvalidOperationException("infographic için en az bir veri satırı (rows) gerekli.");
        }

        var valuesRow = spec.Rows[0];
        if (valuesRow.Count != spec.Headers.Count)
        {
            throw new InvalidOperationException(
                $"infographic veri satırı {valuesRow.Count} değer içeriyor ama {spec.Headers.Count} kategori var — sayılar eşleşmeli.");
        }

        var values = new double[valuesRow.Count];
        for (var i = 0; i < valuesRow.Count; i++)
        {
            if (!double.TryParse(valuesRow[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || values[i] < 0)
            {
                throw new InvalidOperationException($"infographic veri değeri negatif olmayan bir sayı olmalı: '{valuesRow[i]}'.");
            }
        }
        if (values.All(v => v == 0))
        {
            throw new InvalidOperationException("infographic verilerinin tamamı sıfır olamaz.");
        }

        var chartKind = string.IsNullOrWhiteSpace(spec.ChartKind) ? ChartKinds.Bar : spec.ChartKind;
        return chartKind switch
        {
            ChartKinds.Bar => RenderBarChart(spec.Headers, values, spec.YLabel),
            ChartKinds.Pie => RenderPieChart(spec.Headers, values),
            _ => throw new InvalidOperationException($"Bilinmeyen chart_kind: '{chartKind}'. Desteklenen: {ChartKinds.Bar}, {ChartKinds.Pie}.")
        };
    }

    private static string RenderBarChart(IReadOnlyList<string> categories, double[] values, string? yLabel)
    {
        var maxValue = values.Max();
        var barAreaHeight = CanvasHeight - 2.0 * Margin;
        var slot = (CanvasWidth - 2.0 * Margin) / values.Length;
        var barWidth = slot * 0.6;

        var sb = new StringBuilder();
        BeginSvg(sb);
        sb.Append($"<line x1=\"{Margin}\" y1=\"{CanvasHeight - Margin}\" x2=\"{CanvasWidth - Margin}\" y2=\"{CanvasHeight - Margin}\" stroke=\"#111827\" stroke-width=\"1.5\" />\n");

        for (var i = 0; i < values.Length; i++)
        {
            var barHeight = maxValue > 0 ? values[i] / maxValue * barAreaHeight : 0;
            var x = Margin + slot * i + (slot - barWidth) / 2;
            var y = CanvasHeight - Margin - barHeight;
            var color = SeriesColors[i % SeriesColors.Length];
            sb.Append($"<rect x=\"{Fmt(x)}\" y=\"{Fmt(y)}\" width=\"{Fmt(barWidth)}\" height=\"{Fmt(barHeight)}\" fill=\"{color}\" />\n");
            DrawText(sb, x + barWidth / 2, y - 6, Escape(FmtLabel(values[i])), "#111827", fontSize: 11);
            DrawText(sb, x + barWidth / 2, CanvasHeight - Margin + 18, Escape(categories[i]), "#374151", fontSize: 11);
        }

        if (!string.IsNullOrWhiteSpace(yLabel))
        {
            sb.Append($"<text x=\"14\" y=\"{Fmt(CanvasHeight / 2.0)}\" font-size=\"13\" fill=\"#374151\" text-anchor=\"middle\" " +
                      $"transform=\"rotate(-90 14 {Fmt(CanvasHeight / 2.0)})\">{Escape(yLabel)}</text>\n");
        }

        EndSvg(sb);
        return sb.ToString();
    }

    private static string RenderPieChart(IReadOnlyList<string> categories, double[] values)
    {
        var total = values.Sum();
        var cx = CanvasWidth / 2.0 - 70;
        var cy = CanvasHeight / 2.0;
        var r = Math.Min(CanvasWidth, CanvasHeight) / 2.0 - Margin;

        var sb = new StringBuilder();
        BeginSvg(sb);

        var startAngle = -Math.PI / 2;
        for (var i = 0; i < values.Length; i++)
        {
            var sweep = total > 0 ? values[i] / total * 2 * Math.PI : 0;
            var endAngle = startAngle + sweep;
            var x1 = cx + r * Math.Cos(startAngle);
            var y1 = cy + r * Math.Sin(startAngle);
            var x2 = cx + r * Math.Cos(endAngle);
            var y2 = cy + r * Math.Sin(endAngle);
            var largeArc = sweep > Math.PI ? 1 : 0;
            var color = SeriesColors[i % SeriesColors.Length];
            sb.Append($"<path d=\"M {Fmt(cx)} {Fmt(cy)} L {Fmt(x1)} {Fmt(y1)} A {Fmt(r)} {Fmt(r)} 0 {largeArc} 1 {Fmt(x2)} {Fmt(y2)} Z\" " +
                      $"fill=\"{color}\" stroke=\"white\" stroke-width=\"1.5\" />\n");
            startAngle = endAngle;
        }

        var legendX = cx + r + 30;
        var legendY = Margin;
        for (var i = 0; i < values.Length; i++)
        {
            var color = SeriesColors[i % SeriesColors.Length];
            var pct = total > 0 ? values[i] / total * 100 : 0;
            sb.Append($"<rect x=\"{Fmt(legendX)}\" y=\"{Fmt(legendY)}\" width=\"14\" height=\"14\" fill=\"{color}\" />\n");
            DrawText(sb, legendX + 20, legendY + 12, Escape($"{categories[i]} (%{FmtLabel(pct)})"), "#374151", fontSize: 11, anchor: "start");
            legendY += 22;
        }

        EndSvg(sb);
        return sb.ToString();
    }

    // ============================== VISUAL SCENARIO (Faz 2b) ==============================

    private const int MaxIconsPerGroupDisplayed = 20;
    private const double IconSize = 28, IconGap = 10, IconRowHeight = 60;

    private static string RenderVisualScenario(VisualSpec spec)
    {
        if (spec.IconGroups is null || spec.IconGroups.Count == 0)
        {
            throw new InvalidOperationException("visual_scenario için en az bir ikon grubu (icon_groups) gerekli.");
        }
        if (spec.IconGroups.Any(g => g.Count <= 0))
        {
            throw new InvalidOperationException("icon_groups içindeki her grup için count pozitif olmalı.");
        }

        var height = 2.0 * Margin + spec.IconGroups.Count * IconRowHeight;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{CanvasWidth}\" height=\"{Fmt(height)}\" viewBox=\"0 0 {CanvasWidth} {Fmt(height)}\">\n");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{CanvasWidth}\" height=\"{Fmt(height)}\" fill=\"white\" />\n");

        var y = (double)Margin;
        foreach (var group in spec.IconGroups)
        {
            var color = string.IsNullOrWhiteSpace(group.Color) ? "#c6242a" : group.Color!;
            var displayCount = Math.Min(group.Count, MaxIconsPerGroupDisplayed);
            var x = (double)Margin;
            for (var i = 0; i < displayCount; i++)
            {
                DrawIcon(sb, group.Icon, x, y, IconSize, color);
                x += IconSize + IconGap;
            }
            if (group.Count > MaxIconsPerGroupDisplayed)
            {
                DrawText(sb, x + 10, y + IconSize / 2 + 5, Escape($"× {group.Count}"), "#111827", fontSize: 14, anchor: "start");
            }
            var caption = group.Label ?? $"{group.Icon} ({group.Count})";
            DrawText(sb, Margin, y + IconSize + 16, Escape(caption), "#374151", fontSize: 12, anchor: "start");
            y += IconRowHeight;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void DrawIcon(StringBuilder sb, string icon, double x, double y, double size, string color)
    {
        switch (icon)
        {
            case DiagramIcons.Square:
                sb.Append($"<rect x=\"{Fmt(x)}\" y=\"{Fmt(y)}\" width=\"{Fmt(size)}\" height=\"{Fmt(size)}\" fill=\"{color}\" />\n");
                break;
            case DiagramIcons.Triangle:
                var tx1 = x + size / 2;
                var ty1 = y;
                var tx2 = x;
                var ty2 = y + size;
                var tx3 = x + size;
                var ty3 = y + size;
                sb.Append($"<polygon points=\"{Fmt(tx1)},{Fmt(ty1)} {Fmt(tx2)},{Fmt(ty2)} {Fmt(tx3)},{Fmt(ty3)}\" fill=\"{color}\" />\n");
                break;
            case DiagramIcons.Star:
                sb.Append(BuildStarPolygon(x + size / 2, y + size / 2, size / 2, color));
                break;
            case DiagramIcons.Circle:
            default:
                sb.Append($"<circle cx=\"{Fmt(x + size / 2)}\" cy=\"{Fmt(y + size / 2)}\" r=\"{Fmt(size / 2)}\" fill=\"{color}\" />\n");
                break;
        }
    }

    private static string BuildStarPolygon(double cx, double cy, double r, string color)
    {
        var points = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var angle = Math.PI / 5 * i - Math.PI / 2;
            var radius = i % 2 == 0 ? r : r * 0.45;
            var x = cx + radius * Math.Cos(angle);
            var y = cy + radius * Math.Sin(angle);
            points.Add($"{Fmt(x)},{Fmt(y)}");
        }
        return $"<polygon points=\"{string.Join(" ", points)}\" fill=\"{color}\" />\n";
    }

    // ============================== MIXED VISUAL (Faz 2b) ==============================

    /// <summary>"Karma Görsel" — gerçek textbook sorularında en sık görülen kombinasyonu
    /// (bir şekil/grafik + altında onu açıklayan bir tablo) temsil eder. Yeni bir çizim mantığı
    /// İCAT ETMEZ — zaten test edilmiş RenderFunctionGraph/RenderGeometricShape/
    /// RenderCoordinateSystem/RenderTable'ın ÜRETTİĞİ SVG'leri (her biri kendi doğrulamasından
    /// geçmiş) string düzeyinde alt alta birleştirir (ComposeStacked). Bu yüzden İKİ unsur da
    /// (birincil görsel VE tablo) birlikte zorunludur — yalnızca biri varsa karma değil, tek-tür
    /// spec'i (function_graph/geometric_shape/coordinate_system/table) kullanılmalı.</summary>
    private static string RenderMixedVisual(VisualSpec spec)
    {
        var hasPrimary = (spec.Functions?.Count ?? 0) > 0
            || !string.IsNullOrWhiteSpace(spec.Shape)
            || (spec.Points?.Count ?? 0) > 0 || (spec.Segments?.Count ?? 0) > 0 || (spec.Vectors?.Count ?? 0) > 0;
        var hasTable = (spec.Headers?.Count ?? 0) > 0 && (spec.Rows?.Count ?? 0) > 0;

        if (!hasPrimary || !hasTable)
        {
            throw new InvalidOperationException(
                "mixed_visual iki unsuru BİRLİKTE gerektirir: bir birincil görsel (functions/shape+vertices/" +
                "points+segments+vectors) VE bir tablo (headers/rows). Yalnızca biri varsa ilgili tek-tür " +
                "spec'ini (function_graph/geometric_shape/coordinate_system/table) kullanın.");
        }

        var primarySvg = spec.Functions is { Count: > 0 }
            ? RenderFunctionGraph(spec)
            : !string.IsNullOrWhiteSpace(spec.Shape)
                ? RenderGeometricShape(spec)
                : RenderCoordinateSystem(spec);
        var tableSvg = RenderTable(spec);

        return ComposeStacked(primarySvg, tableSvg);
    }

    private static string ComposeStacked(string topSvg, string bottomSvg)
    {
        const double gap = 20;
        var (topContent, topWidth, topHeight) = ExtractSvgParts(topSvg);
        var (bottomContent, bottomWidth, bottomHeight) = ExtractSvgParts(bottomSvg);

        var width = Math.Max(topWidth, bottomWidth);
        var height = topHeight + gap + bottomHeight;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" viewBox=\"0 0 {Fmt(width)} {Fmt(height)}\">\n");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{Fmt(width)}\" height=\"{Fmt(height)}\" fill=\"white\" />\n");
        sb.Append($"<g transform=\"translate({Fmt((width - topWidth) / 2)}, 0)\">\n{topContent}\n</g>\n");
        sb.Append($"<g transform=\"translate({Fmt((width - bottomWidth) / 2)}, {Fmt(topHeight + gap)})\">\n{bottomContent}\n</g>\n");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Kendi ürettiğimiz, formatı bilinen bir SVG dizesinden (hep düz sayısal
    /// width/height özniteliği — InvariantCulture, nokta ayıraçlı) iç içeriği ve boyutları çıkarır.</summary>
    private static (string Content, double Width, double Height) ExtractSvgParts(string svg)
    {
        var widthMatch = System.Text.RegularExpressions.Regex.Match(svg, "width=\"([\\d.]+)\"");
        var heightMatch = System.Text.RegularExpressions.Regex.Match(svg, "height=\"([\\d.]+)\"");
        var width = double.Parse(widthMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var height = double.Parse(heightMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        var startIdx = svg.IndexOf('>') + 1;
        var endIdx = svg.LastIndexOf("</svg>", StringComparison.Ordinal);
        var content = svg[startIdx..endIdx];
        return (content, width, height);
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
