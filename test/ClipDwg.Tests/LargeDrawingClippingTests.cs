using System;
using System.Drawing;
using ClipDwg.Extract;
using ClipDwg.Render;
using Xunit;
using Xunit.Abstractions;

namespace ClipDwg.Tests;

/// <summary>
/// 도면이 커질수록 오른쪽·아래가 잘리는지 확인한다.
/// 좌표 보정 계수의 오차는 비율이라 도면 크기에 비례해 커지는데 여백은 고정이므로,
/// 어느 크기부터는 여백을 다 먹고 잘리게 된다.
/// </summary>
public class LargeDrawingClippingTests
{
    private readonly ITestOutputHelper _out;

    public LargeDrawingClippingTests(ITestOutputHelper output) => _out = output;

    private static readonly IrColor Black = new(7, 0, 0, 0);

    private static IrDocument Rect(double w, double h)
    {
        var p = new IrPath { Color = Black, Start = new Pt(0, 0), Closed = true };
        p.Segments.Add(IrSegment.Line(new Pt(w, 0)));
        p.Segments.Add(IrSegment.Line(new Pt(w, h)));
        p.Segments.Add(IrSegment.Line(new Pt(0, h)));
        p.Segments.Add(IrSegment.Line(new Pt(0, 0)));

        var doc = new IrDocument();
        doc.Shapes.Add(p);
        return doc;
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(500, 300)]
    [InlineData(2000, 1200)]
    [InlineData(10000, 6000)]
    [InlineData(50000, 30000)]
    public void RightAndBottomEdges_StayInsideFrame(double w, double h)
    {
        // 큰 도면은 출력 축척을 줄여서 붙이는 게 보통이다. 결과물이 약 200mm가 되게 맞춘다.
        double scale = 200.0 / w;
        var opts = new RenderOptions { OutputScale = scale };

        RenderResult r = EmfRenderer.Render(Rect(w, h), opts);
        using System.Drawing.Imaging.Metafile mf = r.Metafile;

        const int ppm = 10;
        int pw = (int)Math.Round(r.WidthMm * ppm), ph = (int)Math.Round(r.HeightMm * ppm);
        using var bmp = new Bitmap(pw, ph);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawImage(mf, new Rectangle(0, 0, pw, ph));
        }

        (int minX, int minY, int maxX, int maxY) = InkBounds(bmp);

        double leftMm = minX / (double)ppm;
        double rightMm = (pw - 1 - maxX) / (double)ppm;
        double topMm = minY / (double)ppm;
        double bottomMm = (ph - 1 - maxY) / (double)ppm;

        _out.WriteLine($"도면 {w} x {h} (축척 {scale:0.#####}) -> {r.WidthMm:0.##} x {r.HeightMm:0.##} mm");
        _out.WriteLine($"  여백 실측(mm)  좌 {leftMm:0.00}  우 {rightMm:0.00}  상 {topMm:0.00}  하 {bottomMm:0.00}");

        // 기본 여백 1.0mm - 선굵기 절반 0.125mm = 0.875mm 정도가 나와야 한다.
        Assert.True(rightMm > 0.5, $"우측 여백이 부족하다. {rightMm:0.00}mm");
        Assert.True(bottomMm > 0.5, $"하단 여백이 부족하다. {bottomMm:0.00}mm");

        // 프레임을 정수 장치 단위로 내림하면 오른쪽·아래만 좁아진다(원래 겪은 증상).
        // 남는 쪽은 무해하므로 "부족하지 않을 것"만 확인한다.
        Assert.True(rightMm >= leftMm - 0.15,
            $"우측 여백이 좌측보다 좁다. 좌 {leftMm:0.00}mm, 우 {rightMm:0.00}mm");
        Assert.True(bottomMm >= topMm - 0.15,
            $"하단 여백이 상단보다 좁다. 상 {topMm:0.00}mm, 하 {bottomMm:0.00}mm");
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) InkBounds(Bitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            Color c = bmp.GetPixel(x, y);
            if (c.R >= 200 && c.G >= 200 && c.B >= 200)
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
