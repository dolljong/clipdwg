using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

namespace ClipDwg.Style;

/// <summary>
/// AutoCAD 문자 스타일 → 실제로 그릴 TrueType 글꼴 이름.
/// <para>
/// SHX 글꼴(romans.shx, whgtxt.shx 등)은 윤곽선 글꼴이 아니라서 EMF에 글자로 넣을 수 없다.
/// AutoCAD도 SHX를 자체 벡터 획으로 그린다. 그래서 대체 TrueType 글꼴로 바꿔 넣는다.
/// 자간이 원본과 다소 달라지지만, 붙여넣은 뒤에도 글자로 남아 편집·검색이 된다.
/// </para>
/// </summary>
public sealed class FontResolver
{
    private readonly Dictionary<string, string> _substitutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultShxFont;
    private readonly HashSet<string> _installed;
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    public FontResolver(Profile profile)
        : this(profile, InstalledFamilies())
    {
    }

    internal FontResolver(Profile profile, HashSet<string> installedFamilies)
    {
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        _installed = installedFamilies;
        _defaultShxFont = FirstAvailable(profile.DefaultShxFont, "Arial");

        foreach (FontSubstitute s in profile.ShxSubstitutes)
        {
            if (!string.IsNullOrWhiteSpace(s.Shx) && !string.IsNullOrWhiteSpace(s.Font))
                _substitutes[Normalize(s.Shx!)] = s.Font!;
        }
    }

    /// <summary>대체가 일어난 SHX 이름들. 명령행에 한 번 알려 주려고 모은다.</summary>
    public IReadOnlyCollection<string> SubstitutedShxFonts => _reported;

    /// <summary>
    /// <paramref name="typeface"/>가 있으면 TrueType 스타일이므로 그대로 쓴다.
    /// 비어 있으면 SHX 스타일이라 <paramref name="shxFileName"/>으로 대체 글꼴을 찾는다.
    /// </summary>
    public string Resolve(string? typeface, string? shxFileName, string? bigFontFileName)
    {
        if (!string.IsNullOrWhiteSpace(typeface))
            return FirstAvailable(typeface!, _defaultShxFont);

        // 한글 등은 큰글꼴(bigfont) 쪽에 지정되므로 그쪽을 먼저 본다.
        string? fromBig = Lookup(bigFontFileName);
        if (fromBig is not null)
            return fromBig;

        string? fromShx = Lookup(shxFileName);
        if (fromShx is not null)
            return fromShx;

        Report(shxFileName ?? bigFontFileName);
        return _defaultShxFont;
    }

    private string? Lookup(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        if (!_substitutes.TryGetValue(Normalize(fileName!), out string? family))
            return null;

        Report(fileName);
        return FirstAvailable(family, _defaultShxFont);
    }

    private void Report(string? shx)
    {
        if (!string.IsNullOrWhiteSpace(shx))
            _reported.Add(shx!.Trim());
    }

    /// <summary>설치되지 않은 글꼴을 지정하면 GDI+가 조용히 엉뚱한 글꼴로 바꾸므로 먼저 확인한다.</summary>
    private string FirstAvailable(string preferred, string fallback)
    {
        if (_installed.Count == 0 || _installed.Contains(preferred))
            return preferred;

        return _installed.Contains(fallback) ? fallback : "Arial";
    }

    /// <summary>확장자와 대소문자를 무시하고 비교한다. "whgtxt.shx" 와 "WHGTXT" 를 같게 본다.</summary>
    private static string Normalize(string fileName)
    {
        string s = fileName.Trim();
        int dot = s.LastIndexOf('.');
        return dot > 0 ? s.Substring(0, dot) : s;
    }

    private static HashSet<string> InstalledFamilies()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var installed = new InstalledFontCollection();
            foreach (FontFamily family in installed.Families)
                set.Add(family.Name);
        }
        catch (Exception)
        {
            // 글꼴 목록을 못 얻으면 검사를 건너뛴다(빈 집합 = 무조건 통과).
        }

        return set;
    }
}
