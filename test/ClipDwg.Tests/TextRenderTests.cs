using System;
using System.Collections.Generic;
using System.Drawing;
using ClipDwg.Extract;
using ClipDwg.Render;
using ClipDwg.Style;
using Xunit;

namespace ClipDwg.Tests;

public class FontResolverTests
{
    private static readonly HashSet<string> Installed =
        new(StringComparer.OrdinalIgnoreCase) { "Arial", "맑은 고딕", "Consolas" };

    private static Profile DefaultProfile() => SettingsFile.CreateDefault().GetActiveProfile();

    [Fact]
    public void TrueTypeStyle_KeepsItsTypeface()
    {
        var r = new FontResolver(DefaultProfile(), Installed);
        Assert.Equal("Arial", r.Resolve("Arial", "arial.ttf", null));
        Assert.Empty(r.SubstitutedShxFonts);
    }

    [Fact]
    public void ShxStyle_UsesSubstituteAndReportsIt()
    {
        var r = new FontResolver(DefaultProfile(), Installed);
        Assert.Equal("맑은 고딕", r.Resolve(null, "whgtxt.shx", null));
        Assert.Contains("whgtxt.shx", r.SubstitutedShxFonts);
    }

    [Fact]
    public void BigFontWinsOverShx()
    {
        var r = new FontResolver(DefaultProfile(), Installed);
        // 한글 도면의 전형: 주 글꼴은 romans.shx, 큰글꼴이 whgtxt.shx
        Assert.Equal("맑은 고딕", r.Resolve(null, "romans.shx", "whgtxt.shx"));
    }

    [Fact]
    public void ExtensionAndCaseAreIgnored()
    {
        var r = new FontResolver(DefaultProfile(), Installed);
        Assert.Equal("맑은 고딕", r.Resolve(null, "WHGTXT", null));
    }

    [Fact]
    public void UnknownShx_FallsBackToDefaultAndReports()
    {
        var r = new FontResolver(DefaultProfile(), Installed);
        Assert.Equal("Arial", r.Resolve(null, "mystery.shx", null));
        Assert.Contains("mystery.shx", r.SubstitutedShxFonts);
    }

    [Fact]
    public void UninstalledSubstitute_FallsBackInsteadOfSilentlyMisrendering()
    {
        Profile p = DefaultProfile();
        p.ShxSubstitutes.Add(new FontSubstitute { Shx = "weird", Font = "설치안된글꼴" });

        var r = new FontResolver(p, Installed);
        Assert.Equal("Arial", r.Resolve(null, "weird.shx", null));
    }
}

public class TextRenderTests
{
    private static readonly IrColor Black = new(7, 0, 0, 0);

    private static IrText MakeText(string content, TextHAlign h = TextHAlign.Left,
        TextVAlign v = TextVAlign.Baseline, double rotation = 0)
    {
        return new IrText
        {
            Color = Black,
            Anchor = new Pt(0, 0),
            Height = 10,
            Text = content,
            FontFamily = "Arial",
            HAlign = h,
            VAlign = v,
            Rotation = rotation,
            // 실제로는 AutoCAD가 넣어 주는 값. 테스트에서는 넉넉히 잡는다.
            Extents = MakeBounds(-40, -20, 40, 20),
        };
    }

    private static Bounds MakeBounds(double minX, double minY, double maxX, double maxY)
    {
        var b = Bounds.Empty;
        b.Add(new Pt(minX, minY));
        b.Add(new Pt(maxX, maxY));
        return b;
    }

    [Fact]
    public void Text_ProducesInk()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(MakeText("AB"));

        using Bitmap bmp = Rasterize(doc, 4);
        Assert.True(CountInk(bmp) > 0, "글자가 그려져야 한다");
    }

    [Fact]
    public void EmptyText_IsSkippedNotCrash()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(MakeText(""));
        doc.Shapes.Add(new IrCircle { Color = Black, Center = new Pt(0, 0), Radius = 5 });

        using Bitmap bmp = Rasterize(doc, 4);
        Assert.True(CountInk(bmp) > 0);
    }

    [Fact]
    public void Alignment_ShiftsTextRelativeToAnchor()
    {
        // 앵커는 같고 정렬만 다르면 글자 덩어리의 좌우 위치가 달라져야 한다.
        double leftCenter = InkCenterX(MakeText("MMMM", TextHAlign.Left));
        double rightCenter = InkCenterX(MakeText("MMMM", TextHAlign.Right));

        Assert.True(rightCenter < leftCenter,
            $"오른쪽 정렬이 왼쪽 정렬보다 왼쪽에 와야 한다. left={leftCenter:0.#}, right={rightCenter:0.#}");
    }

    [Fact]
    public void Rotation_MakesTextTaller()
    {
        // 90도 돌리면 가로로 긴 글자 덩어리가 세로로 길어진다.
        (double w0, double h0) = InkSize(MakeText("MMMMMM"));
        (double w90, double h90) = InkSize(MakeText("MMMMMM", rotation: Math.PI / 2));

        Assert.True(w0 > h0, "회전 전에는 가로로 길어야 한다");
        Assert.True(h90 > w90, "90도 회전 후에는 세로로 길어야 한다");
    }

    [Fact]
    public void TextHeight_ScalesWithDrawingHeight()
    {
        IrText small = MakeText("M");
        small.Height = 5;
        IrText large = MakeText("M");
        large.Height = 20;

        (_, double smallH) = InkSize(small);
        (_, double largeH) = InkSize(large);

        // 4배 높이면 잉크 높이도 대략 4배. 글꼴 메트릭 환산 오차를 감안해 폭넓게 본다.
        Assert.InRange(largeH / smallH, 3.0, 5.0);
    }

    [Fact]
    public void Text_IsRecordedAsTextNotOutlines()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(MakeText("Hello"));

        RenderResult r = EmfRenderer.Render(doc, new RenderOptions());

        // ContainsTextRecord 가 핸들을 가져가 해제하므로 여기서 Dispose 하지 않는다.
        Assert.True(EmfInspector.ContainsTextRecord(r.Metafile),
            "붙여넣은 뒤에도 글자로 남으려면 EMF에 ExtTextOut 레코드가 있어야 한다");
    }

    // ---- 도우미 --------------------------------------------------------

    private static (double Width, double Height) InkSize(IrText text)
    {
        var doc = new IrDocument();
        doc.Shapes.Add(text);
        using Bitmap bmp = Rasterize(doc, 4);
        (int minX, int minY, int maxX, int maxY) = InkBounds(bmp);
        return (maxX - minX, maxY - minY);
    }

    private static double InkCenterX(IrText text)
    {
        var doc = new IrDocument();
        doc.Shapes.Add(text);
        using Bitmap bmp = Rasterize(doc, 4);
        (int minX, _, int maxX, _) = InkBounds(bmp);
        return (minX + maxX) / 2.0;
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

    private const int InkThreshold = 200;

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
