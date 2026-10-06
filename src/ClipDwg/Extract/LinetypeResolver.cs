using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace ClipDwg.Extract;

/// <summary>
/// ByLayer/ByBlock 을 풀어낸 선종류와, 그 엔티티에 적용될 배율(CELTSCALE × 공간 배율).
/// <see cref="Id"/>가 Null 이면 실선.
/// </summary>
internal readonly struct LinetypeRef
{
    public static readonly LinetypeRef Continuous = new(ObjectId.Null, 1.0);

    public readonly ObjectId Id;
    public readonly double Scale;

    public LinetypeRef(ObjectId id, double scale)
    {
        Id = id;
        Scale = scale;
    }
}

/// <summary>
/// 엔티티의 선종류를 실제 패턴(<see cref="IrLinetype"/>)으로 바꾼다.
/// <para>
/// 화면에 보이는 대시 길이 = 선종류 정의 × LTSCALE × 객체 선종류 축척(CELTSCALE)
/// × (모형 공간이고 MSLTSCALE=1 이면) 주석 축척의 역수.
/// </para>
/// </summary>
internal sealed class LinetypeResolver
{
    private readonly Transaction _tr;
    private readonly double _ltscale;
    private readonly ObjectId _byLayer;
    private readonly ObjectId _byBlock;
    private readonly ObjectId _modelSpace;
    private readonly double _modelSpaceScale;

    /// <summary>선종류 정의(축척 미반영). 실선이면 null.</summary>
    private readonly Dictionary<ObjectId, (string Name, double[]? Dashes)> _patterns = new();

    public LinetypeResolver(Transaction tr, Database? db)
    {
        _tr = tr;
        _ltscale = 1.0;
        _modelSpaceScale = 1.0;

        if (db is null)
            return;

        _ltscale = db.Ltscale > 0 ? db.Ltscale : 1.0;
        _byLayer = db.ByLayerLinetype;
        _byBlock = db.ByBlockLinetype;
        _modelSpace = SymbolUtilityServices.GetBlockModelSpaceId(db);

        try
        {
            // 주석 축척 1:100 이면 Scale = 0.01, 모형 공간의 선종류는 100배로 보인다.
            if (db.MsLtScale && db.Cannoscale is { Scale: > 0 } annotation)
                _modelSpaceScale = 1.0 / annotation.Scale;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // 주석 축척을 못 읽으면 배율 없이 간다.
        }
    }

    /// <summary>엔티티가 놓인 공간에서 추가로 곱해지는 배율(MSLTSCALE).</summary>
    public double SpaceScale(Entity entity) =>
        !_modelSpace.IsNull && entity.OwnerId == _modelSpace ? _modelSpaceScale : 1.0;

    /// <summary>
    /// <paramref name="blockLinetype"/>은 ByBlock 을 해석할 선종류다(치수를 분해한 조각이면
    /// 그 치수의 선종류). 결과의 배율에는 LTSCALE 이 아직 들어가지 않는다.
    /// </summary>
    public LinetypeRef Resolve(Entity entity, ObjectId layerLinetype, LinetypeRef blockLinetype, double spaceScale)
    {
        double scale = SafeScale(entity) * spaceScale;
        ObjectId id = entity.LinetypeId;

        if (id.IsNull || id == _byLayer || IsNamed(entity, "BYLAYER"))
            return new LinetypeRef(layerLinetype, scale);

        if (id == _byBlock || IsNamed(entity, "BYBLOCK"))
            return new LinetypeRef(blockLinetype.Id, blockLinetype.Scale * SafeScale(entity));

        return new LinetypeRef(id, scale);
    }

    /// <summary>
    /// <paramref name="first"/> 이후에 문서에 추가된 선 도형에 선종류를 붙인다.
    /// 텍스트·채움 도형에는 붙이지 않는다.
    /// </summary>
    public void Apply(IrDocument doc, int first, LinetypeRef linetype, bool linetypeGen)
    {
        IrLinetype? pattern = ToPattern(linetype);

        for (int i = first; i < doc.Shapes.Count; i++)
        {
            IrShape shape = doc.Shapes[i];
            if (shape is not (IrPath or IrCircle))
                continue;

            shape.Linetype = pattern;

            if (shape is IrPath path && linetypeGen)
                path.LinetypeGen = true;
        }
    }

    private IrLinetype? ToPattern(LinetypeRef linetype)
    {
        if (linetype.Id.IsNull)
            return null;

        (string name, double[]? dashes) = GetPattern(linetype.Id);
        if (dashes is null)
            return null;

        double scale = _ltscale * linetype.Scale;
        var scaled = new double[dashes.Length];
        for (int i = 0; i < dashes.Length; i++)
            scaled[i] = dashes[i] * scale;

        var result = new IrLinetype(name, scaled);
        return result.IsDashed ? result : null;
    }

    private (string Name, double[]? Dashes) GetPattern(ObjectId id)
    {
        if (_patterns.TryGetValue(id, out (string, double[]?) cached))
            return cached;

        (string, double[]?) pattern = (string.Empty, null);
        try
        {
            if (_tr.GetObject(id, OpenMode.ForRead, false, false) is LinetypeTableRecord ltr && ltr.NumDashes > 0)
            {
                var dashes = new double[ltr.NumDashes];
                for (int i = 0; i < dashes.Length; i++)
                    dashes[i] = ltr.DashLengthAt(i);

                // 복합 선종류(문자·모양이 낀 것)는 모양을 빼고 대시·공백만 그린다.
                pattern = (ltr.Name, dashes);
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // 못 읽는 선종류는 실선으로 둔다.
        }

        _patterns[id] = pattern;
        return pattern;
    }

    private static double SafeScale(Entity entity)
    {
        double scale = entity.LinetypeScale;
        return scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale) ? scale : 1.0;
    }

    /// <summary>데이터베이스 밖의 분해 조각은 ByLayer/ByBlock id 비교가 안 될 수 있어 이름도 본다.</summary>
    private static bool IsNamed(Entity entity, string name)
    {
        try
        {
            return string.Equals(entity.Linetype, name, StringComparison.OrdinalIgnoreCase);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }
}
