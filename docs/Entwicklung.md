# Screencap – Entwicklung

## Aufbau

```
src/
  Screencap.Core/              plattformunabhängig, ohne UI
    Annotations/               Markierungen, Dokument mit Undo/Redo und Zuschnitt
    Geometry/                  Rechtecke, Platzierung des Vorschaufensters
    Imaging/                   PixelBuffer (BGRA), PNG/JPEG, Weichzeichner, Verpixeln
    Ocr/                       Textlayout, QR-/Barcode-Leser (ZXing.Net)
    Platform/                  Schnittstellen, die pro OS implementiert werden
    Session/, Settings/        Sitzung und Einstellungen (JSON)
  Screencap.Platform.MacOS/    screencapture, Carbon-Hotkeys, AppKit, Vision-OCR, NSPasteboard
  Screencap.Platform.Windows/  GDI-Aufnahme, RegisterHotKey, Windows.Media.Ocr, Win32-Zwischenablage
  Screencap.App/               Avalonia-Oberfläche (für beide Systeme gemeinsam)
tests/
  Screencap.Core.Tests/        Unit-Tests
  Screencap.App.Tests/         Headless-UI-Tests (Avalonia.Headless, xUnit v3)
```

Die gesamte Oberfläche ist geteilter Code. Betriebssystem-spezifisch ist nur, was hinter den Schnittstellen in `Core/Platform/PlatformServices.cs` steckt: Aufnahme, globale Hotkeys, Fensterebenen/Dock, Texterkennung, Zwischenablage.

`Screencap.App` hat zwei Zielplattformen:

- `net10.0` – macOS (und Tests)
- `net10.0-windows10.0.19041.0` – Windows; bindet `Screencap.Platform.Windows` mit den WinRT-Projektionen ein

Beide lassen sich auf jedem System bauen (`EnableWindowsTargeting`).

### Bemerkenswertes

- **Koordinaten:** Avalonia liefert auf macOS Bildschirmgrenzen in *Punkten* (`Screen.Scaling` = 1), unter Windows in physischen Pixeln. Die echte Skalierung einer Aufnahme ergibt sich daher aus Bildbreite ÷ logischer Breite.
- **Overlay:** macOS verkleinert randlose Fenster auf den Bereich zwischen Menüleiste und Dock; das Overlay setzt seinen Frame nach dem Anheben der Fensterebene deshalb selbst (`setFrame:display:`).
- **Zwischenablage:** Avalonias `SetBitmapAsync` legte auf macOS einen leeren PNG-Eintrag ab – deshalb native Implementierungen.
- **Markierungen** liegen in Bildpixeln; Zuschnitt ist nicht-destruktiv (`AnnotationDocument.Crop`), die Arbeitsfläche ist die Vereinigung aus Screenshot und Elementen (`Capture.ContentBounds`).

## Bauen und testen

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build
dotnet test
```

Ein Test schreibt in die echte Mac-Zwischenablage und läuft nur auf Wunsch:
`SCREENCAP_CLIPBOARD_TEST=1 dotnet test`.

### macOS

```sh
./scripts/create-dev-certificate.sh   # einmalig: stabile Signatur, Berechtigungen bleiben über Neubauten erhalten
./scripts/build-macos-app.sh          # → artifacts/macos/Screencap.app
open artifacts/macos/Screencap.app
```

Per `dotnet run` gestartet, gilt die Bildschirmaufnahme-Berechtigung des Terminals statt der von Screencap – zum Testen daher das App-Bundle verwenden.

### Windows

```powershell
dotnet run --project src/Screencap.App -f net10.0-windows10.0.19041.0
```

## Updates

`Screencap.Core/Updates` fragt `releases/latest` der GitHub-API ab (öffentliches Repository, ohne Anmeldung) und wählt das Paket nach dem **Dateinamen**:

| System | Datei |
|---|---|
| macOS (Apple Silicon) | `Screencap-<version>-macos-apple-silicon.dmg` |
| Windows x64 / ARM64 | `Screencap-<version>-windows-x64.zip` / `…-windows-arm64.zip` |

Diese Namen nicht ändern, sonst findet die App ihr Update nicht mehr. Außerdem:

- Es werden nur Downloads von `https://github.com/M0nk3yOo/screencap/releases/download/` akzeptiert; Größe und SHA-256 (`digest` der GitHub-API) werden geprüft.
- Ein Release wird erst angeboten, wenn das Paket für das jeweilige System angehängt ist (die Windows-Pakete kommen einige Minuten nach dem Veröffentlichen aus dem Workflow).
- **Windows**: die laufende `Screencap.exe` wird in `Screencap.exe.old` umbenannt, die neue kommt an ihre Stelle, PowerShell startet sie nach zwei Sekunden; `.old` wird beim nächsten Start gelöscht.
- **macOS**: das DMG wird eingehängt, `Screencap.app` per `ditto` neben die installierte App kopiert, `codesign --verify` geprüft; ein abgelöstes Shell-Skript wartet auf das Ende des Prozesses, tauscht die Ordner und öffnet die App. Für die Bildschirmaufnahme-Berechtigung sollten alle Releases mit derselben Signatur-Identität gebaut werden (`scripts/create-dev-certificate.sh`).
- Vor dem Neustart setzt die App `RestoreTabsOnce`, damit offene Screenshots wiederkommen.

## Screenshots für die GitHub-Seite

Die Bilder in `docs/images` entstehen aus den echten Fenstern mit erfundenen Beispieldaten (`tests/Screencap.App.Tests/ScreenshotGallery.cs`). Nach Änderungen an der Oberfläche neu erzeugen:

```sh
SCREENCAP_SCREENSHOTS=$PWD/docs/images dotnet test tests/Screencap.App.Tests --filter ScreenshotGallery
```

Ohne die Variable tun die Tests nichts. Der Glas-Effekt ist in der headless Darstellung nicht sichtbar.

## Release erstellen

1. Version in `Directory.Build.props` (`<Version>`) erhöhen, `CHANGELOG.md` ergänzen.
2. macOS-Pakete bauen (auf einem Mac):
   ```sh
   ./scripts/package-macos.sh
   ```
   → `artifacts/release/Screencap-<version>-macos-apple-silicon.dmg` (nur Apple Silicon, keine Intel-Pakete)
3. Release anlegen und die DMGs anhängen:
   ```sh
   gh release create v<version> artifacts/release/*.dmg --title "Screencap <version>" --notes-file …
   ```
4. Das Veröffentlichen löst den Workflow **Release (Windows)** aus: Er baut auf einem Windows-Runner `Screencap.exe` (x64 und ARM64, eigenständig, eine Datei) und hängt die ZIPs an das Release. Manuell nachholen: *Actions → Release (Windows) → Run workflow* mit dem Tag.

Der Workflow **CI** baut und testet jeden Push auf `main` und jeden Pull Request auf Windows.

### Signierung

Die Releases sind nicht von Apple notariell beglaubigt bzw. nicht mit einem Windows-Codesignatur-Zertifikat signiert; Gatekeeper und SmartScreen warnen daher beim ersten Start (siehe Benutzerhandbuch). Für eine Verteilung ohne Warnungen bräuchte es einen Apple-Developer-Account (Notarisierung) bzw. z. B. Azure Trusted Signing.
