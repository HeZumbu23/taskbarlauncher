# TaskbarLauncher

**Click one icon in your Windows 11 taskbar. Get your entire folder structure as a menu.**

No config file, no database, no drag-and-drop editor to learn — a folder *is* the menu. Organize it exactly like any other folder on your PC, and TaskbarLauncher turns it into a fast, hierarchical launcher that lives in your taskbar.

```
%AppData%\TaskbarLauncher\Menue\
├── 01 Projects\
│   ├── Client Report.lnk
│   └── Roadmap.url
├── 02 Links\
│   └── Design System.url
└── Notes.txt
```

→ click the taskbar icon → the same tree, as a menu.

---

## Why this exists

Windows has no built-in way to turn a folder tree into a taskbar menu. TaskbarLauncher is a small, self-contained .NET 8 app that does exactly that — nothing more. It stays out of the way: no tray icon, no separate window, just one ordinary taskbar button.

## Features

**📁 The filesystem is the UI**
Subfolders become submenus, `.lnk`/`.url` files become entries, any other file opens like a double-click in Explorer. Edit the folder in Explorer and the menu updates on the next click — no restart needed.

**🖱️ Built for taskbar clicks**
Big font (11.5pt) and big icons (28px) — easy to hit with a mouse from a small taskbar button. One click on a completely ordinary, pinnable taskbar icon opens the menu instantly (see [How the taskbar icon works](#how-the-taskbar-icon-works) for the trick behind that).

**✏️ Manage the menu from the menu itself**
- **Paste from clipboard** — copy a file/folder in Explorer (or a URL), click "Paste from clipboard" in any menu level, and a shortcut appears right there.
- **Right-click any entry or folder** to rename it, change its target (`.lnk`/`.url`), delete it, or move it up/down among its siblings.
- Renaming/reordering never touches the visible menu — order is tracked with hidden numeric prefixes (`01 `, `02 `, …) that get normalized automatically the first time you reorder something.

**🔲 Expanded View — a full tile grid**
Toggle "Expanded View" for a completely different way to browse: a borderless, light-themed tile grid (large canvas, scrollable) instead of nested menus. The top level is grouped and visible right away — each folder shows up as a header with its contents laid out directly underneath, separated by thin dividers, no clicking in required. Deeper folders still drill in on click, with "Back" to go up; right-click to edit, same as everywhere else. Groups are sorted by how often their contents get opened, most-used first — no setup, it just learns from usage over time. Just start typing while the grid is open to filter down to matching tiles live, no search box needed (Escape clears the filter, or closes the grid if it's already empty).

**🔎 Real-time search**
Press **Win+Alt+Y**, start typing — every keystroke filters live across every file in the entire menu tree, no matter how deep it's nested. Arrow keys to move through results, Enter to open the selected one.

**⌨️ Global hotkeys, no taskbar click needed**
`Win+Alt+L` jumps straight to the tile grid; `Win+Alt+Y` opens the search box already focused. See the [shortcuts table](#keyboard-shortcuts) below.

**🚀 One instance, always ready**
A named mutex keeps a second launch from ever starting — it just tells the running instance to show the menu instead, so a stray double-click never does nothing.

---

## Getting started

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows 11.

```powershell
dotnet publish -c Release
```

The self-contained `TaskbarLauncher.exe` lands in `bin\Release\net8.0-windows\win-x64\publish\`.

### Pin it to the taskbar

1. Run `TaskbarLauncher.exe` once (double-click). The first click shows the menu right away; the app then keeps running, minimized, in the background — its taskbar button stays put.
2. Right-click that button → **"Pin to taskbar"**.
3. Done. Every further click on the pinned icon opens the menu directly, for as long as the app is running.

For the pinned icon to keep working after a reboot, also set up autostart (below) — otherwise the first click after a restart just relaunches the app instead of opening the menu.

### Autostart

`Win+R` → `shell:startup` → add a shortcut to `TaskbarLauncher.exe`, and append `--startup` to its **Target** field:

```
"C:\Path\to\TaskbarLauncher.exe" --startup
```

This makes the app start silently on login instead of popping the menu open immediately. Since it's already running by the time you'd click the pinned icon, the two merge into a single, always-instant button.

### Deploy script

For local development: `deploy.ps1` watches the current branch and redeploys automatically whenever new commits land — no manual "pull, rebuild, relaunch" cycle.

```powershell
.\deploy.ps1                      # watch, checking every 60s (default)
.\deploy.ps1 -IntervalSeconds 15  # check more often
.\deploy.ps1 -Once                # deploy immediately, once, no watch loop
```

It fetches on every interval but only stops the running instance, pulls, rebuilds, and relaunches when the remote branch actually has new commits — an unchanged remote is a silent no-op. Press Ctrl+C to stop watching.

---

## How the taskbar icon works

TaskbarLauncher shows no window and no tray icon. It runs permanently minimized in the background, which makes it appear as a completely ordinary taskbar entry — like any other open app. When you click that button, Windows first sends the minimized window a `WM_SYSCOMMAND`/`SC_RESTORE` message *before* actually restoring it. TaskbarLauncher intercepts exactly that message and opens the menu instead of letting the restore happen. The result: one normal, pinnable icon whose click opens the menu instantly — no tray, no relaunch delay.

## Filling the menu

TaskbarLauncher creates `%AppData%\TaskbarLauncher\Menue` on first run. Populate it however you like:

- Drag files in while holding **Alt** → creates a shortcut instead of a copy.
- Drag a link straight from your browser's address bar → Windows creates a `.url` file.
- Create subfolders → they become submenus automatically.
- Or skip Explorer entirely and use **Paste from clipboard** / **right-click → edit** directly from the menu (see Features above).

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Win+Alt+L` | Open the tile grid (Expanded View) directly, regardless of the default mode |
| `Win+Alt+Y` | Open the real-time search box, focused and ready to type |
| *(taskbar click)* | Open the menu in whichever mode is currently the default |

`Win+Alt+L`/`Win+Alt+Y` were chosen because Windows reserves very few Win+Alt combinations (mostly Xbox Game Bar shortcuts and `Win+Alt+D` for the date/time flyout) — both are free and easy to remember ("Launcher", "You searching?"). Change them in `Program.cs` (`VK_L`/`VK_Y`, `MOD_WIN`/`MOD_ALT`) if they ever collide with something else on your system.

## Customizing the icon

`app.ico` is embedded both as the Win32 application icon and as a .NET resource (so the app can load the full multi-resolution image at runtime instead of a single upscaled size — otherwise the taskbar icon looks blurry). To use your own icon, replace `app.ico` with a multi-resolution `.ico` (16×16 up to 256×256 recommended).

## Ideas for further extension

- Fuzzy matching in search (typo-tolerant, not just substring)
- Pin recently-used entries to the top
- Per-folder custom accent colors in the tile grid

---

Built as a small, dependency-free WinForms app (.NET 8) — shortcut creation goes through the `WScript.Shell` COM automation object that ships with every Windows install, so there's nothing extra to install beyond the .NET runtime bundled into the published `.exe`.
