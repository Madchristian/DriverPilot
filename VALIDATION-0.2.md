# Prüfung von DriverPilot 0.2

Stand: 26.09.2026. Nur lesende Herstellerprüfung, keine Treiber-/Firmwareinstallation.

- Release-Build erfolgreich, keine Compilerwarnungen oder -fehler.
- Regressionen geprüft: HTTPS-/Domainfreigabe, irreführende URLs, NVIDIA-Versionsumrechnung, AMD-Paketabschnitt, unbekannte installierte Version, Downgrade, gesperrte Seiten, Beta-Ausschluss.
- Live geprüft: AMD-Katalog (8.05.04.516 installiert, 8.08.12.551 angeboten), NVIDIA-Katalog (617.14 installiert und angeboten), Creative-ZxR-Katalog.
- MSI liefert HTTP 403. Die App meldet den fehlenden Vergleich ausdrücklich; kein neuer BIOS-Stand wurde verifiziert.
- SSD-Firmware wurde lokal gelesen. SanDisk/Corsair-Verfügbarkeit erfordert die jeweiligen Herstellertools und wurde nicht als bestätigt ausgegeben.
- Neue WPF-Seite mit echten Gerätedaten gerendert und visuell geprüft.
- Keine KI-Anfrage, kein Git-Testprozess und kein Neustart während der Tests.

Die neue Herstellerprüfung kann keine Pakete installieren. Sie erweitert die privilegierte Windows-Update-Wartung nicht.