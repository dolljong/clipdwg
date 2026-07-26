using System;
using System.Collections.Generic;
using System.Globalization;
using ClipDwg.Extract;

namespace ClipDwg.Style;

/// <summary>
/// 색 → 선두께(mm) 해석.
/// <para>
/// ByLayer / ByBlock 은 추출 단계에서 이미 실제 색으로 치환되므로 여기서는
/// 트루컬러 규칙 → ACI 규칙 → 기본값 순서만 본다.
/// </para>
/// </summary>
public sealed class ColorWeightMap
{
    private readonly Dictionary<int, double> _byRgb = new();
    private readonly Dictionary<short, double> _byAci = new();
    private readonly double _defaultMm;

    public ColorWeightMap(Profile profile)
    {
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        _defaultMm = profile.DefaultWidthMm;

        foreach (WidthRule rule in profile.Widths)
        {
            if (!string.IsNullOrWhiteSpace(rule.Rgb))
            {
                if (TryParseRgb(rule.Rgb!, out int rgb))
                    _byRgb[rgb] = rule.Mm;
            }
            else if (rule.Aci is >= 1 and <= 255)
            {
                _byAci[(short)rule.Aci] = rule.Mm;
            }
        }
    }

    public double Resolve(IrColor color)
    {
        int rgb = (color.R << 16) | (color.G << 8) | color.B;
        if (_byRgb.TryGetValue(rgb, out double mm))
            return mm;

        if (color.Aci is >= 1 and <= 255 && _byAci.TryGetValue(color.Aci, out mm))
            return mm;

        return _defaultMm;
    }

    /// <summary>문서의 모든 도형에 두께를 채워 넣는다.</summary>
    public void Apply(IrDocument document)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));

        // 같은 색이 반복되는 게 보통이라 마지막 결과를 하나만 기억해도 조회가 크게 줄어든다.
        var cache = new Dictionary<IrColor, double>();
        foreach (IrShape shape in document.Shapes)
        {
            if (!cache.TryGetValue(shape.Color, out double mm))
            {
                mm = Resolve(shape.Color);
                cache[shape.Color] = mm;
            }

            shape.WidthMm = mm;
        }
    }

    /// <summary>"#RRGGBB" 또는 "RRGGBB" 를 0xRRGGBB 로.</summary>
    public static bool TryParseRgb(string text, out int rgb)
    {
        rgb = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string s = text.Trim();
        if (s.StartsWith("#", StringComparison.Ordinal))
            s = s.Substring(1);

        return s.Length == 6
               && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    public static string FormatRgb(int rgb) => "#" + rgb.ToString("X6", CultureInfo.InvariantCulture);
}
