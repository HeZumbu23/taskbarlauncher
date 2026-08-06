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
              oder Löschen. Dort lässt sich bei Dateien auch der 3-stellige
              Schnellzugriffscode ändern (leer lassen = automatische
              Neuvergabe; ist ein Code schon vergeben, wird angezeigt an wen).
            - "Erweiterte Ansicht" (unten im Menü) zeigt den Inhalt der
              obersten Ordnerebene direkt im Hauptmenü statt in Untermenüs.

            Jede Datei bekommt automatisch einen eindeutigen 3-stelligen Code
            (unsichtbar im Dateinamen als "[123]"-Suffix, im Menü ausgeblendet).
            Win+Alt+Y öffnet das Menü mit einem bereits fokussierten
            Eingabefeld dafür - Code tippen und Enter öffnet den Eintrag
            direkt, egal wie tief er verschachtelt ist.
            Win+Alt+L öffnet das Menü direkt in der Erweiterten Ansicht.

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

    // Win+Alt+L ("L" wie Launcher) öffnet das Menü in der Erweiterten Ansicht,
    // unabhängig vom sonst eingestellten Standardmodus. Win+Alt+Y öffnet das
    // Menü ganz normal, aber mit einem bereits fokussierten Eingabefeld für
    // 3-stellige Dokument-Codes (Enter öffnet den Treffer direkt). Unter den
    // Win+Alt-Kombinationen sind nur wenige von Windows selbst belegt
    // (R/G/B/Enter/PrtScn für die Xbox Game Bar, D für Datum/Uhrzeit) -
    // L und Y sind frei.
    private const int ExpandedViewHotkeyId = 1;
    private const int CodeSearchHotkeyId = 2;
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
        RegisterHotKey(Handle, CodeSearchHotkeyId, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_Y);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, ExpandedViewHotkeyId);
        UnregisterHotKey(Handle, CodeSearchHotkeyId);
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
                try { Invoke(new Action(ShowMenu)); }
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
            ShowMenu(forceExpanded: true);
            return;
        }

        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == CodeSearchHotkeyId)
        {
            ShowMenu(showCodeBox: true);
            return;
        }

        base.WndProc(ref m);
    }

    private void ShowMenu(bool forceExpanded = false, bool showCodeBox = false)
    {
        _menu?.Dispose();
        _menu = MenuBuilder.Build(Program.MenuRoot, showExit: true, forceExpanded, showCodeBox);

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
/// Vergibt automatisch eindeutige 3-stellige Codes an alle Dokument-Dateien
/// (nicht an Ordner), unsichtbar eingebettet als " [123]"-Suffix direkt im
/// Dateinamen - dadurch bleibt der Code beim Umbenennen/manuellen Sortieren
/// automatisch erhalten, ganz ohne separate Zuordnungstabelle. Für den
/// schnellen Zugriff per Tastatur (Win+Alt+Y, siehe MainForm).
/// </summary>
internal static class CodeRegistry
{
    private static readonly Regex Suffix = new(@"\s*\[(\d{3})\]$", RegexOptions.Compiled);

    public static string? ExtractCode(string baseNameWithoutExtension)
    {
        var m = Suffix.Match(baseNameWithoutExtension);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string StripSuffix(string baseNameWithoutExtension)
        => Suffix.Replace(baseNameWithoutExtension, "");

    /// <summary>Vergibt fehlende Codes an alle Dateien unterhalb von root (rekursiv). Läuft bei jedem Menüaufbau.</summary>
    public static void EnsureAllAssigned(string root)
    {
        if (!Directory.Exists(root)) return;

        List<string> files;
        try { files = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)]; }
        catch (UnauthorizedAccessException) { return; }

        var used = new HashSet<string>();
        foreach (var file in files)
        {
            var code = ExtractCode(Path.GetFileNameWithoutExtension(file));
            if (code is not null) used.Add(code);
        }

        foreach (var file in files)
        {
            string name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

            string baseName = Path.GetFileNameWithoutExtension(file);
            string withoutOrderPrefix = OrderPrefixHelper.Strip(baseName);
            if (withoutOrderPrefix.Length > 0 && withoutOrderPrefix.All(c => c == '-')) continue; // Trennlinien-Dateien
            if (ExtractCode(baseName) is not null) continue; // hat schon einen Code

            string code = NextFreeCode(used);
            string ext = Path.GetExtension(file);
            string dir = Path.GetDirectoryName(file)!;
            string newPath = Path.Combine(dir, $"{baseName} [{code}]{ext}");

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

    public static string? FindByCode(string root, string code)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                if (ExtractCode(Path.GetFileNameWithoutExtension(file)) == code)
                    return file;
        }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static string NextFreeCode(HashSet<string> used)
    {
        string code;
        do { code = Random.Shared.Next(0, 1000).ToString("000"); }
        while (!used.Add(code));
        return code;
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

    public static ContextMenuStrip Build(string root, bool showExit, bool forceExpanded = false, bool showCodeBox = false)
    {
        CodeRegistry.EnsureAllAssigned(root);

        var menu = new ContextMenuStrip
        {
            Font = MenuFont,
            ImageScalingSize = MenuImageSize,
            ShowImageMargin = true,
            RenderMode = ToolStripRenderMode.System
        };

        if (showCodeBox)
        {
            var codeBox = new ToolStripTextBox { Width = 200 };
            codeBox.Control.PlaceholderText = "Code eingeben und Enter…";
            codeBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;

                string code = codeBox.Text.Trim();
                if (code.Length != 3 || !code.All(char.IsDigit)) return;

                string? target = CodeRegistry.FindByCode(root, code);
                if (target is null) return;

                menu.Close();
                Launcher.Open(target);
            };
            menu.Items.Add(codeBox);
            menu.Items.Add(new ToolStripSeparator());
            menu.Opened += (_, _) => codeBox.Focus();
        }

        menu.Items.Add(CreatePasteItem(root));
        menu.Items.Add(new ToolStripSeparator());

        bool expanded = forceExpanded || Settings.ExpandedView;
        if (expanded)
        {
            BuildFlatItems(root, menu.Items);
        }
        else
        {
            var items = BuildItems(root, 0);
            if (items.Length == 0)
                menu.Items.Add(Style(new ToolStripMenuItem("(Menü-Ordner ist leer)") { Enabled = false }));
            else
                menu.Items.AddRange(items);
        }

        menu.Items.Add(new ToolStripSeparator());

        var open = Style(new ToolStripMenuItem("Menü-Ordner öffnen…"));
        open.Click += (_, _) => Launcher.Open(Program.MenuRoot);
        menu.Items.Add(open);

        // Zeigt/ändert den *dauerhaften* Standardmodus. Bei einem per Hotkey
        // erzwungenen Aufruf (forceExpanded) bleibt die Checkbox daher am
        // gespeicherten Wert, nicht am für diesen Aufruf erzwungenen.
        var expandedView = Style(new ToolStripMenuItem("Erweiterte Ansicht (erste Ebene flach)")
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

    /// <summary>
    /// "Erweiterte Ansicht": die oberste Ebene wird direkt ins Hauptmenü
    /// geschrieben (fette Kopfzeile je Ordner statt Untermenü). Tiefere
    /// Ebenen bleiben normal verschachtelt.
    /// </summary>
    private static void BuildFlatItems(string root, ToolStripItemCollection target)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = MenuFs.GetOrderedEntries(root);
        }
        catch (UnauthorizedAccessException)
        {
            target.Add(Style(new ToolStripMenuItem("(kein Zugriff)") { Enabled = false }));
            return;
        }

        if (entries.Count == 0)
        {
            target.Add(Style(new ToolStripMenuItem("(Menü-Ordner ist leer)") { Enabled = false }));
            return;
        }

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo sub)
            {
                var header = Style(new ToolStripMenuItem(DisplayName(sub.Name))
                {
                    Font = new Font(MenuFont, FontStyle.Bold),
                    ForeColor = SystemColors.GrayText
                });
                header.MouseUp += (_, e) =>
                {
                    if (e.Button == MouseButtons.Right) EntryEditForm.Show(sub.FullName, isFolder: true);
                };
                target.Add(header);
                target.Add(CreatePasteItem(sub.FullName));

                var children = BuildItems(sub.FullName, 1);
                if (children.Length == 0)
                    target.Add(Style(new ToolStripMenuItem("(leer)") { Enabled = false }));
                else
                    foreach (var child in children) target.Add(child);

                target.Add(new ToolStripSeparator());
            }
            else if (entry is FileInfo file)
            {
                var leaf = BuildLeafOrSeparator(file);
                if (leaf is not null) target.Add(leaf);
            }
        }
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
        item.Click += (_, _) => Launcher.Open(target);
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
    /// Meldungen ("Code bereits vergeben an ...") wiederverwenden kann.</summary>
    public static string DisplayName(string name)
    {
        var ext = Path.GetExtension(name);
        bool isShortcutType = ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                               ext.Equals(".url", StringComparison.OrdinalIgnoreCase);

        string withoutExt = Path.GetFileNameWithoutExtension(name);
        withoutExt = OrderPrefixHelper.Strip(withoutExt);
        withoutExt = CodeRegistry.StripSuffix(withoutExt);

        // Endung nur bei Verknüpfungen entfernen - bei echten Dateien ist sie
        // eine nützliche Information.
        string shown = isShortcutType ? withoutExt : withoutExt + ext;
        return shown.Replace("&", "&&"); // & sonst als Tastenkürzel interpretiert
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
                foreach (string path in Clipboard.GetFileDropList())
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
    private readonly TextBox? _codeBox;
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
            Text = CodeRegistry.StripSuffix(OrderPrefixHelper.Strip(rawBase))
        };

        _up = new Button { Text = "▲", Location = new Point(374, 35), Width = 26, Height = 23 };
        _down = new Button { Text = "▼", Location = new Point(402, 35), Width = 26, Height = 23 };
        _up.Click += (_, _) => { _currentPath = MenuOrder.Move(_currentPath, -1); RefreshAfterMove(); };
        _down.Click += (_, _) => { _currentPath = MenuOrder.Move(_currentPath, 1); RefreshAfterMove(); };

        Controls.AddRange([nameLabel, _nameBox, _up, _down]);

        int y = 68;

        if (!isFolder)
        {
            string currentCode = CodeRegistry.ExtractCode(OrderPrefixHelper.Strip(Path.GetFileNameWithoutExtension(path))) ?? "";
            var codeLabel = new Label { Text = "Code:", AutoSize = true, Location = new Point(12, y + 3) };
            _codeBox = new TextBox { Location = new Point(60, y), Width = 50, Height = 23, MaxLength = 3, Text = currentCode };
            var codeHint = new Label
            {
                Text = "3 Ziffern - leer lassen für automatische Neuvergabe",
                AutoSize = true,
                Location = new Point(120, y + 4),
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 8f)
            };
            Controls.AddRange([codeLabel, _codeBox, codeHint]);
            y += 33;
        }

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
            ? CodeRegistry.StripSuffix(OrderPrefixHelper.Strip(Path.GetFileNameWithoutExtension(_currentPath)))
            : SanitizeFileName(_nameBox.Text.Trim());

        string codeSuffix = "";
        if (_codeBox is not null)
        {
            string enteredCode = _codeBox.Text.Trim();
            if (enteredCode.Length > 0)
            {
                if (enteredCode.Length != 3 || !enteredCode.All(char.IsDigit))
                    throw new InvalidOperationException("Der Code muss aus genau 3 Ziffern bestehen (oder leer bleiben für automatische Neuvergabe).");

                string? owner = CodeRegistry.FindByCode(Program.MenuRoot, enteredCode);
                if (owner is not null && !string.Equals(owner, _currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    string ownerLabel = MenuBuilder.DisplayName(Path.GetFileName(owner));
                    throw new InvalidOperationException($"Code {enteredCode} ist bereits vergeben an \"{ownerLabel}\".");
                }

                codeSuffix = $" [{enteredCode}]";
            }
        }

        string parent = Path.GetDirectoryName(_currentPath)!;
        string newFileName = _orderPrefix + newBaseName + codeSuffix + (_isFolder ? "" : _extension);
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
