using System.Drawing.Imaging;

namespace ClipDwg.Render;

/// <summary>렌더 결과와 붙여넣었을 때의 실제 물리 크기.</summary>
public readonly struct RenderResult
{
    public readonly Metafile Metafile;
    public readonly double WidthMm;
    public readonly double HeightMm;

    public RenderResult(Metafile metafile, double widthMm, double heightMm)
    {
        Metafile = metafile;
        WidthMm = widthMm;
        HeightMm = heightMm;
    }
}
