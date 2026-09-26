using System.Numerics;
using System.Runtime.InteropServices;

namespace Anatomi3D.Core.Pack;

/// <summary>
/// "anatomy.pak" ikili model paketi biçimi.
///
/// Dosya düzeni (little-endian):
///   Başlık (<see cref="PackHeader"/>) → parça (chunk) dizini → parçalar.
///   Her parça Brotli ile sıkıştırılmıştır; köşe ve indeks akışları ayrıca meshoptimizer kodeğiyle kodlanır.
///
///   META  UTF-8 JSON: yapı adları, kategori, hiyerarşi, kaynak bilgisi
///   PART  <see cref="PartRecord"/> dizisi (META'daki yapı sırasıyla)
///   VERT  <see cref="PackVertex"/> dizisi (16 bayt; konum küresel sınır kutusunda 16 bit nicemlenmiş)
///   IDX0..IDX3  LOD düzeyi başına üçgen listesi (32 bit mutlak köşe indeksi)
///   DESC  UTF-8 JSON: açıklama anahtarı → metin
/// </summary>
public static class PackFormat
{
    public const ulong Magic = 0x3130_4B41_5044_3341; // "A3DPAK01"
    public const uint Version = 2;
    public const int MaxLods = 4;
    public const int VertexSize = 16;

    public static uint FourCC(string s) => (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));

    public static readonly uint ChunkMeta = FourCC("META");
    public static readonly uint ChunkParts = FourCC("PART");
    public static readonly uint ChunkVertices = FourCC("VERT");
    public static readonly uint ChunkDescriptions = FourCC("DESC");
    public static uint ChunkIndices(int lod) => FourCC("IDX" + (char)('0' + lod));
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackHeader
{
    public ulong Magic;
    public uint Version;
    public uint PartCount;
    public uint VertexCount;
    public uint LodCount;
    public uint ChunkCount;
    public uint Flags;
    public Vector3 QuantMin;
    public Vector3 QuantMax;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ChunkEntry
{
    public uint Id;
    /// <summary>1 = Brotli, 2 = meshopt köşe kodeği, 4 = meshopt indeks kodeği</summary>
    public uint Encoding;
    public ulong Offset;
    public ulong StoredSize;
    /// <summary>Çözülmüş bayt uzunluğu</summary>
    public ulong RawSize;
    /// <summary>Kodlanan öğe sayısı (köşe / indeks)</summary>
    public ulong ElementCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LodRange
{
    public uint IndexOffset;
    public uint IndexCount;
    /// <summary>Bu LOD'un tam çözünürlüğe göre en büyük geometrik sapması (metre)</summary>
    public float Error;
}

/// <summary>Bir anatomik yapının paket içindeki geometri aralıkları ve sınırları.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PartRecord
{
    public uint VertexOffset;
    public uint VertexCount;
    public LodRange Lod0, Lod1, Lod2, Lod3;
    public Vector3 Center;
    public float Radius;
    public Vector3 Min;
    public Vector3 Max;
    /// <summary>Köşe dağılımının ana ekseni (en büyük yayılım)</summary>
    public Vector3 Axis;
    /// <summary>En küçük yayılım yönü (yassı yapıların normali)</summary>
    public Vector3 Minor;
    public uint Flags;

    public readonly LodRange Lod(int i) => i switch { 0 => Lod0, 1 => Lod1, 2 => Lod2, _ => Lod3 };

    public void SetLod(int i, LodRange r)
    {
        switch (i)
        {
            case 0: Lod0 = r; break;
            case 1: Lod1 = r; break;
            case 2: Lod2 = r; break;
            default: Lod3 = r; break;
        }
    }
}

/// <summary>GPU'ya doğrudan yüklenen 16 baytlık köşe.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackVertex
{
    public ushort X, Y, Z;
    /// <summary>Yapı (parça) indeksi</summary>
    public ushort Part;
    /// <summary>Oktahedral kodlanmış normal (snorm16)</summary>
    public short NormalX, NormalY;
    /// <summary>Oktahedral kodlanmış lif / damar yönü (snorm8)</summary>
    public sbyte DirX, DirY;
    /// <summary>Önceden hesaplanmış ortam kapatması (255 = açık)</summary>
    public byte Occlusion;
    /// <summary>Girinti (kavite) değeri (128 = düz)</summary>
    public byte Cavity;
}

[Flags]
public enum PartFlags : uint
{
    None = 0,
    /// <summary>Kas yapışma alanı (origo / insersiyo) katmanı; normal sahnede çizilmez</summary>
    Attachment = 1,
    /// <summary>Alt bölümleme ile yumuşatılmış deri</summary>
    Subdivided = 2,
    /// <summary>Kapalı (su geçirmez) yüzey — kesit kapağı çizilebilir</summary>
    Closed = 4,
}

/// <summary>Oktahedral normal kodlama yardımcıları.</summary>
public static class Octahedral
{
    public static Vector2 Encode(Vector3 n)
    {
        float l1 = MathF.Abs(n.X) + MathF.Abs(n.Y) + MathF.Abs(n.Z);
        if (l1 < 1e-20f) return new Vector2(0, 0);
        var p = new Vector2(n.X / l1, n.Y / l1);
        if (n.Z < 0)
        {
            p = new Vector2((1 - MathF.Abs(p.Y)) * (p.X >= 0 ? 1 : -1), (1 - MathF.Abs(p.X)) * (p.Y >= 0 ? 1 : -1));
        }
        return p;
    }

    public static Vector3 Decode(Vector2 e)
    {
        var v = new Vector3(e.X, e.Y, 1 - MathF.Abs(e.X) - MathF.Abs(e.Y));
        float t = MathF.Max(-v.Z, 0);
        v.X += v.X >= 0 ? -t : t;
        v.Y += v.Y >= 0 ? -t : t;
        return Vector3.Normalize(v);
    }

    public static (short, short) EncodeSnorm16(Vector3 n)
    {
        var e = Encode(n);
        return ((short)Math.Clamp(MathF.Round(e.X * 32767f), -32767, 32767), (short)Math.Clamp(MathF.Round(e.Y * 32767f), -32767, 32767));
    }

    public static (sbyte, sbyte) EncodeSnorm8(Vector3 n)
    {
        var e = Encode(n);
        return ((sbyte)Math.Clamp(MathF.Round(e.X * 127f), -127, 127), (sbyte)Math.Clamp(MathF.Round(e.Y * 127f), -127, 127));
    }
}
