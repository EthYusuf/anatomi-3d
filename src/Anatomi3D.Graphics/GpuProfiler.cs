using Vortice.Direct3D11;

namespace Anatomi3D.Graphics;

/// <summary>
/// Geçiş başına GPU süresi (D3D11 zaman damgası sorguları). Sonuçlar birkaç kare gecikmeyle okunur; GPU beklenmez.
/// </summary>
public sealed unsafe class GpuProfiler : IDisposable
{
    private const int Frames = 4;
    private readonly ID3D11DeviceContext ctx;
    private readonly ID3D11Query[] disjoint = new ID3D11Query[Frames];
    private readonly List<(string name, ID3D11Query q)>[] marks = new List<(string, ID3D11Query)>[Frames];
    private readonly Stack<ID3D11Query> pool = new();
    private readonly ID3D11Device dev;
    private int frame;
    private int active = -1;
    public bool Enabled { get; set; } = true;
    /// <summary>Son ölçülen geçiş süreleri (ms)</summary>
    public List<(string name, float ms)> Last { get; } = [];
    public float TotalMs { get; private set; }

    public GpuProfiler(ID3D11Device dev, ID3D11DeviceContext ctx)
    {
        this.dev = dev;
        this.ctx = ctx;
        for (int i = 0; i < Frames; i++)
        {
            disjoint[i] = dev.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
            marks[i] = [];
        }
    }

    public void BeginFrame()
    {
        if (!Enabled) return;
        Collect();
        active = frame % Frames;
        foreach (var (_, q) in marks[active]) pool.Push(q);
        marks[active].Clear();
        ctx.Begin(disjoint[active]);
        Mark("start");
    }

    public void Mark(string name)
    {
        if (!Enabled || active < 0) return;
        var q = pool.Count > 0 ? pool.Pop() : dev.CreateQuery(new QueryDescription(QueryType.Timestamp));
        ctx.End(q);
        marks[active].Add((name, q));
    }

    public void EndFrame()
    {
        if (!Enabled || active < 0) return;
        Mark("end");
        ctx.End(disjoint[active]);
        frame++;
        active = -1;
    }

    private void Collect()
    {
        if (frame < Frames - 1) return;
        int idx = (frame + 1) % Frames; // en eski kare
        if (marks[idx].Count < 2) return;
        QueryDataTimestampDisjoint dj;
        if (ctx.GetData(disjoint[idx], (IntPtr)(&dj), (uint)sizeof(QueryDataTimestampDisjoint), AsyncGetDataFlags.DoNotFlush).Code != 0) return;
        if (dj.Disjoint || dj.Frequency == 0) return;
        var stamps = new ulong[marks[idx].Count];
        for (int i = 0; i < stamps.Length; i++)
        {
            ulong t;
            if (ctx.GetData(marks[idx][i].q, (IntPtr)(&t), 8, AsyncGetDataFlags.DoNotFlush).Code != 0) return;
            stamps[i] = t;
        }
        Last.Clear();
        for (int i = 1; i < stamps.Length; i++)
            Last.Add((marks[idx][i].name, (float)((stamps[i] - stamps[i - 1]) * 1000.0 / dj.Frequency)));
        TotalMs = (float)((stamps[^1] - stamps[0]) * 1000.0 / dj.Frequency);
    }

    public void Dispose()
    {
        foreach (var d in disjoint) d.Dispose();
        foreach (var m in marks) foreach (var (_, q) in m) q.Dispose();
        while (pool.Count > 0) pool.Pop().Dispose();
    }
}
