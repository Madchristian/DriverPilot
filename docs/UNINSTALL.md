# DriverPilot entfernen

Diese Anleitung beschreibt die neue Deinstallation im Quellstand; bereits veröffentlichte Installer bis einschließlich 0.3.4 registrieren noch keinen Uninstaller.

## Standard: Programm entfernen, Daten behalten

1. DriverPilot über **Infobereich → Beenden** schließen. Das Schließen des Hauptfensters allein minimiert die App nur.
2. Eine laufende Wartung zu Ende laufen lassen. Die Deinstallation bricht weder eine Treiberinstallation noch einen Wartungsprozess gewaltsam ab. Bei laufenden oder nicht sicher prüfbaren Prozessen wird die Entfernung zurückgestellt.
3. Als ursprünglicher Installationsbenutzer **Windows → Einstellungen → Apps → Installierte Apps → DriverPilot → Deinstallieren** öffnen und die Administratorabfrage bestätigen. Wie die Einrichtung unterstützt die Entfernung das gleiche lokale Administratorkonto; keine fremden Administrator-Anmeldedaten verwenden.
4. Die drei Fragen zur Datenlöschung stehen jeweils auf **Nein**. Ohne ausdrückliche Auswahl bleiben Daten erhalten.

Entfernt werden die Programmdateien, zugehörige geschützte alte Programmversionen, der eigene Wartungsauftrag, der passende Autostarteintrag und die passende Startmenüverknüpfung. Fremde oder unerwartet umgebogene Einträge werden nicht blind gelöscht. Die Deinstallation ist kein Treiber-Rollback: bereits installierte Treiber und Windows-Update-Einstellungen bleiben unverändert.

Leere geschützte `DriverPilot.next-<GUID>`-Zwischenordner werden nichtrekursiv entfernt. Nach einem abgebrochenen Update können nichtleere, unvollständige und deshalb nicht authentifizierbare Zwischenordner zurückbleiben. Diese werden mit Warnung aufbewahrt, statt ihre unbekannten Dateien zu löschen oder die Entfernung der intakten App zu blockieren. Die Abschlussmeldung weist auf mögliche Reste hin; bei Bedarf manuell prüfen lassen.

Der Uninstaller liegt getrennt unter `%ProgramFiles%\DriverPilot.Uninstall`. Dadurch bleibt `%ProgramFiles%\DriverPilot` ein exakt prüfbares signiertes Paket und App-Upgrades können den Programmordner weiter vollständig austauschen. Ein Update nur per ZIP kann einen fehlenden Windows-Deinstallationseintrag nicht nachträglich erzeugen; dafür einmal den neuen, unabhängig geprüften Installer verwenden.

## Bewusste Datenlöschung

Die Auswahl wirkt nur auf bekannte DriverPilot-Daten, nicht auf andere Benutzerprofile oder beliebige unbekannte Dateien:

| Auswahl | Bedeutung | Standard |
| --- | --- | --- |
| Einstellungen löschen | Benutzereinstellungen, Wartungsregeln, eigener DriverPilot-Codex-Anmeldespeicher und lokaler Ferndiagnose-Zugang samt Fallreferenz (`remote-diagnosis.json` einschließlich `.tmp`) | Behalten |
| Protokolle löschen | Gespeicherte Diagnosen, Chat, Verlauf, Hersteller-/Beobachtungsdaten und Wartungsnachweise | Behalten |
| Treibersicherungen löschen | `%ProgramData%\DriverPilot\Backups` einschließlich exportierter INF-Pakete | **Behalten** |

Unbekannte Dateien und die kleine geschützte Besitzerzuordnung `ProgramData\DriverPilot\app-install.json` werden konservativ belassen. Diese Zuordnung ermöglicht sichere Wiederholungsversuche und verhindert, dass ein anderes Administratorkonto erhaltene Daten übernimmt. Aufbewahrte Daten werden bei einer späteren Neuinstallation wieder vorgefunden. Vor dem Löschen von Wartungsnachweisen insbesondere einen offenen oder fehlerhaften Wartungslauf manuell prüfen. Sicherungen können beim manuellen Wiederherstellen helfen, garantieren aber kein vollständiges Rollback.

