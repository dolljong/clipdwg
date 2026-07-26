using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace ClipDwg.Extract;

public enum EntityKind
{
    Unsupported = 0,
    Line,
    Arc,
    Circle,
    LwPolyline,
    Polyline2d,
    Polyline3d,
    Text,
    MText,
    Dimension,
    Leader,
    MLeader,
    Solid,
}

/// <summary>
/// ObjectId만으로 지원 여부를 판정한다. 트랜잭션으로 객체를 열지 않으므로
/// 큰 선택집합에서도 분류 비용이 사실상 0이다.
/// </summary>
public static class EntityClassifier
{
    private static readonly Dictionary<RXClass, EntityKind> Map = new()
    {
        [RXObject.GetClass(typeof(Line))] = EntityKind.Line,
        [RXObject.GetClass(typeof(Arc))] = EntityKind.Arc,
        [RXObject.GetClass(typeof(Circle))] = EntityKind.Circle,
        [RXObject.GetClass(typeof(Polyline))] = EntityKind.LwPolyline,
        [RXObject.GetClass(typeof(Polyline2d))] = EntityKind.Polyline2d,
        [RXObject.GetClass(typeof(Polyline3d))] = EntityKind.Polyline3d,
        [RXObject.GetClass(typeof(DBText))] = EntityKind.Text,
        [RXObject.GetClass(typeof(MText))] = EntityKind.MText,
        // 치수는 종류마다 클래스가 다르다(RotatedDimension 등). 상속 검사로 잡힌다.
        [RXObject.GetClass(typeof(Dimension))] = EntityKind.Dimension,
        [RXObject.GetClass(typeof(Leader))] = EntityKind.Leader,
        [RXObject.GetClass(typeof(MLeader))] = EntityKind.MLeader,
        [RXObject.GetClass(typeof(Solid))] = EntityKind.Solid,
    };

    /// <summary>
    /// 상속 검사 결과를 클래스별로 기억한다. 치수처럼 하위 클래스로 들어오는 타입이
    /// 많이 선택되면 매번 전체 목록을 훑게 되기 때문이다.
    /// </summary>
    private static readonly Dictionary<RXClass, EntityKind> DerivedCache = new();

    public static EntityKind Classify(ObjectId id)
    {
        RXClass? cls = id.ObjectClass;
        if (cls is null)
            return EntityKind.Unsupported;

        // 정확히 일치하는 경우가 압도적으로 많으므로 먼저 조회
        if (Map.TryGetValue(cls, out EntityKind kind))
            return kind;

        if (DerivedCache.TryGetValue(cls, out kind))
            return kind;

        kind = EntityKind.Unsupported;
        foreach (KeyValuePair<RXClass, EntityKind> entry in Map)
        {
            if (cls.IsDerivedFrom(entry.Key))
            {
                kind = entry.Value;
                break;
            }
        }

        DerivedCache[cls] = kind;
        return kind;
    }

    public static string DisplayName(EntityKind kind) => kind switch
    {
        EntityKind.Line => "Line",
        EntityKind.Arc => "Arc",
        EntityKind.Circle => "Circle",
        EntityKind.LwPolyline => "LWPolyline",
        EntityKind.Polyline2d => "Polyline2d",
        EntityKind.Polyline3d => "Polyline3d",
        EntityKind.Text => "Text",
        EntityKind.MText => "MText",
        EntityKind.Dimension => "치수",
        EntityKind.Leader => "지시선",
        EntityKind.MLeader => "다중지시선",
        EntityKind.Solid => "솔리드",
        _ => "Unsupported",
    };
}
