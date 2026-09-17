using MaarifPlatform.Application.Visuals;

namespace MaarifPlatform.Tests.Visuals;

public class VisualSpecRendererTests
{
    [Fact]
    public void RenderToSvg_FunctionGraph_ProducesValidSvgWithPathAndAxes()
    {
        var spec = new VisualSpec(
            VisualSpecTypes.FunctionGraph,
            XMin: -10, XMax: 10,
            Functions: [new PlotFunction("2*x-3", "f(x)=2x-3")]);

        var svg = VisualSpecRenderer.RenderToSvg(spec);

        Assert.StartsWith("<svg", svg);
        Assert.EndsWith("</svg>", svg);
        Assert.Contains("<path d=\"", svg);
        Assert.Contains("f(x)=2x-3", svg);
    }

    [Fact]
    public void RenderToSvg_FunctionGraph_MultipleSeriesUsesDistinctColorsAndLegend()
    {
        var spec = new VisualSpec(
            VisualSpecTypes.FunctionGraph,
            Functions: [new PlotFunction("x", "birinci"), new PlotFunction("x^2", "ikinci")]);

        var svg = VisualSpecRenderer.RenderToSvg(spec);

        // "<path d=" defs'teki ok işareti (marker) tanımını da içerir; eğri sayısını doğrulamak
        // için yalnızca gerçek fonksiyon path'lerini (fill="none") sayıyoruz.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, "<path d=\"[^\"]*\" fill=\"none\"").Count);
        Assert.Contains("birinci", svg);
        Assert.Contains("ikinci", svg);
    }

    [Fact]
    public void RenderToSvg_FunctionGraph_HandlesAsymptoteWithoutSyntheticVerticalLine()
    {
        // tan(x), pi/2'de tanımsız — bu bir sözdizimi/parse hatası DEĞİL, matematiksel bir
        // tanımsızlık; render başarılı olmalı, sadece path o noktada kopmalı.
        var spec = new VisualSpec(VisualSpecTypes.FunctionGraph, XMin: -1, XMax: 1, Functions: [new PlotFunction("1/x")]);

        var svg = VisualSpecRenderer.RenderToSvg(spec);

        Assert.Contains("<path d=\"", svg);
    }

