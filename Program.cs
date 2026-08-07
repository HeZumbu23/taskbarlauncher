using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TaskbarLauncher;

internal static class Program
{
    /// <summary>Wurzel der Menü-Hierarchie. Unterordner = Untermenüs, Dateien = Einträge.</summary>
    public static readonly string MenuRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarLauncher", "Menue");

    private const string SingleInstanceMutexName = "TaskbarLauncher-9F1E2C3B-SingleInstance";
    private const string ShowMenuEventName = "TaskbarLauncher-9F1E2C3B-ShowMenu";

    // Von der Autostart-Verknüpfung gesetzt, damit der stille Start beim
    // Anmelden nicht sofort das Menü aufklappt (siehe MainForm.OnShown).
    public const string StartupArg = "--startup";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        using var showMenuEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowMenuEventName);

        if (!createdNew)
        {
            // Es läuft bereits eine Instanz (z. B. per Autostart). Statt eine
            // zweite zu starten, bekommt die laufende einfach den Auftrag,
            // das Menü zu zeigen - der Klick geht so nie ins Leere.
            showMenuEvent.Set();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        EnsureMenuFolder();

        bool startedSilently = args.Any(a => a.Equals(StartupArg, StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(startedSilently, showMenuEvent));
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

            Auch direkt aus dem Menü heraus:
            - "Aus Zwischenablage einfügen" (oben in jeder Ebene) erstellt aus
              einer kopierten Datei/einem kopierten Link eine neue Verknüpfung
              genau in dieser Ebene.
            - Rechtsklick auf einen Eintrag oder Ordner öffnet ihn zum
              Umbenennen, Ziel ändern, manuellen Einsortieren (Rauf/Runter)
              oder Löschen.
            - "Erweiterte Ansicht" (unten im Menü, an/aus) zeigt statt des
              Menüs ein großes Kachelraster: die oberste Ebene steht sofort
              gruppiert und sichtbar da, tiefere Ordner navigiert man per
              Klick hinein, "Zurück" geht wieder hoch.

            Win+Alt+Y öffnet einen Suchschlitz mit sofort fokussiertem
            Eingabefeld - tippen filtert in Echtzeit über alle Dateien der
            gesamten Hierarchie, Pfeiltasten wählen, Enter öffnet den
            markierten Treffer direkt, egal wie tief er verschachtelt ist.
            Win+Alt+L öffnet direkt das Kachelraster (Erweiterte Ansicht).

            Änderungen wirken sofort, das Menü wird bei jedem Klick neu gelesen.
            """);
    }
}

/// <summary>
/// Läuft dauerhaft im Hintergrund als ganz normaler Taskleisten-Eintrag -
/// aber immer minimiert, also ohne je ein Fenster zu zeigen. Ein Klick auf
/// den Taskleisten-Button lässt Windows zuerst WM_SYSCOMMAND/SC_RESTORE an
/// das Fenster schicken, bevor es tatsächlich wiederhergestellt wird. Genau
/// diese Nachricht fangen wir ab: statt das Fenster zu zeigen, öffnen wir
/// das Menü und lassen es minimiert. So bleibt exakt ein normales, anheftbares
/// Icon übrig, dessen Klick sofort das Menü öffnet - ohne Tray, ohne Neustart.
/// </summary>
internal sealed class MainForm : Form
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_RESTORE = 0xF120;
    private const int WM_HOTKEY = 0x0312;

    // Win+Alt+L ("L" wie Launcher) öffnet das Kachelraster (Erweiterte
    // Ansicht), unabhängig vom sonst eingestellten Standardmodus. Win+Alt+Y
    // öffnet den Echtzeit-Suchschlitz mit bereits fokussiertem Eingabefeld.
    // Unter den Win+Alt-Kombinationen sind nur wenige von Windows selbst
    // belegt (R/G/B/Enter/PrtScn für die Xbox Game Bar, D für Datum/Uhrzeit)
    // - L und Y sind frei.
    private const int ExpandedViewHotkeyId = 1;
    private const int SearchHotkeyId = 2;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_L = 0x4C;
    private const uint VK_Y = 0x59;

    private readonly EventWaitHandle _showMenuEvent;
    private ContextMenuStrip? _menu;

    public MainForm(bool startedSilently, EventWaitHandle showMenuEvent)
    {
        _showMenuEvent = showMenuEvent;

        Text = "Launcher";
        Icon = LoadAppIcon();
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;

        Load += (_, _) => WindowState = FormWindowState.Minimized;

        if (!startedSilently)
        {
            // Manueller Start (Doppelklick / erster Klick auf ein noch nicht
            // laufendes, angeheftetes Icon) - direkt das Menü zeigen, statt
            // den Klick "wirkungslos" verpuffen zu lassen.
            Shown += (_, _) => ShowMenu();
        }

        new Thread(ListenForShowMenuRequests) { IsBackground = true }.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Fehlschlag (z. B. Hotkey bereits durch ein anderes Programm belegt)
        // wird bewusst stillschweigend ignoriert - die App bleibt trotzdem
        // über den Taskleisten-Klick voll nutzbar.
        RegisterHotKey(Handle, ExpandedViewHotkeyId, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_L);
        RegisterHotKey(Handle, SearchHotkeyId, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_Y);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, ExpandedViewHotkeyId);
        UnregisterHotKey(Handle, SearchHotkeyId);
        base.OnHandleDestroyed(e);
    }

    /// <summary>
    /// Läuft auf einem Hintergrund-Thread. Ein zweiter Programmstart (z. B.
    /// Doppelklick, während bereits eine Instanz läuft) setzt dieses Signal
    /// statt eine zweite Instanz zu starten - so öffnet auch dieser Klick
    /// zuverlässig das Menü.
    /// </summary>
    private void ListenForShowMenuRequests()
    {
        while (!IsDisposed)
        {
            if (_showMenuEvent.WaitOne(250) && !IsDisposed)
            {
                try { Invoke(new Action(() => ShowMenu())); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SC_RESTORE)
        {
            ShowMenu();
            return; // nicht an Windows weiterreichen -> Fenster bleibt minimiert/unsichtbar
        }

        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == ExpandedViewHotkeyId)
        {
            TileGridForm.ShowAt(Program.MenuRoot, Cursor.Position);
            return;
        }

        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == SearchHotkeyId)
        {
            SearchForm.ShowAt(Program.MenuRoot, Cursor.Position);
            return;
        }

        base.WndProc(ref m);
    }

    private void ShowMenu()
    {
        // Der Standardmodus "Erweiterte Ansicht" zeigt statt des normalen
        // Menüs das Kachelraster.
        if (Settings.ExpandedView)
        {
            TileGridForm.ShowAt(Program.MenuRoot, Cursor.Position);
            return;
        }

        _menu?.Dispose();
        _menu = MenuBuilder.Build(Program.MenuRoot, showExit: true);

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

    private static Icon LoadAppIcon()
    {
        // Über die eingebettete Ressource geladen (nicht per
        // Icon.ExtractAssociatedIcon), damit die volle Auflösungsreihe aus
        // app.ico erhalten bleibt - Windows kann sich dann je nach Kontext
        // (Taskleiste, DPI-Skalierung, Alt-Tab, ...) die passend scharfe
        // Größe herausholen, statt eine einzelne kleine Auflösung
        // hochzuskalieren.
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("TaskbarLauncher.app.ico");
            if (stream is not null) return new Icon(stream);
        }
        catch
        {
            // Fällt unten auf die weniger scharfe Variante zurück.
        }

        try
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Persistierte Anzeige-Einstellungen, als einfache Textdatei neben dem Menü-Ordner.</summary>
internal static class Settings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarLauncher", "settings.ini");

    public static bool ExpandedView
    {
        get => File.Exists(FilePath) &&
               File.ReadAllLines(FilePath).Any(l => l.Trim().Equals("ExpandedView=true", StringComparison.OrdinalIgnoreCase));
        set
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, $"ExpandedView={(value ? "true" : "false")}\r\n");
        }
    }
}

/// <summary>
/// Zählt, wie oft ein Eintrag geöffnet wurde, persistiert als einfache
/// "Anzahl&lt;TAB&gt;Schlüssel"-Textdatei. Der Schlüssel ist der relative Pfad ab
/// dem Menü-Ordner mit entfernten Sortier-Präfixen ("01 " etc.) an jedem
/// Segment - dadurch übersteht die Statistik das manuelle Umsortieren
/// (das nur Präfixe ändert), nicht aber ein echtes Umbenennen.
/// </summary>
internal static class UsageStats
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarLauncher", "usage.txt");

    private static Dictionary<string, int>? _cache;

    public static void RecordOpen(string root, string fullPath)
    {
        var stats = Load();
        string key = KeyFor(root, fullPath);
        stats[key] = stats.GetValueOrDefault(key) + 1;
        Save();
    }

    public static int GetCount(string root, string fullPath) =>
        Load().GetValueOrDefault(KeyFor(root, fullPath), 0);

    /// <summary>Summe aller Öffnungen von Dateien unterhalb von folder (rekursiv) - für die Gruppen-Sortierung.</summary>
    public static int GetGroupTotal(string root, string folder)
    {
        int total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                total += GetCount(root, file);
        }
        catch (UnauthorizedAccessException) { }
        return total;
    }

    private static string KeyFor(string root, string fullPath)
    {
        string rel = Path.GetRelativePath(root, fullPath);
        var segments = rel.Split(Path.DirectorySeparatorChar).Select(OrderPrefixHelper.Strip);
        return string.Join('/', segments);
    }

    private static Dictionary<string, int> Load()
    {
        if (_cache is not null) return _cache;

        _cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(FilePath))
        {
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int tab = line.IndexOf('\t');
                if (tab < 0) continue;
                if (int.TryParse(line.AsSpan(0, tab), out int count))
                    _cache[line[(tab + 1)..]] = count;
            }
        }
        return _cache;
    }

    private static void Save()
    {
        if (_cache is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, _cache.Select(kv => $"{kv.Value}\t{kv.Key}"));
        }
        catch
        {
            // Statistik ist ein "nice to have" - ein Schreibfehler soll nie
            // das eigentliche Öffnen eines Eintrags verhindern.
        }
    }
}

/// <summary>Zahlen-Präfixe wie "01 " für die manuelle Sortierung - werden in der Anzeige ausgeblendet.</summary>
internal static class OrderPrefixHelper
{
    public static readonly Regex Regex = new(@"^\d+\s*[\s._\-]\s*", RegexOptions.Compiled);

    public static string Strip(string name) => Regex.Replace(name, "");

    public static string Extract(string name)
    {
        var m = Regex.Match(name);
        return m.Success ? m.Value : "";
    }
}

/// <summary>
/// Räumt einmalig die " [123]"-Codesuffixe eines früheren Prototyps aus
/// vorhandenen Dateinamen wieder heraus (ersetzt durch den Echtzeit-
/// Suchschlitz, Win+Alt+Y). Läuft bei jedem Menüaufbau; sobald alle Namen
/// bereinigt sind, ist es ein reiner No-op-Scan.
/// </summary>
internal static class LegacyCodeCleanup
{
    private static readonly Regex Suffix = new(@"\s*\[(\d{3})\]$", RegexOptions.Compiled);

    public static void StripAll(string root)
    {
        if (!Directory.Exists(root)) return;

        List<string> files;
        try { files = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)]; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var file in files)
        {
            string baseName = Path.GetFileNameWithoutExtension(file);
            if (!Suffix.IsMatch(baseName)) continue;

            string cleanBase = Suffix.Replace(baseName, "");
            string ext = Path.GetExtension(file);
            string dir = Path.GetDirectoryName(file)!;
            string newPath = Path.Combine(dir, cleanBase + ext);

            for (int n = 2; File.Exists(newPath); n++)
                newPath = Path.Combine(dir, $"{cleanBase} ({n}){ext}");

            try
            {
                File.Move(file, newPath);
                IconCache.Invalidate(file);
            }
            catch
            {
                // Datei evtl. gerade in Benutzung o.ä. - beim nächsten Öffnen erneut versuchen.
            }
        }
    }
}

/// <summary>Liest ein Verzeichnis konsistent für Anzeige *und* manuelle Sortierung aus.</summary>
internal static class MenuFs
{
    public static List<FileSystemInfo> GetOrderedEntries(string folder)
    {
        var dir = new DirectoryInfo(folder);
        if (!dir.Exists) return [];

        return [.. dir.EnumerateFileSystemInfos()
                      .Where(IsVisible)
                      .Where(f => !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                      .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static bool IsVisible(FileSystemInfo fsi)
        => (fsi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    /// <summary>Eine Datei wie "---" oder "---.txt" ist eine Trennlinie, kein echter Eintrag.</summary>
    public static bool IsSeparatorFile(FileInfo file)
    {
        var bare = OrderPrefixHelper.Strip(Path.GetFileNameWithoutExtension(file.Name));
        return bare.Length > 0 && bare.All(c => c == '-');
    }
}

/// <summary>
/// Manuelles Verschieben eines Eintrags innerhalb seiner Ebene. Sobald zum
/// ersten Mal verschoben wird, bekommen alle Geschwister durchgängige
/// "01 ", "02 ", ... Präfixe, damit die Reihenfolge ab dann exakt und
/// dauerhaft kontrollierbar ist.
/// </summary>
internal static class MenuOrder
{
    public static bool CanMove(string entryPath, int direction)
    {
        var siblings = GetOrderedSiblingPaths(entryPath);
        int index = siblings.FindIndex(p => string.Equals(p, entryPath, StringComparison.OrdinalIgnoreCase));
        int target = index + direction;
        return index >= 0 && target >= 0 && target < siblings.Count;
    }

    /// <summary>Verschiebt den Eintrag um eine Position und gibt seinen (ggf. neuen) Pfad zurück.</summary>
    public static string Move(string entryPath, int direction)
    {
        string parent = Path.GetDirectoryName(entryPath)!;
        var siblings = GetOrderedSiblingPaths(entryPath);

        int index = siblings.FindIndex(p => string.Equals(p, entryPath, StringComparison.OrdinalIgnoreCase));
        int target = index + direction;
        if (index < 0 || target < 0 || target >= siblings.Count) return entryPath;

        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);

        string movedPath = entryPath;
        for (int i = 0; i < siblings.Count; i++)
        {
            string oldPath = siblings[i];
            string baseName = OrderPrefixHelper.Strip(Path.GetFileName(oldPath));
            string newPath = Path.Combine(parent, $"{i + 1:00} {baseName}");

            if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) continue;

            if (Directory.Exists(oldPath)) Directory.Move(oldPath, newPath);
            else File.Move(oldPath, newPath);

            IconCache.Invalidate(oldPath);

            if (string.Equals(oldPath, entryPath, StringComparison.OrdinalIgnoreCase))
                movedPath = newPath;
        }

        return movedPath;
    }

    private static List<string> GetOrderedSiblingPaths(string entryPath)
        => [.. MenuFs.GetOrderedEntries(Path.GetDirectoryName(entryPath)!).Select(f => f.FullName)];
}

/// <summary>Baut aus einer Ordnerstruktur ein hierarchisches Menü.</summary>
internal static class MenuBuilder
{
    private const int MaxDepth = 8;

    // Deutlich größer als die WinForms-Standardwerte (~9pt Text, 16px Icons),
    // wie gewünscht - besser greifbar bei Taskleisten-Klicks per Maus/Touch.
    private static readonly Font MenuFont = new("Segoe UI", 11.5f);
    private static readonly Size MenuImageSize = new(28, 28);

    public static ContextMenuStrip Build(string root, bool showExit)
    {
        LegacyCodeCleanup.StripAll(root);

        var menu = new ContextMenuStrip
        {
            Font = MenuFont,
            ImageScalingSize = MenuImageSize,
            ShowImageMargin = true,
            RenderMode = ToolStripRenderMode.System
        };

        menu.Items.Add(CreatePasteItem(root));
        menu.Items.Add(new ToolStripSeparator());

        var items = BuildItems(root, 0);
        if (items.Length == 0)
            menu.Items.Add(Style(new ToolStripMenuItem("(Menü-Ordner ist leer)") { Enabled = false }));
        else
            menu.Items.AddRange(items);

        menu.Items.Add(new ToolStripSeparator());

        var open = Style(new ToolStripMenuItem("Menü-Ordner öffnen…"));
        open.Click += (_, _) => Launcher.Open(Program.MenuRoot);
        menu.Items.Add(open);

        // Steuert den *dauerhaften* Standardmodus: ist er aktiv, öffnet ein
        // Klick auf das Taskleisten-Icon künftig das Kachelraster
        // (TileGridForm) statt dieses klassischen Menüs.
        var expandedView = Style(new ToolStripMenuItem("Erweiterte Ansicht (Kachelraster)")
        {
            CheckOnClick = true,
            Checked = Settings.ExpandedView
        });
        expandedView.Click += (_, _) => Settings.ExpandedView = expandedView.Checked;
        menu.Items.Add(expandedView);

        if (showExit)
        {
            var exit = Style(new ToolStripMenuItem("Beenden"));
            exit.Click += (_, _) => Application.Exit();
            menu.Items.Add(exit);
        }

        return menu;
    }

    /// <summary>Normale, vollständig verschachtelte Darstellung.</summary>
    private static ToolStripItem[] BuildItems(string path, int depth)
    {
        var result = new List<ToolStripItem>();

        try
        {
            foreach (var entry in MenuFs.GetOrderedEntries(path))
            {
                if (entry is DirectoryInfo sub)
                {
                    result.Add(BuildFolderItem(sub, depth));
                }
                else if (entry is FileInfo file)
                {
                    var leaf = BuildLeafOrSeparator(file);
                    if (leaf is not null) result.Add(leaf);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            result.Add(Style(new ToolStripMenuItem("(kein Zugriff)") { Enabled = false }));
        }

        return [.. result];
    }

    private static ToolStripMenuItem BuildFolderItem(DirectoryInfo sub, int depth)
    {
        var item = Style(new ToolStripMenuItem(DisplayName(sub.Name))
        {
            Image = IconCache.Get(sub.FullName)
        });
        item.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) EntryEditForm.Show(sub.FullName, isFolder: true);
        };

        var dropDown = new ToolStripDropDownMenu
        {
            Font = MenuFont,
            ImageScalingSize = MenuImageSize,
            ShowImageMargin = true,
            RenderMode = ToolStripRenderMode.System
        };
        dropDown.Items.Add(CreatePasteItem(sub.FullName));
        dropDown.Items.Add(new ToolStripSeparator());

        var children = depth < MaxDepth ? BuildItems(sub.FullName, depth + 1) : [];
        if (children.Length == 0)
            dropDown.Items.Add(Style(new ToolStripMenuItem("(leer)") { Enabled = false }));
        else
            dropDown.Items.AddRange(children);

        item.DropDown = dropDown;
        return item;
    }

    /// <summary>Erzeugt einen Menüeintrag für eine Datei, oder eine Trennlinie bei "---"-Dateien.</summary>
    private static ToolStripItem? BuildLeafOrSeparator(FileInfo file)
    {
        if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return null;

        var bare = Path.GetFileNameWithoutExtension(file.Name);
        if (bare.Length > 0 && bare.All(c => c == '-')) return new ToolStripSeparator();

        string target = file.FullName;
        var item = Style(new ToolStripMenuItem(DisplayName(file.Name))
        {
            Image = IconCache.Get(target),
            ToolTipText = target
        });
        item.Click += (_, _) => { UsageStats.RecordOpen(Program.MenuRoot, target); Launcher.Open(target); };
        item.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) EntryEditForm.Show(target, isFolder: false);
        };
        return item;
    }

    private static ToolStripMenuItem CreatePasteItem(string targetFolder)
    {
        var item = Style(new ToolStripMenuItem("Aus Zwischenablage einfügen"));
        item.Click += (_, _) => ClipboardPaste.PasteInto(targetFolder);
        return item;
    }

    private static ToolStripMenuItem Style(ToolStripMenuItem item)
    {
        item.Padding = new Padding(4, 6, 4, 6);
        return item;
    }

    /// <summary>Öffentlich, damit z. B. EntryEditForm dieselbe Anzeigelogik für
    /// Meldungen wiederverwenden kann (z. B. im Suchschlitz).</summary>
    public static string DisplayName(string name)
    {
        var ext = Path.GetExtension(name);
        bool isShortcutType = ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                               ext.Equals(".url", StringComparison.OrdinalIgnoreCase);

        string withoutExt = Path.GetFileNameWithoutExtension(name);
        withoutExt = OrderPrefixHelper.Strip(withoutExt);

        // Endung nur bei Verknüpfungen entfernen - bei echten Dateien ist sie
        // eine nützliche Information.
        string shown = isShortcutType ? withoutExt : withoutExt + ext;
        return shown.Replace("&", "&&"); // & sonst als Tastenkürzel interpretiert
    }
}

/// <summary>
/// "Erweiterte Ansicht": ein eigenes Popup-Fenster statt eines Menüs - alle
/// Einträge der aktuellen Ebene als große Kacheln in einem Raster (bis zu
/// 9 Spalten/Reihen sichtbar, darüber hinaus scrollbar). Klick auf eine
/// Datei-Kachel öffnet sie, Klick auf eine Ordner-Kachel navigiert im
/// selben Fenster hinein. Rechtsklick bearbeitet wie im normalen Menü.
/// </summary>
internal sealed class TileGridForm : Form
{
    private const int Columns = 9;
    private const int MaxVisibleRows = 9;
    private const int TileSize = 96;
    private const int TileGap = 10;
    private const int HeaderHeight = 40;
    private const int IconSize = 34;

    // Helles, Windows-11-typisches Theme statt der ursprünglichen dunklen
    // Variante - reine Geschmacksentscheidung, kein technischer Zwang.
    private static readonly Color BackgroundColor = Color.FromArgb(246, 246, 248);
    private static readonly Color HeaderColor = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(224, 224, 228);
    private static readonly Color TileColor = Color.White;
    private static readonly Color TextColor = Color.FromArgb(32, 32, 32);
    private static readonly Color MutedTextColor = Color.FromArgb(96, 96, 100);

    private readonly string _root;
    private readonly FlowLayoutPanel _flow;
    private readonly Label _pathLabel;
    private readonly Button _back;
    private string _currentFolder;
    private string _filterQuery = "";

    private TileGridForm(string root)
    {
        _root = root;
        _currentFolder = root;

        FormBorderStyle = FormBorderStyle.None;
        BackColor = BackgroundColor;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;

        var header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = HeaderColor };
        var headerBorder = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = BorderColor };
        header.Controls.Add(headerBorder);

        _back = new Button
        {
            Text = "⬅ Zurück",
            FlatStyle = FlatStyle.Flat,
            ForeColor = TextColor,
            BackColor = HeaderColor,
            Location = new Point(6, 5),
            Size = new Size(90, 30)
        };
        _back.FlatAppearance.BorderSize = 0;
        _back.Click += (_, _) => NavigateUp();

        _pathLabel = new Label
        {
            AutoSize = true,
            ForeColor = MutedTextColor,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(106, 11)
        };

        var paste = new Button
        {
            Text = "Einfügen",
            FlatStyle = FlatStyle.Flat,
            ForeColor = TextColor,
            BackColor = HeaderColor,
            Size = new Size(90, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        paste.FlatAppearance.BorderSize = 0;
        paste.Click += (_, _) => { ClipboardPaste.PasteInto(_currentFolder); RefreshTiles(); };

        header.Controls.Add(_back);
        header.Controls.Add(_pathLabel);
        header.Controls.Add(paste);
        header.Resize += (_, _) => paste.Location = new Point(header.Width - paste.Width - 6, 5);

        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = BackgroundColor,
            Padding = new Padding(TileGap)
        };

        Controls.Add(_flow);
        Controls.Add(header);

        Deactivate += (_, _) => Close();

        // Tippen bei geöffnetem Kachelraster filtert live, ganz ohne eigenes
        // Eingabefeld - wie beim Windows-Startmenü. KeyPreview sorgt dafür,
        // dass die Form Tastendrücke bekommt, obwohl kein Steuerelement
        // fokussiert ist.
        KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar)) return;
            _filterQuery += e.KeyChar;
            RefreshTiles();
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (_filterQuery.Length > 0)
                {
                    _filterQuery = "";
                    RefreshTiles();
                }
                else
                {
                    Close();
                }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Back && _filterQuery.Length > 0)
            {
                _filterQuery = _filterQuery[..^1];
                RefreshTiles();
                e.Handled = true;
            }
        };

        Load += (_, _) => RefreshTiles();
    }

    public static void ShowAt(string root, Point cursorPos)
    {
        var form = new TileGridForm(root);
        var area = Screen.FromPoint(cursorPos).WorkingArea;

        int width = Math.Min(Columns * (TileSize + TileGap) + TileGap, area.Width - 20);
        int height = Math.Min(HeaderHeight + MaxVisibleRows * (TileSize + TileGap) + TileGap, area.Height - 20);

        bool above = cursorPos.Y > area.Top + area.Height / 2;
        int x = Math.Clamp(cursorPos.X, area.Left, area.Right - width);
        int y = above ? cursorPos.Y - height : cursorPos.Y;
        y = Math.Clamp(y, area.Top, area.Bottom - height);

        form.Size = new Size(width, height);
        form.Location = new Point(x, y);
        form.Show();
        form.Activate();
    }

    private void NavigateUp()
    {
        if (string.Equals(_currentFolder, _root, StringComparison.OrdinalIgnoreCase)) return;
        _currentFolder = Path.GetDirectoryName(_currentFolder) ?? _root;
        _filterQuery = "";
        RefreshTiles();
    }

    private bool IsAtRoot => string.Equals(_currentFolder, _root, StringComparison.OrdinalIgnoreCase);

    private bool Matches(string name) =>
        _filterQuery.Length == 0 || name.Contains(_filterQuery, StringComparison.OrdinalIgnoreCase);

    private void RefreshTiles()
    {
        LegacyCodeCleanup.StripAll(_root);

        _pathLabel.Text = GetRelativeLabel() +
            (_filterQuery.Length > 0 ? $"   🔎 {_filterQuery}" : "");
        _back.Enabled = !IsAtRoot;

        _flow.SuspendLayout();
        var oldControls = _flow.Controls.Cast<Control>().ToArray();
        _flow.Controls.Clear();
        foreach (Control c in oldControls) c.Dispose();

        // Auf der Wurzelebene sind Ordner keine eigenen Klick-Kacheln mehr -
        // ihr Inhalt steht direkt und gruppiert im Raster, ganz ohne
        // Reinklicken. Tiefere Ebenen navigiert man wie gewohnt hinein.
        if (IsAtRoot)
            BuildGroupedRoot();
        else
            BuildSingleFolder(_currentFolder);

        _flow.ResumeLayout();
    }

    private void BuildGroupedRoot()
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = MenuFs.GetOrderedEntries(_root);
        }
        catch (UnauthorizedAccessException)
        {
            _flow.Controls.Add(InfoLabel("Kein Zugriff."));
            return;
        }

        var looseFiles = FilterVisible(entries.Where(e => e is FileInfo))
            .Where(f => Matches(MenuBuilder.DisplayName(f.Name)))
            .ToList();
        var folders = entries.OfType<DirectoryInfo>().ToList();

        // Meistgenutzte Gruppen zuerst - noch ungenutzte Ordner (Summe 0)
        // fallen alphabetisch dahinter, statt zufällig durcheinander zu wirken.
        var orderedFolders = folders
            .OrderByDescending(f => UsageStats.GetGroupTotal(_root, f.FullName))
            .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Erst alle sichtbaren Abschnitte sammeln, dann mit Trennlinien
        // dazwischen rendern - vermeidet fehleranfällige Index-Sonderfälle
        // für "war das der letzte sichtbare Abschnitt?".
        var sections = new List<Action>();

        if (looseFiles.Count > 0)
            sections.Add(() => _flow.Controls.Add(BuildTileRow(looseFiles)));

        foreach (var folder in orderedFolders)
        {
            bool folderNameMatches = Matches(MenuBuilder.DisplayName(folder.Name));

            List<FileSystemInfo> children;
            try
            {
                children = FilterVisible(MenuFs.GetOrderedEntries(folder.FullName));
            }
            catch (UnauthorizedAccessException)
            {
                if (_filterQuery.Length > 0 && !folderNameMatches) continue;
                sections.Add(() =>
                {
                    _flow.Controls.Add(BuildGroupHeader(folder));
                    _flow.Controls.Add(InfoLabel("Kein Zugriff."));
                });
                continue;
            }

            // Passt der Ordnername selbst zum Filter, zeigen wir seinen
            // ganzen Inhalt (praktisch, um eine Kategorie per Namen
            // aufzurufen); sonst nur die einzeln passenden Einträge.
            var visibleChildren = folderNameMatches
                ? children
                : [.. children.Where(c => Matches(MenuBuilder.DisplayName(c.Name)))];

            if (_filterQuery.Length > 0 && visibleChildren.Count == 0) continue;

            sections.Add(() =>
            {
                _flow.Controls.Add(BuildGroupHeader(folder));
                _flow.Controls.Add(visibleChildren.Count == 0
                    ? InfoLabel("(leer)")
                    : BuildTileRow(visibleChildren));
            });
        }

        if (sections.Count == 0)
        {
            _flow.Controls.Add(InfoLabel(_filterQuery.Length > 0 ? "Keine Treffer." : "Dieser Ordner ist leer."));
            return;
        }

        for (int i = 0; i < sections.Count; i++)
        {
            sections[i]();
            if (i < sections.Count - 1) _flow.Controls.Add(BuildSeparator());
        }
    }

    private Panel BuildSeparator()
    {
        int width = Math.Max(TileSize, _flow.ClientSize.Width - TileGap * 2 - SystemInformation.VerticalScrollBarWidth);
        return new Panel
        {
            Size = new Size(width, 1),
            BackColor = BorderColor,
            Margin = new Padding(0, 4, 0, 4)
        };
    }

    private void BuildSingleFolder(string folder)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = MenuFs.GetOrderedEntries(folder);
        }
        catch (UnauthorizedAccessException)
        {
            _flow.Controls.Add(InfoLabel("Kein Zugriff."));
            return;
        }

        var visible = FilterVisible(entries)
            .Where(e => Matches(MenuBuilder.DisplayName(e.Name)))
            .ToList();

        _flow.Controls.Add(visible.Count == 0
            ? InfoLabel(_filterQuery.Length > 0 ? "Keine Treffer." : "Dieser Ordner ist leer.")
            : BuildTileRow(visible));
    }

    private static List<FileSystemInfo> FilterVisible(IEnumerable<FileSystemInfo> entries) =>
        [.. entries
            .Where(e => e is DirectoryInfo || !MenuFs.IsSeparatorFile((FileInfo)e))
            .Where(e => !(e is FileInfo f && f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))];

    /// <summary>Ein horizontal umbrechendes Kachel-"Regal" mit fester Breite
    /// (= sichtbare Fensterbreite), damit es tatsächlich mehrzeilig umbricht
    /// statt einfach immer breiter zu werden.</summary>
    private FlowLayoutPanel BuildTileRow(IEnumerable<FileSystemInfo> entries)
    {
        // Vorsorglich um die Breite eines Scrollbalkens reduziert, damit
        // Zeilen nicht knapp überlaufen, sobald das Raster hoch genug für
        // einen vertikalen Scrollbalken wird.
        int availableWidth = Math.Max(TileSize + TileGap * 2,
            _flow.ClientSize.Width - TileGap * 2 - SystemInformation.VerticalScrollBarWidth);
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(availableWidth, 0),
            BackColor = BackgroundColor,
            Margin = new Padding(0, 0, 0, TileGap)
        };
        foreach (var entry in entries) row.Controls.Add(BuildTile(entry));
        return row;
    }

    private Label BuildGroupHeader(DirectoryInfo folder)
    {
        var header = new Label
        {
            Text = MenuBuilder.DisplayName(folder.Name),
            AutoSize = true,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Margin = new Padding(2, 16, 0, 8)
        };
        header.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            EntryEditForm.Show(folder.FullName, isFolder: true);
            RefreshTiles();
        };
        return header;
    }

    private string GetRelativeLabel()
    {
        if (string.Equals(_currentFolder, _root, StringComparison.OrdinalIgnoreCase)) return "Menü";
        string rel = Path.GetRelativePath(_root, _currentFolder).Replace('\\', '›');
        return string.Join('›', rel.Split('›').Select(MenuBuilder.DisplayName));
    }

    private static Label InfoLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = MutedTextColor,
        Font = new Font("Segoe UI", 10f),
        Location = new Point(TileGap, TileGap)
    };

    private Control BuildTile(FileSystemInfo entry)
    {
        bool isFolder = entry is DirectoryInfo;
        string path = entry.FullName;
        string label = MenuBuilder.DisplayName(entry.Name);

        var tile = new Panel
        {
            Size = new Size(TileSize, TileSize),
            Margin = new Padding(TileGap / 2),
            BackColor = TileColor,
            Cursor = Cursors.Hand
        };
        tile.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, tile.Width - 1, tile.Height - 1);
        };

        var icon = new PictureBox
        {
            Image = IconCache.Get(path),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(IconSize, IconSize),
            Location = new Point((TileSize - IconSize) / 2, 14),
            BackColor = Color.Transparent
        };

        var caption = new Label
        {
            Text = label,
            ForeColor = TextColor,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(2, 14 + IconSize + 6),
            Size = new Size(TileSize - 4, TileSize - (14 + IconSize + 6) - 2),
            AutoEllipsis = true
        };

        tile.Controls.Add(icon);
        tile.Controls.Add(caption);

        void OnMouseUp(object? _, MouseEventArgs e)
        {
            // Wichtig: als ein einziger MouseUp-Handler statt Click+MouseUp -
            // Control.Click feuert bei einer normalen Control (Panel/Label/
            // PictureBox) für JEDE Maustaste, nicht nur links. Getrennte
            // Handler hätten bei Rechtsklick sowohl den Bearbeiten-Dialog
            // als auch das Öffnen ausgelöst.
            if (e.Button == MouseButtons.Left)
            {
                if (isFolder)
                {
                    _currentFolder = path;
                    _filterQuery = "";
                    RefreshTiles();
                }
                else
                {
                    Close();
                    UsageStats.RecordOpen(_root, path);
                    Launcher.Open(path);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                EntryEditForm.Show(path, isFolder);
                RefreshTiles();
            }
        }

        foreach (Control c in new Control[] { tile, icon, caption })
            c.MouseUp += OnMouseUp;

        return tile;
    }

    // Kein eigener Dispose-Override nötig - Form.Dispose räumt die
    // Controls-Hierarchie (inklusive aller Kacheln in _flow) bereits ab.
}

/// <summary>
/// Echtzeit-Suchschlitz (Win+Alt+Y): Eingabefeld direkt fokussiert, jeder
/// Tastendruck filtert live über alle Dateien der gesamten Menü-Hierarchie
/// (rekursiv, unabhängig von der Verschachtelungstiefe). Pfeiltasten zum
/// Navigieren, Enter öffnet den markierten Treffer.
/// </summary>
internal sealed class SearchForm : Form
{
    private const int MaxResults = 30;
    private const int RowHeight = 40;

    private static readonly Color BackgroundColor = Color.FromArgb(32, 32, 36);
    private static readonly Color InputColor = Color.FromArgb(24, 24, 28);
    private static readonly Color RowSelectedColor = Color.FromArgb(50, 50, 58);

    private readonly string _root;
    private readonly TextBox _searchBox;
    private readonly ListBox _results;
    private List<FileInfo> _matches = [];

    private SearchForm(string root)
    {
        _root = root;

        FormBorderStyle = FormBorderStyle.None;
        BackColor = BackgroundColor;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;

        _searchBox = new TextBox
        {
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 14f),
            BackColor = InputColor,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            PlaceholderText = "Tippen zum Suchen…"
        };
        _searchBox.Height = _searchBox.PreferredHeight + 16;

        _results = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = RowHeight,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 10.5f)
        };
        _results.DrawItem += ResultsOnDrawItem;
        _results.MouseDown += (_, e) =>
        {
            int index = _results.IndexFromPoint(e.Location);
            if (index < 0) return;
            _results.SelectedIndex = index;
            OpenSelected();
        };

        _searchBox.TextChanged += (_, _) => RunSearch();
        _searchBox.KeyDown += SearchBoxOnKeyDown;

        Controls.Add(_results);
        Controls.Add(_searchBox);

        Deactivate += (_, _) => Close();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        Load += (_, _) => RunSearch();
    }

    public static void ShowAt(string root, Point cursorPos)
    {
        var form = new SearchForm(root);
        var area = Screen.FromPoint(cursorPos).WorkingArea;

        int width = Math.Min(520, area.Width - 20);
        int height = Math.Min(480, area.Height - 20);

        bool above = cursorPos.Y > area.Top + area.Height / 2;
        int x = Math.Clamp(cursorPos.X, area.Left, area.Right - width);
        int y = above ? cursorPos.Y - height : cursorPos.Y;
        y = Math.Clamp(y, area.Top, area.Bottom - height);

        form.Size = new Size(width, height);
        form.Location = new Point(x, y);
        form.Show();
        form.Activate();
        form._searchBox.Focus();
    }

    private void RunSearch()
    {
        string query = _searchBox.Text.Trim();

        if (query.Length == 0)
        {
            _matches = [];
        }
        else
        {
            List<FileInfo> files;
            try
            {
                files = [.. Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                                      .Select(p => new FileInfo(p))
                                      .Where(MenuFs.IsVisible)
                                      .Where(f => !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                                      .Where(f => !MenuFs.IsSeparatorFile(f))];
            }
            catch (UnauthorizedAccessException)
            {
                files = [];
            }

            _matches = files
                .Select(f => (File: f, Name: MenuBuilder.DisplayName(f.Name)))
                .Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxResults)
                .Select(x => x.File)
                .ToList();
        }

        _results.BeginUpdate();
        _results.Items.Clear();
        foreach (var f in _matches) _results.Items.Add(f.FullName);
        _results.EndUpdate();

        if (_results.Items.Count > 0) _results.SelectedIndex = 0;
    }

    private void ResultsOnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _matches.Count) { e.DrawBackground(); return; }

        var file = _matches[e.Index];
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var bg = new SolidBrush(selected ? RowSelectedColor : BackgroundColor))
            e.Graphics.FillRectangle(bg, e.Bounds);

        var icon = IconCache.Get(file.FullName);
        if (icon is not null)
            e.Graphics.DrawImage(icon, new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 6, 28, 28));

        string name = MenuBuilder.DisplayName(file.Name);
        string relDir = Path.GetRelativePath(_root, file.DirectoryName ?? _root);
        string path = relDir == "." ? "" : string.Join(" › ", relDir.Split(Path.DirectorySeparatorChar).Select(MenuBuilder.DisplayName));

        using var nameBrush = new SolidBrush(Color.White);
        using var pathBrush = new SolidBrush(Color.Gainsboro);
        using var pathFont = new Font(_results.Font.FontFamily, 8f);

        e.Graphics.DrawString(name, _results.Font, nameBrush, e.Bounds.X + 44, e.Bounds.Y + 3);
        if (path.Length > 0)
            e.Graphics.DrawString(path, pathFont, pathBrush, e.Bounds.X + 44, e.Bounds.Y + 22);
    }

    private void SearchBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down:
                if (_results.Items.Count > 0)
                    _results.SelectedIndex = Math.Min(_results.SelectedIndex + 1, _results.Items.Count - 1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Up:
                if (_results.Items.Count > 0)
                    _results.SelectedIndex = Math.Max(_results.SelectedIndex - 1, 0);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Enter:
                OpenSelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
        }
    }

    private void OpenSelected()
    {
        if (_results.SelectedIndex < 0 || _results.SelectedIndex >= _matches.Count) return;
        string target = _matches[_results.SelectedIndex].FullName;
        Close();
        Launcher.Open(target);
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

/// <summary>
/// Erstellt aus dem aktuellen Zwischenablage-Inhalt eine neue Verknüpfung im
/// angegebenen Menü-Ordner: kopierte Dateien/Ordner aus dem Explorer werden
/// zu .lnk-Verknüpfungen, kopierte .lnk/.url-Dateien werden direkt
/// übernommen, kopierter Text mit einer URL wird zu einer .url-Verknüpfung
/// (nach kurzer Namensabfrage).
/// </summary>
internal static class ClipboardPaste
{
    public static void PasteInto(string targetFolder)
    {
        try
        {
            Directory.CreateDirectory(targetFolder);

            if (Clipboard.ContainsFileDropList())
            {
                var created = new List<string>();
                foreach (string? path in Clipboard.GetFileDropList())
                {
                    if (string.IsNullOrWhiteSpace(path) || !(File.Exists(path) || Directory.Exists(path))) continue;
                    created.Add(PasteFile(targetFolder, path));
                }
                Report(created);
                return;
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();

                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFtp))
                {
                    string suggested = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                        ? uri.Host[4..] : uri.Host;
                    string? name = NamePromptForm.Ask("Verknüpfung benennen", suggested);
                    if (name is null) return; // abgebrochen

                    string path = UniquePath(targetFolder, name, ".url");
                    File.WriteAllText(path, $"[InternetShortcut]\r\nURL={text}\r\n");
                    Report([path]);
                    return;
                }

                if (File.Exists(text) || Directory.Exists(text))
                {
                    Report([PasteFile(targetFolder, text)]);
                    return;
                }
            }

            MessageBox.Show(
                "Die Zwischenablage enthält weder eine kopierte Datei/Verknüpfung noch einen Link.",
                "Einfügen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Einfügen fehlgeschlagen:\n{ex.Message}", "Einfügen",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string PasteFile(string targetFolder, string sourcePath)
    {
        string ext = Path.GetExtension(sourcePath);
        bool alreadyLink = ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".url", StringComparison.OrdinalIgnoreCase);

        if (alreadyLink)
        {
            string dest = UniquePath(targetFolder, Path.GetFileNameWithoutExtension(sourcePath), ext);
            File.Copy(sourcePath, dest);
            return dest;
        }

        string lnkDest = UniquePath(targetFolder, Path.GetFileNameWithoutExtension(sourcePath), ".lnk");
        ShortcutFactory.CreateLnk(lnkDest, sourcePath);
        return lnkDest;
    }

    private static string UniquePath(string folder, string baseName, string extension)
    {
        baseName = SanitizeFileName(baseName);
        string path = Path.Combine(folder, baseName + extension);
        for (int n = 2; File.Exists(path) || Directory.Exists(path); n++)
            path = Path.Combine(folder, $"{baseName} ({n}){extension}");
        return path;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "Verknüpfung" : name;
    }

    private static void Report(IReadOnlyCollection<string> created)
    {
        if (created.Count == 0) return;
        string list = string.Join("\n", created.Select(Path.GetFileName));
        MessageBox.Show($"Erstellt:\n{list}", "Einfügen", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

/// <summary>Erzeugt/liest .lnk-Verknüpfungen über die WScript.Shell-COM-Automation
/// (Bestandteil jeder Windows-Installation) - ohne zusätzliche Abhängigkeiten.</summary>
internal static class ShortcutFactory
{
    public static void CreateLnk(string lnkPath, string targetPath)
    {
        WithShortcut(lnkPath, (shortcutType, shortcut) =>
        {
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetPath]);

            string? workingDir = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(workingDir))
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [workingDir]);

            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        });
    }

    public static string? ReadTargetPath(string lnkPath)
    {
        string? result = null;
        WithShortcut(lnkPath, (shortcutType, shortcut) =>
        {
            result = shortcutType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
        });
        return result;
    }

    private static void WithShortcut(string lnkPath, Action<Type, object> action)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell (Windows Script Host) ist nicht verfügbar.");

        object shell = Activator.CreateInstance(shellType)!;
        try
        {
            object shortcut = shellType.InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod, null, shell, [lnkPath])!;
            try
            {
                action(shortcut.GetType(), shortcut);
            }
            finally
            {
                Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }
}

/// <summary>Kompakter Eingabedialog, z. B. für den Namen einer neuen Verknüpfung.</summary>
internal sealed class NamePromptForm : Form
{
    private readonly TextBox _textBox;

    private NamePromptForm(string title, string defaultValue)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(360, 92);

        var label = new Label { Text = "Name:", AutoSize = true, Location = new Point(12, 15) };
        _textBox = new TextBox { Location = new Point(12, 35), Width = 336, Text = defaultValue };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(192, 58), Width = 75 };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(273, 58), Width = 75 };

        Controls.AddRange([label, _textBox, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) => { _textBox.Focus(); _textBox.SelectAll(); };
    }

    public static string? Ask(string title, string defaultValue)
    {
        using var form = new NamePromptForm(title, defaultValue);
        return form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(form._textBox.Text)
            ? form._textBox.Text.Trim()
            : null;
    }
}

/// <summary>
/// Rechtsklick-Dialog für einen bestehenden Menüeintrag: umbenennen, bei
/// Verknüpfungen (.lnk) oder Links (.url) das Ziel ändern, manuell
/// rauf/runter sortieren, oder löschen.
/// </summary>
internal sealed class EntryEditForm : Form
{
    private readonly TextBox _nameBox;
    private readonly TextBox? _targetBox;
    private readonly Button _up;
    private readonly Button _down;
    private readonly bool _isFolder;
    private readonly string _extension;
    private string _currentPath;
    private string _orderPrefix;
    private bool _deleted;

    private EntryEditForm(string path, bool isFolder)
    {
        _currentPath = path;
        _isFolder = isFolder;
        _extension = isFolder ? "" : Path.GetExtension(path);
        _orderPrefix = OrderPrefixHelper.Extract(isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path));

        bool isShortcut = !isFolder &&
            (_extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
             _extension.Equals(".url", StringComparison.OrdinalIgnoreCase));

        Text = isFolder ? "Ordner bearbeiten" : "Eintrag bearbeiten";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(440, 300); // wird unten anhand der tatsächlich vorhandenen Zeilen korrigiert

        var nameLabel = new Label { Text = "Name:", AutoSize = true, Location = new Point(12, 15) };
        string rawBase = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        _nameBox = new TextBox
        {
            Location = new Point(12, 35),
            Width = 356,
            Height = 23,
            Text = OrderPrefixHelper.Strip(rawBase)
        };

        _up = new Button { Text = "▲", Location = new Point(374, 35), Width = 26, Height = 23 };
        _down = new Button { Text = "▼", Location = new Point(402, 35), Width = 26, Height = 23 };
        _up.Click += (_, _) => { _currentPath = MenuOrder.Move(_currentPath, -1); RefreshAfterMove(); };
        _down.Click += (_, _) => { _currentPath = MenuOrder.Move(_currentPath, 1); RefreshAfterMove(); };

        Controls.AddRange([nameLabel, _nameBox, _up, _down]);

        int y = 68;

        if (isShortcut)
        {
            bool isUrl = _extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
            var targetLabel = new Label { Text = isUrl ? "Link:" : "Ziel:", AutoSize = true, Location = new Point(12, y) };
            y += 20;
            _targetBox = new TextBox
            {
                Location = new Point(12, y),
                Width = isUrl ? 416 : 382,
                Height = 23,
                Text = ReadTarget(path, _extension)
            };
            Controls.Add(targetLabel);
            Controls.Add(_targetBox);

            if (!isUrl)
            {
                var browse = new Button { Text = "…", Location = new Point(398, y - 1), Width = 30, Height = 25 };
                browse.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Title = "Ziel auswählen" };
                    if (dlg.ShowDialog(this) == DialogResult.OK) _targetBox.Text = dlg.FileName;
                };
                Controls.Add(browse);
            }

            y += 36;
        }

        int buttonsY = y;
        ClientSize = new Size(440, buttonsY + 46);

        var save = new Button { Text = "Speichern", DialogResult = DialogResult.OK, Location = new Point(160, buttonsY), Width = 90 };
        var delete = new Button { Text = "Löschen", Location = new Point(254, buttonsY), Width = 80 };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(338, buttonsY), Width = 80 };

        delete.Click += (_, _) =>
        {
            string what = _isFolder ? "diesen Ordner samt Inhalt" : "diesen Eintrag";
            if (MessageBox.Show(this, $"{what} wirklich löschen?", "Löschen",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                if (_isFolder) Directory.Delete(_currentPath, recursive: true);
                else File.Delete(_currentPath);
                IconCache.Invalidate(_currentPath);
                _deleted = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Löschen fehlgeschlagen:\n{ex.Message}", "Löschen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        Controls.AddRange([save, delete, cancel]);
        AcceptButton = save;
        CancelButton = cancel;

        Shown += (_, _) => { RefreshMoveButtons(); _nameBox.Focus(); _nameBox.SelectAll(); };
    }

    private void RefreshAfterMove()
    {
        string rawBase = _isFolder ? Path.GetFileName(_currentPath) : Path.GetFileNameWithoutExtension(_currentPath);
        _orderPrefix = OrderPrefixHelper.Extract(rawBase);
        RefreshMoveButtons();
    }

    private void RefreshMoveButtons()
    {
        _up.Enabled = MenuOrder.CanMove(_currentPath, -1);
        _down.Enabled = MenuOrder.CanMove(_currentPath, 1);
    }

    public static void Show(string path, bool isFolder)
    {
        using var form = new EntryEditForm(path, isFolder);
        if (form.ShowDialog() != DialogResult.OK || form._deleted) return;

        try
        {
            form.Apply();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Speichern fehlgeschlagen:\n{ex.Message}", "Eintrag bearbeiten",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Apply()
    {
        string newBaseName = string.IsNullOrWhiteSpace(_nameBox.Text)
            ? OrderPrefixHelper.Strip(Path.GetFileNameWithoutExtension(_currentPath))
            : SanitizeFileName(_nameBox.Text.Trim());

        string parent = Path.GetDirectoryName(_currentPath)!;
        string newFileName = _orderPrefix + newBaseName + (_isFolder ? "" : _extension);
        string newPath = Path.Combine(parent, newFileName);

        if (!string.Equals(_currentPath, newPath, StringComparison.Ordinal))
        {
            if (_isFolder) Directory.Move(_currentPath, newPath);
            else File.Move(_currentPath, newPath);
            IconCache.Invalidate(_currentPath);
            _currentPath = newPath;
        }

        if (_targetBox is not null)
        {
            string target = _targetBox.Text.Trim();
            if (_extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                ShortcutFactory.CreateLnk(_currentPath, target);
            else
                File.WriteAllText(_currentPath, $"[InternetShortcut]\r\nURL={target}\r\n");

            IconCache.Invalidate(_currentPath);
        }
    }

    private static string ReadTarget(string path, string extension)
    {
        try
        {
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                return ShortcutFactory.ReadTargetPath(path) ?? "";

            if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                foreach (var line in File.ReadAllLines(path))
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        return line["URL=".Length..].Trim();
        }
        catch
        {
            // Ziel konnte nicht gelesen werden - leeres Feld ist besser als ein Absturz.
        }
        return "";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Unbenannt" : name;
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

    /// <summary>Verwirft ein zwischengespeichertes Icon, z. B. nach Umbenennen/Zieländerung.</summary>
    public static void Invalidate(string path) => Cache.Remove(path);

    private static Image? Extract(string path)
    {
        var info = new SHFILEINFO();
        try
        {
            var res = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                                    SHGFI_ICON | SHGFI_LARGEICON);
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
    private const uint SHGFI_LARGEICON = 0x000000000;

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
