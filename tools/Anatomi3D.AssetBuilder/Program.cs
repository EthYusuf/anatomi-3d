using System.Globalization;
using Anatomi3D.AssetBuilder;

// Kullanım:
//   dotnet run -c Release --project tools/Anatomi3D.AssetBuilder -- [--raw <_raw klasörü>] [--out <anatomy.pak>] [--skin-levels 2]
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string root = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Environment.CurrentDirectory)
              ?? throw new InvalidOperationException("Depo kökü (Anatomi3D.sln) bulunamadı");
string raw = Path.Combine(root, "_raw");
string output = Path.Combine(root, "data", "anatomy.pak");
int skinLevels = 2;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--raw": raw = Path.GetFullPath(args[++i]); break;
        case "--out": output = Path.GetFullPath(args[++i]); break;
        case "--skin-levels": skinLevels = int.Parse(args[++i]); break;
        default:
            Console.Error.WriteLine($"Bilinmeyen argüman: {args[i]}");
            return 2;
    }
}

if (!Directory.Exists(Path.Combine(raw, "za")))
{
    Console.Error.WriteLine($"Ham veri bulunamadı: {Path.Combine(raw, "za")}\n" +
                            "Z-Anatomy FBX'lerini GLB'ye çevirip _raw/za/ altına, depo Resources klasörünü _raw/za_repo/Resources olarak koyun.");
    return 1;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
var builder = new Builder(raw, Console.WriteLine) { SkinLevels = skinLevels };
builder.Run(output);
return 0;

static string? FindRepoRoot(string start)
{
    for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "Anatomi3D.sln"))) return d.FullName;
    return null;
}
