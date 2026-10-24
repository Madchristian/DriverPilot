# DriverPilot 0.3.7 — persönlicher Testkandidat

**Vorbereitet, nicht veröffentlicht.** Erweitert die Ferndiagnose um Detailangaben zur Hardware (Bericht-Schema 1.1). Die veröffentlichte Version bleibt bis zur gesonderten Freigabe 0.3.6.

## Änderungen gegenüber 0.3.6

- **Mehr Hardwaredetails im Ferndiagnosebericht** (`hardware_details`, Schema 1.1):
  - CPU: Kerne, Threads, Nenntakt.
  - Arbeitsspeicher: Steckplatz, Größe, DDR-Typ, Nenn- und eingestellte Geschwindigkeit (zeigt XMP/EXPO), Hersteller, Typnummer.
  - Grafikkarten: Modell, VRAM, Treiberversion; bei NVIDIA zusätzlich eine `nvidia-smi`-Momentaufnahme (Temperatur, Lüfter, Takt aktuell/maximal, Leistungsaufnahme und Power-Limits, PCIe-Generation und -Breite).
  - Laufwerke: Modell, SSD/HDD, Bus (NVMe/SATA/…), Größe, Zustand laut Windows, Firmware; Größe und freier Platz des Systemlaufwerks, Auslagerungsdatei.
  - Plattform: UEFI/Legacy, Secure Boot, Energiesparplan (nur Windows-Standardplan oder „custom“, nie der frei vergebene Name), hardwarebeschleunigte GPU-Planung, Laufzeit seit dem Start.
- Keine Seriennummern. Neue Textfelder unterliegen denselben Textmarkern wie bisher; Werte außerhalb der Schemagrenzen werden `null`, nie geschätzt. Jeder Bereich, dessen Abfrage scheitert, ist `null` und erscheint als Warnung im Scan.
- Vertrag: `schema_version` 1.1 mit Pflichtfeld `hardware_details`; Textmarker-Zeiger unterstützen `*` für Array-Elemente. Der Client sendet nur an Server, die 1.1 anbieten.

## Voraussetzungen

- Server mit `report_schema_versions` einschließlich `1.1` und aktualisiertem Datenschutzhinweis, der die neuen Felder nennt. Freunde stimmen beim nächsten Senden dem neuen Hinweis zu.

## Prüfung

Vertragsvalidator, plattformneutrale Ferndiagnosetests (Projektion, Grenzwerte, feindliche Werte, Textmarker, Schemakonformität) und die Windows-Tests (Inventur mit Mocks einschließlich Einzelelement-Arrays und Seriennummernausschluss, echte Inventur im Selbsttest) laufen in CI. Ein echter Lauf von `nvidia-smi` ist nur auf einem PC mit NVIDIA-Karte prüfbar und steht noch aus.
