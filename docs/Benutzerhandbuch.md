# Screencap – Benutzerhandbuch

Screencap ist ein Screenshot-Werkzeug für **macOS** und **Windows** nach dem Vorbild von Shottr: aufnehmen, kurz prüfen, markieren, teilen – mit möglichst wenig Klicks.

**Inhalt**

1. [Installation](#1-installation)
2. [Erste Schritte](#2-erste-schritte)
3. [Aufnehmen](#3-aufnehmen)
4. [Vorschaufenster](#4-vorschaufenster)
5. [Anpinnen](#5-anpinnen)
6. [Der Editor](#6-der-editor)
7. [Werkzeuge](#7-werkzeuge)
8. [Text- und QR-Erkennung](#8-text--und-qr-erkennung)
9. [Kopieren, Speichern, Exportieren](#9-kopieren-speichern-exportieren)
10. [Einstellungen](#10-einstellungen)
11. [Tastenkürzel](#11-tastenkürzel)
12. [Problemlösung](#12-problemlösung)

---

## 1. Installation

### macOS (ab macOS 12)

1. Passende Datei aus dem Release laden:
   - **Apple Silicon** (M1/M2/M3/M4 …): `Screencap-…-macos-apple-silicon.dmg`
   - **Intel**: `Screencap-…-macos-intel.dmg`
2. DMG öffnen und **Screencap** in den Ordner **Programme** ziehen.
3. **Erster Start:** Die App ist nicht von Apple notariell beglaubigt. macOS blockiert deshalb den ersten Start.
   - Systemeinstellungen → **Datenschutz & Sicherheit** → ganz unten bei „Screencap wurde blockiert“ auf **Dennoch öffnen** klicken, oder
   - im Terminal: `xattr -dr com.apple.quarantine /Applications/Screencap.app`
4. Beim ersten Screenshot fragt macOS nach der Berechtigung **Bildschirmaufnahme**. Erlauben und Screencap danach einmal neu starten (Menüleiste → Screencap beenden, dann erneut öffnen).

Screencap lebt in der **Menüleiste** (Sucher-Symbol). Solange der Editor offen ist, erscheint es zusätzlich im Dock.

### Windows (10 und 11)

1. Passende Datei laden: `Screencap-…-windows-x64.zip` (normale PCs) oder `…-windows-arm64.zip` (ARM-Geräte, z. B. Surface Pro X / Copilot+ PCs).
2. ZIP entpacken, z. B. nach `C:\Programme\Screencap` oder in den Benutzerordner, und `Screencap.exe` starten.
3. **SmartScreen:** Da die App nicht signiert ist, warnt Windows beim ersten Start. **Weitere Informationen** → **Trotzdem ausführen**.
4. Screencap erscheint im **Infobereich** der Taskleiste (ggf. über den Pfeil „Ausgeblendete Symbole“).

> Tipp: Für den automatischen Start mit Windows eine Verknüpfung zu `Screencap.exe` in `shell:startup` legen (Win+R → `shell:startup`).

---

## 2. Erste Schritte

| Was | macOS | Windows |
|---|---|---|
| Bereich aufnehmen | ⌘⇧2 | Strg+Umschalt+2 |
| Vollbild aufnehmen | ⌘⇧1 | Strg+Umschalt+1 |
| Text erkennen (Bereich) | ⌘⇧O | Strg+Umschalt+O |

Alle drei Kürzel sind in den [Einstellungen](#10-einstellungen) änderbar. Alles ist außerdem über das Menü des Symbols in der Menüleiste bzw. im Infobereich erreichbar.

Der typische Ablauf:

1. **⌘⇧2** drücken, Bereich aufziehen.
2. Neben dem Bereich erscheint das **Vorschaufenster**.
3. **Kopieren**, **Speichern**, **Anpinnen** – oder **Bearbeiten**, um den Screenshot im Editor zu markieren.

---

## 3. Aufnehmen

### Bereich

Nach dem Tastenkürzel friert der Bildschirm ein und wird abgedunkelt.

- **Ziehen** wählt den Bereich. Oben links wird die Größe in Pixeln angezeigt.
- Die **Lupe** neben dem Mauszeiger zeigt die Pixel stark vergrößert, dazu Farbe (Hex) und Koordinaten – für pixelgenaues Arbeiten.
- **Klick ohne Ziehen** oder **Enter/Leertaste**: ganzer Bildschirm.
- **Esc** oder **Rechtsklick**: abbrechen.

Bei mehreren Monitoren wird auf jedem Monitor ausgewählt; der Bereich liegt immer auf einem Monitor.

### Vollbild

Nimmt den Monitor auf, auf dem sich der Mauszeiger befindet.

### Text erkennen

Wie „Bereich“, aber statt eines Screenshots wird der **Text** im Bereich erkannt (und **QR-Codes** gelesen). Details in [Kapitel 8](#8-text--und-qr-erkennung).

---

## 4. Vorschaufenster

Erscheint nach jeder Aufnahme neben dem aufgenommenen Bereich.

| Knopf | Taste | Funktion |
|---|---|---|
| **Bearbeiten** | Enter oder E, Doppelklick aufs Bild | Öffnet den Editor mit dem Screenshot als neuem Reiter |
| Kopieren | ⌘C / Strg+C | Bild in die Zwischenablage |
| Speichern | ⌘S / Strg+S | In den Standardordner speichern |
| Text | – | Text erkennen und kopieren |
| Anpinnen | P | Als schwebendes Fenster anpinnen |
| Schließen | Esc | |

Das Fenster lässt sich am Bild verschieben.

---

## 5. Anpinnen

Ein angepinnter Screenshot schwebt **immer im Vordergrund** – praktisch als Vorlage beim Abtippen oder Nachbauen. Er erscheint genau dort, wo aufgenommen wurde, in **Originalgröße** (1 Bildpixel = 1 Bildschirmpixel), in einem dezenten Glasrahmen.

- **Ziehen**: verschieben
- **Mausrad**: zoomen · **0**: zurück auf 100 %
- **⌘/Strg + Mausrad**: Deckkraft
- **Überfahren** zeigt Knöpfe für **Kopieren**, **Bearbeiten** und **Schließen**
- **Doppelklick** oder **E**: im Editor öffnen (der Pin bleibt offen)
- **Rechtsklick**: Menü mit Kopieren, Speichern, Originalgröße, Deckkraft 100 %
- **Esc**: schließen

Es können beliebig viele Pins gleichzeitig offen sein. Auf dem Mac sind Pins auf allen Schreibtischen (Spaces) sichtbar.

---

## 6. Der Editor

Jeder Screenshot bekommt einen **Reiter** in der Titelzeile.

- **Reiter wechseln**: anklicken oder Strg+Tab / Strg+Umschalt+Tab
- **Reiter verschieben**: per Drag & Drop nach links/rechts
- **Rechtsklick auf einen Reiter**: *Im Finder/Explorer zeigen* (sobald gespeichert), *Speicherort öffnen*, *Schließen*, *Andere Reiter schließen*
- **Orangener Punkt**: ungespeicherte Änderungen
- **+** oder **⌘N / Strg+N**: neuen Screenshot direkt als Reiter aufnehmen (der Editor blendet sich dabei kurz aus)

### Werkzeugleiste

Von links nach rechts: **Werkzeuge** · **Erkennung** (Text markieren, ganzer Text, QR-Code) · **Farben** · **Aktionen** (Aufnahme einfügen, Ebenen, Rückgängig/Wiederholen, Anpinnen, Speichern unter, Speichern) · **Kopieren**.
Ist das Fenster schmal, bricht die Leiste in eine zweite Zeile um.

### Werkzeug-Optionen

Unter dem gewählten Werkzeug erscheint – nur wo sinnvoll – eine kleine Optionsleiste (Linienstärke, Schrift, Stil …). Sie blendet sich aus, sobald man auf dem Bild weiterarbeitet, und erscheint wieder beim Wählen eines Werkzeugs oder Anklicken eines Elements.

Screencap **merkt sich die letzten Einstellungen jedes Werkzeugs** (auch nach einem Neustart): Linienstärke getrennt für Pfeil, Rechteck und Ellipse, Größe der Nummern, Textformat, Verpixeln/Verwischen und die zuletzt verwendete Farbe.

### Elemente nachträglich bearbeiten

Alles bleibt editierbar:

- Ein neu gezeichnetes Element ist **automatisch ausgewählt** – Farbe, Stärke usw. lassen sich direkt ändern.
- **Auswählen (V)**: Element anklicken, verschieben; **⌫/Entf** löscht.
- **Griffe** am ausgewählten Element: Größe ändern (Rechteck, Ellipse, Marker, Bereiche, Bilder), Pfeil-Enden, Zeiger einer Nummer, Breite eines Textkastens.
  Bei eingefügten Bildern bleibt das Seitenverhältnis an den Ecken erhalten; **Umschalt** erzwingt es bei anderen Formen.
- Elemente, die über den Rand gezogen werden, **vergrößern die Arbeitsfläche** – schon während des Ziehens. Die neue Fläche ist standardmäßig transparent (Schachbrett).

### Ebenen

Der Knopf mit dem Stapel-Symbol öffnet das **Ebenen-Panel**: alle Elemente, das oberste zuerst. Dort lassen sich Elemente auswählen, **ein-/ausblenden** (Auge), **nach vorne/hinten** schieben und löschen. Ausgeblendete Elemente werden nicht exportiert.

### Zoom

- **⌘/Strg + Mausrad** oder **Trackpad-Pinch**: zoomt zur Mausposition
- Statusleiste: **−**, **+**, **Einpassen**, **100 %**
- ⌘+/⌘− · **⌘0** einpassen · **⌘1** 100 %

Die Statusleiste zeigt außerdem Größe, Mausposition und Pixelfarbe.

---

## 7. Werkzeuge

| Werkzeug | Taste | Bedienung |
|---|---|---|
| Auswählen | V | Elemente anklicken, verschieben, Griffe nutzen |
| Zuschneiden | C | Rahmen an **Kanten/Ecken** ziehen, Ausschnitt verschieben oder neu aufziehen. **⌫** setzt zurück, **Enter** beendet. Nicht-destruktiv: jederzeit änderbar oder rückgängig. |
| Pfeil | A | Ziehen. **Umschalt**: 45°-Schritte |
| Rechteck | R | Ziehen. **Umschalt**: Quadrat |
| Ellipse | O | Ziehen. **Umschalt**: Kreis |
| Marker | H | Halbtransparente Markierung aufziehen |
| Text | T | Klicken und tippen. **Enter** fertig, **Umschalt+Enter** neue Zeile, **Esc** abbrechen. **Doppelklick** auf einen Text bearbeitet ihn (mit jedem Werkzeug). |
| Nummer | N | Klicken setzt die nächste Nummer. **Ziehen** zieht einen **Zeiger** heraus, der auf die gemeinte Stelle zeigt. |
| Verpixeln / Verwischen | B | Bereich aufziehen. Erkannte Wörter sind blau umrandet: **Klick** auf ein Wort macht es unkenntlich, **⌥/Alt + Klick** die ganze Zeile. Stil (Verpixeln/Verwischen) und Stärke in den Optionen. |
| Spotlight | S | Bereich aufziehen – alles andere wird abgedunkelt |
| Text markieren | E | Bereich aufziehen → Text und QR-Codes darin werden erkannt |
| Pipette | I | Farbe im Bild anklicken: wird Zeichenfarbe (und färbt die Auswahl um), Hex-Wert landet in der Zwischenablage, danach zurück zum vorigen Werkzeug |

### Text-Optionen

Schriftart (alle installierten Schriften), Größe, **Fett**, *Kursiv*, Unterstrichen, Durchgestrichen und drei Stile:

- **Kontur** – Textfarbe mit Kontrastrand, auf jedem Hintergrund lesbar
- **Schlicht** – nur der Text
- **Box** – farbiger Kasten, Text in Schwarz oder Weiß

Die **Breite des Textkastens** lässt sich mit den Griffen links/rechts ändern; der Text bricht dann um.

### Farben

Acht Standardfarben plus das Regenbogen-Feld für **beliebige Farben**. Eigene Farben bleiben als zusätzliche Felder erhalten (höchstens sechs); über das **×**, das beim Überfahren erscheint, lassen sie sich entfernen. Ist ein Element ausgewählt, ändert ein Farbklick dessen Farbe.

### Weitere Aufnahme einfügen

**Einfügen** (⌘I / Strg+I) nimmt einen weiteren Bereich auf und setzt ihn als verschiebbares Bild in den aktuellen Screenshot.

---

## 8. Text- und QR-Erkennung

Die Texterkennung läuft komplett lokal: auf dem Mac über **Apple Vision**, unter Windows über die eingebaute **Windows-Texterkennung**. QR-Codes und Barcodes (QR, Data Matrix, Aztec, PDF417, EAN, UPC, Code 128/39, ITF) werden auf beiden Systemen gleich gelesen.

| Weg | Ergebnis |
|---|---|
| **⌘⇧O / Strg+Umschalt+O** oder Menü „Text erkennen …“ | Bereich auf dem Bildschirm markieren → Box mit dem Text neben dem Bereich, Text ist bereits kopiert. Enthält der Bereich einen QR-Code, steht dessen Inhalt oben. |
| Editor: **Text markieren (E)** | Bereich im Screenshot aufziehen → Seitenleiste mit Text (und QR-Inhalt) |
| Editor: **Gesamter Text** (Seiten-Symbol) | Text des ganzen (zugeschnittenen) Bildes |
| Editor: **QR-Code** | Alle Codes im Bild |
| Vorschaufenster: **Text** | Text erkennen und direkt kopieren |

Ist der Inhalt ein Link, gibt es zusätzlich **Link öffnen**.

---

## 9. Kopieren, Speichern, Exportieren

- **Kopieren** legt das Bild mit allen Markierungen in die Zwischenablage – einfügbar in Mail, Office, Chat-Programme usw.
- **Speichern** (⌘S) speichert ohne Nachfrage im **Standardordner**. Ist noch keiner festgelegt, wird beim ersten Mal gefragt.
- **Speichern unter** (⇧⌘S) mit Wahl von **PNG** oder **JPG**.
- Dateinamen: `Screencap JJJJ-MM-TT HH-MM-SS.png` – sekundengenau; bei zwei Aufnahmen in derselben Sekunde wird „(2)“ angehängt.
- Transparente Flächen bleiben in PNG erhalten; JPG bekommt Weiß.

---

## 10. Einstellungen

Menü des Symbols → **Einstellungen …** (oder das Zahnrad im Editor).

**Nach der Aufnahme**
- Automatisch in die Zwischenablage kopieren
- Jeden Screenshot automatisch im Speicherort sichern
- Reiter beim nächsten Start wiederherstellen (inklusive aller Markierungen)

**Speicherort**
- Ordner wählen / öffnen
- Format für schnelles und automatisches Speichern: **PNG** oder **JPG** (mit Qualität)

**Hintergrund**
- Farbe für Flächen, die beim Herausziehen von Elementen entstehen: **Transparent** (Standard), Weiß, Schwarz oder eigene Farbe

**Aufräumen**
- Screenshots nach *n* Tagen löschen (Standard: 180). Betrifft **nur** Dateien, die Screencap selbst angelegt hat (Namensschema oben) – andere Dateien im Ordner bleiben unberührt.

**Darstellung**
- **Glas-Effekt** des Editors (macOS: Vibrancy, Windows: Mica/Acrylic), 0 % = aus

**Tastenkürzel**
- Auf ein Kürzel klicken und die neue Kombination drücken (Esc bricht ab). *Standard wiederherstellen* setzt alle zurück. Ist eine Kombination schon belegt, erscheint ein Hinweis.

---

## 11. Tastenkürzel

### Global

| Aktion | macOS | Windows |
|---|---|---|
| Bereich aufnehmen | ⌘⇧2 | Strg+Umschalt+2 |
| Vollbild aufnehmen | ⌘⇧1 | Strg+Umschalt+1 |
| Text erkennen | ⌘⇧O | Strg+Umschalt+O |

### Editor

Unter Windows steht **Strg** für **⌘**.

| Aktion | Kürzel |
|---|---|
| Rückgängig / Wiederholen | ⌘Z / ⇧⌘Z (Windows auch Strg+Y) |
| Kopieren | ⌘C |
| Speichern / Speichern unter | ⌘S / ⇧⌘S |
| Neue Aufnahme als Reiter | ⌘N |
| Aufnahme einfügen | ⌘I |
| Reiter schließen | ⌘W |
| Reiter wechseln | Strg+Tab / Strg+Umschalt+Tab |
| Zoom | ⌘+ / ⌘− / ⌘0 (einpassen) / ⌘1 (100 %) / ⌘ + Mausrad |
| Anpinnen | P |
| Auswahl löschen | ⌫ / Entf |
| Auswahl aufheben / Auswählen-Werkzeug | Esc |
| Werkzeuge | V C A R O H T N B S E I |

---

## 12. Problemlösung

**Der Screenshot zeigt nur den Hintergrund (macOS)**
Die Berechtigung *Bildschirmaufnahme* fehlt: Systemeinstellungen → Datenschutz & Sicherheit → Bildschirmaufnahme → Screencap aktivieren, dann Screencap neu starten.

**Ein Tastenkürzel funktioniert nicht**
Es ist vermutlich von einer anderen App oder dem System belegt (die Einstellungen zeigen einen Hinweis). Unter Windows 11 belegt z. B. das Snipping Tool die Druck-Taste. Ein anderes Kürzel wählen.

**Texterkennung unter Windows meldet einen Fehler**
Für eine der Windows-Sprachen muss die Texterkennung installiert sein: Einstellungen → Zeit und Sprache → Sprache → Sprache → Optionen → *Optische Zeichenerkennung*.

**Screenshots wirken unter Windows blass**
Bei aktivem HDR ist das derzeit eine bekannte Einschränkung.

**Manche Fenster sind schwarz**
Kopiergeschützte Inhalte (z. B. Streaming-Dienste) lassen sich vom System aus nicht aufnehmen.

**Wo liegen Einstellungen und Protokoll?**
- macOS: `~/Library/Application Support/Screencap/`
- Windows: `%APPDATA%\Screencap\`

Dort liegen `settings.json`, die gespeicherte Sitzung und `screencap.log` mit Fehlermeldungen – bei Problemen hilft diese Datei weiter.

**Bekannte Einschränkungen in Version 1.0**
- Noch keine Scrolling-Aufnahme und keine automatische Fenster-Erkennung
- Pins erscheinen unter Windows nur auf dem aktuellen virtuellen Desktop
- Unter- und Durchstreichen ist während des Tippens noch nicht sichtbar, erst danach
