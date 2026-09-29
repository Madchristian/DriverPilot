# Signierte App-Upgrades in CI

Der Job **Signed app upgrade and recovery** in `.github/workflows/release.yml`
läuft auf einem eigenen wegwerfbaren `windows-2022`-Runner bei Pull Requests,
main-Pushes, Tags und manuellen Workflow-Läufen. Die Veröffentlichung eines
Release-Tags hängt zusätzlich von diesem Job ab.

## Sicherheitsgrenzen

- Einstieg: `tests/Test-AppUpgrade.ps1`. Installationsprüfungen dürfen nicht auf
  einem produktiven Rechner oder einem selbst gehosteten Runner laufen.
- Die Testpakete verwenden einen kurzlebigen Testschlüssel. Produktionsschlüssel
  und GitHub-Signiergeheimnisse werden diesem Job nicht bereitgestellt.
- Testpakete sind keine veröffentlichten DriverPilot-Updates. Sie dürfen weder
  in Release-Artefakte übernommen noch auf einem Benutzer-PC installiert werden.
- Es werden keine echten Treiberinstallationen und keine Windows-Neustarts
  ausgelöst.

## Nachweise

Konkreter erfolgreicher Lauf und Abgrenzung zum veröffentlichten Release: [Validierung des signierten Stands](VALIDATION-SIGNED.md).

Die Baseline ist ein Build des aktuellen Quellstands mit Version `0.0.1`,
kein historischer Download. Beide Versionen binden an denselben kurzlebigen
Testschlüssel. Der Bootstrap verwendet das authentifizierte Setup-Skript;
das Upgrade startet das bereits installierte `Setup.ps1` mit Paketpfad und
freigegebenem Manifest-Hash. Dateiauswahldialog und interaktive UAC-Bestätigung
werden nicht automatisiert. Der vorhandene Installer-Smoke-Test bleibt separat.

Geprüft werden laufender App-Prozess, manipuliertes Paket mit neu berechnetem
Manifest, echter Kopierfehler durch exklusiven Dateizugriff, erfolgreiches
Upgrade sowie gleiche und ältere Paketversionen. Jeder Fehlerfall prüft erneut
Signatur, Dateiinventar, Version, unveränderte Einstellungen/Wartungsregeln und
Sicherungen sowie den Start im isolierten App-Testmodus. Nach dem Upgrade wird
auch die zurückbehaltene vorherige Installation gestartet. Der Kopierfehler
tritt vor dem Verzeichnistausch auf; die bestehende Installation bleibt dabei
unverändert. Das ist kein Nachweis für Wiederherstellung nach Stromausfall.

`tests/Test-UpgradeFaults.ps1` prüft die Fehler-Injektionshelfer lokal ausschließlich
mit Scratch-Dateien und eigenen Testprozessen, ohne Installation oder Zertifikate.

Das Artefakt **app-upgrade-evidence** wird auch bei fehlgeschlagenen Tests
hochgeladen und 14 Tage aufbewahrt. Es stammt aus `artifacts/upgrade-tests/`.
Maßgeblich ist der CI-Lauf des jeweiligen Commit-SHA, nicht allein die Existenz
des Testskripts. Ein grüner Lauf belegt nur die dort ausgeführten Szenarien.

Die historische `VALIDATION-0.3.md` bleibt ein unveränderter Bericht über den
Stand von 0.3.0; ihre damaligen Einschränkungen sind keine aktuelle Aussage zum
Signaturmodell. Dessen Regeln stehen in `docs/security/UPDATE-SECURITY.md`.
