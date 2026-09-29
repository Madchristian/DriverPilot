# Validierung des signierten Stands

Nachweisstand: 29.09.2026. Dieser Bericht trennt das veröffentlichte **v0.3.4** von späteren CI-Erweiterungen auf main. Er dokumentiert nachgelesene GitHub-Job-/Schrittergebnisse, relevante Logausgaben und das Upgrade-Ergebnisartefakt; er behauptet keinen neuen lokalen Signier- oder Installationslauf für diese Dokumentationsänderung.

## Veröffentlichter Release 0.3.4

- [Release v0.3.4](https://github.com/Madchristian/DriverPilot/releases/tag/v0.3.4), veröffentlicht am 26.09.2026.
- Getesteter Commit: `40ab4d893f135d247b6ac66d65e871bc80724920`.
- [Tag-Lauf 36277764280](https://github.com/Madchristian/DriverPilot/actions/runs/36277764280): `success` für Build und Veröffentlichung. Die nachstehenden Schritte wurden ausgeführt, nicht übersprungen.

| Nachweis | Ergebnis und Umfang | Quelle |
| --- | --- | --- |
| Signierung und Manipulationsabwehr | `Verify signatures and reject tampering`: erfolgreich. Erwarteter Herausgeber, gültige Authenticode-Signaturen und Zeitstempel; manipulierte EXE-/Skriptkopien zurückgewiesen; Zertifikat ohne privaten Schlüssel. Signiertes Manifest und exaktes ZIP-Inventar einschließlich Duplikat-/Byteprüfung werden vom Test geprüft. | [Build-Job 108503683489](https://github.com/Madchristian/DriverPilot/actions/runs/36277764280/job/108503683489), Schritt gleichen Namens; [Testcode am Release-SHA](https://github.com/Madchristian/DriverPilot/blob/40ab4d893f135d247b6ac66d65e871bc80724920/tests/Test-Signing.ps1) |
| Adversariale Pakettests | `Adversarial package tests (ephemeral keys, no production bypass)`: erfolgreich. Eigene kurzlebige Testschlüssel, kein Produktions-Bypass. Nicht mit einer realen Treiberinstallation gleichzusetzen. | [Build-Job](https://github.com/Madchristian/DriverPilot/actions/runs/36277764280/job/108503683489) |
| Installer | `Test installation on disposable runner`: erfolgreich. Log bestätigt geschützte Installation, Dateiprüfsummen und Wartungsauftrag; Auftrag anschließend deaktiviert. Keine Treiber installiert. | [Build-Job](https://github.com/Madchristian/DriverPilot/actions/runs/36277764280/job/108503683489), [Testcode am Release-SHA](https://github.com/Madchristian/DriverPilot/blob/40ab4d893f135d247b6ac66d65e871bc80724920/tests/Test-Pipeline.ps1) |
| Release-Paketierung und Veröffentlichung | `Package installer and ZIP` und `Verify and publish release`: erfolgreich. `sha256sum --check SHA256SUMS.txt` meldet `OK` für Setup-EXE, ZIP, CER und Zertifikatanleitung; Release-Assets vorhanden. | [Release-Job 108504104951](https://github.com/Madchristian/DriverPilot/actions/runs/36277764280/job/108504104951), [Release](https://github.com/Madchristian/DriverPilot/releases/tag/v0.3.4) |
| App-/Transport-/Hardware-/Icon-Regressionen | Die entsprechenden Build-Schritte sind erfolgreich. Synthetische Dialogtests ersetzen keine praktische Freundes-PC-Abnahme. | [Build-Job](https://github.com/Madchristian/DriverPilot/actions/runs/36277764280/job/108503683489) |

Der Release liefert `DriverPilot-0.3.4-Setup-x64.exe`, `DriverPilot-0.3.4-win-x64.zip`, `DriverPilot.cer`, `ZERTIFIKAT-ANLEITUNG.txt` und `SHA256SUMS.txt`. Die Prüfsummenprüfung im Release-Job erfolgt vor dem Upload; sie ist **kein separat wiederholter Download-Artefakt-E2E-Test nach der Veröffentlichung**. Paketinhalt, privater Zugriff und Erstinstallation sind in der [README](../../README.md) beschrieben.

## Spätere signierte Upgrade-/Fehlerprüfung auf main

- Getesteter Commit: `5c485d87633b23831d6f0042ea67a3964383a582`, nicht der Release-Commit oben.
- [main-Lauf 36536778676](https://github.com/Madchristian/DriverPilot/actions/runs/36536778676), 29.09.2026: erfolgreich.
- [Job „Signed app upgrade and recovery“ 109302531795](https://github.com/Madchristian/DriverPilot/actions/runs/36536778676/job/109302531795): erfolgreich; `app-upgrade-evidence/results.json` heruntergeladen und geprüft.

Alle dort aufgeführten Fälle melden `PASS`: `bootstrap`, `running-app`, `tampered-rehashed`, `copy-fault`, `upgrade`, `same-version`, `older-version`, `safety`. Geprüft wurden der bereits installierte vertrauenswürdige Updater, Zurückweisung bei laufender App, Manipulation trotz neu berechneter Hashes, ein realer exklusiver Dateizugriff als Kopierfehler, ein erfolgreicher Versionssprung sowie Ablehnung gleicher/älterer Versionen. Der Test kontrolliert unveränderte Benutzer-/Wartungsdaten und Sicherungen, Paketbytes und isolierte App-Starts einschließlich der zurückbehaltenen Installation.

**Grenzen:** Die Baseline `0.0.1` ist synthetisch aus demselben Quellstand gebaut; beide Testpakete verwenden einen kurzlebigen Schlüssel und einen nur in der Testkopie angepassten Pin. Das ist weder ein historischer Release-Download noch ein Upgrade von 0.3.2 mit dem Produktionsschlüssel. Der Kopierfehler tritt vor dem Verzeichnistausch auf; Stromausfall-Recovery wird damit nicht bewiesen. Dateiauswahl und interaktive UAC-Bestätigung sind nicht automatisiert. Details: [APP-UPGRADE-CI.md](APP-UPGRADE-CI.md).

Im **Build-Job dieses main-Laufs** sind Produktionssignierung, Release-Paketierung, `Test-Signing.ps1` und Installer-Smoke-Test übersprungen; der Release-Job ist ebenfalls übersprungen. Ein grüner main-Lauf ist daher kein neuer signierter Produktionsrelease. Das Upgrade-Evidence-Artefakt wird 14 Tage aufbewahrt; seine spätere Abwesenheit ist kein fehlgeschlagener Test, begrenzt aber die erneute Artefaktprüfung.

## Manueller signierter Testbuild ist kein Release

Der [manuelle main-Lauf 36249997758](https://github.com/Madchristian/DriverPilot/actions/runs/36249997758) am Commit `e00c5610eed1caed2e5bdaf8daf7678d89d38b88` (Stand 0.3.2) war erfolgreich: Signatur-/Manipulationsprüfung und Installerprüfung wurden ausgeführt, der Release-Job übersprungen. Das belegt diesen historischen signierten Testbuild, **nicht** die erst ab 0.3.3 eingeführte Manifestauthentifizierung oder spätere Upgrade-CI. Die aktuelle Auswahl `sign=true` ist nur auf `main` produktionssigniert; normale PR-/Branch-Builds liefern keine Release-ZIPs oder Installer. Vorgehen: [README – Repository und Release](../../README.md#repository-und-release).

## Historische Berichte und weiterhin offene Grenzen

[VALIDATION-0.2.md](VALIDATION-0.2.md) und [VALIDATION-0.3.md](VALIDATION-0.3.md) bleiben unveränderte historische Berichte. Die Aussage „nicht signiert“ beschreibt dort 0.3.0, nicht den heutigen Release. Damals nicht ausgeführte Tests werden nicht nachträglich als bestanden ausgegeben. Dateisignaturen ab 0.3.2 machen dessen alten Hash-only-Updater nicht nachträglich sicher: Migration bis einschließlich 0.3.2 nur über einen unabhängig geprüften signierten Installer.

Weiterhin **nicht als Ende-zu-Ende bestanden** ausgewiesen:

- reale Treiberinstallation und anschließendes Treiber-Rollback;
- BIOS-/Firmwareflash oder automatische Hersteller-EXE-Installation;
- neue Live-Codex-Anmeldung und vollständiger angemeldeter KI-Ablauf;
- praktische Freundes-PC-/ASUS-Modellabnahme, interaktive Dateiauswahl/UAC und Versionswechsel der lokalen Benutzerinstallation;
- Wiederherstellung nach Stromausfall oder vollständiger Systemausfall.

Windows-Signaturvertrauen ist kein Stabilitätsnachweis und beseitigt SmartScreen-/Smart-App-Control-Blockaden nicht automatisch. Keine Schutzfunktionen abschalten. [Zertifikatanleitung](../security/ZERTIFIKAT-ANLEITUNG.txt), [Update-Sicherheitsmodell](../security/UPDATE-SECURITY.md) und [Signing-Lifecycle-Runbook](../security/SIGNING-LIFECYCLE.md) beschreiben Vertrauensaufbau, Ablauf/Rotation und Schlüsselverlust. Neue CI-/Betriebsänderungen und diese Dokumentationsänderung sind durch frühere grüne Läufe nicht automatisch freigegeben.
