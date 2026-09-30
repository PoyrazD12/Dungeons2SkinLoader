using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Dungeons2SkinLoader
{
    static class App
    {
        public const string Name = "Dungeons 2 Skin Loader";
        public const string Author = "Poyraz Captain";
        public const string Version = "1.1.0";

        public static GameData LoadData()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("md2data.bin"))
                return GameData.Load(s);
        }

        public static string DataDir
        {
            get
            {
                // Documents rather than AppData: easy for people to find, and not
                // redirected by app sandboxes (packaged launchers virtualise AppData)
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var d = Path.Combine(docs, "Dungeons 2 Skin Loader");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--cli") return Cli.Run(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
