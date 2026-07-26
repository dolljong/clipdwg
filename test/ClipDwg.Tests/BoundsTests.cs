using System;
using ClipDwg.Extract;
using Xunit;

namespace ClipDwg.Tests;

public class BoundsTests
{
    [Fact]
    public void Empty_HasNoExtent()
    {
        var b = Bounds.Empty;
        Assert.True(b.IsEmpty);
        Assert.Equal(0, b.Width);
        Assert.Equal(0, b.Height);
    }

    [Fact]
    public void Add_GrowsToContainPoints()
    {
        var b = Bounds.Empty;
        b.Add(new Pt(3, -1));
        b.Add(new Pt(-2, 5));

        Assert.Equal(-2, b.MinX, 9);
        Assert.Equal(-1, b.MinY, 9);
        Assert.Equal(3, b.MaxX, 9);
        Assert.Equal(5, b.MaxY, 9);
        Assert.Equal(5, b.Width, 9);
        Assert.Equal(6, b.Height, 9);
    }

    [Fact]
    public void AddArc_FirstQuadrant_UsesEndpointsOnly()
    {
        // 원점 중심 반지름 10, 0°→90°. 사분점 0°와 90°가 곧 양 끝점이다.
        var b = Bounds.Empty;
        b.AddArc(new Pt(0, 0), 10, 0, Math.PI / 2);

        Assert.Equal(0, b.MinX, 6);
        Assert.Equal(0, b.MinY, 6);
        Assert.Equal(10, b.MaxX, 6);
        Assert.Equal(10, b.MaxY, 6);
    }

    [Fact]
    public void AddArc_IncludesCardinalPointInsideSweep()
    {
        // 45°→135°. 양 끝점만 보면 MaxY가 7.07이지만 90°를 지나므로 10이어야 한다.
        var b = Bounds.Empty;
        b.AddArc(new Pt(0, 0), 10, Math.PI / 4, Math.PI / 2);

        Assert.Equal(10, b.MaxY, 6);
        Assert.Equal(-Math.Sqrt(50), b.MinX, 6);
        Assert.Equal(Math.Sqrt(50), b.MaxX, 6);
    }

    [Fact]
    public void AddArc_ClockwiseSweepCoversOppositeSide()
    {
        // 45°에서 시계로 90° => 315°까지. 0°(동쪽)를 지나므로 MaxX = 10.
        var b = Bounds.Empty;
        b.AddArc(new Pt(0, 0), 10, Math.PI / 4, -Math.PI / 2);

        Assert.Equal(10, b.MaxX, 6);
        Assert.Equal(-Math.Sqrt(50), b.MinY, 6);
        Assert.Equal(Math.Sqrt(50), b.MaxY, 6);
    }

    [Fact]
    public void AddArc_FullCircleCoversAllCardinals()
    {
        var b = Bounds.Empty;
        b.AddArc(new Pt(5, 5), 3, 0, GeomUtil.TwoPi);

        Assert.Equal(2, b.MinX, 6);
        Assert.Equal(2, b.MinY, 6);
        Assert.Equal(8, b.MaxX, 6);
        Assert.Equal(8, b.MaxY, 6);
    }

    [Fact]
    public void IrPath_BoundsFollowArcSegment()
    {
        var path = new IrPath { Start = new Pt(10, 0) };
        path.Segments.Add(IrSegment.Arc(new Pt(-10, 0), new Pt(0, 0), 10, 0, Math.PI));

        var b = Bounds.Empty;
        path.AccumulateBounds(ref b);

        Assert.Equal(-10, b.MinX, 6);
        Assert.Equal(10, b.MaxX, 6);
        Assert.Equal(0, b.MinY, 6);
        Assert.Equal(10, b.MaxY, 6);
    }

    [Fact]
    public void IrCircle_BoundsIsSquare()
    {
        var circle = new IrCircle { Center = new Pt(-4, 7), Radius = 2.5 };

        var b = Bounds.Empty;
        circle.AccumulateBounds(ref b);

        Assert.Equal(-6.5, b.MinX, 9);
        Assert.Equal(4.5, b.MinY, 9);
        Assert.Equal(-1.5, b.MaxX, 9);
        Assert.Equal(9.5, b.MaxY, 9);
    }
}
