using System;
using System.Drawing;
using ClipDwg.Extract;
using ClipDwg.Render;
using Xunit;

namespace ClipDwg.Tests;

/// <summary>치수 화살촉이 채워진 도형으로 들어온다. 속이 실제로 칠해지는지 확인한다.</summary>
public class FilledPolygonTests
{
    private static readonly IrColor Black = new(7, 0, 0, 0);

    private static IrFilledPolygon Triangle(double size)
    {
        var p = new IrFilledPolygon { Color = Black };
        p.Points.Add(new Pt(0, 0));
        p.Points.Add(new Pt(size, size / 3));
        p.Points.Add(new Pt(size, -size / 3));
        return p;
    }

    [Fact]
    public void Bounds_CoverAllVertices()
    {
        var b = Bounds.Empty;
        Triangle(30).AccumulateBounds(ref b);

        Assert.Equal(0, b.MinX, 9);
        Assert.Equal(30, b.MaxX, 9);
        Assert.Equal(-10, b.MinY, 9);
        Assert.Equal(10, b.MaxY, 9);
    }

    [Fact]
    public void Interior_IsFilledNotHollow()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Triangle(30));

        using Bitmap bmp = Rasterize(doc, 8);

        // 삼각형 안쪽 깊숙한 지점. 윤곽선만 그렸다면 여기는 비어 있다.
        // 도면 (20, 0) -> 페이지 (20+여백, 10+여백)
        int x = (int)Math.Round((20 + 1.0) * 8);
        int y = (int)Math.Round((10 + 1.0) * 8);
        Assert.True(IsInk(bmp, x, y), $"삼각형 내부 ({x},{y})가 칠해져 있어야 한다");
    }

    [Fact]
    public void Outside_StaysEmpty()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(Triangle(30));

        using Bitmap bmp = Rasterize(doc, 8);

        // 도면 (2, 8) - 뾰족한 쪽 바깥. 삼각형 밖이다.
        int x = (int)Math.Round((2 + 1.0) * 8);
        int y = (int)Math.Round((10 - 8 + 1.0) * 8);
        Assert.False(IsInk(bmp, x, y), $"삼각형 바깥 ({x},{y})은 비어 있어야 한다");
    }

    [Fact]
    public void FilledPolygon_DoesNotInflateMarginLikeAStroke()
    {
        // 채움 도형은 선두께가 없으므로 두께를 크게 줘도 프레임이 커지면 안 된다.
        var thin = new IrDocument();
        thin.Shapes.Add(Triangle(30));

        var thick = new IrDocument();
        IrFilledPolygon p = Triangle(30);
        p.WidthMm = 5.0;
        thick.Shapes.Add(p);

        RenderResult a = EmfRenderer.Render(thin, new RenderOptions());
        using System.Drawing.Imaging.Metafile ma = a.Metafile;
        RenderResult b = EmfRenderer.Render(thick, new RenderOptions());
        using System.Drawing.Imaging.Metafile mb = b.Metafile;

        Assert.Equal(a.WidthMm, b.WidthMm, 3);
    }

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
}
