using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ClipDwg.Extract;

namespace ClipDwg.Render;

/// <summary>
/// IR을 EMF(Enhanced Metafile)로 그린다.
/// <para>
/// EmfPlus가 아니라 순수 <see cref="EmfType.EmfOnly"/>로 기록한다. Office에서 붙여넣은 그림을
/// 그룹해제해 편집할 때 EMF+ 레코드는 깨지는 경우가 있기 때문이다.
/// </para>
/// <para>
/// 좌표는 mm가 아니라 메타파일의 장치 단위로 직접 넣는다. 자세한 이유는
/// <see cref="DeviceResolution"/> 참고.
/// </para>
/// </summary>
public static class EmfRenderer
{
    /// <summary>
    /// IR을 EMF로 그린다. 반환된 <see cref="Metafile"/>의 소유권은 호출자에게 있다.
    /// </summary>
    public static RenderResult Render(IrDocument document, RenderOptions options)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (document.Shapes.Count == 0)
            throw new InvalidOperationException("그릴 도형이 없습니다.");

        Bounds bounds = ComputeBounds(document);
        if (bounds.IsEmpty)
            throw new InvalidOperationException("도형의 경계를 계산할 수 없습니다.");

        double scale = options.UnitScale;
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "축척은 0보다 커야 합니다.");

        // 경계에 걸친 선이 잘리지 않도록 가장 굵은 선의 절반을 여백에 더한다.
        double maxWidth = options.DefaultWidthMm;
        foreach (IrShape shape in document.Shapes)
        {
            // 텍스트와 채움 도형은 획으로 그리지 않으므로 여백 계산에서 뺀다.
            if (shape is not (IrText or IrFilledPolygon))
                maxWidth = Math.Max(maxWidth, EffectiveWidth(shape, options));
        }

        double margin = options.MarginMm + (maxWidth / 2);

        double widthMm = Math.Max(options.MinExtentMm, (bounds.Width * scale) + (margin * 2));
        double heightMm = Math.Max(options.MinExtentMm, (bounds.Height * scale) + (margin * 2));

        // 프레임과 내용을 같은 단위로 지정한다. 그래야 어긋날 여지가 없다.
        DeviceResolution resolution = DeviceResolution.Probe();

        // 프레임은 정수 장치 단위로 잘리고 좌표도 정수로 반올림되므로, 올림 + 1단위를 더해
        // 오른쪽·아래에서 여백을 잃지 않게 한다. (이걸 빼면 우측 세로선이 잘린다.)
        var frame = new RectangleF(
            0, 0,
            (float)(Math.Ceiling(widthMm * resolution.PerMmX) + 1),
            (float)(Math.Ceiling(heightMm * resolution.PerMmY) + 1));

        Metafile metafile = CreateMetafile(frame);

        try
        {
            var transform = new PlaneTransform(bounds, scale, margin, resolution);

            using Graphics g = Graphics.FromImage(metafile);
            g.PageUnit = GraphicsUnit.Pixel;
            g.PageScale = 1f;
            g.SmoothingMode = SmoothingMode.HighQuality;
            DrawShapes(g, document, options, transform, resolution);
        }
        catch
        {
            metafile.Dispose();
            throw;
        }

        // 계산값이 아니라 메타파일에 실제로 기록된 물리 치수를 돌려준다.
        SizeF physical = metafile.PhysicalDimension; // 0.01mm 단위
        return physical.Width > 0 && physical.Height > 0
            ? new RenderResult(metafile, physical.Width / 100.0, physical.Height / 100.0)
            : new RenderResult(metafile, widthMm, heightMm);
    }

    /// <summary>
    /// IR이 신고한 범위에, 텍스트가 실제로 그려질 범위를 합집합으로 더한다.
    /// 대체 글꼴은 원본보다 넓게 그려질 수 있어서 이걸 빼면 오른쪽이 잘린다.
    /// </summary>
    private static Bounds ComputeBounds(IrDocument document)
    {
        Bounds bounds = document.ComputeBounds();

        bool hasText = document.Shapes.Exists(s => s is IrText);
        if (!hasText)
            return bounds;

        using var scratch = new Bitmap(1, 1);
        using Graphics measuring = Graphics.FromImage(scratch);
        foreach (IrShape shape in document.Shapes)
        {
            if (shape is IrText text)
                TextMeasurer.AccumulateRenderedBounds(measuring, text, ref bounds);
        }

        return bounds;
    }

    private static Metafile CreateMetafile(RectangleF frameUnits)
    {
        // Metafile 생성에는 해상도 기준이 될 참조 DC가 필요하다. 화면 DC를 쓴다.
        using Graphics reference = Graphics.FromHwndInternal(IntPtr.Zero);
        IntPtr hdc = reference.GetHdc();
        try
        {
            return new Metafile(hdc, frameUnits, MetafileFrameUnit.Pixel, EmfType.EmfOnly);
        }
        finally
        {
            reference.ReleaseHdc(hdc);
        }
    }

    private static void DrawShapes(
        Graphics g, IrDocument document, RenderOptions options, PlaneTransform t, DeviceResolution resolution)
    {
        double penScale = resolution.DrawPerMmIsotropic;

        // 같은 펜을 쓰는 도형을 하나의 경로로 모아 상태 전환 횟수를 색상 종류 수까지 줄인다.
        var groups = new Dictionary<PenKey, GraphicsPath>();
        try
        {
            // 채움 도형(치수 화살촉 등)을 먼저 깔고 그 위에 선을 긋는다.
            DrawFills(g, document, options, t);

            foreach (IrShape shape in document.Shapes)
            {
                if (shape is IrText or IrFilledPolygon)
                    continue; // 텍스트와 채움 도형은 따로 그린다

                var key = new PenKey(
                    ResolveArgb(shape.Color, options),
                    (float)(EffectiveWidth(shape, options) * penScale),
                    // 폴리라인의 폭은 끝을 평평하게 자른다. 선가중치만 둥근 끝이다.
                    flatCaps: shape.IntrinsicWidth > 0);

                if (!groups.TryGetValue(key, out GraphicsPath? path))
                {
                    path = new GraphicsPath();
                    groups[key] = path;
                }

                switch (shape)
                {
                    case IrPath p:
                        AddPath(path, p, t);
                        break;
                    case IrCircle c:
                        path.StartFigure();
                        path.AddEllipse(t.CircleRect(c.Center, c.Radius));
                        path.CloseFigure();
                        break;
                }
            }

            // 텍스트는 획이 아니라 채움이고 펜 묶음과 상관없으므로 따로 그린다.
            // 선보다 나중에 그려 겹칠 때 글자가 위로 오게 한다.

            foreach (KeyValuePair<PenKey, GraphicsPath> group in groups)
            {
                if (group.Value.PointCount == 0)
                    continue;

                LineCap cap = group.Key.FlatCaps ? LineCap.Flat : LineCap.Round;
                using var pen = new Pen(Color.FromArgb(group.Key.Argb), group.Key.WidthDevice)
                {
                    // AutoCAD의 선가중치는 끝단·모서리가 둥글다.
                    StartCap = cap,
                    EndCap = cap,
                    LineJoin = LineJoin.Round,
                };
                g.DrawPath(pen, group.Value);
            }

            DrawTexts(g, document, options, t);
        }
        finally
        {
            foreach (GraphicsPath path in groups.Values)
                path.Dispose();
        }
    }

    /// <summary>채워진 다각형을 색깔별로 묶어 칠한다.</summary>
    private static void DrawFills(Graphics g, IrDocument document, RenderOptions options, PlaneTransform t)
    {
        var groups = new Dictionary<int, GraphicsPath>();
        try
        {
            foreach (IrShape shape in document.Shapes)
            {
                if (shape is not IrFilledPolygon poly || poly.Points.Count < 3)
                    continue;

                int argb = ResolveArgb(poly.Color, options);
                if (!groups.TryGetValue(argb, out GraphicsPath? path))
                {
                    path = new GraphicsPath();
                    groups[argb] = path;
                }

                var points = new PointF[poly.Points.Count];
                for (int i = 0; i < points.Length; i++)
                    points[i] = t.Map(poly.Points[i]);

                path.StartFigure();
                path.AddPolygon(points);
                path.CloseFigure();
            }

            foreach (KeyValuePair<int, GraphicsPath> group in groups)
            {
                if (group.Value.PointCount == 0)
                    continue;

                using var brush = new SolidBrush(Color.FromArgb(group.Key));
                g.FillPath(brush, group.Value);
            }
        }
        finally
        {
            foreach (GraphicsPath path in groups.Values)
                path.Dispose();
        }
    }

    /// <summary>
    /// 텍스트는 윤곽선으로 분해하지 않고 EMF의 글자 레코드로 넣는다.
    /// 그래야 Word에 붙여넣은 뒤에도 글자로 남아 선택·편집·검색이 된다.
    /// </summary>
    private static void DrawTexts(Graphics g, IrDocument document, RenderOptions options, PlaneTransform t)
    {
        var brushes = new Dictionary<int, SolidBrush>();
        Matrix original = g.Transform;

        try
        {
            foreach (IrShape shape in document.Shapes)
            {
                if (shape is not IrText text || string.IsNullOrEmpty(text.Text) || text.Height <= 0)
                    continue;

                int argb = ResolveArgb(text.Color, options);
                if (!brushes.TryGetValue(argb, out SolidBrush? brush))
                {
                    brush = new SolidBrush(Color.FromArgb(argb));
                    brushes[argb] = brush;
                }

                DrawText(g, text, t, brush);
            }
        }
        finally
        {
            g.Transform = original;
            original.Dispose();
            foreach (SolidBrush brush in brushes.Values)
                brush.Dispose();
        }
    }

    private static void DrawText(Graphics g, IrText text, PlaneTransform t, Brush brush)
    {
        FontStyle style = FontStyle.Regular;
        if (text.Bold)
            style |= FontStyle.Bold;
        if (text.Italic)
            style |= FontStyle.Italic;

        // AutoCAD의 글자 높이는 대문자 높이 기준이고 GDI의 크기는 em 크기 기준이라
        // 어센트 비율로 환산한다. 이렇게 해야 붙여넣은 글자 크기가 도면과 얼추 맞는다.
        double deviceHeight = text.Height * t.ScaleY;
        using FontFamily family = TextMeasurer.CreateFamily(text.FontFamily);
        int ascent = family.GetCellAscent(style);
        int emHeight = family.GetEmHeight(style);
        var emSize = (float)(ascent > 0 ? deviceHeight * emHeight / ascent : deviceHeight);
        if (emSize <= 0.01f)
            return;

        using var font = new Font(family, emSize, style, GraphicsUnit.Pixel);

        float ascentPx = emSize * ascent / emHeight;
        float descentPx = emSize * family.GetCellDescent(style) / emHeight;

        // 장치 픽셀이 정사각형이 아닐 수 있으므로 가로 배율 차이를 폭계수에 흡수시킨다.
        var widthFactor = (float)(text.WidthFactor * t.ScaleX / t.ScaleY);
        if (widthFactor <= 0)
            widthFactor = 1f;

        StringFormat format = StringFormat.GenericTypographic;
        float advance = g.MeasureString(text.Text, font, int.MaxValue, format).Width;

        float dx = text.HAlign switch
        {
            TextHAlign.Center => -advance / 2f,
            TextHAlign.Right => -advance,
            _ => 0f,
        };

        float dy = text.VAlign switch
        {
            TextVAlign.Top => 0f,
            TextVAlign.Middle => -(ascentPx + descentPx) / 2f,
            TextVAlign.Bottom => -(ascentPx + descentPx),
            _ => -ascentPx, // Baseline
        };

        PointF anchor = t.Map(text.Anchor);

        using var m = new Matrix();
        m.Translate(anchor.X, anchor.Y);
        m.Rotate((float)(-text.Rotation * 180.0 / Math.PI)); // 장치 Y가 아래로 향하므로 부호를 뒤집는다
        if (Math.Abs(text.Oblique) > 1e-9)
            m.Shear((float)-Math.Tan(text.Oblique), 0f);
        m.Scale(widthFactor, 1f);

        g.Transform = m;
        g.DrawString(text.Text, font, brush, dx, dy, format);
    }


    private static void AddPath(GraphicsPath target, IrPath path, PlaneTransform t)
    {
        if (path.Segments.Count == 0)
            return;

        target.StartFigure();

        Pt cursor = path.Start;
        foreach (IrSegment seg in path.Segments)
        {
            if (seg.Kind == SegKind.Arc)
                AddArcAsBeziers(target, seg, t);
            else
                target.AddLine(t.Map(cursor), t.Map(seg.End));

            cursor = seg.End;
        }

        if (path.Closed)
            target.CloseFigure();
    }

    /// <summary>
    /// 원호를 90° 이하 3차 베지에로 나눠 넣는다.
    /// <para>
    /// <c>GraphicsPath.AddArc</c>를 쓰지 않는 이유: 메타파일의 장치 픽셀이 정사각형이 아닐 수 있는데
    /// (모니터의 물리 가로/세로 해상도가 다르다) 그때 AddArc의 각도는 실제 각이 아니라
    /// 타원의 매개변수각으로 해석되어 시작·끝점이 어긋난다. 베지에 제어점은 아핀변환에
    /// 정확히 따라가므로 이 문제가 없다. 근사 오차는 반지름의 0.03% 미만이다.
    /// </para>
    /// </summary>
    private static void AddArcAsBeziers(GraphicsPath target, IrSegment seg, PlaneTransform t)
    {
        double sweep = seg.SweepAngle;
        int count = Math.Max(1, (int)Math.Ceiling((Math.Abs(sweep) / (Math.PI / 2)) - 1e-9));
        double step = sweep / count;
        double k = 4.0 / 3.0 * Math.Tan(step / 4);
        double a0 = seg.StartAngle;

        for (int i = 0; i < count; i++)
        {
            double a1 = a0 + step;

            Pt p0 = GeomUtil.PointOnCircle(seg.Center, seg.Radius, a0);
            Pt p3 = GeomUtil.PointOnCircle(seg.Center, seg.Radius, a1);

            double kr = k * seg.Radius;
            var p1 = new Pt(p0.X - (kr * Math.Sin(a0)), p0.Y + (kr * Math.Cos(a0)));
            var p2 = new Pt(p3.X + (kr * Math.Sin(a1)), p3.Y - (kr * Math.Cos(a1)));

            target.AddBezier(t.Map(p0), t.Map(p1), t.Map(p2), t.Map(p3));
            a0 = a1;
        }
    }

    private static double EffectiveWidth(IrShape shape, RenderOptions options)
    {
        // 폴리라인이 가진 폭은 도형의 크기이므로 색상 규칙이나 축척과 함께 움직여야 한다.
        if (shape.IntrinsicWidth > 0)
            return Math.Max(shape.IntrinsicWidth * options.UnitScale, options.MinWidthMm);

        double w = shape.WidthMm > 0 ? shape.WidthMm : options.DefaultWidthMm;
        return Math.Max(w, options.MinWidthMm);
    }

    private static int ResolveArgb(IrColor color, RenderOptions options)
    {
        if (options.ForceBlack)
            return unchecked((int)0xFF000000);

        byte r = color.R, g = color.G, b = color.B;
        if (options.WhiteToBlack && r == 255 && g == 255 && b == 255)
            return unchecked((int)0xFF000000);

        return unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b));
    }

    private readonly struct PenKey : IEquatable<PenKey>
    {
        public readonly int Argb;
        public readonly float WidthDevice;
        public readonly bool FlatCaps;

        public PenKey(int argb, float widthDevice, bool flatCaps)
        {
            Argb = argb;
            WidthDevice = widthDevice;
            FlatCaps = flatCaps;
        }

        public bool Equals(PenKey other) =>
            Argb == other.Argb && WidthDevice.Equals(other.WidthDevice) && FlatCaps == other.FlatCaps;

        public override bool Equals(object? obj) => obj is PenKey other && Equals(other);

        public override int GetHashCode() =>
            ((Argb * 397) ^ WidthDevice.GetHashCode()) * 397 ^ (FlatCaps ? 1 : 0);
    }

    /// <summary>
    /// 도면 좌표 → 메타파일 장치 좌표. 좌표값이 클 수 있으므로(도면 원점이 수십만 떨어진 경우)
    /// 이동·축척을 GDI+ 행렬(float)에 맡기지 않고 double로 직접 계산한다.
    /// </summary>
    private readonly struct PlaneTransform
    {
        private readonly double _minX;
        private readonly double _maxY;
        private readonly double _scaleX;
        private readonly double _scaleY;
        private readonly double _marginX;
        private readonly double _marginY;

        public PlaneTransform(Bounds bounds, double unitScale, double marginMm, DeviceResolution resolution)
        {
            _minX = bounds.MinX;
            _maxY = bounds.MaxY;
            _scaleX = unitScale * resolution.DrawPerMmX;
            _scaleY = unitScale * resolution.DrawPerMmY;
            _marginX = marginMm * resolution.DrawPerMmX;
            _marginY = marginMm * resolution.DrawPerMmY;
        }

        /// <summary>도면 단위 1당 장치 좌표 수.</summary>
        public double ScaleX => _scaleX;

        public double ScaleY => _scaleY;

        public PointF Map(Pt p) => new(
            (float)(((p.X - _minX) * _scaleX) + _marginX),
            (float)(((_maxY - p.Y) * _scaleY) + _marginY));

        /// <summary>원이 놓인 외접 사각형(장치 좌표). 장치 픽셀이 비정사각이면 타원이 된다.</summary>
        public RectangleF CircleRect(Pt center, double radius)
        {
            PointF c = Map(center);
            var rx = (float)(radius * _scaleX);
            var ry = (float)(radius * _scaleY);
            return new RectangleF(c.X - rx, c.Y - ry, rx * 2, ry * 2);
        }
    }
}
