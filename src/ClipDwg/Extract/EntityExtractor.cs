using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ClipDwg.Style;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace ClipDwg.Extract;

public sealed class ExtractStats
{
    /// <summary>레이어가 꺼짐/동결이거나 엔티티 자체가 비표시라 건너뛴 개수.</summary>
    public int Invisible;

    /// <summary>WCS XY 평면과 평행하지 않아 직선 근사로 처리한 곡선 개수.</summary>
    public int Tessellated;

    /// <summary>열지 못했거나 지오메트리가 퇴화해 버린 개수.</summary>
    public int Failed;
}

public static class EntityExtractor
{
    /// <summary>법선이 ±Z와 이 정도로 가까우면 WCS XY 평면 도형으로 취급한다.</summary>
    private const double PlanarTolerance = 1e-8;

    private const int MinSamples = 32;
    private const int MaxSamples = 2048;

    public static IrDocument Extract(
        Transaction tr, IReadOnlyList<ObjectId> ids, ExtractStats stats, FontResolver? fonts = null)
    {
        var doc = new IrDocument();
        var layers = new Dictionary<ObjectId, LayerInfo>();

        foreach (ObjectId id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForRead, false, false) is not Entity ent)
            {
                stats.Failed++;
                continue;
            }

            LayerInfo layer = GetLayer(tr, ent.LayerId, layers);
            if (!ent.Visible || !layer.Visible)
            {
                stats.Invisible++;
                continue;
            }

            IrColor color = ResolveColor(ent.Color, layer.Color, DefaultColor);

            // 여러 개의 도형으로 쪼개지는 것들은 문서에 직접 담는 경로를 따로 둔다.
            if (ent is MText mtext)
            {
                if (!AddMText(doc, mtext, color, tr, fonts, stats))
                    stats.Failed++;
                continue;
            }

            if (ent is Dimension or Leader or MLeader)
            {
                if (!AddAnnotation(doc, ent, color, tr, layers, fonts, stats, depth: 0))
                    stats.Failed++;
                continue;
            }

            if (ent is Polyline lwPolyline)
            {
                if (!AddLwPolyline(doc, lwPolyline, color, stats))
                    stats.Failed++;
                continue;
            }

            IrShape? shape = ent switch
            {
                Line line => FromLine(line, color),
                Arc arc => FromArc(arc, color, stats),
                Circle circle => FromCircle(circle, color, stats),
                Polyline2d p2 => FromExploded(p2, color, p2.Closed, stats, Polyline2dWidth(p2)),
                Polyline3d p3 => FromExploded(p3, color, p3.Closed, stats),
                DBText dbText => FromText(dbText, color, tr, fonts),
                Solid solid => FromSolid(solid, color),
                _ => null,
            };

