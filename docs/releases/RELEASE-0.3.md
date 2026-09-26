# DriverPilot 0.3.0

Mehr Kontrolle über automatische Treiberwartung und bessere Nachvollziehbarkeit nach Änderungen:

- Einstellbare Wartungstage/-stunden, 14–90 Tage Paketbeobachtung, Pause und Aktivitätsschutz.
- Einzelne Geräte und Windows-Update-Pakete sperren.
- 24-Stunden-Nachbeobachtung nach erkanntem Treiberwechsel mit zusätzlichen Befunden und Benachrichtigung.
- Ereignisse nach Gerät und Stunde gruppieren, disk 158 zuordnen, WLAN-Abbrüche berücksichtigen.
- Wiederherstellungsbereich für Treibersicherungen und manuelle Windows-Rückwege.
- Zusätzliche Herstellerportale und geführte BIOS-/Firmwareprüfung mit Notizen.
- Gespeicherter Chat, gezielte Befunderklärung und sichtbares KI-Werkzeugprotokoll.
- Geordneter App-Neustart und lokale Updatepakete mit Manifestprüfung.

## Installation

ZIP aus diesem privaten Release herunterladen und vollständig entpacken. `DriverPilot.exe` starten; unter Einstellungen die Automatik einrichten. Bei einer bestehenden Version 0.2 zuerst DriverPilot über das Infobereich-Menü beenden und im entpackten Paket `Setup.ps1` mit Administratorrechten ausführen. Ab 0.3 gibt es eine Updateauswahl in den Einstellungen.

Keine Windows-Neustarts durch DriverPilot. BIOS/Firmware bleiben manuell. Herstellerportale ohne verlässlichen Katalog werden nicht als erfolgreiche Versionsprüfung dargestellt. Die Sicherung ist keine Garantie für ein mögliches Rollback.

Das Paket ist nicht digital signiert; das enthaltene SHA-256-Manifest prüft die Dateiintegrität, nicht unabhängig die Herkunft. Nur dieses private Projekt oder eigene Builds als Bezugsquelle verwenden.

Build, 25 Wartungs-/Konfigurationsfälle, Nachbeobachtung, Paketprüfung, UTF-8-Transport, lokale Hardwareabfrage und WPF-Ansichten geprüft. Keine echten Treiberinstallationen, Rollbacks oder Firmwareupdates zum Testen ausgelöst. Details: [VALIDATION-0.3.md](../validation/VALIDATION-0.3.md).
