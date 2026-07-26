using System;
using System.Drawing;
using System.Drawing.Text;
using ClipDwg.Extract;

namespace ClipDwg.Render;

/// <summary>
/// 텍스트가 실제로 그려질 범위를 도면 좌표로 계산한다.
/// <para>
/// AutoCAD가 준 <see cref="IrText.Extents"/>는 원본 글꼴 기준이다. SHX를 TrueType으로
/// 대체하거나 대문자높이→em 환산에 오차가 있으면 실제 렌더 폭이 그보다 넓어져서
/// 프레임 오른쪽에서 글자가 잘린다. 그래서 신고된 범위와 실측 범위를 합집합으로 쓴다.
/// </para>
/// </summary>
public static class TextMeasurer
{
    /// <summary>측정 기준으로 쓸 em 크기. 결과는 비율로만 쓰이므로 값 자체는 중요하지 않다.</summary>
    private const float NominalEm = 100f;

    /// <summary>
    /// <paramref name="text"/>가 그려질 네 모서리를 <paramref name="bounds"/>에 더한다.
    /// 회전·기울기·폭계수·정렬을 모두 렌더러와 같은 방식으로 반영한다.
    /// </summary>
    public static void AccumulateRenderedBounds(Graphics measuringGraphics, IrText text, ref Bounds bounds)
    {
        if (string.IsNullOrEmpty(text.Text) || text.Height <= 0)
            return;

        FontStyle style = FontStyle.Regular;
        if (text.Bold)
            style |= FontStyle.Bold;
        if (text.Italic)
            style |= FontStyle.Italic;

        using FontFamily family = CreateFamily(text.FontFamily);
        int em = family.GetEmHeight(style);
        int ascentUnits = family.GetCellAscent(style);
        if (em <= 0 || ascentUnits <= 0)
            return;

        using var font = new Font(family, NominalEm, style, GraphicsUnit.Pixel);

        float ascentPx = NominalEm * ascentUnits / em;
        float descentPx = NominalEm * family.GetCellDescent(style) / em;
        float advancePx = measuringGraphics
            .MeasureString(text.Text, font, int.MaxValue, StringFormat.GenericTypographic).Width;

        // 도면 단위 환산: AutoCAD의 글자 높이가 어센트에 해당한다(렌더러와 같은 규칙).
        double perPx = text.Height / ascentPx;
        var widthFactor = (float)(text.WidthFactor > 0 ? text.WidthFactor : 1.0);

        // 렌더러가 DrawString 에 넘기는 것과 같은 오프셋(장치 좌표, Y 아래 방향)
        float dx = text.HAlign switch
        {
            TextHAlign.Center => -advancePx / 2f,
            TextHAlign.Right => -advancePx,
            _ => 0f,
        };

        float dy = text.VAlign switch
        {
            TextVAlign.Top => 0f,
            TextVAlign.Middle => -(ascentPx + descentPx) / 2f,
            TextVAlign.Bottom => -(ascentPx + descentPx),
            _ => -ascentPx,
        };

        float x0 = dx * widthFactor;
        float x1 = (dx + advancePx) * widthFactor;
        float y0 = dy;
        float y1 = dy + ascentPx + descentPx;

        double shear = -Math.Tan(text.Oblique);
        double cos = Math.Cos(text.Rotation);
        double sin = Math.Sin(text.Rotation);

        AddCorner(ref bounds, text, x0, y0, shear, perPx, cos, sin);
        AddCorner(ref bounds, text, x1, y0, shear, perPx, cos, sin);
        AddCorner(ref bounds, text, x1, y1, shear, perPx, cos, sin);
        AddCorner(ref bounds, text, x0, y1, shear, perPx, cos, sin);
    }

    private static void AddCorner(
        ref Bounds bounds, IrText text, double x, double y, double shear, double perPx, double cos, double sin)
    {
        // 기울기(장치 좌표에서의 전단)
        x += shear * y;

        // 장치(Y 아래) → 도면(Y 위) 단위
        double dxu = x * perPx;
        double dyu = -y * perPx;

        // 회전(도면 좌표에서 반시계)
        double rx = (dxu * cos) - (dyu * sin);
        double ry = (dxu * sin) + (dyu * cos);

        bounds.Add(new Pt(text.Anchor.X + rx, text.Anchor.Y + ry));
    }

    /// <summary>설치되지 않은 글꼴이면 GDI+가 예외를 던지므로 기본 글꼴로 물러선다.</summary>
    public static FontFamily CreateFamily(string name)
    {
        try
        {
            return new FontFamily(name);
        }
        catch (ArgumentException)
        {
            return new FontFamily(GenericFontFamilies.SansSerif);
        }
    }
}
