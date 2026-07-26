using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ClipDwg.Style;

/// <summary>
/// 색상 하나에 대한 선두께 규칙.
/// <see cref="Rgb"/>가 있으면 트루컬러 규칙, 없으면 <see cref="Aci"/> 인덱스 규칙이다.
/// </summary>
[DataContract]
public sealed class WidthRule
{
    /// <summary>ACI 인덱스 1~255. 트루컬러 규칙이면 0.</summary>
    [DataMember(Name = "aci", Order = 0, EmitDefaultValue = false)]
    public int Aci { get; set; }

    /// <summary>"#RRGGBB" 형식. ACI 규칙이면 null.</summary>
    [DataMember(Name = "rgb", Order = 1, EmitDefaultValue = false)]
    public string? Rgb { get; set; }

    /// <summary>선두께(mm). 0이면 hairline.</summary>
    [DataMember(Name = "mm", Order = 2)]
    public double Mm { get; set; }
}

/// <summary>SHX 글꼴을 어떤 TrueType 글꼴로 바꿔 그릴지.</summary>
[DataContract]
public sealed class FontSubstitute
{
    /// <summary>SHX 파일 이름. 확장자는 있어도 없어도 된다.</summary>
    [DataMember(Name = "shx", Order = 0)]
    public string? Shx { get; set; }

    [DataMember(Name = "font", Order = 1)]
    public string? Font { get; set; }
}

[DataContract]
public sealed class Profile
{
    [DataMember(Name = "name", Order = 0)]
    public string Name { get; set; } = "default";

    /// <summary>도면 단위 1이 몇 mm인가.</summary>
    [DataMember(Name = "mmPerDrawingUnit", Order = 1)]
    public double MmPerDrawingUnit { get; set; } = 1.0;

    [DataMember(Name = "outputScale", Order = 2)]
    public double OutputScale { get; set; } = 1.0;

    /// <summary>도형 바깥 여백(mm). 너무 좁으면 축소 출력 시 가장자리 선이 사라진다.</summary>
    [DataMember(Name = "marginMm", Order = 3)]
    public double MarginMm { get; set; } = 1.0;

    /// <summary>규칙에 걸리지 않는 색에 쓸 두께(mm).</summary>
    [DataMember(Name = "defaultWidthMm", Order = 4)]
    public double DefaultWidthMm { get; set; } = 0.25;

    /// <summary>hairline을 실제로 그릴 때의 최소 두께(mm).</summary>
    [DataMember(Name = "minWidthMm", Order = 5)]
    public double MinWidthMm { get; set; } = 0.05;

    /// <summary>흰색을 검정으로. 흰 배경 문서에 붙일 때 사실상 필수.</summary>
    [DataMember(Name = "whiteToBlack", Order = 6)]
    public bool WhiteToBlack { get; set; } = true;

    [DataMember(Name = "forceBlack", Order = 7)]
    public bool ForceBlack { get; set; }

    [DataMember(Name = "widths", Order = 8)]
    public List<WidthRule> Widths { get; set; } = new();

    /// <summary>대체 규칙이 없는 SHX 글꼴에 쓸 TrueType 글꼴.</summary>
    [DataMember(Name = "defaultShxFont", Order = 9)]
    public string DefaultShxFont { get; set; } = "Arial";

