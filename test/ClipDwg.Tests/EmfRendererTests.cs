using System;
using System.Drawing;
using System.Drawing.Imaging;
using ClipDwg.Extract;
using ClipDwg.Render;
using Xunit;

namespace ClipDwg.Tests;

public class EmfRendererTests
{
    private static readonly IrColor White = new(7, 255, 255, 255);
    private static readonly IrColor Red = new(1, 255, 0, 0);

    private static IrPath Rect(double w, double h, IrColor color)
    {
        var p = new IrPath { Color = color, Start = new Pt(0, 0), Closed = true };
        p.Segments.Add(IrSegment.Line(new Pt(w, 0)));
        p.Segments.Add(IrSegment.Line(new Pt(w, h)));
        p.Segments.Add(IrSegment.Line(new Pt(0, h)));
        p.Segments.Add(IrSegment.Line(new Pt(0, 0)));
        return p;
    }

    [Fact]
    public void Render_SizeIsBoundsPlusMargin()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Rect(100, 50, White));

        var opts = new RenderOptions { MarginMm = 0.5, DefaultWidthMm = 0.25 };
        RenderResult result = EmfRenderer.Render(doc, opts);
        using Metafile mf = result.Metafile;

        // 여백 = MarginMm + 선굵기/2 = 0.5 + 0.125 = 0.625, 양쪽이므로 1.25
        // 보고값은 메타파일에 실제 기록된 물리 치수다. 프레임을 정수 장치 단위로 올림하고
        // 가장자리 안전분 1단위를 더하므로 최대 2단위(약 0.45mm)까지 커질 수 있다.
        Assert.InRange(result.WidthMm, 101.25, 101.75);
        Assert.InRange(result.HeightMm, 51.25, 51.75);
    }

    [Fact]
    public void Render_OutputScaleShrinksPhysicalSize()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Rect(100, 50, White));

        var opts = new RenderOptions { MarginMm = 0, DefaultWidthMm = 0.2, OutputScale = 0.5 };
        RenderResult result = EmfRenderer.Render(doc, opts);
        using Metafile mf = result.Metafile;

        Assert.InRange(result.WidthMm, 50.2, 50.7);
        Assert.InRange(result.HeightMm, 25.2, 25.7);
    }

    [Fact]
    public void Render_ProducesPlainEmfNotEmfPlus()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Rect(10, 10, Red));

        RenderResult result = EmfRenderer.Render(doc, new RenderOptions());
        using Metafile mf = result.Metafile;

        MetafileHeader header = mf.GetMetafileHeader();
        Assert.True(header.IsEmf(), $"EMF+ 가 아닌 순수 EMF여야 한다. 실제: {header.Type}");
        Assert.False(header.IsEmfPlus(), "EMF+ 레코드가 섞이면 Office에서 그룹해제 편집이 깨진다");
    }

    /// <summary>
    /// 가장 중요한 불변식: 그려진 내용이 프레임 안에서 축소되지 않고 실제 치수 그대로 들어가야 한다.
    /// GDI+의 페이지 단위 환산은 디스플레이 배율·프로세스 DPI 인식에 따라 달라지므로
    /// <c>DeviceResolution</c>이 이를 실측 보정한다. 그 보정이 깨지면 여기서 잡힌다.
    /// </summary>
    [Fact]
    public void Render_ContentFillsFrameAtTrueSize()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Rect(100, 50, White));

        const int ppm = 4;
        var opts = new RenderOptions { MarginMm = 0.5, DefaultWidthMm = 0.25 };
        using Bitmap bmp = Rasterize(doc, opts, ppm);

        (int minX, int minY, int maxX, int maxY) = InkBounds(bmp);

        double inkWidthMm = (maxX - minX) / (double)ppm;
        double inkHeightMm = (maxY - minY) / (double)ppm;

        // 선 굵기(0.25mm)만큼의 오차는 허용한다.
        Assert.InRange(inkWidthMm, 99.5, 100.5);
        Assert.InRange(inkHeightMm, 49.5, 50.5);

        // 여백 0.625mm = 2.5px 지점에서 선이 시작해야 한다.
        Assert.InRange(minX, 1, 4);
        Assert.InRange(minY, 1, 4);
    }

    [Fact]
    public void Render_EmptyDocumentThrows()
    {
        Assert.Throws<InvalidOperationException>(() => EmfRenderer.Render(new IrDocument(), new RenderOptions()));
    }

    [Fact]
    public void Render_RectangleInkIsOnBorderNotInside()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Rect(100, 50, White));

        using Bitmap bmp = Rasterize(doc, new RenderOptions(), pixelsPerMm: 4);

        // 테두리에는 잉크가, 내부에는 없어야 한다.
        Assert.True(HasInk(bmp, 0, 0, 10, 10), "좌상단 모서리에 선이 있어야 한다");
        Assert.True(HasInk(bmp, bmp.Width - 10, bmp.Height - 10, 10, 10), "우하단 모서리에 선이 있어야 한다");
        Assert.False(HasInk(bmp, (bmp.Width / 2) - 15, (bmp.Height / 2) - 15, 30, 30), "내부는 비어 있어야 한다");
    }

    [Fact]
    public void Render_WhiteBecomesBlackByDefault()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Rect(20, 20, White));

        using Bitmap bmp = Rasterize(doc, new RenderOptions(), pixelsPerMm: 8);
        Assert.True(HasInk(bmp, 0, 0, 12, 12), "흰색 도형이 검정으로 바뀌어 흰 배경 위에 보여야 한다");
    }

    [Fact]
    public void Render_WhiteStaysWhiteWhenDisabled()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Rect(20, 20, White));

        var opts = new RenderOptions { WhiteToBlack = false };
        using Bitmap bmp = Rasterize(doc, opts, pixelsPerMm: 8);
        Assert.False(HasInk(bmp, 0, 0, 12, 12), "옵션을 끄면 흰색 그대로여서 흰 배경에서 보이지 않아야 한다");
    }

    [Fact]
    public void Render_CounterClockwiseArcBulgesAwayFromCenter()
    {
        // 원점 중심 반지름 10, 0°→90° 반시계. 경계는 x 0..10, y 0..10.
        // 원호는 우상단 쪽으로 부풀고, 중심(=경계의 좌하단 모서리) 근처는 비어야 한다.
        // 스윕 부호를 뒤집으면 정확히 반대가 되므로 Y반전·각도부호 회귀 테스트로 쓴다.
        var path = new IrPath { Color = Red, Start = new Pt(10, 0) };
        path.Segments.Add(IrSegment.Arc(new Pt(0, 10), new Pt(0, 0), 10, 0, Math.PI / 2));

        var doc = new IrDocument();
        doc.Shapes.Add(path);

        using Bitmap bmp = Rasterize(doc, new RenderOptions(), pixelsPerMm: 8);

        // 원의 중심은 경계의 좌하단 모서리에 온다. 그쪽에는 선이 지나갈 수 없다.
        int band = bmp.Width / 4;
        Assert.False(HasInk(bmp, 0, bmp.Height - band, band, band), "좌하단(원의 중심 쪽)은 비어야 한다");

        // 45° 지점. 도면 (7.071, 7.071) -> 여백 0.625를 포함한 페이지 비율.
        // 스윕 부호가 뒤집히면 원호가 반대편(좌하단)으로 돌기 때문에 여기가 비게 된다.
        AssertInkAtFraction(bmp, (7.071 + 0.625) / 11.25, (10 - 7.071 + 0.625) / 11.25, "원호의 45° 지점");
    }

    private static void AssertInkAtFraction(Bitmap bmp, double fx, double fy, string what)
    {
        int box = Math.Max(6, bmp.Width / 10);
        var x = (int)Math.Round(fx * bmp.Width) - (box / 2);
        var y = (int)Math.Round(fy * bmp.Height) - (box / 2);
        Assert.True(HasInk(bmp, x, y, box, box), $"{what}에 선이 있어야 한다 (px {x},{y} 크기 {box})");
    }

    [Fact]
    public void Render_CircleInkIsOnRimNotCenter()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(new IrCircle { Color = Red, Center = new Pt(0, 0), Radius = 10 });

        using Bitmap bmp = Rasterize(doc, new RenderOptions(), pixelsPerMm: 8);

        int cx = bmp.Width / 2, cy = bmp.Height / 2;
        Assert.False(HasInk(bmp, cx - 20, cy - 20, 40, 40), "원 안쪽은 비어야 한다");
        Assert.True(HasInk(bmp, cx - 10, 0, 20, 12), "위쪽 테두리에 선이 있어야 한다");
        Assert.True(HasInk(bmp, 0, cy - 10, 12, 20), "왼쪽 테두리에 선이 있어야 한다");
    }

    [Fact]
    public void Render_ThickerPenMakesMoreInk()
    {
        var thin = new IrDocument();
        thin.Shapes.Add(Rect(40, 40, Red));

        var thick = new IrDocument();
        IrPath p = Rect(40, 40, Red);
        p.WidthMm = 2.0;
        thick.Shapes.Add(p);

        using Bitmap a = Rasterize(thin, new RenderOptions(), pixelsPerMm: 8);
        using Bitmap b = Rasterize(thick, new RenderOptions(), pixelsPerMm: 8);

        Assert.True(CountInk(b) > CountInk(a) * 2,
            $"2mm 선이 기본 0.25mm보다 훨씬 두꺼워야 한다. 얇음={CountInk(a)}, 두꺼움={CountInk(b)}");
    }

    // ---- 도우미 --------------------------------------------------------

    /// <summary>EMF를 흰 배경 비트맵에 재생해서 실제 잉크 위치를 확인한다.</summary>
    private static Bitmap Rasterize(IrDocument doc, RenderOptions opts, int pixelsPerMm)
    {
        RenderResult result = EmfRenderer.Render(doc, opts);
        using Metafile mf = result.Metafile;

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

    private const int InkThreshold = 200;

    private static bool HasInk(Bitmap bmp, int x, int y, int w, int h)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(bmp.Width, x + w), y1 = Math.Min(bmp.Height, y + h);

        for (int py = y0; py < y1; py++)
        for (int px = x0; px < x1; px++)
        {
            Color c = bmp.GetPixel(px, py);
            if (c.R < InkThreshold || c.G < InkThreshold || c.B < InkThreshold)
                return true;
        }

        return false;
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) InkBounds(Bitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            Color c = bmp.GetPixel(x, y);
            if (c.R >= InkThreshold && c.G >= InkThreshold && c.B >= InkThreshold)
                continue;

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        Assert.True(maxX >= 0, "잉크가 전혀 없다");
        return (minX, minY, maxX, maxY);
    }

    private static int CountInk(Bitmap bmp)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            Color c = bmp.GetPixel(x, y);
            if (c.R < InkThreshold || c.G < InkThreshold || c.B < InkThreshold)
                n++;
        }

        return n;
    }
}
