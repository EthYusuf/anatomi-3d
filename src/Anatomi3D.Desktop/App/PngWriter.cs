using System.Buffers.Binary;
using System.IO.Compression;

namespace Anatomi3D.Desktop.App;

/// <summary>Bağımlılıksız PNG yazıcı (RGBA8 → PNG, zlib sıkıştırma).</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrc();

    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFF;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        Span<byte> t = stackalloc byte[4];
        for (int i = 0; i < 4; i++) t[i] = (byte)type[i];
        s.Write(t);
        s.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(t, data));
        s.Write(crc);
    }

    /// <param name="rgba">Satır başına <paramref name="rowPitch"/> bayt</param>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba, int rowPitch, bool opaque = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = (byte)(opaque ? 2 : 6);
        Chunk(fs, "IHDR", ihdr);
        int bpp = opaque ? 3 : 4;
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * bpp];
            for (int y = 0; y < height; y++)
            {
                row[0] = 0;
                var src = rgba.Slice(y * rowPitch, width * 4);
                if (opaque)
                    for (int x = 0; x < width; x++)
                    {
                        row[1 + x * 3] = src[x * 4];
                        row[2 + x * 3] = src[x * 4 + 1];
                        row[3 + x * 3] = src[x * 4 + 2];
                    }
                else src.CopyTo(row.AsSpan(1));
                z.Write(row);
            }
        }
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", []);
    }
}
