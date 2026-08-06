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

### Deploy-Skript

Für die lokale Entwicklung: `deploy.ps1` beendet eine laufende Instanz,
zieht den neuesten Stand, baut neu und startet die App wieder — alles in
einem Schritt.

```powershell
.\deploy.ps1
```

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
  durchgängige Reihenfolge-Präfixe wie `01 `, `02 `, …), zum Ändern des
  Schnellzugriffscodes (siehe unten) und zum Löschen.

## Schnellzugriffscodes (Win+Alt+Y)

Jede Datei bekommt automatisch einen eindeutigen 3-stelligen Code, unsichtbar
im Dateinamen als `[123]`-Suffix hinterlegt (im Menü selbst nicht sichtbar —
er steht nur im tatsächlichen Dateinamen, damit er Umbenennen und manuelles
Sortieren automatisch übersteht, ganz ohne separate Zuordnungstabelle).

**Win+Alt+Y** öffnet das Menü ganz normal, aber mit einem bereits
fokussierten Eingabefeld ganz oben. Code tippen, Enter drücken → der
zugehörige Eintrag öffnet sich sofort, egal wie tief er verschachtelt ist —
kein Durchklicken nötig.

Codes lassen sich im Rechtsklick-Bearbeiten-Dialog auch von Hand setzen
(nützlich für gut merkbare Codes bei sehr häufig genutzten Dokumenten). Ist
ein Code schon vergeben, zeigt der Dialog an, von welchem Eintrag. Leer
lassen vergibt beim nächsten Öffnen automatisch wieder einen neuen,
zufälligen Code.

## Erweiterte Ansicht (Kachelraster)

Normalerweise sind Ordner Untermenüs, die man erst aufklappen muss. Der
Menüpunkt **„Erweiterte Ansicht"** (unten im Menü, ein an/aus-Schalter)
ersetzt das Menü stattdessen durch ein eigenes Fenster mit einem großen
Kachelraster — bis zu 9×9 Kacheln sichtbar (Icon + Name), darüber hinaus
scrollbar. Klick auf eine Datei-Kachel öffnet sie; Klick auf eine
Ordner-Kachel navigiert im *selben* Fenster hinein, „Zurück" oben links geht
wieder eine Ebene hoch. Auch hier: Rechtsklick auf eine Kachel bearbeitet
sie genauso wie im normalen Menü, und „Einfügen" oben rechts fügt aus der
Zwischenablage in den gerade angezeigten Ordner ein.

Ist „Erweiterte Ansicht" als Standard aktiviert, öffnet ein Klick auf das
Taskleisten-Icon künftig direkt das Kachelraster statt des klassischen
Menüs. Zum Zurückschalten: **Win+Alt+Y** öffnet immer das klassische Menü
(dort lässt sich die Checkbox wieder ausschalten) — praktisch als
Rückfalloption, falls das Kachelraster gerade nicht passt.

Zusätzlich öffnet **Win+Alt+L** das Kachelraster jederzeit direkt,
unabhängig vom sonst eingestellten Standardmodus, ganz ohne Klick auf das
Taskleisten-Icon. Die Tastenkombinationen (L hier, Y für die
Schnellzugriffscodes) wurden bewusst so gewählt, weil Windows unter den
Win+Alt-Kombinationen kaum etwas belegt (nur wenige Xbox-Game-Bar-Kürzel wie
R/G/B/Enter/PrtScn und D für Datum/Uhrzeit) — beide sind frei und leicht zu
merken („Launcher", „Code eingeben"). Lassen sich in `Program.cs`
(`VK_L`/`VK_Y`, `MOD_WIN`/`MOD_ALT`) auf andere Tasten ändern, falls sie doch
mit einem anderen Programm kollidieren.

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
