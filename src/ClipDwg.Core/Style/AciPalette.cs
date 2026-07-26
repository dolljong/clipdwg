using System.Drawing;

namespace ClipDwg.Style;

/// <summary>
/// 옵션창 색상 견본용 ACI 팔레트.
/// <para>
/// 도면에서 뽑아낸 색은 이미 실제 RGB를 들고 있으므로 렌더에는 쓰이지 않는다.
/// 여기 있는 값은 순전히 사용자에게 "1번이 빨강" 정도를 보여주기 위한 것이라
/// 표준으로 고정된 1~9번과 250~255번 회색 계열만 담았다.
/// </para>
/// </summary>
public static class AciPalette
{
    public static bool TryGetColor(int aci, out Color color)
    {
        switch (aci)
        {
            case 1: color = Color.FromArgb(255, 0, 0); return true;
            case 2: color = Color.FromArgb(255, 255, 0); return true;
            case 3: color = Color.FromArgb(0, 255, 0); return true;
            case 4: color = Color.FromArgb(0, 255, 255); return true;
            case 5: color = Color.FromArgb(0, 0, 255); return true;
            case 6: color = Color.FromArgb(255, 0, 255); return true;
            case 7: color = Color.FromArgb(255, 255, 255); return true;
            case 8: color = Color.FromArgb(128, 128, 128); return true;
            case 9: color = Color.FromArgb(192, 192, 192); return true;
            case 250: color = Color.FromArgb(51, 51, 51); return true;
            case 251: color = Color.FromArgb(91, 91, 91); return true;
            case 252: color = Color.FromArgb(132, 132, 132); return true;
            case 253: color = Color.FromArgb(173, 173, 173); return true;
            case 254: color = Color.FromArgb(214, 214, 214); return true;
            case 255: color = Color.FromArgb(255, 255, 255); return true;
            default: color = Color.Empty; return false;
        }
    }

    public static string DescribeAci(int aci) => aci switch
    {
        1 => "빨강",
        2 => "노랑",
        3 => "초록",
        4 => "청록",
        5 => "파랑",
        6 => "자홍",
        7 => "흰색/검정",
        8 => "진회색",
        9 => "연회색",
        _ => string.Empty,
    };
}
