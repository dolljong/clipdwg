using System;
using System.Runtime.InteropServices;
using System.Threading;
using ClipDwg.Clipboard;
using ClipDwg.Extract;
using ClipDwg.Render;
using Xunit;

namespace ClipDwg.Tests;

/// <summary>
/// 주의: 이 테스트는 실제 시스템 클립보드를 덮어쓴다.
/// <c>--filter "Category!=Clipboard"</c> 로 제외할 수 있다.
/// </summary>
[Trait("Category", "Clipboard")]
public class ClipboardWriterTests
{
    private const uint CF_ENHMETAFILE = 14;

    [Fact]
    public void SetMetafile_PlacesReadableEmfOnClipboard()
    {
        Result r = RunOnStaThread(() =>
        {
            var doc = new IrDocument();
            var path = new IrPath { Color = new IrColor(1, 255, 0, 0), Start = new Pt(0, 0), Closed = true };
            path.Segments.Add(IrSegment.Line(new Pt(50, 0)));
            path.Segments.Add(IrSegment.Line(new Pt(50, 30)));
            path.Segments.Add(IrSegment.Line(new Pt(0, 30)));
            path.Segments.Add(IrSegment.Line(new Pt(0, 0)));
            doc.Shapes.Add(path);

            RenderResult rendered = EmfRenderer.Render(doc, new RenderOptions());

            // 성공하면 핸들 소유권이 클립보드로 넘어간다. Dispose 하지 않는다.
            ClipboardWriter.SetMetafile(rendered.Metafile, IntPtr.Zero);

            var result = new Result { Available = IsClipboardFormatAvailable(CF_ENHMETAFILE) };
            result.BytesBeforeGc = ReadClipboardEmfSize();

            // Metafile 관리 객체가 수거되어도 클립보드의 핸들은 살아 있어야 한다.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            result.BytesAfterGc = ReadClipboardEmfSize();

            return result;
        });

        Assert.Null(r.Error);
        Assert.True(r.Available, "클립보드에 CF_ENHMETAFILE 포맷이 있어야 한다");
        Assert.True(r.BytesBeforeGc > 0, "클립보드의 EMF를 읽을 수 있어야 한다");
        Assert.True(r.BytesAfterGc > 0,
            $"GC 이후에도 EMF가 살아 있어야 한다 (핸들 소유권 이전 실패). 이전={r.BytesBeforeGc}, 이후={r.BytesAfterGc}");
    }

    /// <summary>클립보드는 다른 프로세스(클립보드 기록 등)와 경합하므로 몇 번 다시 시도한다.</summary>
    private static uint ReadClipboardEmfSize()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(20);
                continue;
            }

            try
            {
                IntPtr h = GetClipboardData(CF_ENHMETAFILE);
                if (h != IntPtr.Zero)
                {
                    uint size = GetEnhMetaFileBits(h, 0, null);
                    if (size > 0)
                        return size;
                }
            }
            finally
            {
                CloseClipboard();
            }

            Thread.Sleep(20);
        }

        return 0;
    }

    private sealed class Result
    {
        public bool Available;
        public uint BytesBeforeGc;
        public uint BytesAfterGc;
        public Exception? Error;
    }

    /// <summary>클립보드 API는 STA 스레드에서만 안전하다. xunit은 기본이 MTA라 따로 띄운다.</summary>
    private static Result RunOnStaThread(Func<Result> action)
    {
        Result result = new();
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                result.Error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("gdi32.dll")]
    private static extern uint GetEnhMetaFileBits(IntPtr hemf, uint nSize, byte[]? lpBuffer);
}
