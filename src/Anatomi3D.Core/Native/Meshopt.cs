using System.Runtime.InteropServices;

namespace Anatomi3D.Core.Native;

/// <summary>
/// meshoptimizer (MIT, Arseny Kapoulkine) yerel kitaplığı için ince P/Invoke katmanı.
/// Kitaplık Meshoptimizer.NET paketinin runtimes/ klasöründen gelir; yalnız ihtiyaç duyulan işlevler bağlanır.
/// </summary>
public static unsafe partial class Meshopt
{
    private const string Lib = "meshoptimizer";

    [Flags]
    public enum SimplifyOptions : uint
    {
        None = 0,
        /// <summary>Sınır kenarlarını sabit tutar (komşu yamalarla dikiş oluşmasın).</summary>
        LockBorder = 1 << 0,
        Sparse = 1 << 1,
        /// <summary>Hata eşiği model birimi cinsinden (mutlak) yorumlanır.</summary>
        ErrorAbsolute = 1 << 2,
        /// <summary>Çok küçük kopuk parçaları atar.</summary>
        Prune = 1 << 3,
    }

    [LibraryImport(Lib, EntryPoint = "meshopt_simplify")]
    private static partial nuint SimplifyNative(uint* destination, uint* indices, nuint indexCount, float* positions, nuint vertexCount,
        nuint stride, nuint targetIndexCount, float targetError, uint options, float* resultError);

    [LibraryImport(Lib, EntryPoint = "meshopt_simplifyWithAttributes")]
    private static partial nuint SimplifyWithAttributesNative(uint* destination, uint* indices, nuint indexCount, float* positions, nuint vertexCount,
        nuint stride, float* attributes, nuint attributeStride, float* attributeWeights, nuint attributeCount, byte* vertexLock,
        nuint targetIndexCount, float targetError, uint options, float* resultError);

    [LibraryImport(Lib, EntryPoint = "meshopt_optimizeVertexCache")]
    private static partial void OptimizeVertexCacheNative(uint* destination, uint* indices, nuint indexCount, nuint vertexCount);

    [LibraryImport(Lib, EntryPoint = "meshopt_optimizeOverdraw")]
    private static partial void OptimizeOverdrawNative(uint* destination, uint* indices, nuint indexCount, float* positions, nuint vertexCount, nuint stride, float threshold);

    [LibraryImport(Lib, EntryPoint = "meshopt_optimizeVertexFetchRemap")]
    private static partial nuint OptimizeVertexFetchRemapNative(uint* destination, uint* indices, nuint indexCount, nuint vertexCount);

    [LibraryImport(Lib, EntryPoint = "meshopt_encodeIndexBufferBound")]
    private static partial nuint EncodeIndexBufferBound(nuint indexCount, nuint vertexCount);

    [LibraryImport(Lib, EntryPoint = "meshopt_encodeIndexBuffer")]
    private static partial nuint EncodeIndexBufferNative(byte* buffer, nuint bufferSize, uint* indices, nuint indexCount);

    [LibraryImport(Lib, EntryPoint = "meshopt_decodeIndexBuffer")]
    private static partial int DecodeIndexBufferNative(void* destination, nuint indexCount, nuint indexSize, byte* buffer, nuint bufferSize);

    [LibraryImport(Lib, EntryPoint = "meshopt_encodeVertexBufferBound")]
    private static partial nuint EncodeVertexBufferBound(nuint vertexCount, nuint vertexSize);

    [LibraryImport(Lib, EntryPoint = "meshopt_encodeVertexBuffer")]
    private static partial nuint EncodeVertexBufferNative(byte* buffer, nuint bufferSize, void* vertices, nuint vertexCount, nuint vertexSize);

    [LibraryImport(Lib, EntryPoint = "meshopt_decodeVertexBuffer")]
    private static partial int DecodeVertexBufferNative(void* destination, nuint vertexCount, nuint vertexSize, byte* buffer, nuint bufferSize);

    /// <summary>Üçgen ağını hedef üçgen sayısına / hata eşiğine kadar sadeleştirir. Sonuç, aynı köşe dizisine indekstir.</summary>
    public static uint[] Simplify(ReadOnlySpan<uint> indices, ReadOnlySpan<float> positions, int targetIndexCount, float targetError,
        SimplifyOptions options, out float resultError)
    {
        var dst = new uint[indices.Length];
        float err;
        nuint n;
        fixed (uint* pd = dst)
        fixed (uint* pi = indices)
        fixed (float* pp = positions)
            n = SimplifyNative(pd, pi, (nuint)indices.Length, pp, (nuint)(positions.Length / 3), 12, (nuint)targetIndexCount, targetError, (uint)options, &err);
        resultError = err;
        Array.Resize(ref dst, (int)n);
        return dst;
    }

