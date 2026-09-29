# DriverPilot 0.3.4

Veröffentlicht als [v0.3.4](https://github.com/Madchristian/DriverPilot/releases/tag/v0.3.4). Konkrete Signatur-, Manipulations-, Installer- und Release-Nachweise sowie die getrennt zu betrachtende spätere Upgrade-CI: [Validierung des signierten Stands](../validation/VALIDATION-SIGNED.md).

## Änderungen

- Strukturiertes Hardwareprofil für fremde PCs: System-/Mainboardhersteller, Modell, Revision, BIOS, Windows-Build und Architektur. OEM-/Notebook-Zuordnung hat Vorrang vor Retail-Mainboards.
- Gemeinsame Herstelleradapter für ASUS, MSI, Gigabyte, ASRock, Dell, HP, Lenovo und Acer. Neue Wege sind ausdrücklich manuelle offizielle Portale, keine bestätigten modellgenauen Versionsvergleiche.
- Systembericht unter Hardware & Treiber: lokale Vorschau, optionale Fehlerbeschreibung, bereinigte PCI-/USB-Modellkennungen, App-/Windows-Version und gebundener Herstellerprüfstatus. Dateiexport ohne Netzwerk; GitHub-Entwurf nur nach Zustimmung. Kein automatischer Upload oder Issue.
- Datenschutz: explizite Feldauswahl statt Snapshot-Serialisierung; keine rohen Ereignisse, Chatdaten oder vollständigen Geräteinstanz-IDs. Filter sind heuristisch: eigene Angaben und Vorschau selbst prüfen.
- Zu lange GitHub-URLs werden abgewiesen, nicht gekürzt. Das Repository bleibt privat; GitHub-Konto und Repository-Zugriff sind erforderlich.
- Veraltete Herstellerergebnisse werden vor Speicherung/Anzeige zurückgewiesen. Die Freigabe eines Berichts bleibt an einen maximal 15 Minuten alten erfolgreichen Scan gebunden.
- Neues blaues Chip-Icon mit mintfarbenem Prüfhaken für EXE, Fenster/Taskleiste, Infobereich und Installer; neun Windows-Icon-Größen.
- Aufgeräumte Quellstruktur mit separaten Ordnern für Anwendung, Tests, Build, Installer, Assets und Dokumentation; das ausgelieferte Paket bleibt unverändert flach.

## Sicherheits- und Upgradegrenzen

Die signierte Manifestprüfung und der fest hinterlegte Herausgeber aus 0.3.3 bleiben bestehen. Veröffentlichte Versionen bis 0.3.2 dürfen nicht über ihren alten App-Updatepfad migriert werden: den unabhängig geprüften signierten Installer verwenden. Keine automatischen BIOS-/Firmwareupdates oder Neustarts. Details: [UPDATE-SECURITY.md](../security/UPDATE-SECURITY.md).

## Prüfung und bewusst offene Abnahmen

Lokale Regressionen umfassen Hardware-/OEM-Zuordnung, Berichtsfeld-Auswahl, Zustimmung, übergroße Entwürfe, Generationenwechsel, WPF-Dialoge in Debug/Release sowie native Icon-Handles und Pixelvergleich der EXE. Die Release-Pipeline prüft zusätzlich das signierte Paket und den Installer auf einem wegwerfbaren Windows-Runner. Konkrete Laufresultate sind im zugehörigen GitHub-Actions-Lauf nachvollziehbar.

Nicht als abgeschlossen behauptet: echte Freundes-PC-/ASUS-Modellabnahme, interaktive GitHub-Veröffentlichung eines echten Systemberichts, manuelle Dateidialog-Abnahme und ein interaktiver Versionssprung der lokalen Installation. Synthetische Tests senden keine echten Hardwareberichte. Die Issues #7 und #10 sind damit nicht vollständig abgeschlossen.
