# DriverPilot – abgeschlossene 0.3-Basis und Folgeplanung

## Ursprünglicher Umfang 0.3 (umgesetzt)

Verbindlich: Windows-Neustarts nur durch den Benutzer; BIOS, Firmware und Speichertreiber bleiben manuell. Keine freien KI-Systembefehle.

1. Wartungsregeln: Wochentage, Zeitfenster, mindestens 14 Tage Wartezeit, Pause bis morgen, Prozessliste, Rücksicht auf Netzwerkverkehr. Der erhöhte Worker validiert alle Eingaben selbst.
2. Sperren: Geräte auf installierter Version halten und einzelne Windows-Update-Pakete blockieren; übersichtliche Aufhebung.
3. Diagnose: Ereignisse nach Gerät und Stunde gruppieren, Datenträger 158 lokal zuordnen, Vorher-/Nachher-Beobachtung über 24 Stunden. Zeitlicher Zusammenhang ist kein Ursachenbeweis.
4. Wiederherstellung: Sicherungen und Installationsprotokolle anzeigen, Geräte-Manager und dokumentierten manuellen Rückweg anbieten. Kein pauschales Zurückspielen sämtlicher Treiber.
5. Hersteller: zusätzliche offizielle Einstiegspunkte für erkannte Hardware, geführte BIOS-/Firmwareprüfung mit Vergleichsnotiz; unbekannte Versionen bleiben unbekannt.
6. Codex: Befund gezielt erklären, lokale Fakten/Herstellerangaben/Hypothesen trennen, Tool-Aufrufe mit Ergebnis protokollieren, lokalen Chat speichern/löschen.
7. App-Wartung: geordnet beenden/neustarten, Versionsanzeige, private GitHub-Releases öffnen und ein lokal ausgewähltes Updatepaket nach Manifestprüfung installieren.
8. Auslieferung: private GitHub-Ablage, reproduzierbare Build-/Prüfanleitung, Regressionstests und Windows-Installation.

Prüfung: Policy-Grenzfälle ohne Treiberinstallation, Vergleichslogik, UTF-8-Transport, echte schreibgeschützte Hardwareabfragen, Herstellerquellen und WPF-Ansichten. Installation echter Treiber/BIOS ist kein Testschritt.

## Abschlussstand 26.09.2026

Alle acht Arbeitsbereiche sind in Version 0.3.0 umgesetzt. Die geschützte Windows-Installation wurde aktualisiert und der tägliche Auftrag auf ein 30-Minuten-Intervall über den ganzen Tag umgestellt; die tatsächlichen Installationen bleiben an die benutzerdefinierten Regeln gebunden. Standard bleibt Sonntag 12–17 Uhr.

Herstellererweiterungen ohne maschinenlesbaren Versionskatalog sind bewusst manuelle Portale/Assistenten. Wiederherstellung nutzt die von Windows angebotenen manuellen Rückwege. App-Updates benötigen derzeit ein heruntergeladenes, entpacktes Paket und die Windows-Administratorfreigabe. Die automatische Suche nach Treibern ist davon unabhängig.

Historische Tests und damalige Grenzen: [VALIDATION-0.3.md](../validation/VALIDATION-0.3.md). Der Bericht bleibt unverändert; insbesondere war 0.3.0 noch unsigniert.

## Signierter Stand und getrennte Folge-Milestones

0.3.2 führte signierte Release-Dateien ein, 0.3.3 das authentifizierte vollständige Updateinventar. 0.3.4 ist veröffentlicht und ergänzt Hardwareprofile, Systemberichte und Icon. Nachfolgend auf main ergänzte Upgrade-CI und Zertifikatsbetriebsverfahren sind nicht rückwirkend Bestandteil des Tags 0.3.4. Erfolgreiche Läufe mit exakten Quellständen und verbleibenden Testlücken: [Validierung des signierten Stands](../validation/VALIDATION-SIGNED.md).

Die GitHub-Milestones führen eigenständige Folgearbeiten; eine veröffentlichte Versionsnummer schließt ihren gleichnamigen Milestone nicht automatisch ab:

| Milestone | Umfang und Abgrenzung |
| --- | --- |
| [0.3.3 – Updates und Signierung absichern](https://github.com/Madchristian/DriverPilot/milestone/1) | Authentifizierte Updates, signierte Upgrade-/Fehlertests, Zertifikatsbetrieb und aktuelle Dokumentation. Der historische Milestone-Name bleibt bestehen, auch wenn Nachweise aus 0.3.4 und späteren main-Commits stammen. |
| [0.4.0 – Nutzung im Freundeskreis](https://github.com/Madchristian/DriverPilot/milestone/2) | Bewusste Aktivierung der Wartung, verständliche Voraussetzungen, Einrichtung/Deinstallation, datensparsame Fehlerberichte und dokumentierter Betatest. Kein bereits abgeschlossener Freundes-PC-Test. |
| [0.5.0 – Hardware und Diagnose erweitern](https://github.com/Madchristian/DriverPilot/milestone/3) | Weitere Hardwarekonfigurationen, reproduzierbare Herstellerquellentests und Nachbeobachtung über App-Neustarts. Unbekannte Versionsstände bleiben offen. |

Maßgeblich sind die Akzeptanzkriterien der verlinkten Issues, nicht allein vorhandener Code. Reale Treiberinstallation und Rollback, BIOS-/Firmwareflash und neue Live-Codex-Anmeldung sind weiterhin nicht Ende-zu-Ende nachgewiesen. Keine automatischen Windows-Neustarts oder freien KI-Systembefehle; BIOS/Firmware bleiben manuell.
