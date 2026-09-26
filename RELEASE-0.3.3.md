# DriverPilot 0.3.3

## Sichere App-Updates

- RSA-SHA256-signierte Paketmanifeste authentifizieren Produkt, exakte Version, vollständiges Dateiinventar und SHA-256-Hashes. Nur das fest hinterlegte Herausgeberzertifikat wird akzeptiert.
- Die Freigabe ist an die exakten Manifestbytes gebunden. Der bereits installierte Updater prüft Quelle und geschützte Zwischenkopie erneut; Code aus dem angebotenen Paket wird dabei nicht ausgeführt.
- Vollständiger Ordneraustausch statt Datei-Overlay, Schutz gegen parallele Updates und ein Arbeitsverzeichnis außerhalb der Installation. Der vorige Installationsordner bleibt für manuelle Wiederherstellung erhalten.
- Unsigned Builds bleiben reine Entwicklungsprogramme; daraus werden keine Installer oder Release-ZIPs erstellt.

## Installation und Grenzen

**Veröffentlichte Versionen bis einschließlich 0.3.2 nur über den unabhängig geprüften signierten Installer aktualisieren, nicht über deren alten App-Updatepfad.** Den Zertifikat-Fingerabdruck direkt mit dem Herausgeber abgleichen und die Authenticode-Signatur vor der Administratorfreigabe prüfen. Siehe [UPDATE-SECURITY.md](UPDATE-SECURITY.md) und [ZERTIFIKAT-ANLEITUNG.txt](ZERTIFIKAT-ANLEITUNG.txt). Selbstsignierung verhindert nicht automatisch SmartScreen-Warnungen oder Smart-App-Control-Blockaden.

Die Manifest-Signatur hat keine Zeitstempel-Verlängerung; das fest hinterlegte Zertifikat muss bei jeder Paketprüfung aktuell gültig sein. Kein automatischer Windows-Neustart, kein automatisches Rollback und keine Erweiterung der Treiber-/Firmwarewartung.

## Teststand

Die lokale Bootstrap-Installation des signierten 0.3.2-Entwicklungsbuilds mit dieser Updateabsicherung wurde durchgeführt; die Benutzeroberfläche wurde vom Benutzer akzeptiert. Dieser lokale Testbuild ist nicht mit dem veröffentlichten 0.3.2 gleichzusetzen. Ein echter Versionssprung über den neuen App-Updatepfad wurde **nicht Ende-zu-Ende getestet**. Automatisierte Paket-, Signatur-, Updater- und Anwendungstests ersetzen diesen Nachweis nicht. Keine Treiberinstallation, Wartung oder Firmwareaktualisierung wurde für diese Releasevorbereitung ausgelöst.
