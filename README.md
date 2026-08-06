# TaskbarLauncher

Ein hierarchisches Kontextmenü für die Windows-Taskleiste, gespeist direkt aus
einer Ordnerstruktur im Dateisystem. Kein eigenes Menü-Format, keine
Konfigurationsdatei — ein Ordner *ist* das Menü.

- Unterordner → Untermenü (beliebig tief verschachtelt)
- Verknüpfung (`.lnk`) oder Internetlink (`.url`) → Menüeintrag
- beliebige andere Datei → Menüeintrag, öffnet wie im Explorer per Doppelklick
- Datei namens `---` → Trennlinie

Gepflegt wird das Menü im Explorer per Drag & Drop; Änderungen wirken sofort,
da der Ordner bei jedem Klick neu eingelesen wird.

## Bauen

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows.

```
dotnet publish -c Release
```

Die eigenständige `TaskbarLauncher.exe` landet unter
`bin\Release\net8.0-windows\win-x64\publish\`.

## Menü füllen

Beim ersten Start legt die App `%AppData%\TaskbarLauncher\Menue` an. Dorthin:

- Dateien mit gedrückter **Alt**-Taste ziehen → erzeugt eine Verknüpfung
  statt einer Kopie.
- Links direkt aus der Browser-Adressleiste in den Ordner ziehen → Windows
  legt eine `.url`-Datei an.
- Unterordner anlegen → werden automatisch zu Untermenüs.

## Auf der Taskleiste platzieren

**Variante A — Tray-Symbol (einfach):** App ohne Argumente starten. Damit
das Symbol dauerhaft sichtbar bleibt: *Einstellungen → Personalisierung →
Taskleiste → „Andere Symbole in der Taskleistenecke"*.

**Variante B — angeheftetes Icon:** Verknüpfung auf die `.exe` anlegen, in
deren Eigenschaften beim Feld „Ziel" `--once` anhängen, dann per Rechtsklick
„An Taskleiste anheften". Ein Klick startet die App, zeigt das Menü und
beendet sich danach automatisch wieder (kurze Startverzögerung, ca.
100–300 ms).

## Autostart (für die Tray-Variante)

`Win+R` → `shell:startup` → Verknüpfung auf die `.exe` in den Ordner legen.

## Mögliche Erweiterungen

- Globaler Hotkey über `RegisterHotKey`
- Tipp-Suche über alle Einträge
- Zuletzt benutzte Einträge oben anpinnen
