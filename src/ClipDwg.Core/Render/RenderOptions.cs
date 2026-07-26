using ClipDwg.Style;

namespace ClipDwg.Render;

public sealed class RenderOptions
{
    /// <summary>도면 단위 1이 몇 mm인가. 도면이 mm 단위면 1.0.</summary>
    public double MmPerDrawingUnit { get; set; } = 1.0;

    /// <summary>추가 축척. 0.5면 절반 크기로 붙는다.</summary>
    public double OutputScale { get; set; } = 1.0;

    /// <summary>
    /// 도형 바깥 여백(mm). 선 굵기의 절반은 여기에 더해 자동 확보된다.
    /// <para>
    /// 0.5mm로는 부족하다. 붙여넣은 그림을 문서 폭에 맞춰 축소하면 가장자리 선이
    /// 1픽셀 미만으로 얇아져 사라지기 때문이다. 실제로 우측 세로선이 안 보이는 문제가 있었다.
    /// </para>
    /// </summary>
    public double MarginMm { get; set; } = 1.0;

    /// <summary>색상별 두께가 지정되지 않은 도형에 쓸 두께(mm).</summary>
    public double DefaultWidthMm { get; set; } = 0.25;

    /// <summary>두께 0(hairline)을 실제로 그릴 때 쓸 최소 두께(mm).</summary>
    public double MinWidthMm { get; set; } = 0.05;

    /// <summary>흰색(ACI 7 등)을 검정으로 바꾼다. 흰 배경 문서에 붙일 때 사실상 필수.</summary>
    public bool WhiteToBlack { get; set; } = true;

    /// <summary>모든 선을 검정으로 출력한다.</summary>
    public bool ForceBlack { get; set; }

    /// <summary>결과 EMF의 최소 크기(mm). 한 점·수평선처럼 한쪽 폭이 0인 경우를 막는다.</summary>
    public double MinExtentMm { get; set; } = 1.0;

    public double UnitScale => MmPerDrawingUnit * OutputScale;

    public static RenderOptions FromProfile(Profile profile) => new()
    {
        MmPerDrawingUnit = profile.MmPerDrawingUnit,
        OutputScale = profile.OutputScale,
        MarginMm = profile.MarginMm,
        DefaultWidthMm = profile.DefaultWidthMm,
        MinWidthMm = profile.MinWidthMm,
        WhiteToBlack = profile.WhiteToBlack,
        ForceBlack = profile.ForceBlack,
    };
}
