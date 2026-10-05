namespace MaarifPlatform.Application.Visuals;

/// <summary>§6 Görsel Soru Motoru — LLM bu yapıyı üretir (ne istediğinin TARİFİ), gerçek görsel
/// (SVG) bunu deterministik olarak render eden <see cref="VisualSpecRenderer"/> tarafından
/// üretilir. LLM'in ürettiği hiçbir alan doğrudan kullanıcıya gösterilmez — yalnızca render'ın
/// girdisidir. Faz 2a kapsamı: function_graph / coordinate_system / geometric_shape / table.
/// Faz 2b kapsamı (2026-10): diagram / infographic / visual_scenario / mixed_visual — DiagramNodes/
/// DiagramEdges/ChartKind/IconGroups yalnızca bu dört türde dolar; mixed_visual kendi alanı yoktur,
/// mevcut geometric/function/table alanlarını BİR ARADA (şekil/grafik + altında tablo) kullanır —
/// bkz. VisualSpecRenderer.RenderMixedVisual'ın doc'u.</summary>
public sealed record VisualSpec(
    string Type,
    double? XMin = null,
    double? XMax = null,
    double? YMin = null,
    double? YMax = null,
    string? XLabel = null,
    string? YLabel = null,
    IReadOnlyList<PlotFunction>? Functions = null,
    IReadOnlyList<PlotPoint>? Points = null,
    IReadOnlyList<PlotPoint>? Vertices = null,
    IReadOnlyList<PlotSegment>? Segments = null,
    IReadOnlyList<PlotVector>? Vectors = null,
    string? Shape = null,
    PlotCircle? Circle = null,
    IReadOnlyList<PlotSideLabel>? SideLabels = null,
    IReadOnlyList<PlotAngleLabel>? AngleLabels = null,
    IReadOnlyList<string>? Headers = null,
    IReadOnlyList<IReadOnlyList<string>>? Rows = null,
    IReadOnlyList<DiagramNode>? DiagramNodes = null,
    IReadOnlyList<DiagramEdge>? DiagramEdges = null,
    string? ChartKind = null,
    IReadOnlyList<IconGroup>? IconGroups = null);

public sealed record PlotFunction(string Expression, string? Label = null);
public sealed record PlotPoint(double X, double Y, string? Label = null);
public sealed record PlotSegment(string? From, string? To, double? X1 = null, double? Y1 = null, double? X2 = null, double? Y2 = null, string? Label = null);
public sealed record PlotVector(double X1, double Y1, double X2, double Y2, string? Label = null);
public sealed record PlotCircle(double CenterX, double CenterY, double Radius);
public sealed record PlotSideLabel(string From, string To, string Label);
public sealed record PlotAngleLabel(string Vertex, string Label);

/// <summary>Faz 2b diagram — bir kutu/düğüm, sabit X/Y verilmemişse RenderDiagram tarafından
/// otomatik bir ızgaraya yerleştirilir (LLM'in piksel koordinatı uydurmasına gerek bırakmaz).</summary>
public sealed record DiagramNode(string Id, string Label, double? X = null, double? Y = null);
public sealed record DiagramEdge(string From, string To, string? Label = null, bool Directed = true);

/// <summary>Faz 2b visual_scenario — bir sayma/kombinatorik/olasılık senaryosundaki nesneleri basit,
/// tekrarlanan ikonlarla (gerçek fotoğraf/ikon kütüphanesi değil, deterministik SVG şekilleri)
/// temsil eder; örn. "4 kırmızı top, 3 mavi top" → iki IconGroup.</summary>
public sealed record IconGroup(string Icon, int Count, string? Label = null, string? Color = null);

public static class VisualSpecTypes
{
    public const string FunctionGraph = "function_graph";
    public const string CoordinateSystem = "coordinate_system";
    public const string GeometricShape = "geometric_shape";
    public const string Table = "table";
    public const string Diagram = "diagram";
    public const string Infographic = "infographic";
    public const string VisualScenario = "visual_scenario";
    public const string MixedVisual = "mixed_visual";
}

public static class DiagramIcons
{
    public const string Circle = "circle";
    public const string Square = "square";
    public const string Triangle = "triangle";
    public const string Star = "star";
}

public static class ChartKinds
{
    public const string Bar = "bar";
    public const string Pie = "pie";
}
