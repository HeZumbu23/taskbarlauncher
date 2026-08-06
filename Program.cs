using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TaskbarLauncher;

internal static class Program
{
    /// <summary>Wurzel der Menü-Hierarchie. Unterordner = Untermenüs, Dateien = Einträge.</summary>
    public static readonly string MenuRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarLauncher", "Menue");

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        EnsureMenuFolder();

        // --once: Menü einmal anzeigen und danach beenden (für eine an die
        // Taskleiste angeheftete Verknüpfung). Ohne Argument: Tray-Symbol.
        bool once = args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase));

        Application.Run(new LauncherContext(once));
    }

    private static void EnsureMenuFolder()
    {
        if (Directory.Exists(MenuRoot)) return;

        Directory.CreateDirectory(MenuRoot);
        Directory.CreateDirectory(Path.Combine(MenuRoot, "01 Projekte"));
        Directory.CreateDirectory(Path.Combine(MenuRoot, "02 Links"));

        File.WriteAllText(Path.Combine(MenuRoot, "LIESMICH.txt"),
            """
            Dieser Ordner ist das Menü.

            - Unterordner        -> Untermenü (beliebig tief verschachtelt)
            - Verknüpfung (.lnk) -> Menüeintrag, öffnet das Ziel
            - Internetlink(.url) -> Menüeintrag, öffnet die Seite im Browser
            - beliebige Datei    -> Menüeintrag, öffnet die Datei
            - Datei namens "---" -> Trennlinie im Menü

            Sortierung: alphabetisch. Zahlen-Präfixe wie "01 " erzwingen eine
            Reihenfolge und werden in der Anzeige ausgeblendet.

            Änderungen wirken sofort, das Menü wird bei jedem Klick neu gelesen.
            """);
    }
}

/// <summary>
/// Hält das Tray-Symbol und zeigt das Menü an. Kein sichtbares Fenster.
/// </summary>
internal sealed class LauncherContext : ApplicationContext
{
    private readonly NotifyIcon? _tray;
    private readonly bool _once;
    private ContextMenuStrip? _menu;

    public LauncherContext(bool once)
    {
        _once = once;

        if (once)
        {
            // Menü erst zeigen, wenn die Nachrichtenschleife läuft.
            var starter = new System.Windows.Forms.Timer { Interval = 1 };
            starter.Tick += (_, _) => { starter.Stop(); starter.Dispose(); ShowMenu(); };
            starter.Start();
            return;
        }

        _tray = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Launcher",
            Visible = true
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) ShowMenu();
        };
    }

    private void ShowMenu()
    {
        _menu?.Dispose();
        _menu = MenuBuilder.Build(Program.MenuRoot, showExit: !_once);

        if (_once)
        {
            // Nach dem Schließen beenden - leicht verzögert, weil das Click-
            // Ereignis eines Eintrags erst nach dem Closed-Ereignis eintrifft.
            _menu.Closed += (_, _) => ExitDelayed();
        }

        var pos = Cursor.Position;
        var area = Screen.FromPoint(pos).WorkingArea;

        // Unten am Bildschirm (übliche Taskleistenposition) nach oben aufklappen.
        bool up = pos.Y > area.Top + area.Height / 2;
        bool left = pos.X > area.Left + area.Width / 2;
        var dir = (up, left) switch
        {
            (true, true) => ToolStripDropDownDirection.AboveLeft,
            (true, false) => ToolStripDropDownDirection.AboveRight,
            (false, true) => ToolStripDropDownDirection.BelowLeft,
            _ => ToolStripDropDownDirection.BelowRight
        };

        _menu.Show(pos, dir);
        _menu.Focus();
    }

    private void ExitDelayed()
    {
        var t = new System.Windows.Forms.Timer { Interval = 400 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); ExitThread(); };
        t.Start();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
            _menu?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Baut aus einer Ordnerstruktur ein hierarchisches Menü.</summary>
internal static class MenuBuilder
{
    private const int MaxDepth = 8;
    private static readonly Regex OrderPrefix = new(@"^\d+\s*[\s._\-]\s*", RegexOptions.Compiled);

    public static ContextMenuStrip Build(string root, bool showExit)
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            RenderMode = ToolStripRenderMode.System
        };

        var items = BuildItems(root, 0);
        if (items.Length == 0)
            menu.Items.Add(new ToolStripMenuItem("(Menü-Ordner ist leer)") { Enabled = false });
        else
            menu.Items.AddRange(items);

        menu.Items.Add(new ToolStripSeparator());

        var open = new ToolStripMenuItem("Menü-Ordner öffnen…");
        open.Click += (_, _) => Launcher.Open(Program.MenuRoot);
        menu.Items.Add(open);

        if (showExit)
        {
            var exit = new ToolStripMenuItem("Beenden");
            exit.Click += (_, _) => Application.Exit();
            menu.Items.Add(exit);
        }

        return menu;
    }

    private static ToolStripItem[] BuildItems(string path, int depth)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return [];

        var result = new List<ToolStripItem>();

        try
        {
            // Ordner zuerst, dann Dateien - jeweils alphabetisch.
            foreach (var sub in dir.EnumerateDirectories()
                                   .Where(IsVisible)
                                   .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = new ToolStripMenuItem(DisplayName(sub.Name))
                {
                    Image = IconCache.Get(sub.FullName)
                };

                var children = depth < MaxDepth ? BuildItems(sub.FullName, depth + 1) : [];
                if (children.Length == 0)
                    item.DropDownItems.Add(new ToolStripMenuItem("(leer)") { Enabled = false });
                else
                    item.DropDownItems.AddRange(children);

                result.Add(item);
            }

            foreach (var file in dir.EnumerateFiles()
                                    .Where(IsVisible)
                                    .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                var bare = Path.GetFileNameWithoutExtension(file.Name);
                if (bare.Length > 0 && bare.All(c => c == '-'))
                {
                    result.Add(new ToolStripSeparator());
                    continue;
                }

                string target = file.FullName;
                var item = new ToolStripMenuItem(DisplayName(file.Name))
                {
                    Image = IconCache.Get(target),
                    ToolTipText = target
                };
                item.Click += (_, _) => Launcher.Open(target);
                result.Add(item);
            }
        }
        catch (UnauthorizedAccessException)
        {
            result.Add(new ToolStripMenuItem("(kein Zugriff)") { Enabled = false });
        }

        return [.. result];
    }

    private static bool IsVisible(FileSystemInfo fsi)
        => (fsi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    private static string DisplayName(string name)
    {
        // Endung nur bei Verknüpfungen entfernen - bei echten Dateien ist sie
        // eine nützliche Information.
        var ext = Path.GetExtension(name);
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name);

        name = OrderPrefix.Replace(name, "");
        return name.Replace("&", "&&"); // & sonst als Tastenkürzel interpretiert
    }
}

internal static class Launcher
{
    public static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Konnte nicht geöffnet werden:\n{path}\n\n{ex.Message}",
                "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

/// <summary>Holt die echten Shell-Symbole (löst auch .lnk-Ziele auf) und merkt sie sich.</summary>
internal static class IconCache
{
    private static readonly Dictionary<string, Image?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Image? Get(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        var img = Extract(path);
        Cache[path] = img;
        return img;
    }

    private static Image? Extract(string path)
    {
        var info = new SHFILEINFO();
        try
        {
            var res = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                                    SHGFI_ICON | SHGFI_SMALLICON);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            using var icon = Icon.FromHandle(info.hIcon);
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon);
        }
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
