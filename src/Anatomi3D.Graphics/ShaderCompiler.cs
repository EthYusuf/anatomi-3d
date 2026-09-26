using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Anatomi3D.Graphics;

/// <summary>
/// Gömülü HLSL kaynaklarını derler. #include satırları kaynak içinde çözülür; derlenmiş bayt kodu
/// %LOCALAPPDATA%\Anatomi3D\ShaderCache altında içerik özetiyle saklanır (ikinci açılış anında).
/// </summary>
public sealed partial class ShaderCompiler
{
    private readonly ID3D11Device device;
    private readonly string cacheDir;
    private readonly Dictionary<string, string> sources = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Assembly Asm = typeof(ShaderCompiler).Assembly;

    [GeneratedRegex("^\\s*#include\\s+\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex IncludeRx();

    public ShaderCompiler(ID3D11Device device)
    {
        this.device = device;
        cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Anatomi3D", "ShaderCache");
        try { Directory.CreateDirectory(cacheDir); } catch { /* salt okunur ortam: önbelleksiz devam */ }
    }

    private string Load(string name)
    {
        if (sources.TryGetValue(name, out var s)) return s;
        using var st = Asm.GetManifestResourceStream("Shaders." + name) ?? throw new FileNotFoundException("Gömülü shader yok: " + name);
        using var r = new StreamReader(st);
        s = r.ReadToEnd();
        s = IncludeRx().Replace(s, m => Load(m.Groups[1].Value));
        sources[name] = s;
        return s;
    }

    public byte[] Compile(string file, string entry, string profile, params (string name, string value)[] defines)
    {
        string src = Load(file);
        var sb = new StringBuilder();
        foreach (var (n, v) in defines) sb.Append(n).Append('=').Append(v).Append(';');
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v3|{file}|{entry}|{profile}|{sb}|{src}")))[..32];
        string cachePath = Path.Combine(cacheDir, key + ".cso");
        try
        {
            if (File.Exists(cachePath)) return File.ReadAllBytes(cachePath);
        }
        catch { /* önbellek okunamadı */ }

        var macros = defines.Select(d => new ShaderMacro(d.name, d.value)).ToArray();
        var flags = ShaderFlags.OptimizationLevel3 | ShaderFlags.PackMatrixRowMajor;
        var r = Compiler.Compile(src, macros, null!, entry, file, profile, flags, out Blob? blob, out Blob? errors);
        if (r.Failure || blob == null)
        {
            string msg = errors?.AsString() ?? r.ToString();
            errors?.Dispose();
            throw new InvalidOperationException($"Shader derleme hatası {file}:{entry} ({profile})\n{msg}");
        }
        errors?.Dispose();
        var bytes = blob.AsBytes();
        blob.Dispose();
        try { File.WriteAllBytes(cachePath, bytes); } catch { /* yoksay */ }
        return bytes;
    }

    public ID3D11VertexShader VS(string file, string entry, out byte[] bytecode, params (string, string)[] d)
    {
        bytecode = Compile(file, entry, "vs_5_0", d);
        return device.CreateVertexShader(bytecode);
    }

    public ID3D11VertexShader VS(string file, string entry, params (string, string)[] d) => device.CreateVertexShader(Compile(file, entry, "vs_5_0", d));
    public ID3D11PixelShader PS(string file, string entry, params (string, string)[] d) => device.CreatePixelShader(Compile(file, entry, "ps_5_0", d));
    public ID3D11HullShader HS(string file, string entry, params (string, string)[] d) => device.CreateHullShader(Compile(file, entry, "hs_5_0", d));
    public ID3D11DomainShader DS(string file, string entry, params (string, string)[] d) => device.CreateDomainShader(Compile(file, entry, "ds_5_0", d));
    public ID3D11ComputeShader CS(string file, string entry, params (string, string)[] d) => device.CreateComputeShader(Compile(file, entry, "cs_5_0", d));
}