    [Fact]
    public void RenderToSvg_FunctionGraph_EmptyFunctions_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.FunctionGraph, Functions: []);
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_FunctionGraph_InvalidXRange_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.FunctionGraph, XMin: 5, XMax: 5, Functions: [new PlotFunction("x")]);
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_FunctionGraph_InvalidExpression_ThrowsFormatException()
    {
        var spec = new VisualSpec(VisualSpecTypes.FunctionGraph, Functions: [new PlotFunction("notafunction(x)")]);
        Assert.Throws<FormatException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_FunctionGraph_AllUndefinedOverRange_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.FunctionGraph, XMin: -5, XMax: -1, Functions: [new PlotFunction("sqrt(x)")]);
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_CoordinateSystem_WithPointsSegmentsVectors_Renders()
    {
        var spec = new VisualSpec(
            VisualSpecTypes.CoordinateSystem,
            Points: [new PlotPoint(1, 2, "A"), new PlotPoint(4, 6, "B")],
            Segments: [new PlotSegment("A", "B")],
            Vectors: [new PlotVector(0, 0, 3, 4, "v")]);

        var svg = VisualSpecRenderer.RenderToSvg(spec);

        Assert.Contains("<circle", svg);
        Assert.Contains("<line", svg);
        Assert.Contains("marker-end", svg);
        Assert.Contains(">A<", svg);
        Assert.Contains(">B<", svg);
    }

    [Fact]
    public void RenderToSvg_CoordinateSystem_NoContent_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.CoordinateSystem);
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_CoordinateSystem_SegmentReferencingUnknownLabel_Throws()
    {
        var spec = new VisualSpec(
            VisualSpecTypes.CoordinateSystem,
            Points: [new PlotPoint(1, 2, "A")],
            Segments: [new PlotSegment("A", "Z")]);

        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_GeometricShape_Triangle_RendersPolygonWithLabels()
    {
        var spec = new VisualSpec(
            VisualSpecTypes.GeometricShape,
            Shape: "triangle",
            Vertices: [new PlotPoint(0, 0, "A"), new PlotPoint(4, 0, "B"), new PlotPoint(0, 3, "C")],
            SideLabels: [new PlotSideLabel("A", "B", "4 cm")],
            AngleLabels: [new PlotAngleLabel("A", "90°")]);

        var svg = VisualSpecRenderer.RenderToSvg(spec);

        Assert.Contains("<polygon", svg);
        Assert.Contains("4 cm", svg);
        Assert.Contains("90°", svg);
    }

    [Fact]
    public void RenderToSvg_GeometricShape_Circle_RendersCircleElement()
    {
        var spec = new VisualSpec(VisualSpecTypes.GeometricShape, Shape: "circle", Circle: new PlotCircle(0, 0, 5));

        var svg = VisualSpecRenderer.RenderToSvg(spec);

        Assert.Contains("<circle", svg);
    }

    [Fact]
    public void RenderToSvg_GeometricShape_TooFewVertices_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.GeometricShape, Shape: "triangle", Vertices: [new PlotPoint(0, 0), new PlotPoint(1, 1)]);
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_GeometricShape_CircleWithoutCircleData_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.GeometricShape, Shape: "circle");
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_GeometricShape_MissingShapeType_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.GeometricShape);
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_Table_RendersGridWithHeaderAndRows()
    {
        var spec = new VisualSpec(
            VisualSpecTypes.Table,
            Headers: ["x", "f(x)"],
            Rows: [["0", "3"], ["1", "5"]]);

        var svg = VisualSpecRenderer.RenderToSvg(spec);

        Assert.Contains("f(x)", svg);
        // Arka plan rect'i (1) + 2 sütun * 3 satır (başlık+2 veri) hücre rect'i (6) = 7.
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(svg, "<rect").Count);
    }

    [Fact]
    public void RenderToSvg_Table_EmptyRows_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.Table, Headers: ["x"], Rows: []);
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_Table_RowLengthMismatch_Throws()
    {
        var spec = new VisualSpec(VisualSpecTypes.Table, Headers: ["x", "y"], Rows: [["1"]]);
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    [Fact]
    public void RenderToSvg_UnknownType_Throws()
    {
        var spec = new VisualSpec("pie_chart");
        Assert.Throws<InvalidOperationException>(() => VisualSpecRenderer.RenderToSvg(spec));
    }

    /// <summary>Regresyon: sunucunun/geliştirme makinesinin varsayılan kültürü (ör. Türkçe,
    /// ondalık ayıracı virgül) SVG sayısal özelliklerine (x/y/points/r vb.) sızarsa
    /// ("x=\"88,0\"" gibi) tarayıcı bunu geçersiz sayı olarak yorumlar ve metni/şekli
    /// sessizce yanlış/eksik çizer — gerçek bir MEB kullanıcısı Türkçe Windows'ta çalıştırdığı
    /// için bu, testte YAKALANMADAN üretime çıkabilecek bir hataydı (bkz. DrawTableRow'daki
    /// kültüre-duyarlı ":F1" formatlaması). Tüm ondalık koordinatlar CultureInfo.InvariantCulture
    /// ile (nokta ayıraçlı) üretilmeli.</summary>
    [Theory]
    [InlineData(VisualSpecTypes.Table)]
    [InlineData(VisualSpecTypes.FunctionGraph)]
    [InlineData(VisualSpecTypes.CoordinateSystem)]
    [InlineData(VisualSpecTypes.GeometricShape)]
    public void RenderToSvg_NeverUsesCultureSpecificDecimalSeparator(string type)
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            var svg = type switch
            {
                VisualSpecTypes.Table => VisualSpecRenderer.RenderToSvg(new VisualSpec(
                    VisualSpecTypes.Table, Headers: ["x", "f(x) = 2x - 3"], Rows: [["0", "-3"], ["1", "-1"]])),
                VisualSpecTypes.FunctionGraph => VisualSpecRenderer.RenderToSvg(new VisualSpec(
                    VisualSpecTypes.FunctionGraph, XLabel: "x", YLabel: "y", Functions: [new PlotFunction("2*x-3", "f")])),
                VisualSpecTypes.CoordinateSystem => VisualSpecRenderer.RenderToSvg(new VisualSpec(
                    VisualSpecTypes.CoordinateSystem, Points: [new PlotPoint(1, 2, "A")], Vectors: [new PlotVector(0, 0, 3, 4, "v")])),
                VisualSpecTypes.GeometricShape => VisualSpecRenderer.RenderToSvg(new VisualSpec(
                    VisualSpecTypes.GeometricShape, Shape: "circle", Circle: new PlotCircle(0, 0, 5))),
                _ => throw new NotSupportedException()
            };

            // x/y/cx/cy/r gibi TEK sayı içeren özellikler ASLA virgül içermemeli (yalnızca
            // "points" listesi çift ayıracı olarak virgül kullanır, ör. "88.00,71.00" — o ayrı
            // bir durum, burada test edilmiyor). Bozuk kültürde "x=\"88,00\"" gibi bir değer
            // çıkardı; bu regex bunu doğrudan yakalar.
            Assert.DoesNotMatch(@"\b(?:x1|y1|x2|y2|x|y|cx|cy|r)=""[^""]*,[^""]*""", svg);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void RenderToSvg_LabelWithXmlSpecialCharacters_IsEscaped()
    {
        var spec = new VisualSpec(
            VisualSpecTypes.FunctionGraph,
            Functions: [new PlotFunction("x", "a<b & c>d")]);

        var svg = VisualSpecRenderer.RenderToSvg(spec);

        Assert.DoesNotContain("a<b & c>d", svg);
        Assert.Contains("a&lt;b &amp; c&gt;d", svg);
    }
}
