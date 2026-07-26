using System;
using ClipDwg.Extract;
using Xunit;

namespace ClipDwg.Tests;

public class GeomUtilTests
{
    private const double Eps = 1e-9;

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(Math.PI, Math.PI)]
    [InlineData(-Math.PI / 2, 3 * Math.PI / 2)]
    [InlineData(3 * Math.PI, Math.PI)]
    [InlineData(-3 * Math.PI, Math.PI)]
    public void Normalize2Pi_MapsIntoRange(double input, double expected)
    {
        Assert.Equal(expected, GeomUtil.Normalize2Pi(input), 9);
    }

    [Fact]
    public void SweepBetween_CounterClockwiseQuarter()
    {
        double sweep = GeomUtil.SweepBetween(0, Math.PI / 2, counterClockwise: true);
        Assert.Equal(Math.PI / 2, sweep, 9);
    }

    [Fact]
    public void SweepBetween_ClockwiseIsNegative()
    {
        double sweep = GeomUtil.SweepBetween(0, Math.PI / 2, counterClockwise: false);
        Assert.Equal(-3 * Math.PI / 2, sweep, 9);
    }

    [Fact]
    public void SweepBetween_WrapsAcrossZero()
    {
        // 315° → 45° 반시계 = 90°
        double sweep = GeomUtil.SweepBetween(7 * Math.PI / 4, Math.PI / 4, counterClockwise: true);
        Assert.Equal(Math.PI / 2, sweep, 9);
    }

    [Fact]
    public void SweepBetween_EqualAnglesMeansFullCircle()
    {
        Assert.Equal(GeomUtil.TwoPi, GeomUtil.SweepBetween(1.2, 1.2, counterClockwise: true), 9);
        Assert.Equal(-GeomUtil.TwoPi, GeomUtil.SweepBetween(1.2, 1.2, counterClockwise: false), 9);
    }

    [Fact]
    public void AngleInSweep_CounterClockwise()
    {
        // 0° 에서 반시계 180°
        Assert.True(GeomUtil.AngleInSweep(Math.PI / 2, 0, Math.PI));
        Assert.False(GeomUtil.AngleInSweep(3 * Math.PI / 2, 0, Math.PI));
    }

    [Fact]
    public void AngleInSweep_Clockwise()
    {
        // 0° 에서 시계 180° => 270°는 포함, 90°는 제외
        Assert.True(GeomUtil.AngleInSweep(3 * Math.PI / 2, 0, -Math.PI));
        Assert.False(GeomUtil.AngleInSweep(Math.PI / 2, 0, -Math.PI));
    }

    [Fact]
    public void PointOnCircle_CardinalPoints()
    {
        var c = new Pt(10, 20);
        Pt east = GeomUtil.PointOnCircle(c, 5, 0);
        Assert.Equal(15, east.X, 9);
        Assert.Equal(20, east.Y, 9);

        Pt north = GeomUtil.PointOnCircle(c, 5, Math.PI / 2);
        Assert.Equal(10, north.X, 9);
        Assert.Equal(25, north.Y, 9);
    }
}
