using System;
using System.Collections.Generic;
using ClipDwg.Extract;

namespace ClipDwg.Render;

/// <summary>
/// 선종류 패턴을 실제 선 조각으로 잘라 낸다.
/// <para>
/// GDI 펜의 대시 기능을 쓰지 않는 이유: 대시 길이가 펜 두께에 비례해 늘어나 도면 축척과
/// 맞지 않고, EMF 전용 레코드로 바꿀 때나 Office에서 그룹해제할 때 패턴이 깨지거나 실선이 된다.
/// 조각으로 잘라 두면 어디에 붙여 넣어도 도면에서 보던 그대로다.
/// </para>
/// <para>
/// 배치 규칙은 AutoCAD를 따른다.
/// 열린 선은 양 끝이 선으로 끝나도록 남는 길이를 양쪽 끝 선에 반씩 나눠 붙이고,
/// 패턴 한 주기보다 짧은 선은 실선으로 그린다. 폴리라인은 PLINEGEN 이 꺼져 있으면
/// 구간마다 패턴을 새로 시작한다. 닫힌 원은 0°에서 시작해 한 바퀴 돈다.
/// </para>
/// </summary>
public static class LinetypeDasher
{
    private const double Eps = 1e-12;

    /// <summary>
    /// 경로를 패턴대로 자른 조각들. 대시가 하나도 생기지 않는 경우(패턴이 없거나 전부 실선으로
    /// 그려야 할 때)는 null — 호출자가 원래 경로를 그대로 그리면 된다.
    /// </summary>
    /// <param name="dotLength">길이 0인 점 요소를 그릴 길이(도면 단위). 둥근 끝단과 함께 점이 된다.</param>
    /// <param name="maxPieces">이보다 많은 조각이 나오면 포기하고 실선으로 그린다.</param>
    public static List<IrPath>? Dash(IrPath path, IrLinetype linetype, double dotLength, int maxPieces)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));
        if (linetype is null || !linetype.IsDashed || path.Segments.Count == 0)
            return null;

        List<IrSegment> segments = ClosedSegments(path);

        var pieces = new List<IrPath>();
        bool anyDashed = false;

        if (path.LinetypeGen || segments.Count == 1)
        {
            bool loop = path.Closed && path.LinetypeGen;
            if (!AddRun(pieces, path, path.Start, segments, 0, segments.Count, linetype, loop, dotLength, maxPieces, ref anyDashed))
                return null;
        }
        else
        {
            Pt cursor = path.Start;
            for (int i = 0; i < segments.Count; i++)
            {
                if (!AddRun(pieces, path, cursor, segments, i, 1, linetype, false, dotLength, maxPieces, ref anyDashed))
                    return null;
                cursor = segments[i].End;
            }
        }

        return anyDashed ? pieces : null;
    }

    /// <summary>원을 패턴대로 자른 조각들. null 이면 원을 그대로 그린다.</summary>
    public static List<IrPath>? Dash(IrCircle circle, IrLinetype linetype, double dotLength, int maxPieces)
    {
        if (circle is null)
            throw new ArgumentNullException(nameof(circle));
        if (linetype is null || !linetype.IsDashed || circle.Radius <= 0)
            return null;

        Pt start = GeomUtil.PointOnCircle(circle.Center, circle.Radius, 0);
        var loop = new IrPath
        {
            Color = circle.Color,
            WidthMm = circle.WidthMm,
            IntrinsicWidth = circle.IntrinsicWidth,
            Start = start,
            Closed = true,
            LinetypeGen = true,
        };
        loop.Segments.Add(IrSegment.Arc(start, circle.Center, circle.Radius, 0, GeomUtil.TwoPi));

        return Dash(loop, linetype, dotLength, maxPieces);
    }

    /// <summary>닫힌 경로의 암묵적인 마지막 구간(끝점 → 시작점)까지 포함한 구간 목록.</summary>
    private static List<IrSegment> ClosedSegments(IrPath path)
    {
        var segments = new List<IrSegment>(path.Segments);
        if (path.Closed)
        {
            Pt last = segments[segments.Count - 1].End;
            if (Distance(last, path.Start) > Eps)
                segments.Add(IrSegment.Line(path.Start));
        }

        return segments;
    }

    /// <summary>
    /// 패턴을 한 번 처음부터 적용하는 단위(run)를 조각으로 잘라 <paramref name="pieces"/>에 더한다.
    /// 조각 수가 한계를 넘으면 false.
    /// </summary>
    private static bool AddRun(
        List<IrPath> pieces,
        IrPath template,
        Pt start,
        List<IrSegment> segments,
        int first,
        int count,
        IrLinetype linetype,
        bool loop,
        double dotLength,
        int maxPieces,
        ref bool anyDashed)
    {
        var lengths = new double[count];
        double total = 0;
        for (int i = 0; i < count; i++)
        {
            Pt from = i == 0 ? start : segments[first + i - 1].End;
            lengths[i] = SegmentLength(from, segments[first + i]);
            total += lengths[i];
        }

        if (total <= Eps)
            return true;

        List<(double A, double B)>? intervals = OnIntervals(total, linetype, loop, dotLength, maxPieces - pieces.Count);
        if (intervals is null)
            return false;

        if (intervals.Count == 1 && intervals[0].A <= Eps && intervals[0].B >= total - Eps)
        {
            // 패턴 한 주기보다 짧다. 실선 그대로.
            pieces.Add(SubPath(template, start, segments, first, lengths, 0, total));
            return true;
        }

        anyDashed = true;
        foreach ((double a, double b) in intervals)
            pieces.Add(SubPath(template, start, segments, first, lengths, a, b));

        return true;
    }

    /// <summary>
    /// 길이 <paramref name="total"/>인 선 위에서 선이 그려지는 구간들. 조각 수 한계를 넘으면 null.
    /// </summary>
    internal static List<(double A, double B)>? OnIntervals(
        double total, IrLinetype linetype, bool loop, double dotLength, int maxPieces)
    {
        double period = linetype.PatternLength;
        var result = new List<(double A, double B)>();

        if (period <= Eps || total < period)
        {
            result.Add((0, total));
            return result;
        }

        int dashesPerPeriod = 0;
        foreach (double d in linetype.Dashes)
        {
            if (d >= 0)
                dashesPerPeriod++;
        }

        double periods = Math.Ceiling(total / period);
        if (periods * Math.Max(1, dashesPerPeriod) > maxPieces)
            return null;

        double offset;
        int count;
        if (loop)
        {
            // 닫힌 도형은 끝이 없으니 처음부터 그대로 돌린다.
            offset = 0;
            count = (int)periods;
        }
        else
        {
            // 양 끝이 선으로 끝나도록 남는 길이를 양쪽에 반씩 둔다.
            count = (int)Math.Floor(total / period);
            offset = (total - (count * period)) / 2;
            if (offset > Eps)
                Append(result, 0, offset);
        }

        double pos = offset;
        for (int k = 0; k < count; k++)
        {
            foreach (double d in linetype.Dashes)
            {
                if (pos >= total - Eps)
                    break;

                if (d > 0)
                    Append(result, pos, Math.Min(pos + d, total));
                else if (d == 0)
                    Append(result, pos, Math.Min(pos + dotLength, total));

                pos += Math.Abs(d);
            }
        }

        if (!loop && pos < total - Eps)
            Append(result, pos, total);

        return result;
    }

    /// <summary>구간을 더한다. 직전 구간과 맞닿으면 하나로 합친다.</summary>
    private static void Append(List<(double A, double B)> list, double a, double b)
    {
        if (b < a)
            b = a;

        if (list.Count > 0 && a <= list[list.Count - 1].B + Eps)
        {
            (double pa, double pb) = list[list.Count - 1];
            list[list.Count - 1] = (pa, Math.Max(pb, b));
            return;
        }

        list.Add((a, b));
    }

    /// <summary>run 안의 거리 [a, b] 부분을 경로로 떼어 낸다.</summary>
    private static IrPath SubPath(
        IrPath template, Pt start, List<IrSegment> segments, int first, double[] lengths, double a, double b)
    {
        var piece = new IrPath
        {
            Color = template.Color,
            WidthMm = template.WidthMm,
            IntrinsicWidth = template.IntrinsicWidth,
        };

        bool started = false;
        double segStart = 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            double segEnd = segStart + lengths[i];
            if (segEnd < a - Eps || lengths[i] <= Eps)
            {
                segStart = segEnd;
                continue;
            }

            if (segStart > b + Eps)
                break;

            Pt from = i == 0 ? start : segments[first + i - 1].End;
            IrSegment seg = segments[first + i];

            double localA = Math.Max(0, a - segStart);
            double localB = Math.Min(lengths[i], b - segStart);

            if (!started)
            {
                piece.Start = PointAt(from, seg, localA);
                started = true;
            }

            if (localB > localA || piece.Segments.Count == 0)
                piece.Segments.Add(Slice(from, seg, localA, Math.Max(localA, localB)));

            segStart = segEnd;
        }

        return piece;
    }

    private static IrSegment Slice(Pt from, IrSegment seg, double localA, double localB)
    {
        if (seg.Kind != SegKind.Arc)
            return IrSegment.Line(PointAt(from, seg, localB));

        double sign = Math.Sign(seg.SweepAngle);
        double a0 = seg.StartAngle + (sign * localA / seg.Radius);
        double sweep = sign * (localB - localA) / seg.Radius;
        return IrSegment.Arc(PointAt(from, seg, localB), seg.Center, seg.Radius, a0, sweep);
    }

    private static Pt PointAt(Pt from, IrSegment seg, double distance)
    {
        if (seg.Kind == SegKind.Arc)
        {
            double angle = seg.StartAngle + (Math.Sign(seg.SweepAngle) * distance / seg.Radius);
            return GeomUtil.PointOnCircle(seg.Center, seg.Radius, angle);
        }

        double length = Distance(from, seg.End);
        if (length <= Eps)
            return seg.End;

        double t = distance / length;
        return new Pt(from.X + ((seg.End.X - from.X) * t), from.Y + ((seg.End.Y - from.Y) * t));
    }

    private static double SegmentLength(Pt from, IrSegment seg) =>
        seg.Kind == SegKind.Arc ? Math.Abs(seg.SweepAngle) * seg.Radius : Distance(from, seg.End);

    private static double Distance(Pt a, Pt b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
