using System;
using System.Collections.Generic;
using System.Drawing;
using ClipDwg.Extract;
using ClipDwg.Render;
using Xunit;
using Xunit.Abstractions;

namespace ClipDwg.Tests;

/// <summary>
/// 선종류(HIDDEN·CENTER 등)가 실선으로 뭉개지지 않고 도면 축척대로 끊어져 그려지는지.
/// </summary>
public class LinetypeTests
{
    private readonly ITestOutputHelper _out;

    public LinetypeTests(ITestOutputHelper output) => _out = output;

    private static readonly IrColor Black = new(7, 0, 0, 0);

    // acad.lin 정의 × LTSCALE 10
    private static IrLinetype Hidden(double ltscale = 10) => new("HIDDEN", new[] { 0.25 * ltscale, -0.125 * ltscale });

    private static IrLinetype Center(double ltscale = 10) =>
        new("CENTER", new[] { 1.25 * ltscale, -0.25 * ltscale, 0.25 * ltscale, -0.25 * ltscale });

    private static IrPath HLine(double length, IrLinetype? linetype)
    {
        var p = new IrPath { Color = Black, Start = new Pt(0, 0), Linetype = linetype };
        p.Segments.Add(IrSegment.Line(new Pt(length, 0)));
        return p;
    }

    private static double PieceLength(IrPath piece)
    {
        double sum = 0;
        Pt cursor = piece.Start;
        foreach (IrSegment seg in piece.Segments)
        {
            sum += seg.Kind == SegKind.Arc
                ? Math.Abs(seg.SweepAngle) * seg.Radius
                : Math.Sqrt(Math.Pow(seg.End.X - cursor.X, 2) + Math.Pow(seg.End.Y - cursor.Y, 2));
            cursor = seg.End;
        }

        return sum;
    }

    [Fact]
    public void Hidden_LineIsCutIntoDashes()
    {
        // 주기 3.75, 길이 100 -> 26주기 + 남는 2.5 를 양 끝에 1.25 씩
        List<IrPath>? pieces = LinetypeDasher.Dash(HLine(100, Hidden()), Hidden(), 0.01, 10000);

        Assert.NotNull(pieces);
        _out.WriteLine($"조각 {pieces!.Count}개");
        Assert.InRange(pieces.Count, 26, 28);

        // 양 끝은 선으로 끝나야 한다.
        Assert.Equal(0, pieces[0].Start.X, 9);
        Assert.Equal(100, pieces[pieces.Count - 1].Segments[0].End.X, 9);

        // 가운데 대시는 정의대로 2.5
        Assert.Equal(2.5, PieceLength(pieces[pieces.Count / 2]), 9);

        // 그려진 총 길이 = 26 × 2.5 + 처음 1.25(첫 대시와 합쳐짐) + 끝 1.25
        double drawn = 0;
        foreach (IrPath piece in pieces)
            drawn += PieceLength(piece);
        Assert.Equal((26 * 2.5) + 2.5, drawn, 6);
    }

    [Fact]
    public void Center_HasLongAndShortDashes()
    {
        List<IrPath>? pieces = LinetypeDasher.Dash(HLine(200, Center()), Center(), 0.01, 10000);

        Assert.NotNull(pieces);
        var lengths = new HashSet<double>();
        for (int i = 1; i < pieces!.Count - 1; i++)
            lengths.Add(Math.Round(PieceLength(pieces[i]), 6));

        Assert.Contains(12.5, lengths);
        Assert.Contains(2.5, lengths);
    }

    [Fact]
    public void LineShorterThanOnePattern_IsSolid()
    {
        // AutoCAD 도 패턴 한 주기보다 짧은 선은 실선으로 그린다.
        Assert.Null(LinetypeDasher.Dash(HLine(3, Hidden()), Hidden(), 0.01, 10000));
    }

