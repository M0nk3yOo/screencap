<img src="assets/icon.svg" width="64" height="64" alt="">

# Screencap

Screenshot-Tool nach dem Vorbild von [Shottr](https://shottr.cc) – für macOS und Windows.

- **Bereich aufnehmen** (⌘⇧2 / Strg+Umschalt+2): Bildschirm wird eingefroren, Bereich aufziehen, Lupe mit Pixelfarbe. Klick ohne Ziehen oder Enter = ganzer Bildschirm.
- **Vollbild aufnehmen** (⌘⇧1 / Strg+Umschalt+1): Bildschirm unter dem Mauszeiger.
- **Vorschaufenster** neben dem aufgenommenen Bereich: Bearbeiten, Kopieren, Speichern, Anpinnen.
- **Angepinnt**: immer im Vordergrund, verschiebbar, in Originalgröße (1 Bildpixel = 1 Bildschirmpixel). Mausrad = Zoom, ⌘/Strg+Mausrad = Deckkraft, 0 = 100 %, Doppelklick = bearbeiten, Esc = schließen.
- **Hauptfenster mit Reitern** in der Titelzeile: Zuschneiden (Rahmen an Kanten/Ecken ziehen), Pfeil, Rechteck, Ellipse, Marker, Text, Nummern (ziehen = Zeiger), Verpixeln oder Verwischen, Spotlight, Farbpipette, freie Farbwahl; Schieberegler für Linienstärke/Textgröße bzw. Unkenntlichkeit; Rückgängig/Wiederholen. „+“ nimmt einen neuen Screenshot als Reiter auf, „Einfügen“ fügt eine weitere Aufnahme ins aktuelle Bild ein.
- **Texterkennung (OCR)** im Editor und im Vorschaufenster – macOS: Apple Vision, Windows: Windows.Media.Ocr. Das Verpixeln-Werkzeug erkennt Textflächen: Wort anklicken = verpixeln, ⌥/Alt + Klick = ganze Zeile.
- **Kopieren** legt PNG und TIFF (macOS) bzw. DIB und PNG (Windows) in die Zwischenablage – einfügbar in Mail, Office, Chat usw.
- **Speichern**: Standardordner (wird beim ersten Speichern abgefragt), optional jeder Screenshot automatisch; Dateinamen `Screencap JJJJ-MM-TT HH-MM-SS.png`. Alte Screenshots werden nach einstellbaren Tagen gelöscht (Standard 180) – nur Dateien, die Screencap selbst angelegt hat.
- Reiter können optional beim Start wiederhergestellt werden.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `Screencap.Core` | Plattformunabhängig: Bildpuffer, Markierungen + Undo, Einstellungen, Sitzung, Platzierung |
| `Screencap.Platform.MacOS` | Aufnahme (`screencapture`), globale Hotkeys (Carbon), Fensterebenen (AppKit) |
| `Screencap.Platform.Windows` | Aufnahme (GDI), globale Hotkeys (`RegisterHotKey`), Topmost |
| `Screencap.App` | Avalonia-UI: Overlay, Vorschau, Pin, Editor, Tray |

Die gesamte Oberfläche ist geteilter Code; nur die Schnittstellen in `Core/Platform/PlatformServices.cs` werden pro Betriebssystem implementiert.

## Entwickeln

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build
dotnet test
```

### macOS

```sh
./scripts/build-macos-app.sh
open artifacts/macos/Screencap.app
```

Beim ersten Screenshot fragt macOS nach der Berechtigung **Bildschirmaufnahme** (Systemeinstellungen → Datenschutz & Sicherheit). Danach Screencap einmal neu starten.

Aus dem Terminal per `dotnet run` gestartet, gilt die Berechtigung des Terminals statt der von Screencap.

Damit macOS die Berechtigung über Neubauten hinweg behält, einmalig ein lokales Signatur-Zertifikat anlegen: `./scripts/create-dev-certificate.sh`.

### Windows

```powershell
dotnet run --project src/Screencap.App -f net10.0-windows10.0.19041.0
```

Die App hat zwei Zielplattformen: `net10.0` (macOS) und `net10.0-windows10.0.19041.0` (Windows, enthält die Windows-Schicht mit WinRT-OCR). Beide lassen sich auch auf dem Mac bauen. Für OCR unter Windows muss für eine Profilsprache die Texterkennung installiert sein.

## Bekannte Einschränkungen

- macOS: Aufnahme läuft über das System-Tool `screencapture`; ScreenCaptureKit wäre schneller.
- Windows: HDR-Bildschirme wirken blass, DRM-geschützte Inhalte bleiben schwarz (GDI). Pins erscheinen nur auf dem aktuellen virtuellen Desktop.
- Tastenkürzel sind noch nicht in der Oberfläche änderbar (nur in `settings.json`).
- Eingefügte Aufnahmen lassen sich verschieben, aber noch nicht skalieren.
- Noch nicht umgesetzt: Scrolling-Capture, Fenster-Erkennung, Lineal.