Für bewusst automatisierte Entfernung unterstützt `unins000.exe` neben den Inno-Optionen `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` die unabhängigen Schalter `/REMOVESETTINGS`, `/REMOVELOGS`, `/REMOVEBACKUPS`. **Ohne diese Schalter bleiben sämtliche Daten erhalten.** Keine Wildcard-Löschbefehle auf ProgramData oder Benutzerprofile verwenden.

Bei einem Fehler bleibt der Deinstallationseintrag für einen erneuten Versuch erhalten.
 Bei unterbrochener Programmentfernung bindet ein geschützter Nachweis unter
 `ProgramData\DriverPilot\UninstallPending` die Fortsetzung an das ursprünglich
 authentifizierte Dateiinventar. Solange dieser Nachweis besteht, verweigert Setup
 eine Neuinstallation oder ein Upgrade. Zuerst dieselbe Deinstallation erneut
 ausführen; den Nachweis nicht manuell löschen. Für die genaue PowerShell-Fehlermeldung kann ein Administrator des ursprünglichen Kontos den geschützten Helfer direkt ausführen:

```powershell
& "$env:ProgramFiles\DriverPilot.Uninstall\Uninstall.ps1"
```

Danach den Windows-Deinstallationseintrag erneut ausführen, um auch die Inno-eigenen Dateien und den Eintrag zu entfernen. Keine `-Remove...`-Schalter ergänzen, wenn Daten erhalten bleiben sollen. Nur den installierten, geschützten Helfer verwenden, kein beliebiges heruntergeladenes Skript.

Die Auswahl **Einstellungen löschen** entfernt nur den lokalen Ferndiagnose-Zugang
und die lokale Fallreferenz. Sie löscht keine serverseitigen Fälle und widerruft
keine serverseitigen Tokens; dafür ist eine getrennte serverseitige Aktion nötig.

## Zertifikatsvertrauen bleibt separat

Weder App-Entfernung noch Datenlöschung verändert einen Windows-Zertifikatspeicher. Dasselbe Zertifikat kann für weitere Programme genutzt werden. Vertrauen nur bewusst separat entfernen:

- `certlm.msc` öffnen, **Vertrauenswürdige Stammzertifizierungsstellen → Zertifikate**.
- Ausschließlich den in der [Zertifikatanleitung](https://github.com/Madchristian/DriverPilot/blob/main/docs/security/ZERTIFIKAT-ANLEITUNG.txt) durch Fingerabdruck identifizierten Eintrag entfernen, nicht nach Anzeigename allein. Die gleiche Anleitung liegt im Programmpaket als `ZERTIFIKAT-ANLEITUNG.txt`.
- Falls Vertrauen zusätzlich manuell für den aktuellen Benutzer oder unter **Vertrauenswürdige Herausgeber** eingerichtet wurde, auch dort den exakt gleichen Fingerabdruck prüfen (`certmgr.msc` für den Benutzer).
- Den privaten Schlüssel unter **Eigene Zertifikate** nicht als Teil einer App-Deinstallation löschen.

## Prüfung

Automatisierte signierte Branch-Abnahme und ihre Freigabegrenzen:
[Deinstallations-CI](validation/UNINSTALL-CI.md). Die destruktiven Tests niemals
durch gefälschte Runner-Variablen auf einem Entwickler-PC oder in einer manuell
gestarteten Sandbox freischalten.

Lokale, nicht privilegierte Regressionen:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Test-InstallationContract.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Test-Uninstall.ps1
```

`tests/Test-UninstallLifecycle.ps1 -InstallerPath ...` prüft mit einem echten Installer Installation, erneute Einrichtung, Deinstallationsregistrierung, Prozesssperren, Standardaufbewahrung, Neuinstallation und bewusste Löschung. Dieser destructive Integrationstest verweigert außerhalb einer erhöhten, wegwerfbaren GitHub-Windows-Testmaschine den Start. Er wird über `tests/Test-Pipeline.ps1 -Installer` im signierten Build-Job ausgeführt. Ein vorhandener Test ist kein Beleg für einen bereits erfolgreich ausgeführten Runner-Lauf; Ergebnisse sind separat im Validierungsbericht auszuweisen.
