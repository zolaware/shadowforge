using System.Text;

namespace ShadowForge.Text;

public static class ShiftJisHelper
{
    private static Encoding? _encoding;

    public static Encoding Encoding
    {
        get
        {
            if (_encoding == null)
            {
                EncodingSetup.EnsureRegistered();
                _encoding = Encoding.GetEncoding(932);
            }
            return _encoding;
        }
    }

    public static string Decode(byte[] data, int offset, int length)
    {
        int end = offset;
        int limit = offset + length;
        while (end < limit && data[end] != 0) end++;
        return Encoding.GetString(data, offset, end - offset);
    }

    public static byte[] Encode(string value) => Encoding.GetBytes(value);

    public static void WriteFixed(byte[] dest, int offset, int length, string value)
    {
        var bytes = Encode(value);
        Array.Clear(dest, offset, length);
        Array.Copy(bytes, 0, dest, offset, Math.Min(bytes.Length, length));
    }
}
