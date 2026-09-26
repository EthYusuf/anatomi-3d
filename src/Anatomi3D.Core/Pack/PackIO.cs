using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Anatomi3D.Core.Native;

namespace Anatomi3D.Core.Pack;

/// <summary>Paketten okunan ham veriler.</summary>
public sealed class PackData
{
    public required PackHeader Header { get; init; }
    public required PartRecord[] Parts { get; init; }
    public required string MetaJson { get; init; }
    public required string DescriptionsJson { get; init; }
    public required PackVertex[] Vertices { get; init; }
    /// <summary>LOD düzeyi başına mutlak köşe indeksli üçgen listeleri</summary>
    public required uint[][] Indices { get; init; }
}

public static class PackWriter
{
    private const uint Brotli = 1, VertexCodec = 2, IndexCodec = 4;

    public static void Write(string path, PackHeader header, PartRecord[] parts, string metaJson, string descJson,
        PackVertex[] vertices, uint[][] lodIndices, Action<string>? log = null)
    {
        var chunks = new List<(uint id, uint enc, byte[] data, ulong raw, ulong count)>();

        // Brotli kalite 9, pencere 24: 11'e yakın oran, çok daha hızlı sıkıştırma; açma hızı aynı
        byte[] Compress(ReadOnlySpan<byte> raw)
        {
            using var enc = new BrotliEncoder(9, 24);
            var dst = new byte[BrotliEncoder.GetMaxCompressedLength(raw.Length)];
            int total = 0;
            var src = raw;
            while (true)
            {
                var st = enc.Compress(src, dst.AsSpan(total), out int read, out int written, isFinalBlock: true);
                src = src[read..];
                total += written;
                if (st == System.Buffers.OperationStatus.Done) break;
                if (st != System.Buffers.OperationStatus.DestinationTooSmall && st != System.Buffers.OperationStatus.NeedMoreData)
                    throw new InvalidOperationException($"Brotli sıkıştırma hatası: {st}");
            }
            Array.Resize(ref dst, total);
            return dst;
        }

        void Add(uint id, uint enc, ReadOnlySpan<byte> payload, ulong raw, ulong count)
        {
            var c = Compress(payload);
            chunks.Add((id, enc | Brotli, c, raw, count));
            log?.Invoke($"  {IdName(id)}: {raw / 1e6,8:F2} MB → {c.Length / 1e6,7:F2} MB");
        }

        var meta = Encoding.UTF8.GetBytes(metaJson);
        Add(PackFormat.ChunkMeta, 0, meta, (ulong)meta.Length, 0);
        var partBytes = MemoryMarshal.AsBytes(parts.AsSpan());
        Add(PackFormat.ChunkParts, 0, partBytes, (ulong)partBytes.Length, (ulong)parts.Length);

        var vbytes = MemoryMarshal.AsBytes(vertices.AsSpan());
        var venc = Meshopt.EncodeVertexBuffer(vbytes, PackFormat.VertexSize);
        Add(PackFormat.ChunkVertices, VertexCodec, venc, (ulong)vbytes.Length, (ulong)vertices.Length);

        for (int l = 0; l < lodIndices.Length; l++)
        {
            var ienc = Meshopt.EncodeIndexBuffer(lodIndices[l], vertices.Length);
            Add(PackFormat.ChunkIndices(l), IndexCodec, ienc, (ulong)lodIndices[l].Length * 4, (ulong)lodIndices[l].Length);
        }

        var desc = Encoding.UTF8.GetBytes(descJson);
        Add(PackFormat.ChunkDescriptions, 0, desc, (ulong)desc.Length, 0);

        header.Magic = PackFormat.Magic;
        header.Version = PackFormat.Version;
        header.ChunkCount = (uint)chunks.Count;
        header.LodCount = (uint)lodIndices.Length;

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        int headerSize = Marshal.SizeOf<PackHeader>();
        int dirSize = Marshal.SizeOf<ChunkEntry>() * chunks.Count;
        ulong offset = (ulong)(headerSize + dirSize);
        var dir = new ChunkEntry[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            dir[i] = new ChunkEntry { Id = c.id, Encoding = c.enc, Offset = offset, StoredSize = (ulong)c.data.Length, RawSize = c.raw, ElementCount = c.count };
            offset += (ulong)c.data.Length;
        }
        w.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<PackHeader>(ref header)));
        w.Write(MemoryMarshal.AsBytes(dir.AsSpan()));
        foreach (var c in chunks) w.Write(c.data);
    }

    internal static string IdName(uint id) => new(new[] { (char)(id & 0xff), (char)((id >> 8) & 0xff), (char)((id >> 16) & 0xff), (char)(id >> 24) });
}

