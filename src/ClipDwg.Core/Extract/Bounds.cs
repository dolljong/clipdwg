using System;

namespace ClipDwg.Extract;

/// <summary>축정렬 경계상자. EMF 프레임 크기를 잡는 데 쓴다.</summary>
public struct Bounds
{
    public double MinX;
    public double MinY;
    public double MaxX;
    public double MaxY;

    public static Bounds Empty => new()
    {
        MinX = double.PositiveInfinity,
        MinY = double.PositiveInfinity,
        MaxX = double.NegativeInfinity,
        MaxY = double.NegativeInfinity,
    };

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public double Width => IsEmpty ? 0 : MaxX - MinX;

    public double Height => IsEmpty ? 0 : MaxY - MinY;

    public void Add(Pt p)
    {
        if (p.X < MinX) MinX = p.X;
        if (p.Y < MinY) MinY = p.Y;
        if (p.X > MaxX) MaxX = p.X;
        if (p.Y > MaxY) MaxY = p.Y;
    }

    /// <summary>
    /// 원호의 정확한 경계. 양 끝점에 더해, 스윕 구간에 포함된 사분점(0/90/180/270°)만 추가한다.
    /// 원호를 통째로 원 경계로 잡으면 여백이 크게 남으므로 이 계산이 필요하다.
    /// </summary>
    public void AddArc(Pt center, double radius, double startAngle, double sweepAngle)
    {
        Add(GeomUtil.PointOnCircle(center, radius, startAngle));
        Add(GeomUtil.PointOnCircle(center, radius, startAngle + sweepAngle));

        for (int k = 0; k < 4; k++)
        {
            double a = k * (Math.PI / 2);
            if (GeomUtil.AngleInSweep(a, startAngle, sweepAngle))
                Add(GeomUtil.PointOnCircle(center, radius, a));
        }
    }

    public override string ToString() =>
        IsEmpty ? "<empty>" : $"[{MinX:0.###}, {MinY:0.###}] .. [{MaxX:0.###}, {MaxY:0.###}]";
}