            if (shape is null)
                stats.Failed++;
            else
                doc.Shapes.Add(shape);
        }

        return doc;
    }

    // ---- 치수·지시선 ----------------------------------------------------

    /// <summary>커스텀 화살촉이 블록으로 들어간 경우를 위한 재귀 한계.</summary>
    private const int MaxAnnotationDepth = 3;

    /// <summary>
    /// 치수·지시선을 AutoCAD가 분해한 결과로 담는다.
    /// <para>
    /// 치수는 종류가 많고(선형·정렬·반지름·지름·각도·좌표·호길이) 화살촉 모양·문자 위치·
    /// 보조선 간격이 모두 치수 스타일에 딸려 있다. 그 규칙을 다시 구현하는 대신 분해 결과를
    /// 쓰면 화면에 보이는 그대로가 나온다.
    /// </para>
    /// </summary>
    private static bool AddAnnotation(
        IrDocument doc,
        Entity entity,
        IrColor blockColor,
        Transaction tr,
        Dictionary<ObjectId, LayerInfo> layers,
        FontResolver? fonts,
        ExtractStats stats,
        int depth)
    {
        if (depth > MaxAnnotationDepth)
            return false;

        using var pieces = new DBObjectCollection();
        try
        {
            entity.Explode(pieces);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        bool added = false;
        foreach (DBObject obj in pieces)
        {
            using (obj)
            {
                if (obj is not Entity piece)
                    continue;

                LayerInfo layer = GetLayer(tr, piece.LayerId, layers);
                if (!piece.Visible || !layer.Visible)
                    continue;

                // 분해된 조각은 대개 ByBlock 이다. 감싸던 치수의 색으로 해석해야 한다.
                IrColor color = ResolveColor(piece.Color, layer.Color, blockColor);

                if (AddPiece(doc, piece, color, tr, layers, fonts, stats, depth))
                    added = true;
            }
        }

        return added;
    }

    private static bool AddPiece(
        IrDocument doc,
        Entity piece,
        IrColor color,
        Transaction tr,
        Dictionary<ObjectId, LayerInfo> layers,
        FontResolver? fonts,
        ExtractStats stats,
        int depth)
    {
        switch (piece)
        {
            case MText mtext:
                return AddMText(doc, mtext, color, tr, fonts, stats);

            // 커스텀 화살촉은 블록으로 들어온다.
            case BlockReference or Dimension or Leader or MLeader:
                return AddAnnotation(doc, piece, color, tr, layers, fonts, stats, depth + 1);

            // 폭을 가진 폴리라인은 여러 도형으로 쪼개질 수 있다. (점 화살촉이 이 경로다)
            case Polyline lwPolyline:
                return AddLwPolyline(doc, lwPolyline, color, stats);

            default:
            {
                IrShape? shape = piece switch
                {
                    Line line => FromLine(line, color),
                    Arc arc => FromArc(arc, color, stats),
                    Circle circle => FromCircle(circle, color, stats),
                    Polyline2d p2 => FromExploded(p2, color, p2.Closed, stats, Polyline2dWidth(p2)),
                    Polyline3d p3 => FromExploded(p3, color, p3.Closed, stats),
                    DBText dbText => FromText(dbText, color, tr, fonts),
                    Solid solid => FromSolid(solid, color),
                    _ => null,
                };

                if (shape is null)
                    return false;

                doc.Shapes.Add(shape);
                return true;
            }
        }
    }

    /// <summary>
    /// AcDbSolid(채워진 2D 도형). 치수 화살촉이 대부분 이걸로 나온다.
    /// 정점은 0-1-3-2 순서로 사각형을 이루고, 삼각형이면 2와 3이 같은 점이다.
    /// </summary>
    private static readonly short[] SolidVertexOrder = { 0, 1, 3, 2 };

    private static IrShape? FromSolid(Solid solid, IrColor color)
    {
        Matrix3d toWorld = Matrix3d.PlaneToWorld(solid.Normal);

        var polygon = new IrFilledPolygon { Color = color };

        Pt? previous = null;
        foreach (short index in SolidVertexOrder)
        {
            Point3d ocs;
            try
            {
                ocs = solid.GetPointAt(index);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                continue;
            }

            Pt p = ToPt(ocs.TransformBy(toWorld));

            // 삼각형은 마지막 두 점이 겹친다. 중복은 걸러 낸다.
            if (previous is { } prev && Math.Abs(prev.X - p.X) < 1e-9 && Math.Abs(prev.Y - p.Y) < 1e-9)
                continue;

            polygon.Points.Add(p);
            previous = p;
        }

        return polygon.Points.Count >= 3 ? polygon : null;
    }

    // ---- 텍스트 ---------------------------------------------------------

    private static IrShape? FromText(DBText text, IrColor color, Transaction tr, FontResolver? fonts)
    {
        string content = text.TextString;
        if (string.IsNullOrEmpty(content) || text.Height <= 0)
            return null;

        (string family, bool bold, bool italic) = ResolveFont(text.TextStyleId, tr, fonts);

        // 좌하단 기준(Left/Baseline)이면 Position, 그 외 정렬은 AlignmentPoint가 실제 기준점이다.
        bool useAlignmentPoint = text.HorizontalMode != TextHorizontalMode.TextLeft
                                 || text.VerticalMode != TextVerticalMode.TextBase;

        Point3d anchor = useAlignmentPoint ? text.AlignmentPoint : text.Position;

        var ir = new IrText
        {
            Color = color,
            Anchor = ToPt(anchor),
            Height = text.Height,
            Rotation = text.Rotation,
            WidthFactor = text.WidthFactor > 0 ? text.WidthFactor : 1.0,
            Oblique = text.Oblique,
            Text = content,
            FontFamily = family,
            Bold = bold,
            Italic = italic,
            HAlign = MapHorizontal(text.HorizontalMode),
            VAlign = MapVertical(text.VerticalMode),
            Extents = TryGetExtents(text),
        };

        return ir;
    }

    /// <summary>
    /// MText는 줄바꿈·서식코드·문단 정렬을 자체 규칙으로 배치한다. 그 규칙을 우리가 다시
    /// 구현하는 대신 AutoCAD가 분해한 결과(줄마다 DBText)를 그대로 받는다.
    /// 인라인 색상 변경 같은 서식은 이 과정에서 사라진다.
    /// </summary>
    private static bool AddMText(
        IrDocument doc, MText mtext, IrColor color, Transaction tr, FontResolver? fonts, ExtractStats stats)
    {
        using var pieces = new DBObjectCollection();
        try
        {
            mtext.Explode(pieces);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        bool added = false;
        foreach (DBObject piece in pieces)
        {
            using (piece)
            {
                if (piece is not DBText line)
                    continue;

                IrShape? shape = FromText(line, color, tr, fonts);
                if (shape is null)
                    continue;

                doc.Shapes.Add(shape);
                added = true;
            }
        }

        return added;
    }

    private static (string Family, bool Bold, bool Italic) ResolveFont(
        ObjectId styleId, Transaction tr, FontResolver? fonts)
    {
        const string fallback = "Arial";
        if (fonts is null || styleId.IsNull)
            return (fallback, false, false);

        if (tr.GetObject(styleId, OpenMode.ForRead, false, false) is not TextStyleTableRecord style)
            return (fallback, false, false);

        // FontDescriptor 는 구조체다.
        Autodesk.AutoCAD.GraphicsInterface.FontDescriptor descriptor = style.Font;
        string family = fonts.Resolve(descriptor.TypeFace, style.FileName, style.BigFontFileName);
        return (family, descriptor.Bold, descriptor.Italic);
    }

    /// <summary>AutoCAD가 계산한 실제 외곽 범위. 대체 글꼴로는 폭을 정확히 재현할 수 없으므로 이 값을 쓴다.</summary>
    private static Bounds TryGetExtents(Entity entity)
    {
        var bounds = Bounds.Empty;
        try
        {
            Extents3d e = entity.GeometricExtents;
            bounds.Add(new Pt(e.MinPoint.X, e.MinPoint.Y));
            bounds.Add(new Pt(e.MaxPoint.X, e.MaxPoint.Y));
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // 빈 문자열 등 범위를 못 내는 경우가 있다. 앵커만으로 대신한다.
        }

        return bounds;
    }

    private static TextHAlign MapHorizontal(TextHorizontalMode mode) => mode switch
    {
        TextHorizontalMode.TextCenter or TextHorizontalMode.TextMid => TextHAlign.Center,
        TextHorizontalMode.TextRight => TextHAlign.Right,
        // Aligned / Fit 은 두 점 사이에 맞춰 늘리는 방식이라 여기서는 왼쪽 정렬로 근사한다.
        _ => TextHAlign.Left,
    };

    private static TextVAlign MapVertical(TextVerticalMode mode) => mode switch
    {
        TextVerticalMode.TextBottom => TextVAlign.Bottom,
        TextVerticalMode.TextVerticalMid => TextVAlign.Middle,
        TextVerticalMode.TextTop => TextVAlign.Top,
        _ => TextVAlign.Baseline,
    };

    // ---- 엔티티별 변환 -------------------------------------------------

    private static IrShape FromLine(Line line, IrColor color)
    {
        var path = new IrPath { Color = color, Start = ToPt(line.StartPoint) };
        path.Segments.Add(IrSegment.Line(ToPt(line.EndPoint)));
        return path;
    }

    private static IrShape? FromArc(Arc arc, IrColor color, ExtractStats stats)
    {
        var path = new IrPath { Color = color, Start = ToPt(arc.StartPoint) };

        if (TryArcSegment(arc.Center, arc.Radius, arc.StartPoint, arc.EndPoint, arc.Normal, out IrSegment seg))
        {
            path.Segments.Add(seg);
            return path;
        }

        stats.Tessellated++;
        return AppendSampled(path, arc, MinSamples) ? path : null;
    }

    private static IrShape? FromCircle(Circle circle, IrColor color, ExtractStats stats)
    {
        if (circle.Radius <= 0)
            return null;

        if (!TryGetPlanarOrientation(circle.Normal, out _))
        {
            // 기울어진 원은 투영하면 타원이다. 직선 근사로 넘긴다.
            stats.Tessellated++;
            var path = new IrPath { Color = color, Start = ToPt(circle.StartPoint), Closed = true };
            return AppendSampled(path, circle, 64) ? path : null;
        }

        return new IrCircle
        {
            Color = color,
            Center = ToPt(circle.Center),
            Radius = circle.Radius,
        };
    }

    /// <summary>
    /// LWPolyline. 직선/원호 구간을 하나의 연속 경로로 유지한다.
    /// bulge 해석과 OCS→WCS 변환은 <c>GetLineSegmentAt</c>/<c>GetArcSegmentAt</c>에 맡긴다.
    /// (미러링된 폴리라인처럼 법선이 -Z인 경우까지 이쪽이 확실하다.)
    /// <para>
    /// 폭이 다른 구간은 따로 떼어 낸다. 굵기가 다른 부분을 한 경로로 묶을 수 없기 때문이다.
    /// 폭이 점점 변하는(테이퍼) 구간은 평균 폭으로 근사한다.
    /// </para>
    /// </summary>
    private static bool AddLwPolyline(IrDocument doc, Polyline pl, IrColor color, ExtractStats stats)
    {
        int vertexCount = pl.NumberOfVertices;
        if (vertexCount < 2)
            return false;

        if (!TryGetPlanarOrientation(pl.Normal, out _))
        {
            stats.Tessellated++;
            var flat = new IrPath
            {
                Color = color,
                Start = ToPt(pl.StartPoint),
                Closed = pl.Closed,
                IntrinsicWidth = SegmentWidth(pl, 0),
            };

            if (!AppendSampled(flat, pl, SampleCount(vertexCount)))
                return false;

            doc.Shapes.Add(flat);
            return true;
        }

        int segmentCount = pl.Closed ? vertexCount : vertexCount - 1;
        var runs = new List<IrPath>();
        IrPath? current = null;
        Pt cursor = ToPt(pl.GetPoint3dAt(0));

        for (int i = 0; i < segmentCount; i++)
        {
            if (!TryBuildSegment(pl, i, out IrSegment segment, out Pt end))
                continue;

            double width = SegmentWidth(pl, i);
            if (current is null || Math.Abs(width - current.IntrinsicWidth) > 1e-12)
            {
                current = new IrPath { Color = color, Start = cursor, IntrinsicWidth = width };
                runs.Add(current);
            }

            current.Segments.Add(segment);
            cursor = end;
        }

        if (runs.Count == 0)
            return false;

        // 폭이 한 가지뿐이면 원래의 닫힘 여부를 그대로 살릴 수 있다.
        if (runs.Count == 1)
            runs[0].Closed = pl.Closed;

        foreach (IrPath run in runs)
        {
            if (run.Segments.Count > 0)
                doc.Shapes.Add(run);
        }

        return true;
    }

    private static bool TryBuildSegment(Polyline pl, int index, out IrSegment segment, out Pt end)
    {
        switch (pl.GetSegmentType(index))
        {
            case SegmentType.Arc:
            {
                using CircularArc3d arc = pl.GetArcSegmentAt(index);
                end = ToPt(arc.EndPoint);
                if (!TryArcSegment(arc.Center, arc.Radius, arc.StartPoint, arc.EndPoint, arc.Normal, out segment))
                    segment = IrSegment.Line(end);
                return true;
            }

            case SegmentType.Line:
            {
                using LineSegment3d line = pl.GetLineSegmentAt(index);
                end = ToPt(line.EndPoint);
                segment = IrSegment.Line(end);
                return true;
            }

            default:
                // Coincident / Point / Empty 는 길이가 없다.
                segment = default;
                end = default;
                return false;
        }
    }

    /// <summary>구간의 폭(도면 단위). 정점별 폭이 없으면 전체 폭을 쓴다.</summary>
    private static double SegmentWidth(Polyline pl, int index)
    {
        try
        {
            if (pl.HasWidth && index < pl.NumberOfVertices)
            {
                double start = pl.GetStartWidthAt(index);
                double end = pl.GetEndWidthAt(index);
                if (start > 0 || end > 0)
                    return (start + end) / 2;
            }

            return Math.Max(0, pl.ConstantWidth);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Polyline2d / Polyline3d. 구버전 폴리라인은 정점이 OCS에 저장되고
    /// 스플라인·커브핏 변형까지 있어서, AutoCAD가 직접 내주는 분해 결과를 쓰는 편이 확실하다.
    /// 분해 결과(Line/Arc)는 WCS이고 순서가 보장되므로 하나의 연속 경로로 다시 이어붙인다.
    /// </summary>
    /// <summary>Polyline2d 는 분해하면 폭 정보가 사라지므로 기본 폭을 따로 읽어 넘긴다.</summary>
    private static double Polyline2dWidth(Polyline2d polyline)
    {
        try
        {
            double start = polyline.DefaultStartWidth;
            double end = polyline.DefaultEndWidth;
            return Math.Max(0, (start + end) / 2);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return 0;
        }
    }

    private static IrShape? FromExploded(
        Entity entity, IrColor color, bool closed, ExtractStats stats, double intrinsicWidth = 0)
    {
        using var pieces = new DBObjectCollection();
        try
        {
            entity.Explode(pieces);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }

        IrPath? path = null;
        bool degraded = false;

        foreach (DBObject piece in pieces)
        {
            using (piece)
            {
                switch (piece)
                {
                    case Line line:
                        path ??= new IrPath
                        {
                            Color = color, Start = ToPt(line.StartPoint), Closed = closed, IntrinsicWidth = intrinsicWidth,
                        };
                        path.Segments.Add(IrSegment.Line(ToPt(line.EndPoint)));
                        break;

                    case Arc arc:
                        path ??= new IrPath
                        {
                            Color = color, Start = ToPt(arc.StartPoint), Closed = closed, IntrinsicWidth = intrinsicWidth,
                        };
                        if (TryArcSegment(arc.Center, arc.Radius, arc.StartPoint, arc.EndPoint, arc.Normal, out IrSegment seg))
                        {
                            path.Segments.Add(seg);
                        }
                        else
                        {
                            degraded = true;
                            AppendSampled(path, arc, MinSamples);
                        }

                        break;
                }
            }
        }

        if (degraded)
            stats.Tessellated++;

        return path is { Segments.Count: > 0 } ? path : null;
    }

    // ---- 지오메트리 도우미 ---------------------------------------------

    /// <summary>
    /// WCS 중심·시작점·끝점으로 원호 구간을 만든다.
    /// ECS 각도를 그대로 쓰지 않고 WCS 점에서 각을 다시 구하므로 뒤집힌 법선(-Z)도 옳게 처리된다.
    /// </summary>
    private static bool TryArcSegment(
        Point3d wcsCenter, double radius, Point3d wcsStart, Point3d wcsEnd, Vector3d normal, out IrSegment segment)
    {
        segment = default;
        if (radius <= 0 || !TryGetPlanarOrientation(normal, out bool counterClockwise))
            return false;

        Pt center = ToPt(wcsCenter);
        Pt start = ToPt(wcsStart);
        Pt end = ToPt(wcsEnd);

        double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        double endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);

        segment = IrSegment.Arc(end, center, radius, startAngle, GeomUtil.SweepBetween(startAngle, endAngle, counterClockwise));
        return true;
    }

    /// <summary>곡선을 등거리 직선으로 근사해 경로 끝에 이어붙인다.</summary>
    private static bool AppendSampled(IrPath path, Curve curve, int samples)
    {
        double length;
        try
        {
            length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        if (length <= 0)
            return false;

        for (int i = 1; i <= samples; i++)
        {
            try
            {
                path.Segments.Add(IrSegment.Line(ToPt(curve.GetPointAtDist(length * i / samples))));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return path.Segments.Count > 0;
            }
        }

        return true;
    }

    private static int SampleCount(int vertexCount) =>
        Math.Min(MaxSamples, Math.Max(MinSamples, vertexCount * 8));

    // ---- 색·레이어 -----------------------------------------------------

    internal readonly struct LayerInfo
    {
        public readonly bool Visible;
        public readonly IrColor Color;

        public LayerInfo(bool visible, IrColor color)
        {
            Visible = visible;
            Color = color;
        }
    }

    private static readonly IrColor DefaultColor = new(7, 255, 255, 255);

    private static LayerInfo GetLayer(Transaction tr, ObjectId layerId, Dictionary<ObjectId, LayerInfo> cache)
    {
        if (cache.TryGetValue(layerId, out LayerInfo info))
            return info;

        info = tr.GetObject(layerId, OpenMode.ForRead, false, false) is LayerTableRecord ltr
            ? new LayerInfo(!ltr.IsOff && !ltr.IsFrozen, ToIrColor(ltr.Color, DefaultColor))
            : new LayerInfo(true, DefaultColor);

        cache[layerId] = info;
        return info;
    }

    /// <summary>
    /// <paramref name="blockColor"/>는 ByBlock 을 해석할 색이다. 최상위 엔티티라면 감쌀 블록이
    /// 없으므로 기본색이고, 치수를 분해한 조각이라면 그 치수의 색이다.
    /// </summary>
    private static IrColor ResolveColor(AcColor color, IrColor layerColor, IrColor blockColor)
    {
        if (color.IsByLayer)
            return layerColor;

        if (color.IsByBlock)
            return blockColor;

        return ToIrColor(color, layerColor);
    }

    private static IrColor ToIrColor(AcColor color, IrColor fallback)
    {
        if (color.IsByLayer || color.IsByBlock)
            return fallback;

        short aci = color.ColorMethod == ColorMethod.ByAci && color.ColorIndex is >= 1 and <= 255
            ? color.ColorIndex
            : (short)0;

        System.Drawing.Color rgb = color.ColorValue;
        return new IrColor(aci, rgb.R, rgb.G, rgb.B);
    }

    // ---- 공통 ----------------------------------------------------------

    private static Pt ToPt(Point3d p) => new(p.X, p.Y);

    /// <summary>
    /// 법선이 ±Z와 평행한지 판정하고, 평행하면 WCS XY에서의 진행 방향을 돌려준다.
    /// (-Z 법선이면 ECS 기준 반시계가 WCS에서는 시계 방향으로 보인다.)
    /// </summary>
    private static bool TryGetPlanarOrientation(Vector3d normal, out bool counterClockwise)
    {
        counterClockwise = normal.Z > 0;
        return Math.Abs(Math.Abs(normal.Z) - 1.0) <= PlanarTolerance
               && Math.Abs(normal.X) <= PlanarTolerance
               && Math.Abs(normal.Y) <= PlanarTolerance;
    }
}
