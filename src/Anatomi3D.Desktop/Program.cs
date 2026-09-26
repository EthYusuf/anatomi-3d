using System.Globalization;
using Anatomi3D.Desktop.App;
using Anatomi3D.Desktop.Platform;
using Anatomi3D.Graphics;

namespace Anatomi3D.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
        var opt = new AppOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data": opt.DataDir = Path.GetFullPath(args[++i]); break;
                case "--gpu": opt.Gpu = args[++i] == "integrated" ? GpuPreference.LowPower : GpuPreference.HighPerformance; break;
                case "--size":
                {
                    var p = args[++i].Split('x');
                    opt.Width = int.Parse(p[0]);
                    opt.Height = int.Parse(p[1]);
                    opt.Maximized = false;
                    break;
                }
                case "--windowed": opt.Maximized = false; break;
                case "--script": opt.Script = args[++i]; break;
                case "--script-file": opt.Script = File.ReadAllText(args[++i]).Replace("\r", "").Replace('\n', ';'); break;
                case "--no-intro": opt.NoIntro = true; break;
                case "--debug": opt.Debug = true; break;
                case "--content-report": opt.ContentReport = Path.GetFullPath(args[++i]); break;
            }
        }
        if (!File.Exists(Path.Combine(opt.DataDir, "anatomy.pak")))
        {
            // geliştirme ortamı: depo kökündeki data/ klasörü
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "data", "anatomy.pak"))) { opt.DataDir = Path.Combine(d.FullName, "data"); break; }
        }

        if (opt.ContentReport != null)
        {
            // grafik başlatmadan içerik kapsama raporu üret (içerik ekibi / CI için)
            var pack = Anatomi3D.Core.Pack.PackReader.Read(Path.Combine(opt.DataDir, "anatomy.pak"));
            var model = Anatomi3D.Core.Model.AnatomyModel.FromPack(pack,
                Anatomi3D.Core.Content.TurkishNames.Load(Path.Combine(opt.DataDir, "content", "names.tr.json")));
            Anatomi3D.Core.Content.ContentLibrary.Load(Path.Combine(opt.DataDir, "content"));
            File.WriteAllText(opt.ContentReport, Anatomi3D.Core.Content.ContentReport.Build(model));
            return 0;
        }

        try
        {
            using var app = new AtlasApp(opt);
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Anatomi3D", "hata.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, $"[{DateTime.Now:u}] {ex}\n\n");
            }
            catch { /* yoksay */ }
            if (opt.Script == null)
                Win32.MessageBoxW(IntPtr.Zero, $"Anatomi 3D beklenmeyen bir hatayla karşılaştı:\n\n{ex.Message}\n\nAyrıntılar: {log}", "Anatomi 3D", 0x10);
            else Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