    /// <summary>Normal gibi köşe öznitelikleri de korunarak sadeleştirme; <paramref name="vertexLock"/> ile tek tek köşeler kilitlenebilir.</summary>
    public static uint[] SimplifyWithAttributes(ReadOnlySpan<uint> indices, ReadOnlySpan<float> positions, ReadOnlySpan<float> attributes,
        int attributeCount, ReadOnlySpan<float> weights, ReadOnlySpan<byte> vertexLock, int targetIndexCount, float targetError,
        SimplifyOptions options, out float resultError)
    {
        var dst = new uint[indices.Length];
        float err;
        nuint n;
        fixed (uint* pd = dst)
        fixed (uint* pi = indices)
        fixed (float* pp = positions)
        fixed (float* pa = attributes)
        fixed (float* pw = weights)
        fixed (byte* pl = vertexLock)
            n = SimplifyWithAttributesNative(pd, pi, (nuint)indices.Length, pp, (nuint)(positions.Length / 3), 12,
                pa, (nuint)(attributeCount * 4), pw, (nuint)attributeCount, vertexLock.IsEmpty ? null : pl,
                (nuint)targetIndexCount, targetError, (uint)options, &err);
        resultError = err;
        Array.Resize(ref dst, (int)n);
        return dst;
    }

    /// <summary>GPU köşe önbelleği için üçgen sırasını iyileştirir (yerinde).</summary>
    public static void OptimizeVertexCache(Span<uint> indices, int vertexCount)
    {
        fixed (uint* pi = indices)
            OptimizeVertexCacheNative(pi, pi, (nuint)indices.Length, (nuint)vertexCount);
    }

    /// <summary>Görünür üst üste çizimi azaltmak için üçgenleri yeniden sıralar (önbellek sırasını çok bozmadan).</summary>
    public static void OptimizeOverdraw(Span<uint> indices, ReadOnlySpan<float> positions, float threshold = 1.05f)
    {
        fixed (uint* pi = indices)
        fixed (float* pp = positions)
            OptimizeOverdrawNative(pi, pi, (nuint)indices.Length, pp, (nuint)(positions.Length / 3), 12, threshold);
    }

    /// <summary>Köşe erişim sırasına göre yeniden numaralandırma tablosu (kullanılmayan köşeler ~0u).</summary>
    public static uint[] OptimizeVertexFetchRemap(ReadOnlySpan<uint> indices, int vertexCount, out int uniqueVertices)
    {
        var remap = new uint[vertexCount];
        fixed (uint* pr = remap)
        fixed (uint* pi = indices)
            uniqueVertices = (int)OptimizeVertexFetchRemapNative(pr, pi, (nuint)indices.Length, (nuint)vertexCount);
        return remap;
    }

    public static byte[] EncodeIndexBuffer(ReadOnlySpan<uint> indices, int vertexCount)
    {
        if (indices.Length % 3 != 0) throw new ArgumentException("İndeks sayısı 3'ün katı olmalı", nameof(indices));
        var buf = new byte[(int)EncodeIndexBufferBound((nuint)indices.Length, (nuint)Math.Max(vertexCount, 1))];
        nuint n;
        fixed (byte* pb = buf)
        fixed (uint* pi = indices)
            n = EncodeIndexBufferNative(pb, (nuint)buf.Length, pi, (nuint)indices.Length);
        if (n == 0 && indices.Length > 0) throw new InvalidOperationException("meshopt indeks kodlaması başarısız");
        Array.Resize(ref buf, (int)n);
        return buf;
    }

    public static void DecodeIndexBuffer(Span<uint> destination, ReadOnlySpan<byte> encoded)
    {
        int r;
        fixed (uint* pd = destination)
        fixed (byte* pb = encoded)
            r = DecodeIndexBufferNative(pd, (nuint)destination.Length, 4, pb, (nuint)encoded.Length);
        if (r != 0) throw new InvalidDataException($"meshopt indeks çözme hatası ({r})");
    }

    public static byte[] EncodeVertexBuffer(ReadOnlySpan<byte> vertices, int vertexSize)
    {
        int count = vertices.Length / vertexSize;
        var buf = new byte[(int)EncodeVertexBufferBound((nuint)count, (nuint)vertexSize)];
        nuint n;
        fixed (byte* pb = buf)
        fixed (byte* pv = vertices)
            n = EncodeVertexBufferNative(pb, (nuint)buf.Length, pv, (nuint)count, (nuint)vertexSize);
        if (n == 0 && count > 0) throw new InvalidOperationException("meshopt köşe kodlaması başarısız");
        Array.Resize(ref buf, (int)n);
        return buf;
    }

    public static void DecodeVertexBuffer(Span<byte> destination, int vertexSize, ReadOnlySpan<byte> encoded)
    {
        int r;
        fixed (byte* pd = destination)
        fixed (byte* pb = encoded)
            r = DecodeVertexBufferNative(pd, (nuint)(destination.Length / vertexSize), (nuint)vertexSize, pb, (nuint)encoded.Length);
        if (r != 0) throw new InvalidDataException($"meshopt köşe çözme hatası ({r})");
    }
}
