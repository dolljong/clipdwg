using System;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClipDwg.Tests;

/// <summary>EMF 레코드 스트림을 직접 훑어 무엇이 기록됐는지 확인한다.</summary>
internal static class EmfInspector
{
    // wingdi.h 의 EMR_* 값
    private const uint EMR_EXTTEXTOUTA = 83;
    private const uint EMR_EXTTEXTOUTW = 84;
    private const uint EMR_POLYTEXTOUTA = 96;
    private const uint EMR_POLYTEXTOUTW = 97;
    private const uint EMR_SMALLTEXTOUT = 108;

    /// <summary>
    /// 주의: <see cref="Metafile.GetHenhmetafile"/> 는 핸들을 떼어내므로 이 호출 이후
    /// 해당 Metafile 객체는 쓸 수 없다. 핸들은 여기서 해제한다.
    /// </summary>
    public static bool ContainsTextRecord(Metafile metafile)
    {
        byte[] bits = GetBits(metafile);
        return ContainsAny(bits, EMR_EXTTEXTOUTA, EMR_EXTTEXTOUTW,
            EMR_POLYTEXTOUTA, EMR_POLYTEXTOUTW, EMR_SMALLTEXTOUT);
    }

    public static byte[] GetBits(Metafile metafile)
    {
        IntPtr handle = metafile.GetHenhmetafile();
        if (handle == IntPtr.Zero)
            return Array.Empty<byte>();

        try
        {
            uint size = GetEnhMetaFileBits(handle, 0, null);
            if (size == 0)
                return Array.Empty<byte>();

            var buffer = new byte[size];
            GetEnhMetaFileBits(handle, size, buffer);
            return buffer;
        }
        finally
        {
            DeleteEnhMetaFile(handle);
        }
    }

    private static bool ContainsAny(byte[] bits, params uint[] recordTypes)
    {
        int offset = 0;
        while (offset + 8 <= bits.Length)
        {
            uint type = BitConverter.ToUInt32(bits, offset);
            uint size = BitConverter.ToUInt32(bits, offset + 4);

            if (size < 8 || offset + size > bits.Length)
                break;

            foreach (uint wanted in recordTypes)
            {
                if (type == wanted)
                    return true;
            }

            offset += (int)size;
        }

        return false;
    }

    [DllImport("gdi32.dll")]
    private static extern uint GetEnhMetaFileBits(IntPtr hemf, uint nSize, byte[]? lpBuffer);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteEnhMetaFile(IntPtr hemf);
}
