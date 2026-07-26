using System;

namespace ClipDwg.Extract;

public static class GeomUtil
{
    public const double TwoPi = 2 * Math.PI;

    /// <summary>각도 비교용 여유. 사분점이 스윕 경계에 아슬아슬하게 걸리는 경우를 포함시킨다.</summary>
    private const double AngleEps = 1e-9;

    public static Pt PointOnCircle(Pt center, double radius, double angle) =>
        new(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle)));

    /// <summary>각도를 [0, 2π) 로 정규화.</summary>
    public static double Normalize2Pi(double angle)
    {
        double a = angle % TwoPi;
        if (a < 0)
            a += TwoPi;
        return a;
    }

    /// <summary>
    /// <paramref name="angle"/>이 <paramref name="startAngle"/>에서 <paramref name="sweepAngle"/>만큼
    /// 진행하는 구간 안에 있는지. 스윕 부호가 진행 방향(양수 = 반시계)이다.
    /// </summary>
    public static bool AngleInSweep(double angle, double startAngle, double sweepAngle)
    {
        double sweep = Math.Abs(sweepAngle);
        if (sweep >= TwoPi - AngleEps)
            return true;

        double delta = sweepAngle >= 0
            ? Normalize2Pi(angle - startAngle)
            : Normalize2Pi(startAngle - angle);

        return delta <= sweep + AngleEps;
    }

    /// <summary>
    /// 시작각에서 끝각까지의 스윕각. <paramref name="counterClockwise"/>가 진행 방향.
    /// 두 각이 같으면 완전한 한 바퀴로 본다(원호 엔티티에서 실제로 나온다).
    /// </summary>
    public static double SweepBetween(double startAngle, double endAngle, bool counterClockwise)
    {
        double delta = counterClockwise
            ? Normalize2Pi(endAngle - startAngle)
            : Normalize2Pi(startAngle - endAngle);

        if (delta <= AngleEps)
            delta = TwoPi;

        return counterClockwise ? delta : -delta;
    }
}