    [Fact]
    public void Polyline_RestartsPatternAtEachVertex_UnlessPlinegen()
    {
        IrPath Make(bool gen)
        {
            var p = new IrPath { Color = Black, Start = new Pt(0, 0), LinetypeGen = gen };
            p.Segments.Add(IrSegment.Line(new Pt(3, 0)));   // 한 주기(3.75)보다 짧다
            p.Segments.Add(IrSegment.Line(new Pt(3, 3)));
            p.Segments.Add(IrSegment.Line(new Pt(6, 3)));
            return p;
        }

        // 구간마다 새로 시작하면 모든 구간이 너무 짧아 실선이 된다.
        Assert.Null(LinetypeDasher.Dash(Make(false), Hidden(), 0.01, 10000));

        // PLINEGEN 이면 길이 9 전체에 패턴이 이어진다.
        List<IrPath>? gen = LinetypeDasher.Dash(Make(true), Hidden(), 0.01, 10000);
        Assert.NotNull(gen);
        Assert.True(gen!.Count >= 2);
    }

    [Fact]
    public void Arc_DashesStayOnTheCircle()
    {
        var arc = new IrPath { Color = Black, Start = new Pt(50, 0) };
        arc.Segments.Add(IrSegment.Arc(new Pt(-50, 0), new Pt(0, 0), 50, 0, Math.PI));

        List<IrPath>? pieces = LinetypeDasher.Dash(arc, Hidden(), 0.01, 10000);

        Assert.NotNull(pieces);
        foreach (IrPath piece in pieces!)
        {
            Assert.Equal(50, Math.Sqrt((piece.Start.X * piece.Start.X) + (piece.Start.Y * piece.Start.Y)), 6);
            foreach (IrSegment seg in piece.Segments)
            {
                Assert.Equal(SegKind.Arc, seg.Kind);
                Assert.True(seg.SweepAngle > 0, "원래 진행 방향(반시계)을 유지해야 한다");
            }
        }
    }

    [Fact]
    public void Circle_IsDashedAllTheWayRound()
    {
        var circle = new IrCircle { Color = Black, Center = new Pt(0, 0), Radius = 20 };
        List<IrPath>? pieces = LinetypeDasher.Dash(circle, Hidden(), 0.01, 10000);

        Assert.NotNull(pieces);
        double drawn = 0;
        foreach (IrPath piece in pieces!)
            drawn += PieceLength(piece);

        // 둘레의 약 2/3 가 선
        double circumference = 2 * Math.PI * 20;
        Assert.InRange(drawn / circumference, 0.6, 0.72);
    }

    [Fact]
    public void Dot_IsDrawnAsShortPiece()
    {
        var dot = new IrLinetype("DOT", new[] { 0.0, -2.5 });
        List<IrPath>? pieces = LinetypeDasher.Dash(HLine(50, dot), dot, 0.01, 10000);

        Assert.NotNull(pieces);
        Assert.True(pieces!.Count >= 19);
        Assert.Equal(0.01, PieceLength(pieces[pieces.Count / 2]), 9);
    }

    [Fact]
    public void TooManyPieces_FallsBackToSolid()
    {
        Assert.Null(LinetypeDasher.Dash(HLine(1_000_000, Hidden(0.01)), Hidden(0.01), 0.01, 1000));
    }

    [Fact]
    public void Rendered_HiddenLineHasGaps()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(HLine(100, Hidden()));

        const int ppm = 8;
        RenderResult result = EmfRenderer.Render(doc, new RenderOptions { DefaultWidthMm = 0.5 });
        using System.Drawing.Imaging.Metafile mf = result.Metafile;

        int w = (int)Math.Round(result.WidthMm * ppm);
        int h = Math.Max(1, (int)Math.Round(result.HeightMm * ppm));
        using var bmp = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawImage(mf, new Rectangle(0, 0, w, h));
        }

        int y = h / 2;
        int transitions = 0;
        bool previous = false;
        for (int x = 0; x < w; x++)
        {
            Color c = bmp.GetPixel(x, y);
            bool ink = c.R < 128;
            if (ink != previous)
                transitions++;
            previous = ink;
        }

        _out.WriteLine($"잉크 전환 {transitions}회");

        // 대시 27개 -> 켜짐/꺼짐 전환 약 54회. 실선이면 2회.
        Assert.InRange(transitions, 40, 60);
    }
}
