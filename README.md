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

Die App zeigt kein Fenster und kein Tray-Symbol. Sie läuft dauerhaft
minimiert im Hintergrund und ist dadurch als ganz normaler Eintrag in der
Taskleiste sichtbar — wie jede andere offene App. Ein Klick auf diesen
Button fängt Windows' „Fenster wiederherstellen"-Befehl ab, bevor er
passiert, und öffnet stattdessen sofort das Menü. Kein Doppelklick-Delay,
kein Tray, kein Neustart pro Klick.

Einrichtung:

1. `TaskbarLauncher.exe` einmal manuell starten (Doppelklick). Der erste
   Klick zeigt gleich das Menü; die App bleibt danach minimiert im
   Hintergrund aktiv — der Taskleisten-Button bleibt stehen.
2. Auf diesen Button rechtsklicken → **„An Taskleiste anheften"**.
3. Fertig. Jeder weitere Klick auf das angeheftete Icon öffnet direkt das
   Menü, solange die App läuft.

Für ein dauerhaft angeheftetes, funktionierendes Icon auch nach einem
Neustart braucht es zusätzlich den Autostart (nächster Abschnitt) — sonst
zeigt ein Klick nach einem Neustart erst wieder nur den Start-Klick
(App startet neu, zeigt das Menü, bleibt dann laufen).

## Autostart

`Win+R` → `shell:startup` → dort eine Verknüpfung auf `TaskbarLauncher.exe`
anlegen und in deren Eigenschaften beim Feld „Ziel" ein Leerzeichen gefolgt
von `--startup` anhängen, z. B.:

```
"C:\Pfad\zu\TaskbarLauncher.exe" --startup
```

Das Argument sorgt dafür, dass die App beim Anmelden lautlos im Hintergrund
startet, statt sofort das Menü aufzuklappen. Da es bereits läuft, sobald
Windows fertig geladen hat, verschmilzt der Autostart-Prozess mit dem
angehefteten Icon zu einem einzigen Button — jeder Klick öffnet ab dann
sofort das Menü, ganz ohne Verzögerung.

## Mögliche Erweiterungen

- Globaler Hotkey über `RegisterHotKey`
- Tipp-Suche über alle Einträge
- Zuletzt benutzte Einträge oben anpinnen
