using System;
using System.Collections.Generic;

namespace ClipDwg.Extract;

/// <summary>
/// 중간표현(IR). AutoCAD 타입에 전혀 의존하지 않는다.
/// 덕분에 렌더러·색상매핑·옵션창·단위테스트가 AutoCAD 없이 동작한다.
/// </summary>
public readonly struct Pt
{
    public readonly double X;
    public readonly double Y;

    public Pt(double x, double y)
    {
        X = x;
        Y = y;
    }

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}

/// <summary>
/// 해석이 끝난 색. ByLayer/ByBlock은 추출 시점에 이미 실제 색으로 치환된다.
/// <see cref="Aci"/>가 0이면 ACI 인덱스가 없는 순수 트루컬러.
/// </summary>
public readonly struct IrColor : IEquatable<IrColor>
{
    public readonly short Aci;
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;

    public IrColor(short aci, byte r, byte g, byte b)
    {
        Aci = aci;
        R = r;
        G = g;
        B = b;
    }

    public bool Equals(IrColor other) => Aci == other.Aci && R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is IrColor other && Equals(other);

    public override int GetHashCode() => (Aci << 24) ^ (R << 16) ^ (G << 8) ^ B;

    public override string ToString() => Aci > 0 ? $"ACI {Aci} #{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}";
}

public enum SegKind
{
    Line = 0,
    Arc = 1,
}

/// <summary>경로의 한 구간. 시작점은 직전 구간의 끝점(첫 구간은 <see cref="IrPath.Start"/>).</summary>
public readonly struct IrSegment
{
    public readonly SegKind Kind;
    public readonly Pt End;

    // Arc 전용
    public readonly Pt Center;
    public readonly double Radius;

    /// <summary>WCS 기준 시작각(라디안, +X에서 반시계).</summary>
    public readonly double StartAngle;

    /// <summary>스윕각(라디안). 양수 = 반시계, 음수 = 시계.</summary>
    public readonly double SweepAngle;

    private IrSegment(SegKind kind, Pt end, Pt center, double radius, double startAngle, double sweepAngle)
    {
        Kind = kind;
        End = end;
        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        SweepAngle = sweepAngle;
    }

    public static IrSegment Line(Pt end) => new(SegKind.Line, end, default, 0, 0, 0);

    public static IrSegment Arc(Pt end, Pt center, double radius, double startAngle, double sweepAngle) =>
        new(SegKind.Arc, end, center, radius, startAngle, sweepAngle);
}

public abstract class IrShape
{
    public IrColor Color;

    /// <summary><c>ColorWeightMap</c>이 색상 규칙으로 채운다. 0 = 최소 두께(hairline).</summary>
    public double WidthMm;

    /// <summary>
    /// 엔티티 자체가 가진 기하학적 폭(도면 단위). 0보다 크면 색상별 두께 규칙보다 우선한다.
    /// <para>
    /// 폭을 가진 폴리라인이 여기에 해당한다. 대표적으로 치수의 점(dot) 화살촉은 도넛,
    /// 즉 폭이 반지름과 같은 닫힌 폴리라인이다. 이 폭을 무시하면 속이 빈 작은 원이 된다.
    /// 색상 규칙으로 덮어써서도 안 된다. 굵기가 아니라 도형의 크기이기 때문이다.
    /// </para>
    /// </summary>
    public double IntrinsicWidth;

    public abstract void AccumulateBounds(ref Bounds bounds);
}

/// <summary>선·호·폴리라인이 모두 여기로 수렴한다.</summary>
public sealed class IrPath : IrShape
{
    public Pt Start;
    public readonly List<IrSegment> Segments = new();
    public bool Closed;

    public override void AccumulateBounds(ref Bounds bounds)
    {
        bounds.Add(Start);
        Pt cursor = Start;
        foreach (IrSegment seg in Segments)
        {
            if (seg.Kind == SegKind.Arc)
                bounds.AddArc(seg.Center, seg.Radius, seg.StartAngle, seg.SweepAngle);
            else
                bounds.Add(seg.End);

            cursor = seg.End;
        }

        bounds.Add(cursor);
    }
}

public sealed class IrCircle : IrShape
{
    public Pt Center;
    public double Radius;

    public override void AccumulateBounds(ref Bounds bounds)
    {
        bounds.Add(new Pt(Center.X - Radius, Center.Y - Radius));
        bounds.Add(new Pt(Center.X + Radius, Center.Y + Radius));
    }
}

/// <summary>
/// 채워진 다각형. 치수 화살촉(AcDbSolid)이 이걸로 들어온다.
/// 획이 아니라 채움이므로 선두께와 무관하다.
/// </summary>
public sealed class IrFilledPolygon : IrShape
{
    public readonly List<Pt> Points = new();

    public override void AccumulateBounds(ref Bounds bounds)
    {
        foreach (Pt p in Points)
            bounds.Add(p);
    }
}

public enum TextHAlign
{
    Left = 0,
    Center = 1,
    Right = 2,
}

public enum TextVAlign
{
    Baseline = 0,
    Bottom = 1,
    Middle = 2,
    Top = 3,
}

/// <summary>
/// 한 줄짜리 텍스트. MText는 추출 단계에서 줄 단위로 분해되어 여러 개의 IrText가 된다.
/// </summary>
public sealed class IrText : IrShape
{
    /// <summary>정렬 기준점(도면 좌표).</summary>
    public Pt Anchor;

    /// <summary>글자 높이(도면 단위).</summary>
    public double Height;

    /// <summary>회전각(라디안, 반시계).</summary>
    public double Rotation;

    /// <summary>폭 계수. 1이 기본.</summary>
    public double WidthFactor = 1.0;

    /// <summary>기울기각(라디안).</summary>
    public double Oblique;

    public string Text = string.Empty;

    /// <summary>실제로 그릴 TrueType 글꼴 이름. SHX는 추출 단계에서 대체 글꼴로 바뀐다.</summary>
    public string FontFamily = "Arial";

    public bool Bold;
    public bool Italic;

    public TextHAlign HAlign;
    public TextVAlign VAlign;

    /// <summary>
    /// AutoCAD가 계산한 실제 외곽 범위. 글꼴 메트릭으로 어림하지 않고 이 값을 쓴다.
    /// (SHX 대체 글꼴은 폭이 원본과 다르므로 어림하면 여백이 어긋난다.)
    /// </summary>
    public Bounds Extents = Bounds.Empty;

    public override void AccumulateBounds(ref Bounds bounds)
    {
        if (!Extents.IsEmpty)
        {
            bounds.Add(new Pt(Extents.MinX, Extents.MinY));
            bounds.Add(new Pt(Extents.MaxX, Extents.MaxY));
            return;
        }

        bounds.Add(Anchor);
    }
}

/// <summary>수집 결과 묶음.</summary>
public sealed class IrDocument
{
    public readonly List<IrShape> Shapes = new();

    public Bounds ComputeBounds()
    {
        var b = Bounds.Empty;
        foreach (IrShape shape in Shapes)
            shape.AccumulateBounds(ref b);
        return b;
    }
}
