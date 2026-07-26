using System;
using System.Drawing;
using ClipDwg.Extract;
using ClipDwg.Render;
using ClipDwg.Style;
using Xunit;
using Xunit.Abstractions;

namespace ClipDwg.Tests;

/// <summary>
/// 폭을 가진 폴리라인. 치수의 점(dot) 화살촉이 도넛(폭 = 반지름인 닫힌 폴리라인)이라
/// 이걸 무시하면 속이 빈 작은 원이 되고 치수선과 사이가 벌어진다. 실제로 겪은 회귀다.
/// </summary>
public class PolylineWidthTests
{
    private readonly ITestOutputHelper _out;

    public PolylineWidthTests(ITestOutputHelper output) => _out = output;

    private static readonly IrColor Black = new(7, 0, 0, 0);

    /// <summary>
    /// 도넛: 지름 <paramref name="outerDiameter"/> 인 꽉 찬 점.
    /// 중심선 반지름은 지름의 1/4, 폭은 지름의 1/2 이다.
    /// </summary>
    private static IrPath Donut(double outerDiameter)
    {
        double r = outerDiameter / 4;
        var p = new IrPath
        {
            Color = Black,
            Start = new Pt(-r, 0),
            Closed = true,
            IntrinsicWidth = outerDiameter / 2,
        };
        p.Segments.Add(IrSegment.Arc(new Pt(r, 0), new Pt(0, 0), r, Math.PI, -Math.PI));
        p.Segments.Add(IrSegment.Arc(new Pt(-r, 0), new Pt(0, 0), r, 0, -Math.PI));
        return p;
    }

    [Fact]
    public void Donut_CenterIsFilled()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Donut(10));

        using Bitmap bmp = Rasterize(doc, 16);
        Assert.True(IsInk(bmp, bmp.Width / 2, bmp.Height / 2),
            "점 화살촉의 한가운데가 칠해져 있어야 한다 (속이 비면 안 된다)");
    }

    [Fact]
    public void Donut_ReachesFullOuterDiameter()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Donut(10));

        const int ppm = 16;
        using Bitmap bmp = Rasterize(doc, ppm);
        (int minX, _, int maxX, _) = InkBounds(bmp);

        double inkWidthMm = (maxX - minX) / (double)ppm;
        _out.WriteLine($"잉크 지름 {inkWidthMm:0.##}mm (기대 10mm)");

        // 폭을 무시하면 중심선 원만 남아 지름 5mm 가 된다. 그러면 치수선과 사이가 벌어진다.
        Assert.InRange(inkWidthMm, 9.3, 10.7);
    }

    [Fact]
    public void IntrinsicWidth_BeatsColorRule()
    {
        // 색상 규칙으로 0.13mm 를 지정해도 폴리라인의 폭이 이겨야 한다.
        var doc = new IrDocument();
        IrPath donut = Donut(10);
        doc.Shapes.Add(donut);

        var profile = new Profile { DefaultWidthMm = 0.13, Widths = { new WidthRule { Aci = 7, Mm = 0.13 } } };
        new ColorWeightMap(profile).Apply(doc);

        Assert.Equal(0.13, donut.WidthMm, 9);   // 색상 규칙은 채워지되
        using Bitmap bmp = Rasterize(doc, 16);
        Assert.True(IsInk(bmp, bmp.Width / 2, bmp.Height / 2), "실제로는 폴리라인 폭이 적용되어야 한다");
    }

    [Fact]
    public void IntrinsicWidth_ScalesWithOutputScale()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Donut(10));

        var opts = new RenderOptions { OutputScale = 0.5, MarginMm = 1.0 };
        RenderResult r = EmfRenderer.Render(doc, opts);
        using System.Drawing.Imaging.Metafile mf = r.Metafile;

        // 중심선 지름 5 -> 2.5mm, 여기에 폭 5 -> 2.5mm 가 붙어 실제 점 지름 5mm.
        // 여백 1mm 가 양쪽에 붙으므로 7mm 언저리.
        _out.WriteLine($"축척 0.5 결과 {r.WidthMm:0.##}mm");
        Assert.InRange(r.WidthMm, 6.8, 7.6);
    }

    [Fact]
    public void ZeroWidthPolyline_StillUsesColorRule()
    {
        var doc = new IrDocument();
        var p = new IrPath { Color = Black, Start = new Pt(0, 0) };
        p.Segments.Add(IrSegment.Line(new Pt(50, 0)));
        doc.Shapes.Add(p);

        new ColorWeightMap(new Profile { DefaultWidthMm = 2.0 }).Apply(doc);

        const int ppm = 16;
        using Bitmap bmp = Rasterize(doc, ppm);
        (_, int minY, _, int maxY) = InkBounds(bmp);

        double thicknessMm = (maxY - minY) / (double)ppm;
        Assert.InRange(thicknessMm, 1.5, 2.5);
    }

    // ---- 도우미 --------------------------------------------------------

    private static Bitmap Rasterize(IrDocument doc, int pixelsPerMm)
    {
        RenderResult result = EmfRenderer.Render(doc, new RenderOptions());
        using System.Drawing.Imaging.Metafile mf = result.Metafile;

        int w = Math.Max(1, (int)Math.Round(result.WidthMm * pixelsPerMm));
        int h = Math.Max(1, (int)Math.Round(result.HeightMm * pixelsPerMm));

        var bmp = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawImage(mf, new Rectangle(0, 0, w, h));
        }

        return bmp;
    }

    private static bool IsInk(Bitmap bmp, int x, int y)
    {
        if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height)
            return false;

        Color c = bmp.GetPixel(x, y);
        return c.R < 200 || c.G < 200 || c.B < 200;
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) InkBounds(Bitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            if (!IsInk(bmp, x, y))
                continue;

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        Assert.True(maxX >= 0, "잉크가 전혀 없다");
        return (minX, minY, maxX, maxY);
    }
}