    [DataMember(Name = "shxSubstitutes", Order = 10)]
    public List<FontSubstitute> ShxSubstitutes { get; set; } = new();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        // DataContractJsonSerializer는 빠진 항목을 타입 기본값(0/false)으로 두므로
        // 역직렬화 전에 우리 기본값을 먼저 넣어 둔다.
        Name = "default";
        MmPerDrawingUnit = 1.0;
        OutputScale = 1.0;
        MarginMm = 1.0;
        DefaultWidthMm = 0.25;
        MinWidthMm = 0.05;
        WhiteToBlack = true;
        ForceBlack = false;
        Widths = new List<WidthRule>();
        DefaultShxFont = "Arial";
        ShxSubstitutes = new List<FontSubstitute>();
    }

    public Profile Clone() => new()
    {
        Name = Name,
        MmPerDrawingUnit = MmPerDrawingUnit,
        OutputScale = OutputScale,
        MarginMm = MarginMm,
        DefaultWidthMm = DefaultWidthMm,
        MinWidthMm = MinWidthMm,
        WhiteToBlack = WhiteToBlack,
        ForceBlack = ForceBlack,
        Widths = Widths.ConvertAll(w => new WidthRule { Aci = w.Aci, Rgb = w.Rgb, Mm = w.Mm }),
        DefaultShxFont = DefaultShxFont,
        ShxSubstitutes = ShxSubstitutes.ConvertAll(s => new FontSubstitute { Shx = s.Shx, Font = s.Font }),
    };
}

[DataContract]
public sealed class SettingsFile
{
    [DataMember(Name = "activeProfile", Order = 0)]
    public string ActiveProfile { get; set; } = "default";

    [DataMember(Name = "profiles", Order = 1)]
    public List<Profile> Profiles { get; set; } = new();

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        ActiveProfile = "default";
        Profiles = new List<Profile>();
    }

    public Profile GetActiveProfile()
    {
        foreach (Profile p in Profiles)
        {
            if (string.Equals(p.Name, ActiveProfile, System.StringComparison.OrdinalIgnoreCase))
                return p;
        }

        return Profiles.Count > 0 ? Profiles[0] : CreateDefault().Profiles[0];
    }

    /// <summary>
    /// 처음 실행할 때 깔아 줄 기본 설정.
    /// 색상별 두께는 현장마다 관례가 달라서, 흔히 쓰이는 값을 출발점으로만 넣어 둔다.
    /// CLIPDWGCFG 에서 고쳐 쓰면 된다.
    /// </summary>
    public static SettingsFile CreateDefault()
    {
        var profile = new Profile
        {
            Name = "default",
            Widths =
            {
                new WidthRule { Aci = 1, Mm = 0.13 }, // 빨강
                new WidthRule { Aci = 2, Mm = 0.15 }, // 노랑
                new WidthRule { Aci = 3, Mm = 0.18 }, // 초록
                new WidthRule { Aci = 4, Mm = 0.20 }, // 청록
                new WidthRule { Aci = 5, Mm = 0.25 }, // 파랑
                new WidthRule { Aci = 6, Mm = 0.30 }, // 자홍
                new WidthRule { Aci = 7, Mm = 0.35 }, // 흰색/검정
                new WidthRule { Aci = 8, Mm = 0.13 }, // 진회색
                new WidthRule { Aci = 9, Mm = 0.13 }, // 연회색
            },
            DefaultShxFont = "Arial",
            ShxSubstitutes =
            {
                // 영문 SHX
                new FontSubstitute { Shx = "txt", Font = "Arial" },
                new FontSubstitute { Shx = "simplex", Font = "Arial" },
                new FontSubstitute { Shx = "romans", Font = "Arial" },
                new FontSubstitute { Shx = "romand", Font = "Arial" },
                new FontSubstitute { Shx = "monotxt", Font = "Consolas" },
                new FontSubstitute { Shx = "isocp", Font = "Arial" },
                // 한글 큰글꼴
                new FontSubstitute { Shx = "whgtxt", Font = "맑은 고딕" },
                new FontSubstitute { Shx = "whgdtxt", Font = "맑은 고딕" },
                new FontSubstitute { Shx = "ghs", Font = "맑은 고딕" },
                new FontSubstitute { Shx = "hygt", Font = "맑은 고딕" },
                new FontSubstitute { Shx = "extfont", Font = "맑은 고딕" },
                new FontSubstitute { Shx = "extfont2", Font = "맑은 고딕" },
            },
        };

        return new SettingsFile { ActiveProfile = "default", Profiles = { profile } };
    }
}
