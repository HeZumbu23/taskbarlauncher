# TaskbarLauncher

**A normal Windows app that turns a folder tree into a fast, tile-based launcher.**

No config file, no database — a folder *is* the menu. Organize it exactly like any other folder on your PC, pin the app to your taskbar like any other program, and it opens instantly as a real window showing everything as big, categorized tiles.

```
%AppData%\TaskbarLauncher\Menue\
├── 01 Projects\
│   ├── Client Report.lnk
│   └── Roadmap.url
├── 02 Links\
│   └── Design System.url
└── Notes.txt
```

→ open the app → the same tree, as categorized tiles.

---

## Why this exists

Windows has no built-in way to turn a folder tree into a fast, glanceable launcher. TaskbarLauncher is a small, self-contained .NET 8 app that does exactly that — a completely ordinary window: pin it, click it, resize it, close it (well — minimize it, see below), just like any other program.

## Features

**📁 The filesystem is the UI**
Subfolders become categories, `.lnk`/`.url` files become tiles, any other file opens like a double-click in Explorer. Edit the folder in Explorer and the window refreshes with the latest state — no restart needed.

**🔲 Everything visible at a glance**
The top level is grouped and shown right away as compact, text-only tiles under bold category headers, separated by thin dividers — no clicking into folders required just to see what's there. Tiles are icon-free by design, to keep them small enough that more fit on screen at once. Deeper subfolders still drill in on click, with "Back" to go up. Categories are sorted by how often their contents get opened, most-used first — no setup, it just learns from usage over time. The window never scrolls: categories flow into as many side-by-side columns as fit the window's height, so a long list spreads sideways instead of requiring a scrollbar. Widen the window to reveal more columns at once.

**🔎 Type to filter, no search box**
Just start typing while the window is focused — every keystroke filters the currently visible tiles live, Windows-Start-Menu style. Backspace to edit, Escape to clear the filter (or minimize the window if the filter's already empty).

**✏️ Manage the menu from the window itself**
- **Paste from clipboard** — copy a file/folder in Explorer (or a URL), click "Einfügen" in the header, and a shortcut appears in the category you're currently viewing.
- **Right-click any tile or category header** to rename it, change its target (`.lnk`/`.url`), delete it, or move it up/down among its siblings.
- Renaming/reordering never touches the visible name — order is tracked with hidden numeric prefixes (`01 `, `02 `, …) that get normalized automatically the first time you reorder something.

**⌨️ Global hotkeys, no click needed**
`Win+Alt+L` brings the window to front exactly where you left it; `Win+Alt+Y` brings it to front *and* jumps back to the top level with an empty filter, ready to type a fresh search. See the [shortcuts table](#keyboard-shortcuts) below.

**🚀 One instance, always ready**
A named mutex keeps a second launch from ever starting a duplicate process — it just tells the running instance to come to the front instead, so a stray double-click never does nothing. Closing the window (✕) only minimizes it to the taskbar; the app keeps running so it's instantly available again.

**🪟 Jumps to what's already open instead of duplicating it**
Opening a `.exe`/`.lnk` entry whose program is already running switches to its window instead of starting a second instance, matched by the running process's actual executable path. `.url` entries just open normally in the browser — opening a link that's already in a tab is harmless there, so no extra matching needed.

---

## Getting started

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows 11.

```powershell
dotnet publish -c Release
```

The self-contained `TaskbarLauncher.exe` lands in `bin\Release\net8.0-windows\win-x64\publish\`.

### Pin it to the taskbar

Just like any other program:

1. Run `TaskbarLauncher.exe`.
2. Right-click its taskbar button → **"Pin to taskbar"**.
3. Done. Click the pinned icon any time to bring the window to front; closing it only minimizes, so it's ready to go instantly next time.

### Autostart

`Win+R` → `shell:startup` → add a shortcut to `TaskbarLauncher.exe`, and append `--startup` to its **Target** field:

```
"C:\Path\to\TaskbarLauncher.exe" --startup
```

This makes the app start minimized on login instead of popping its window open immediately.

### Deploy script

For local development: `deploy.ps1` deploys immediately on startup, then keeps watching the current branch and redeploys automatically whenever new commits land — no manual "pull, rebuild, relaunch" cycle.

```powershell
.\deploy.ps1                      # watch, checking every 60s (default)
.\deploy.ps1 -IntervalSeconds 15  # check more often
.\deploy.ps1 -Once                # deploy immediately, once, no watch loop
```

It fetches on every interval but only stops the running instance, pulls, rebuilds, and relaunches when the remote branch actually has new commits — an unchanged remote is a silent no-op. Press Ctrl+C to stop watching.

---

## Filling the menu

TaskbarLauncher creates `%AppData%\TaskbarLauncher\Menue` on first run. Populate it however you like:

- Drag files in while holding **Alt** → creates a shortcut instead of a copy.
- Drag a link straight from your browser's address bar → Windows creates a `.url` file.
- Create subfolders → they become categories automatically.
- Or skip Explorer entirely and use **Paste from clipboard** / **right-click → edit** directly from the window (see Features above).

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Win+Alt+L` | Bring the window to front, unchanged (wherever you left it) |
| `Win+Alt+Y` | Bring the window to front, back at the top level, filter cleared — ready to type |
| *(taskbar click)* | Restore/activate the window, standard Windows behavior |

Both were chosen because Windows reserves very few Win+Alt combinations (mostly Xbox Game Bar shortcuts and `Win+Alt+D` for the date/time flyout) — both are free and easy to remember ("Launcher", "You searching?"). Change them in `Program.cs` (`VK_L`/`VK_Y`, `MOD_WIN`/`MOD_ALT`) if they ever collide with something else on your system.

## Customizing the icon

`app.ico` is embedded both as the Win32 application icon and as a .NET resource (so the app can load the full multi-resolution image at runtime instead of a single upscaled size — otherwise the taskbar icon looks blurry). To use your own icon, replace `app.ico` with a multi-resolution `.ico` (16×16 up to 256×256 recommended).

## Ideas for further extension

- Fuzzy matching in the type-to-filter (typo-tolerant, not just substring)
- Remember window size/position across sessions
- Per-category custom accent colors

---

Built as a small, dependency-free WinForms app (.NET 8) — shortcut creation goes through the `WScript.Shell` COM automation object that ships with every Windows install, so there's nothing extra to install beyond the .NET runtime bundled into the published `.exe`.
