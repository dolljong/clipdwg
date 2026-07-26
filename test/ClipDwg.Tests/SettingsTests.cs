using System;
using System.IO;
using ClipDwg.Extract;
using ClipDwg.Style;
using Xunit;

namespace ClipDwg.Tests;

public class ColorWeightMapTests
{
    private static Profile ProfileWith(params WidthRule[] rules) =>
        new() { DefaultWidthMm = 0.25, Widths = new(rules) };

    [Fact]
    public void Resolve_UsesAciRule()
    {
        var map = new ColorWeightMap(ProfileWith(new WidthRule { Aci = 3, Mm = 0.6 }));
        Assert.Equal(0.6, map.Resolve(new IrColor(3, 0, 255, 0)));
    }

    [Fact]
    public void Resolve_FallsBackToDefault()
    {
        var map = new ColorWeightMap(ProfileWith(new WidthRule { Aci = 3, Mm = 0.6 }));
        Assert.Equal(0.25, map.Resolve(new IrColor(5, 0, 0, 255)));
    }

    [Fact]
    public void Resolve_TrueColorRuleBeatsAciRule()
    {
        var map = new ColorWeightMap(ProfileWith(
            new WidthRule { Aci = 3, Mm = 0.6 },
            new WidthRule { Rgb = "#00FF00", Mm = 1.2 }));

        // ACI 3 이면서 RGB가 #00FF00 인 색 -> 트루컬러 규칙이 이긴다
        Assert.Equal(1.2, map.Resolve(new IrColor(3, 0, 255, 0)));
    }

    [Fact]
    public void Resolve_TrueColorOnlyEntityMatchesRgbRule()
    {
        var map = new ColorWeightMap(ProfileWith(new WidthRule { Rgb = "#FF8000", Mm = 0.9 }));
        Assert.Equal(0.9, map.Resolve(new IrColor(0, 0xFF, 0x80, 0x00)));
    }

    [Fact]
    public void Apply_SetsWidthOnEveryShape()
    {
        var doc = new IrDocument();
        doc.Shapes.Add(new IrCircle { Color = new IrColor(1, 255, 0, 0), Center = default, Radius = 1 });
        doc.Shapes.Add(new IrCircle { Color = new IrColor(9, 1, 2, 3), Center = default, Radius = 1 });

        new ColorWeightMap(ProfileWith(new WidthRule { Aci = 1, Mm = 0.7 })).Apply(doc);

        Assert.Equal(0.7, doc.Shapes[0].WidthMm);
        Assert.Equal(0.25, doc.Shapes[1].WidthMm);
    }

    [Theory]
    [InlineData("#FF8000", 0xFF8000)]
    [InlineData("ff8000", 0xFF8000)]
    [InlineData("  #000000  ", 0x000000)]
    public void TryParseRgb_AcceptsCommonForms(string text, int expected)
    {
        Assert.True(ColorWeightMap.TryParseRgb(text, out int rgb));
        Assert.Equal(expected, rgb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("#FF80000")]
    public void TryParseRgb_RejectsBadForms(string text)
    {
        Assert.False(ColorWeightMap.TryParseRgb(text, out _));
    }

    [Fact]
    public void InvalidRgbRuleIsIgnoredNotFatal()
    {
        var map = new ColorWeightMap(ProfileWith(new WidthRule { Rgb = "nonsense", Mm = 5 }));
        Assert.Equal(0.25, map.Resolve(new IrColor(0, 1, 2, 3)));
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "clipdwg-tests-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Load_MissingFileReturnsDefaults()
    {
        SettingsFile s = SettingsStore.Load(Path_, out string? error);

        Assert.Null(error);
        Assert.Single(s.Profiles);
        Assert.Equal("default", s.GetActiveProfile().Name);
        Assert.NotEmpty(s.GetActiveProfile().Widths);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        SettingsFile original = SettingsFile.CreateDefault();
        Profile p = original.GetActiveProfile();
        p.MmPerDrawingUnit = 0.001;
        p.OutputScale = 2.5;
        p.MarginMm = 1.25;
        p.DefaultWidthMm = 0.33;
        p.MinWidthMm = 0.02;
        p.WhiteToBlack = false;
        p.ForceBlack = true;
        p.Widths.Add(new WidthRule { Rgb = "#123456", Mm = 0.77 });

        SettingsStore.Save(original, Path_);
        SettingsFile loaded = SettingsStore.Load(Path_, out string? error);

        Assert.Null(error);
        Profile q = loaded.GetActiveProfile();
        Assert.Equal(0.001, q.MmPerDrawingUnit);
        Assert.Equal(2.5, q.OutputScale);
        Assert.Equal(1.25, q.MarginMm);
        Assert.Equal(0.33, q.DefaultWidthMm);
        Assert.Equal(0.02, q.MinWidthMm);
        Assert.False(q.WhiteToBlack);
        Assert.True(q.ForceBlack);
        Assert.Contains(q.Widths, w => w.Rgb == "#123456" && w.Mm == 0.77);
    }

    [Fact]
    public void Save_WritesIndentedJson()
    {
        SettingsStore.Save(SettingsFile.CreateDefault(), Path_);
        string text = File.ReadAllText(Path_);

        Assert.Contains("\n", text);
        Assert.Contains("\"activeProfile\": \"default\"", text);
    }

    [Fact]
    public void Load_CorruptFileFallsBackWithMessage()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{ this is not json");

        SettingsFile s = SettingsStore.Load(Path_, out string? error);

        Assert.NotNull(error);
        Assert.Single(s.Profiles);
    }

    [Fact]
    public void Load_PartialFileKeepsDefaultsForMissingFields()
    {
        Directory.CreateDirectory(_dir);
        // outputScale 등이 빠져 있다. 0으로 떨어지면 렌더가 실패하므로 기본값이 살아야 한다.
        File.WriteAllText(Path_,
            "{\"activeProfile\":\"only\",\"profiles\":[{\"name\":\"only\",\"widths\":[{\"aci\":1,\"mm\":0.5}]}]}");

        SettingsFile s = SettingsStore.Load(Path_, out string? error);
        Profile p = s.GetActiveProfile();

        Assert.Null(error);
        Assert.Equal("only", p.Name);
        Assert.Equal(1.0, p.OutputScale);
        Assert.Equal(1.0, p.MmPerDrawingUnit);
        Assert.Equal(0.25, p.DefaultWidthMm);
        Assert.True(p.WhiteToBlack);
        Assert.Single(p.Widths);
    }
}

public class JsonFormatterTests
{
    [Fact]
    public void Indent_KeepsEmptyContainersOnOneLine()
    {
        Assert.Equal("{}", JsonFormatter.Indent("{}"));
        Assert.Contains("\"widths\": []", JsonFormatter.Indent("{\"widths\":[]}"));
    }

    [Fact]
    public void Indent_DoesNotBreakInsideStrings()
    {
        string result = JsonFormatter.Indent("{\"name\":\"a,b{c}d\"}");
        Assert.Contains("\"a,b{c}d\"", result);
    }

    [Fact]
    public void Indent_HandlesEscapedQuotes()
    {
        string result = JsonFormatter.Indent("{\"name\":\"say \\\"hi\\\", ok\"}");
        Assert.Contains("say \\\"hi\\\", ok", result);
    }
}
