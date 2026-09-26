# DriverPilot 0.3 – Stabilität und nachvollziehbare Wartung

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
