namespace MaarifPlatform.Application.Visuals;

/// <summary>§6 Görsel Soru Motoru — LLM bu yapıyı üretir (ne istediğinin TARİFİ), gerçek görsel
/// (SVG) bunu deterministik olarak render eden <see cref="VisualSpecRenderer"/> tarafından
/// üretilir. LLM'in ürettiği hiçbir alan doğrudan kullanıcıya gösterilmez — yalnızca render'ın
/// girdisidir. Faz 2a kapsamı: function_graph / coordinate_system / geometric_shape / table.</summary>
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
    IReadOnlyList<IReadOnlyList<string>>? Rows = null);

public sealed record PlotFunction(string Expression, string? Label = null);
public sealed record PlotPoint(double X, double Y, string? Label = null);
public sealed record PlotSegment(string? From, string? To, double? X1 = null, double? Y1 = null, double? X2 = null, double? Y2 = null, string? Label = null);
public sealed record PlotVector(double X1, double Y1, double X2, double Y2, string? Label = null);
public sealed record PlotCircle(double CenterX, double CenterY, double Radius);
public sealed record PlotSideLabel(string From, string To, string Label);
public sealed record PlotAngleLabel(string Vertex, string Label);

public static class VisualSpecTypes
{
    public const string FunctionGraph = "function_graph";
    public const string CoordinateSystem = "coordinate_system";
    public const string GeometricShape = "geometric_shape";
    public const string Table = "table";
}
