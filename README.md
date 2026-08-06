# TaskbarLauncher

Ein hierarchisches Kontextmenü für die Windows-Taskleiste, gespeist direkt aus
einer Ordnerstruktur im Dateisystem. Kein eigenes Menü-Format, keine
Konfigurationsdatei — ein Ordner *ist* das Menü.

- Unterordner → Untermenü (beliebig tief verschachtelt)
- Verknüpfung (`.lnk`) oder Internetlink (`.url`) → Menüeintrag
- beliebige andere Datei → Menüeintrag, öffnet wie im Explorer per Doppelklick
- Datei namens `---` → Trennlinie

Gepflegt wird das Menü im Explorer per Drag & Drop; Änderungen wirken sofort,
da der Ordner bei jedem Klick neu eingelesen wird. Zusätzlich lässt sich das
Menü auch direkt aus sich selbst heraus pflegen (siehe unten): neue
Verknüpfungen aus der Zwischenablage einfügen, bestehende Einträge per
Rechtsklick umbenennen/Ziel ändern/löschen, und die Reihenfolge von Hand
festlegen.

Die Einträge sind bewusst groß gehalten (größere Schrift, größere Icons als
Windows-Standardmenüs) für einen bequemen Treffer auch mit der Maus aus der
Taskleiste heraus.

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

Oder direkt aus dem Menü heraus, ohne den Explorer zu öffnen:

- **Aus Zwischenablage einfügen** (oben in jeder Menü-Ebene, egal ob Hauptmenü
  oder ein Untermenü): Datei/Ordner im Explorer kopieren (Strg+C) und diesen
  Menüpunkt klicken → legt eine `.lnk`-Verknüpfung genau in dieser Ebene an.
  Bereits kopierte `.lnk`/`.url`-Dateien werden 1:1 übernommen. Kopierter
  Text mit einer URL wird nach kurzer Namensabfrage zu einer `.url`-Datei.
- **Rechtsklick auf einen Eintrag oder Ordner** öffnet einen kleinen Dialog
  zum Umbenennen, bei Verknüpfungen/Links zum Ändern des Ziels, zum manuellen
  Verschieben (▲/▼ — sortiert die gesamte Ebene neu ein und vergibt dafür
  durchgängige Reihenfolge-Präfixe wie `01 `, `02 `, …) und zum Löschen.

## Erweiterte Ansicht

Normalerweise sind Ordner Untermenüs, die man erst aufklappen muss. Der
Menüpunkt **„Erweiterte Ansicht"** (unten im Menü, ein an/aus-Schalter)
zeigt stattdessen den Inhalt der obersten Ordnerebene direkt im Hauptmenü an
— jeder Ordner erscheint als fette Überschrift, gefolgt von seinen
Einträgen. Tiefer verschachtelte Unterordner bleiben normale Untermenüs.
Praktisch, wenn man lieber alles auf einen Blick sieht, statt sich
durchzuhangeln.

Zusätzlich öffnet **Win+Alt+L** das Menü jederzeit direkt in der Erweiterten
Ansicht — unabhängig vom sonst eingestellten Standardmodus, ganz ohne Klick
auf das Taskleisten-Icon. Die Tastenkombination wurde bewusst so gewählt,
weil Windows unter den Win+Alt-Kombinationen kaum etwas belegt (nur wenige
Xbox-Game-Bar-Kürzel wie R/G/B/Enter/PrtScn und D für Datum/Uhrzeit) — L ist
frei und mit „Launcher" leicht zu merken. Lässt sich in `Program.cs`
(`VK_L`, `MOD_WIN`/`MOD_ALT`) auf eine andere Taste ändern, falls sie doch
mit einem anderen Programm kollidiert.

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

## Immer nur eine Instanz

Ein benannter Mutex verhindert einen zweiten Prozess von vornherein. Startet
man die App trotzdem ein zweites Mal (z. B. per Doppelklick oder aus dem
Startmenü, während sie schon im Hintergrund läuft), beendet sich dieser
zweite Versuch sofort wieder — signalisiert der bereits laufenden Instanz
aber vorher, das Menü zu zeigen. Der Klick geht also nie ins Leere.

## Icon

`app.ico` ist im Projekt hinterlegt und wird über
`<ApplicationIcon>` in `TaskbarLauncher.csproj` in die `.exe` eingebettet.
Für ein eigenes Icon einfach `app.ico` ersetzen (mehrere Auflösungen, min.
16×16 bis 256×256, empfohlen).

## Mögliche Erweiterungen

- Globaler Hotkey über `RegisterHotKey`
- Tipp-Suche über alle Einträge
- Zuletzt benutzte Einträge oben anpinnen