public static class PackReader
{
    /// <summary>Paketi okur; büyük akışlar paralel çözülür. <paramref name="progress"/> 0..1 bildirir.</summary>
    public static PackData Read(string path, Action<float>? progress = null)
    {
        byte[] file = File.ReadAllBytes(path);
        progress?.Invoke(0.15f);
        var header = MemoryMarshal.Read<PackHeader>(file);
        if (header.Magic != PackFormat.Magic) throw new InvalidDataException("Geçersiz model paketi (imza uyuşmuyor)");
        if (header.Version != PackFormat.Version) throw new InvalidDataException($"Desteklenmeyen paket sürümü {header.Version}");
        int headerSize = Marshal.SizeOf<PackHeader>();
        var dir = MemoryMarshal.Cast<byte, ChunkEntry>(file.AsSpan(headerSize, Marshal.SizeOf<ChunkEntry>() * (int)header.ChunkCount)).ToArray();

        ChunkEntry Find(uint id)
        {
            foreach (var c in dir) if (c.Id == id) return c;
            throw new InvalidDataException($"Pakette '{PackWriter.IdName(id)}' bölümü yok");
        }

        byte[] Inflate(ChunkEntry c, int size)
        {
            var src = file.AsMemory((int)c.Offset, (int)c.StoredSize);
            if ((c.Encoding & 1) == 0) return src.ToArray();
            var dst = new byte[size];
            using var br = new BrotliStream(new MemoryStream(file, (int)c.Offset, (int)c.StoredSize, false), CompressionMode.Decompress);
            int got = 0;
            while (got < size)
            {
                int n = br.Read(dst, got, size - got);
                if (n <= 0) break;
                got += n;
            }
            Array.Resize(ref dst, got);
            return dst;
        }

        // Brotli çıktısının üst sınırı bilinmediğinden kodeğin tahmini boyutu yerine akışı sonuna dek oku
        byte[] InflateAll(ChunkEntry c)
        {
            if ((c.Encoding & 1) == 0) return file.AsSpan((int)c.Offset, (int)c.StoredSize).ToArray();
            using var br = new BrotliStream(new MemoryStream(file, (int)c.Offset, (int)c.StoredSize, false), CompressionMode.Decompress);
            using var ms = new MemoryStream((int)Math.Min(c.RawSize, int.MaxValue / 2));
            br.CopyTo(ms);
            return ms.ToArray();
        }

        var metaC = Find(PackFormat.ChunkMeta);
        var partC = Find(PackFormat.ChunkParts);
        var vertC = Find(PackFormat.ChunkVertices);
        var descC = Find(PackFormat.ChunkDescriptions);

        string meta = Encoding.UTF8.GetString(Inflate(metaC, (int)metaC.RawSize));
        var parts = MemoryMarshal.Cast<byte, PartRecord>(Inflate(partC, (int)partC.RawSize)).ToArray();

        var vertices = new PackVertex[header.VertexCount];
        var lods = new uint[header.LodCount][];
        int done = 0;
        int total = 1 + (int)header.LodCount;
        var tasks = new List<Task>
        {
            Task.Run(() =>
            {
                var enc = InflateAll(vertC);
                Meshopt.DecodeVertexBuffer(MemoryMarshal.AsBytes(vertices.AsSpan()), PackFormat.VertexSize, enc);
                progress?.Invoke(0.15f + 0.8f * Interlocked.Increment(ref done) / total);
            }),
        };
        for (int l = 0; l < header.LodCount; l++)
        {
            int lod = l;
            tasks.Add(Task.Run(() =>
            {
                var c = Find(PackFormat.ChunkIndices(lod));
                var enc = InflateAll(c);
                var idx = new uint[c.ElementCount];
                Meshopt.DecodeIndexBuffer(idx, enc);
                lods[lod] = idx;
                progress?.Invoke(0.15f + 0.8f * Interlocked.Increment(ref done) / total);
            }));
        }
        string desc = Encoding.UTF8.GetString(Inflate(descC, (int)descC.RawSize));
        Task.WaitAll(tasks.ToArray());
        progress?.Invoke(1f);

        return new PackData { Header = header, Parts = parts, MetaJson = meta, DescriptionsJson = desc, Vertices = vertices, Indices = lods };
    }
}
