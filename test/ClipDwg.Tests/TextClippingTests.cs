using System;
using System.Drawing;
using ClipDwg.Extract;
using ClipDwg.Render;
using Xunit;
using Xunit.Abstractions;

namespace ClipDwg.Tests;

/// <summary>
/// 프레임이 실제 렌더 범위를 담는지 확인한다.
/// AutoCAD가 준 텍스트 범위는 원본 글꼴 기준이라, 대체 글꼴이 더 넓게 그려지면
/// 그대로 쓸 경우 오른쪽 끝이 잘린다. (실제로 겪은 회귀다.)
/// </summary>
public class TextClippingTests
{
    private readonly ITestOutputHelper _out;

    public TextClippingTests(ITestOutputHelper output) => _out = output;

    private static readonly IrColor Black = new(7, 0, 0, 0);

    private static Bounds Box(double minX, double minY, double maxX, double maxY)
    {
        var b = Bounds.Empty;
        b.Add(new Pt(minX, minY));
        b.Add(new Pt(maxX, maxY));
        return b;
    }

    [Fact]
    public void TextWiderThanReportedExtents_IsNotClipped()
    {
        // AutoCAD가 준 범위(원본 글꼴 기준)는 좁은데 대체 글꼴이 더 넓게 그려지는 상황.
        // 폭 40 짜리로 신고했지만 실제로는 그보다 넓게 그려진다.
        var text = new IrText
        {
            Color = Black,
            Anchor = new Pt(0, 0),
            Height = 10,
            Text = "가나다라마바사아자차",
            FontFamily = "맑은 고딕",
            HAlign = TextHAlign.Left,
            VAlign = TextVAlign.Baseline,
            Extents = Box(0, 0, 40, 10),
        };

        var doc = new IrDocument();
        doc.Shapes.Add(text);

        const int ppm = 8;
        RenderResult r = EmfRenderer.Render(doc, new RenderOptions());
        using System.Drawing.Imaging.Metafile mf = r.Metafile;

        int w = (int)Math.Round(r.WidthMm * ppm), h = (int)Math.Round(r.HeightMm * ppm);
        using var bmp = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawImage(mf, new Rectangle(0, 0, w, h));
        }

        int rightmost = -1;
        for (int x = w - 1; x >= 0 && rightmost < 0; x--)
        {
            for (int y = 0; y < h; y++)
            {
                Color c = bmp.GetPixel(x, y);
                if (c.R < 200 || c.G < 200 || c.B < 200)
                {
                    rightmost = x;
                    break;
                }
            }
        }

        _out.WriteLine($"프레임 : {r.WidthMm:0.##} x {r.HeightMm:0.##} mm  ({w} x {h} px)");
        _out.WriteLine($"잉크 오른쪽 끝 : {rightmost} px / 폭 {w} px");

        // 잉크가 오른쪽 가장자리에 닿아 있으면 잘린 것이다.
        Assert.True(rightmost < w - 2,
            $"텍스트가 프레임 오른쪽에서 잘렸다. 잉크 끝={rightmost}, 프레임 폭={w}");

        // 신고된 40 보다 실제가 넓으므로 프레임도 그만큼 커져 있어야 한다.
        Assert.True(r.WidthMm > 41.25,
            $"실측 범위가 프레임에 반영되지 않았다. 폭={r.WidthMm:0.##}mm");
    }

    [Fact]
    public void RotatedText_IsNotClipped()
    {
        var text = new IrText
        {
            Color = Black,
            Anchor = new Pt(0, 0),
            Height = 10,
            Text = "회전된 긴 문자열입니다",
            FontFamily = "맑은 고딕",
            Rotation = Math.PI / 4,
            Extents = Box(0, 0, 5, 5), // 일부러 엉뚱하게 좁게 신고
        };

        var doc = new IrDocument();
        doc.Shapes.Add(text);

        RenderResult r = EmfRenderer.Render(doc, new RenderOptions());
        using System.Drawing.Imaging.Metafile mf = r.Metafile;

        // 45도로 돌아간 긴 문자열이므로 가로·세로 모두 신고값보다 훨씬 커야 한다.
        Assert.True(r.WidthMm > 20, $"회전 텍스트의 가로 범위가 반영되지 않았다: {r.WidthMm:0.##}mm");
        Assert.True(r.HeightMm > 20, $"회전 텍스트의 세로 범위가 반영되지 않았다: {r.HeightMm:0.##}mm");
    }

    [Fact]
    public void RightAlignedText_ExtendsLeftOfAnchor()
    {
        var text = new IrText
        {
            Color = Black,
            Anchor = new Pt(0, 0),
            Height = 10,
            Text = "오른쪽 정렬",
            FontFamily = "맑은 고딕",
            HAlign = TextHAlign.Right,
            Extents = Bounds.Empty, // 신고값 없음 - 실측만으로 잡아야 한다
        };

        var doc = new IrDocument();
        doc.Shapes.Add(text);

        RenderResult r = EmfRenderer.Render(doc, new RenderOptions());
        using System.Drawing.Imaging.Metafile mf = r.Metafile;

        Assert.True(r.WidthMm > 10, $"오른쪽 정렬 텍스트 범위가 잡히지 않았다: {r.WidthMm:0.##}mm");
    }
}
