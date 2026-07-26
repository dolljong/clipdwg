using System;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClipDwg.Clipboard;

/// <summary>
/// EMF를 클립보드에 올린다.
/// <para>
/// .NET의 <c>System.Windows.Forms.Clipboard</c>는 메타파일을 넣으면 비트맵으로 떨어지거나
/// 아예 붙지 않는 경우가 있어 Win32 API를 직접 쓴다.
/// </para>
/// </summary>
public static class ClipboardWriter
{
    private const uint CF_ENHMETAFILE = 14;

    private const int OpenAttempts = 10;
    private const int OpenRetryDelayMs = 20;

    /// <summary>
    /// 메타파일을 클립보드에 올린다. 성공하면 <paramref name="metafile"/>의 네이티브 핸들
    /// 소유권이 시스템으로 넘어가므로 호출자는 더 이상 이 객체를 쓰면 안 된다.
    /// </summary>
    public static void SetMetafile(Metafile metafile, IntPtr ownerWindow)
    {
        if (metafile is null)
            throw new ArgumentNullException(nameof(metafile));

        // GetHenhmetafile은 핸들을 Metafile 객체에서 떼어낸다. 이후 Dispose하면 안 된다.
        IntPtr hEmf = metafile.GetHenhmetafile();
        if (hEmf == IntPtr.Zero)
            throw new InvalidOperationException("EMF 핸들을 얻지 못했습니다.");

        SetEnhMetafileHandle(hEmf, ownerWindow);
    }

    private static void SetEnhMetafileHandle(IntPtr hEmf, IntPtr ownerWindow)
    {
        if (!TryOpenClipboard(ownerWindow))
        {
            DeleteEnhMetaFile(hEmf);
            throw new InvalidOperationException(
                "클립보드를 열 수 없습니다. 다른 프로그램이 클립보드를 붙잡고 있는지 확인하세요.");
        }

        try
        {
            if (!EmptyClipboard())
            {
                DeleteEnhMetaFile(hEmf);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "클립보드를 비우지 못했습니다.");
            }

            if (SetClipboardData(CF_ENHMETAFILE, hEmf) == IntPtr.Zero)
            {
                // 실패했으면 소유권이 넘어가지 않았으므로 우리가 해제해야 한다.
                int error = Marshal.GetLastWin32Error();
                DeleteEnhMetaFile(hEmf);
                throw new Win32Exception(error, "클립보드에 EMF를 넣지 못했습니다.");
            }

            // 성공. 핸들은 이제 시스템 소유이므로 DeleteEnhMetaFile을 부르면 안 된다.
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryOpenClipboard(IntPtr ownerWindow)
    {
        for (int i = 0; i < OpenAttempts; i++)
        {
            if (OpenClipboard(ownerWindow))
                return true;

            Thread.Sleep(OpenRetryDelayMs);
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteEnhMetaFile(IntPtr hemf);
}
