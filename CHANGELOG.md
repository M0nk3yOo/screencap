# Changelog

## 1.0.4 – 2026-10-29

- macOS: nur noch Apple Silicon; die Intel-Pakete (`…-macos-intel.dmg`) entfallen
- Aufnahme lässt sich mit Esc abbrechen, auch wenn das Auswahl-Overlay noch keinen Tastaturfokus hatte (Windows: Overlay kommt in den Vordergrund; mehrere Monitore: Esc greift auf jedem)

## 1.0.3 – 2026-10-14

- Jeder Screenshot landet automatisch als Reiter im Hauptfenster, auch wenn er nur kopiert oder angepinnt wurde (höchstens 30 unbearbeitete, älteste fallen weg)

## 1.0.2 – 2026-10-11

- Zuschneiden: Knöpfe „Zuschneiden“, „Abbrechen“ und „Zurücksetzen“ unter dem Werkzeug

## 1.0.1 – 2026-10-11

- Autostart beim Anmelden (macOS: Anmeldeobjekt, Windows: Autostart), beim ersten Start aktiv, abschaltbar
- App und Fenstertitel werden bei der Aufnahme erkannt und als Metadaten in PNG/JPG gespeichert (Text-Chunks, EXIF inkl. XPTitle, XMP)
- Design-Einstellung: Automatisch, Hell, Dunkel; Vorschau- und Textfenster folgen dem Design
- Freihand-Werkzeug (F) mit Stift, Textmarker und Konturstift; geglättete Striche, Umschalt für gerade Linien
- Lupe (L): markiert eine Stelle und zeigt sie daneben vergrößert; Markierung und Vergrößerung getrennt verschiebbar, Zoom 1,5–6×
- Pfeil-Stile: Standard, offen, Doppelpfeil, Keil, gebogen (mit Biegegriff), Linie
- Einstellungen in Bereiche gegliedert (Allgemein, Speichern, Darstellung, Tastenkürzel)
- Pin und Vorschaufenster schließen sich nach dem Kopieren
- Bereichsauswahl: Hilfslinien verlängern die Auswahlkanten über den ganzen Bildschirm; Fadenkreuz auch über erkannten Fenstern
- Fenster-Aufnahme: Fenster werden bei der Bereichsauswahl erkannt, ein Klick nimmt sie auf
- Scroll-Aufnahme (⌘⇧6 / Strg+Umschalt+6): beim Scrollen aufnehmen und automatisch zusammensetzen

### Behoben
- Kopierte und gespeicherte Screenshots hatten auf macOS vertauschte Rot-/Blau-Kanäle
- Windows: Titelleiste überlagerte Reiter und Einstellungsknopf, ein zusätzlicher Vollbild-Knopf ließ die Fensterknöpfe verschwinden – jetzt native Titelleiste, Reiter darunter

## 1.0.0 – 2026-10-10

Erste Version.

- Bereichs- und Vollbildaufnahme mit eingefrorenem Bildschirm, Lupe und Pixelfarbe; globale, einstellbare Tastenkürzel
- Vorschaufenster neben dem aufgenommenen Bereich; schwebende Pins in Originalgröße
- Editor mit verschiebbaren Reitern: Zuschneiden, Pfeil, Rechteck, Ellipse, Marker, Text (Schriftart, Formatierung, Stile, Kastenbreite), Nummern mit Zeiger, Verpixeln/Verwischen inkl. Klick auf erkannte Wörter, Spotlight, Pipette, eingefügte Aufnahmen
- Werkzeug-Optionen unter dem aktiven Werkzeug, Griffe, Ebenen-Panel, Rückgängig/Wiederholen, Zoom; Werkzeug-Einstellungen werden gemerkt
- Arbeitsfläche wächst beim Herausziehen von Elementen; Hintergrund transparent oder farbig
- Texterkennung (Apple Vision / Windows-OCR) und QR-/Barcode-Lesen – global per Tastenkürzel und im Editor
- Native Zwischenablage, Export als PNG oder JPG, automatisches Speichern, Aufräumen alter Screenshots, Sitzung wiederherstellen
- Glas-Effekt für das Editorfenster
