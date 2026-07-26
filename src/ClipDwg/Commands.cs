using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using ClipDwg.Clipboard;
using ClipDwg.Extract;
using ClipDwg.Render;
using ClipDwg.Style;
using ClipDwg.Ui;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ClipDwg;

public static class Commands
{
    [CommandMethod("CLIPDWG", CommandFlags.UsePickSet | CommandFlags.Redraw)]
    public static void ClipDwgCommand()
    {
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;

        Editor ed = doc.Editor;

        PromptSelectionResult psr = GetTargets(ed);
        if (psr.Status != PromptStatus.OK || psr.Value is null || psr.Value.Count == 0)
        {
            ed.WriteMessage("\nclipdwg: 취소되었습니다.\n");
            return;
        }

        ObjectId[] ids = psr.Value.GetObjectIds();

        var targets = new List<ObjectId>(ids.Length);
        var supported = new Dictionary<EntityKind, int>();
        var ignored = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (ObjectId id in ids)
        {
            EntityKind kind = EntityClassifier.Classify(id);
            if (kind == EntityKind.Unsupported)
            {
                Bump(ignored, id.ObjectClass?.DxfName ?? "Unknown");
                continue;
            }

            targets.Add(id);
            Bump(supported, kind);
        }

        ed.WriteMessage($"\nclipdwg: 대상 {targets.Count}개{FormatKinds(supported)}\n");
        if (ignored.Count > 0)
            ed.WriteMessage($"clipdwg: 무시 {ids.Length - targets.Count}개{FormatNames(ignored)}\n");

        if (targets.Count == 0)
        {
            ed.WriteMessage("clipdwg: 복사할 수 있는 객체가 없습니다. " +
                            "(선, 호, 원, 폴리라인, 텍스트, 치수, 지시선만 지원)\n");
            return;
        }

        SettingsFile settings = SettingsStore.Load(SettingsStore.FilePath, out string? settingsError);
        if (settingsError is not null)
            ed.WriteMessage($"clipdwg: {settingsError}\n");

        Profile profile = settings.GetActiveProfile();
        var fonts = new FontResolver(profile);

        var stats = new ExtractStats();
        IrDocument ir;
        using (Transaction tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
        {
            ir = EntityExtractor.Extract(tr, targets, stats, fonts);
            tr.Commit();
        }

        ReportStats(ed, stats);
        if (fonts.SubstitutedShxFonts.Count > 0)
        {
            ed.WriteMessage($"clipdwg: SHX 글꼴 {string.Join(", ", fonts.SubstitutedShxFonts)} " +
                            "은(는) 대체 TrueType 글꼴로 그렸습니다 (CLIPDWGCFG에서 변경 가능)\n");
        }

        if (ir.Shapes.Count == 0)
        {
            ed.WriteMessage("clipdwg: 복사할 지오메트리가 없습니다.\n");
            return;
        }

        new ColorWeightMap(profile).Apply(ir);

        RenderOptions options = RenderOptions.FromProfile(profile);
        try
        {
            RenderResult result = EmfRenderer.Render(ir, options);

            // 성공하면 EMF 핸들 소유권이 클립보드로 넘어가므로 여기서 Dispose하지 않는다.
            ClipboardWriter.SetMetafile(result.Metafile, GetOwnerWindow());

            ed.WriteMessage($"clipdwg: 도형 {ir.Shapes.Count}개를 클립보드에 복사했습니다 " +
                            $"({result.WidthMm:0.##} x {result.HeightMm:0.##} mm, 프로파일 '{profile.Name}').\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"clipdwg: 복사 실패 - {ex.Message}\n");
        }
    }

    [CommandMethod("CLIPDWGCFG", CommandFlags.Modal)]
    public static void ClipDwgConfigCommand()
    {
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;

        Editor ed = doc.Editor;

        SettingsFile settings = SettingsStore.Load(SettingsStore.FilePath, out string? loadError);
        if (loadError is not null)
            ed.WriteMessage($"clipdwg: {loadError}\n");

        try
        {
            using var form = new OptionsForm(settings);
            if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK || form.Result is null)
            {
                ed.WriteMessage("clipdwg: 옵션 변경을 취소했습니다.\n");
                return;
            }

            SettingsStore.Save(form.Result, SettingsStore.FilePath);
            ed.WriteMessage($"clipdwg: 옵션을 저장했습니다 (프로파일 '{form.Result.ActiveProfile}').\n" +
                            $"clipdwg: {SettingsStore.FilePath}\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"clipdwg: 옵션 저장 실패 - {ex.Message}\n");
        }
    }

    /// <summary>클립보드 소유자로 지정할 AutoCAD 메인 윈도우. 못 얻으면 현재 태스크에 귀속시킨다.</summary>
    private static IntPtr GetOwnerWindow()
    {
        try
        {
            return AcApp.MainWindow?.Handle ?? IntPtr.Zero;
        }
        catch (System.Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static void ReportStats(Editor ed, ExtractStats stats)
    {
        if (stats.Invisible > 0)
            ed.WriteMessage($"clipdwg: 비표시 {stats.Invisible}개 제외 (레이어 꺼짐/동결)\n");
        if (stats.Tessellated > 0)
            ed.WriteMessage($"clipdwg: 기울어진 곡선 {stats.Tessellated}개는 직선 근사로 처리\n");
        if (stats.Failed > 0)
            ed.WriteMessage($"clipdwg: 처리 실패 {stats.Failed}개\n");
    }

    /// <summary>사전선택(픽셋)이 있으면 그대로 쓰고, 없으면 선택을 요청한다.</summary>
    private static PromptSelectionResult GetTargets(Editor ed)
    {
        PromptSelectionResult implied = ed.SelectImplied();
        if (implied.Status == PromptStatus.OK && implied.Value is { Count: > 0 })
        {
            // 픽셋을 소비했음을 AutoCAD에 알린다
            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            return implied;
        }

        var opts = new PromptSelectionOptions
        {
            MessageForAdding = "\n복사할 객체 선택",
            MessageForRemoval = "\n제외할 객체 선택",
        };
        return ed.GetSelection(opts);
    }

    private static void Bump<TKey>(Dictionary<TKey, int> counts, TKey key) where TKey : notnull
    {
        counts.TryGetValue(key, out int n);
        counts[key] = n + 1;
    }

    private static string FormatKinds(Dictionary<EntityKind, int> counts)
    {
        if (counts.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(" (");
        bool first = true;
        foreach (KeyValuePair<EntityKind, int> entry in counts)
        {
            if (!first)
                sb.Append(", ");
            sb.Append(EntityClassifier.DisplayName(entry.Key)).Append(' ').Append(entry.Value);
            first = false;
        }
        return sb.Append(')').ToString();
    }

    private static string FormatNames(Dictionary<string, int> counts)
    {
        var sb = new StringBuilder(" (");
        bool first = true;
        foreach (KeyValuePair<string, int> entry in counts)
        {
            if (!first)
                sb.Append(", ");
            sb.Append(entry.Key).Append(' ').Append(entry.Value);
            first = false;
        }
        return sb.Append(')').ToString();
    }
}
