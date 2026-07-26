using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClipDwg.Render;

/// <summary>
/// 메타파일 좌표계 보정값.
/// <para>
/// GDI+ 메타파일은 단위가 두 군데서 어긋난다.
/// 하나는 프레임 크기를 mm로 줬을 때 쓰이는 해상도, 다른 하나는 기록용 <c>Graphics</c>에
/// <c>PageUnit.Pixel</c>로 넣은 좌표가 실제 기록되는 배율이다. 후자는 디스플레이 배율과
/// 프로세스의 DPI 인식 여부에까지 좌우돼서(테스트 프로세스와 AutoCAD가 서로 다르다)
/// 계산으로 맞힐 수가 없다.
/// </para>
/// <para>
/// 그래서 추측하지 않는다. 크기를 아는 시험용 메타파일을 하나 만들어 물리 치수를 되읽고,
/// 그 안에 크기를 아는 사각형을 그려 실제 기록된 크기를 되재서 두 계수를 모두 실측한다.
/// </para>
/// </summary>
public readonly struct DeviceResolution
{
    /// <summary>mm당 프레임 좌표 단위.</summary>
    public readonly double PerMmX;
    public readonly double PerMmY;

    /// <summary>기록용 Graphics에 넣은 좌표 1이 실제로 기록되는 프레임 좌표 수.</summary>
    public readonly double ContentScaleX;
    public readonly double ContentScaleY;

    public DeviceResolution(double perMmX, double perMmY, double contentScaleX, double contentScaleY)
    {
        PerMmX = perMmX;
        PerMmY = perMmY;
        ContentScaleX = contentScaleX;
        ContentScaleY = contentScaleY;
    }

    /// <summary>mm를 기록용 Graphics에 넣을 좌표로 바꾸는 배율.</summary>
    public double DrawPerMmX => PerMmX / ContentScaleX;

    public double DrawPerMmY => PerMmY / ContentScaleY;

    /// <summary>선 굵기처럼 방향이 없는 값에 쓸 등방 배율.</summary>
    public double DrawPerMmIsotropic => Math.Sqrt(DrawPerMmX * DrawPerMmY);

    private static readonly DeviceResolution Fallback = new(96.0 / 25.4, 96.0 / 25.4, 1.0, 1.0);

    // 디스플레이 구성과 프로세스 DPI 인식은 프로세스 수명 동안 바뀌지 않으므로 한 번만 잰다.
    private static readonly Lazy<DeviceResolution> Cached = new(Measure, isThreadSafe: true);

    public static DeviceResolution Probe() => Cached.Value;

    private const int FrameUnits = 2000;
    private const int FillUnits = 1600;
    private const int StripThickness = 8;

    private static DeviceResolution Measure()
    {
        try
        {
            Metafile probe = CreateProbe(FrameUnits);
            using (probe)
            {
                using (Graphics g = Graphics.FromImage(probe))
                {
                    g.PageUnit = GraphicsUnit.Pixel;
                    g.PageScale = 1f;
                    g.SmoothingMode = SmoothingMode.None;
                    g.FillRectangle(Brushes.Black, 0, 0, FillUnits, FillUnits);
                }

                SizeF physical = probe.PhysicalDimension; // 0.01mm 단위
                double mmX = physical.Width / 100.0;
                double mmY = physical.Height / 100.0;
                if (mmX <= 0 || mmY <= 0 || double.IsNaN(mmX) || double.IsNaN(mmY))
                    return Fallback;

                double contentX = MeasureContentScale(probe, horizontal: true);
                double contentY = MeasureContentScale(probe, horizontal: false);

                return new DeviceResolution(FrameUnits / mmX, FrameUnits / mmY, contentX, contentY);
            }
        }
        catch (Exception)
        {
            return Fallback;
        }
    }

    /// <summary>
    /// 시험용 메타파일을 프레임 크기와 1:1로 래스터라이즈해서 사각형이 실제로 몇 단위를
    /// 차지했는지 잰다. 한 줄(또는 한 칸)만 보면 되므로 얇은 띠로만 그린다.
    /// </summary>
    private static double MeasureContentScale(Metafile probe, bool horizontal)
    {
        int w = horizontal ? FrameUnits : StripThickness;
        int h = horizontal ? StripThickness : FrameUnits;

        using var strip = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(strip))
        {
            g.Clear(Color.White);
            g.DrawImage(probe, new Rectangle(0, 0, FrameUnits, FrameUnits));
        }

        int extent = horizontal ? FindInkExtentX(strip) : FindInkExtentY(strip);
        if (extent <= 0 || extent >= FrameUnits)
            return 1.0;

        return extent / (double)FillUnits;
    }

    private static int FindInkExtentX(Bitmap strip)
    {
        const int row = 2;
        for (int x = strip.Width - 1; x >= 0; x--)
        {
            if (IsInk(strip.GetPixel(x, row)))
                return x + 1;
        }

        return 0;
    }

    private static int FindInkExtentY(Bitmap strip)
    {
        const int column = 2;
        for (int y = strip.Height - 1; y >= 0; y--)
        {
            if (IsInk(strip.GetPixel(column, y)))
                return y + 1;
        }

        return 0;
    }

    private static bool IsInk(Color c) => c.R < 128 && c.G < 128 && c.B < 128;

    private static Metafile CreateProbe(float units)
    {
        using Graphics reference = Graphics.FromHwndInternal(IntPtr.Zero);
        IntPtr hdc = reference.GetHdc();
        try
        {
            return new Metafile(
                hdc, new RectangleF(0, 0, units, units), MetafileFrameUnit.Pixel, EmfType.EmfOnly);
        }
        finally
        {
            reference.ReleaseHdc(hdc);
        }
    }
}
