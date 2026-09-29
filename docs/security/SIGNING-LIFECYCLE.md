# Signierschlüssel: Sicherung, Ablauf, Wechsel und Vorfall

Maintainer-Runbook für [Issue #3](https://github.com/Madchristian/DriverPilot/issues/3).
Für Empfänger gilt die [Zertifikatanleitung](ZERTIFIKAT-ANLEITUNG.txt), für den
Prüfvertrag [Update-Sicherheit](UPDATE-SECURITY.md). Dieses Runbook ist keine
Freigabe, Produktionsschlüssel zu exportieren oder GitHub-Einstellungen zu ändern.
Solche Schritte führt ausschließlich der autorisierte Maintainer bewusst aus.

## Sicherheitsgrenzen und Inventar

- `assets/certificates/DriverPilot.cer` enthält ausschließlich das öffentliche
  Zertifikat. `PackageTrust.CertificateHash` in
  `src/DriverPilot/Security/PackageTrust.cs` pinnt SHA-256 der exakten DER-Datei.
  Der SHA-1-Thumbprint identifiziert dagegen den Windows-Speichereintrag; er ist
  nicht der Signaturalgorithmus. Aktuelle Fingerabdrücke stehen in der
  Zertifikatanleitung und müssen über einen unabhängigen Kanal bestätigt werden.
- Lokal liegt der private Schlüssel unter `CurrentUser\My` (certmgr.msc →
  Eigene Zertifikate). GitHub Actions verwendet `DRIVERPILOT_SIGNING_PFX`
  (Base64 der **verschlüsselten** PFX) und `DRIVERPILOT_SIGNING_PASSWORD`.
  Base64 ist keine Verschlüsselung. GitHub Secrets lassen sich nicht als
  Sicherung wieder auslesen; sie ersetzen kein Offline-Backup.
- App und Installer importieren **keine** Zertifikate automatisch. Windows-Vertrauen
  ist eine getrennte, bewusste Entscheidung des Empfängers. Es verändert den
  fest eingebauten Updater-Pin nicht. Ein Kandidatenzertifikat kann sich nicht
  selbst autorisieren; es gibt keinen Produktionsschalter zum Umgehen des Pins.
- Ausnahme nur für ausdrücklich autorisierte signierte CI-Läufe:
  `build/Initialize-CISigning.ps1` importiert Schlüssel und temporäres
  Root-Vertrauen auf dem wegwerfbaren GitHub-Runner.
  `build/Remove-CISigning.ps1` entfernt beides vor dem Upload. Diese Skripte
  nicht durch gefälschte Runner-Umgebungsvariablen lokal ausführen.

## 1. Verschlüsseltes Offline-Backup

Vor Rechnerwechsel und nach jeder Rotation; zusätzlich regelmäßig die
Wiederherstellbarkeit prüfen. Ein öffentliches `.cer` allein genügt nicht.

1. Auf dem vertrauenswürdigen Signierrechner das Zertifikat in certmgr.msc anhand
   des unabhängig bekannten Thumbprints auswählen, nicht allein anhand des Namens.
   Gültigkeit und vorhandenen privaten Schlüssel kontrollieren.
2. Über **Alle Aufgaben → Exportieren → Ja, privaten Schlüssel exportieren →
   PKCS #12 (.PFX)** exportieren. Starke passwortbasierte Verschlüsselung wählen
   (AES-256, sofern angeboten) und ein langes, zufälliges, einmaliges Passwort
   aus einem Passwortmanager verwenden. Den produktiven Schlüssel beim Export
   **nicht löschen**. Ist der Schlüssel nicht exportierbar, abbrechen und eine
   geplante Rotation vorbereiten; keine Schutzmechanismen umgehen.
3. Die verschlüsselte PFX direkt auf einen zugriffsgeschützten, verschlüsselten
   Offline-Datenträger schreiben. Eine zweite verschlüsselte Kopie getrennt
   aufbewahren. Passwort separat im abgesicherten Passwortmanager mit eigenem
   Wiederherstellungsweg halten, nicht neben die PFX legen.
4. Nur öffentliche Metadaten dokumentieren: Zertifikatfingerabdrücke,
   NotBefore/NotAfter in UTC, verantwortliche Person, Sicherungsdatum und Datum
   der erfolgreichen Wiederherstellungsprobe. Keine PFX-/Passwortinhalte in
   Tickets, Chat, Screenshots, Zwischenablagehistorie, Transkripte oder Logs.
5. Datenträger auswerfen und offline verwahren. Keine PFX in Repository,
   Build-Ausgabe, Release, CI-Artefakt, synchronisierten Ordner oder E-Mail ablegen.
   Eine `.gitignore`-Regel ist kein Schutz vor einem absichtlichen Upload.

## 2. Wiederherstellung und Prüfung

Bei möglichem Diebstahl/Kompromittierung zuerst Abschnitt 5, nicht einfach
wiederherstellen und weitersignieren.

1. Auf einem sauberen, zugriffsgeschützten Windows-System das öffentliche
   Referenzzertifikat und seinen unabhängig bestätigten Fingerabdruck bereithalten.
   Für die Probe vorzugsweise eine isolierte, anschließend verworfene Umgebung
   ohne Cloud-Synchronisation verwenden. Kein Terminal-Transkript oder Debuglog
   mit Geheimnissen erzeugen; Passwort niemals als Kommandozeilenargument eingeben.
2. Die PFX mit der Windows-Importoberfläche öffnen, Passwort ausschließlich im
   geschützten Dialog eingeben und bewusst nach **Aktueller Benutzer → Eigene
   Zertifikate** importieren, nicht nach Root/TrustedPublisher. Den restaurierten
   Schlüssel nicht erneut exportierbar machen, sofern betrieblich nicht nötig.
   Ein Root-Import ist für den Nachweis des privaten Schlüssels nicht erforderlich.
3. Öffentliches Zertifikat aus dem restaurierten Eintrag als DER `.cer`
   exportieren. SHA-256 dieser Datei und Thumbprint müssen exakt zur Referenz
   passen; Gültigkeitsintervall und vorhandener privater Schlüssel ebenfalls prüfen.
   Abweichung, fehlender Schlüssel oder nicht entschlüsselbare PFX: **STOPP**.
4. In der isolierten Umgebung eine eindeutig als lokale Wiederherstellungsprobe
   gekennzeichnete, nicht ausführbare Zufallsdatei mit dem restaurierten RSA-Schlüssel
   signieren (SHA-256, PKCS#1 v1.5). Mit dem **separat bekannten öffentlichen
   Referenzzertifikat** verifizieren; eine veränderte Dateikopie muss scheitern.
   Keine Release-Tags, Installer oder echten Update-Manifeste als Probe erzeugen
   und die Signatur nicht veröffentlichen. Nur Erfolg/Fehler und öffentliche
   Fingerabdrücke protokollieren, niemals Schlüsselmaterial oder Passwort.
5. Probeschlüssel und temporäre Dateien entfernen, isolierte Umgebung verwerfen,
   Datenträger wieder offline nehmen. Einfaches Dateilöschen garantiert auf SSDs
   keine sichere physische Löschung; von Anfang an verschlüsselte Medien verwenden.
   Für eine echte Rechnermigration nur den ausdrücklich benötigten Schlüssel im
   persönlichen Signierspeicher belassen. Alte Rechner vor Weitergabe sicher bereinigen.
6. Falls CI-Secrets wieder eingerichtet werden müssen: verschlüsselte PFX lokal
   ohne Konsolenausgabe nach Base64 umwandeln und über einen geschützten
   Secret-Eingabeweg übertragen; Passwort separat hinterlegen. Niemals Werte in
   Shell-History, Workflow-YAML, Logs oder Befehlsargumenten festhalten. GitHub-
   Maskierung ist kein ausreichender Schutz, insbesondere nicht für abgeleitete
   Base64-Werte. Erst nach Review einen expliziten signierten `main`-Testlauf ohne
   Release auslösen und dessen Prüfungen sowie Runner-Cleanup kontrollieren.

Die automatisierte Lifecycle-Probe nutzt stattdessen ein **separates kurzlebiges
RSA-Testzertifikat**: verschlüsselter PFX-Export, Wiederherstellung, Signieren und
Verifizieren, ohne Import in einen Vertrauensspeicher. Sie prüft den Mechanismus,
nicht das Vorhandensein, Passwort oder die Lesbarkeit des echten Offline-Backups.
Sie benötigt keine Produktions-Secrets und ersetzt die autorisierte manuelle
Wiederherstellungsprobe nicht.

## 3. Ablaufüberwachung und Freigabesperren

- Der gemeinsame Gültigkeits-Guard der Signierpfade warnt bei höchstens **90 Tagen**
  Restlaufzeit. Noch nicht gültige und abgelaufene Zertifikate verhindern die
  Signierung. Bei Warnung Rotation mit Verantwortlichem und Termin beginnen;
  die Warnung ist keine automatische Schlüsselerneuerung.
- Der wöchentliche Zertifikatsmonitor verwendet ausschließlich das öffentliche
  Repository-Zertifikat, keine Secrets und keinen Vertrauensimport. Im
  90-Tage-Fenster schlägt er bewusst fehl, damit Handlungsbedarf sichtbar bleibt.
  Das ist strenger als die Signierwarnung und kein Beleg für bereits abgelaufene
  Signaturen. Maintainer müssen Actions-Benachrichtigungen aktiv verfolgen und
  einen ausbleibenden/deaktivierten Zeitplan ebenfalls kontrollieren.
- GitHub-Zeitpläne sind kein garantierter Alarmdienst. Bei längerer Releasepause
  und vor jedem Release den letzten Monitorlauf und das UTC-Ablaufdatum prüfen;
  zusätzlich einen unabhängigen Kalendertermin vor dem 90-Tage-Fenster pflegen.
- Das Manifest hat **keinen vertrauenswürdigen Zeitstempel**. Bei jeder Paketprüfung
  muss das gepinnte Zertifikat aktuell gültig sein. Auch früher signierte Pakete
  werden nach Ablauf abgelehnt; Authenticode-Zeitstempel verlängern diese Frist
  nicht. Uhrzeit nicht zurückstellen, Gültigkeitsprüfung nicht abschalten.

## 4. Geplanter Zertifikatswechsel

Vor Ablauf abschließen und Zeit für Empfänger einplanen. Auch bei identischem
Anzeigenamen bedeutet ein neues Zertifikat einen neuen Fingerabdruck und Pin.

1. Neuen RSA-Codesigning-Schlüssel auf sauberem System erzeugen; öffentlichen
   DER-Export, Verwendungszweck, Gültigkeit und Schlüssellänge prüfen. Alten und
   neuen Fingerabdruck eindeutig unterscheiden. Offline-Backup und Restore-Probe
   des neuen Schlüssels abschließen, bevor der alte außer Betrieb geht.
2. In einem überprüften Änderungsstand zusammen aktualisieren:
   `assets/certificates/DriverPilot.cer`, `PackageTrust.CertificateHash`, öffentliche
   Fingerabdrücke/Ablaufdatum in `ZERTIFIKAT-ANLEITUNG.txt` und `UPDATE-SECURITY.md`
   sowie betroffene Tests/Dokumentation. Kein privates Material einchecken.
   CI-Secrets nur vom autorisierten Maintainer passend ersetzen. Bereits gebaute
   Pakete nicht nachträglich verändern; Manifest und Signatur authentifizieren
   auch Zertifikat, Verifier und ausgelieferte Anleitung.
3. **Standardweg: unabhängig geprüfter neuer signierter Installer.** Ein bereits
   installierter Updater mit altem Pin lehnt ein mit dem neuen Schlüssel signiertes
   Update auch dann ab, wenn Windows dem neuen Zertifikat bereits vertraut.
   Empfänger bestätigen den neuen Fingerabdruck direkt mit Christian, richten
   neues Windows-Vertrauen bewusst gemäß Anleitung ein und prüfen anschließend
   die Authenticode-Signatur der neuen Setup-EXE vor UAC. Ein heruntergeladenes
   `Setup.ps1` ist kein sicherer Bootstrap. Das gilt auch für alte Versionen bis
   0.3.2 mit ihrem nicht authentifizierten Updatepfad.
4. **Optionaler Brückenweg nur bei noch gültigem, sicherem alten Schlüssel:** Ein
   vom alten installierten Updater geprüftes und mit dem alten Schlüssel signiertes
   Übergangspaket kann den überprüften neuen Verifier samt neuem Pin/Zertifikat
   ausliefern. Erst danach dürfen Nachfolgepakete mit dem neuen Schlüssel kommen.
   Das ist ein gesondert zu entwickelnder und zu testender Releasepfad, **kein**
   vorhandener Rotationsschalter: Die normalen Buildskripte signieren und prüfen
   mit einem einzigen Repository-Zertifikat/Pin. Einfach `.cer`/Pin austauschen und
   `Build.ps1 -Sign` ausführen erstellt keine solche Brücke. Vor Einsatz beide
   Übergänge, Versionsreihenfolge und verpasste Brückenupdates isoliert prüfen.
   Wer die Brücke vor Ablauf nicht installiert hat, benötigt den Installerweg.
5. Windows-Vertrauen wird auch bei der Brücke nicht automatisch importiert.
   Freunde über neuen Fingerabdruck und Wechselgrund unabhängig informieren.
   Nach abgeschlossener Migration alten Root-Eintrag manuell anhand des **alten**
   Thumbprints entfernen, nicht pauschal alle Einträge namens Christian Strube.
   Alte signierte Programme können dadurch ihren Windows-Vertrauensstatus verlieren.
6. Freigabe erst nach negativen Tests (alter/fremder Pin, manipuliertes Paket,
   abgelaufenes Zertifikat), gültigem signierten Testbuild und überprüftem
   Installer-Bootstrap auf einer wegwerfbaren Testmaschine. Echte Freundes-
   Migration separat bestätigen; ein grüner Unit-Test beweist sie nicht.

## 5. Schlüsselverlust oder Kompromittierung

### Sofort eindämmen

1. **Release-Stopp:** keine Tags pushen, keine signierten Testläufe starten,
   Downloads/Weitergabe betroffener Ausgaben stoppen und Verantwortliche informieren.
2. In GitHub Actions **alle aktiven und wartenden signierten Build-/Release-Läufe
   abbrechen**, einschließlich manuell gestarteter Läufe und bereits laufender
   Veröffentlichungsjobs. Prüfen, dass sie wirklich beendet sind. Danach
   `Windows build and release` (`.github/workflows/release.yml`) deaktivieren und
   kontrollieren, dass keine neuen Läufe gestartet wurden. Workflow-Deaktivierung
   oder Secret-Löschung beendet bereits laufende Jobs nicht automatisch.
3. Unter **Settings → Secrets and variables → Actions**
   `DRIVERPILOT_SIGNING_PFX` und `DRIVERPILOT_SIGNING_PASSWORD` löschen. Auch
   gleichnamige Environment-/Organization-Secrets prüfen, sofern eingerichtet;
   Zugriff entziehen oder Werte ersetzen. Löschung über die Secret-Liste prüfen,
   niemals durch Ausgabe des Werts. Bei kompromittiertem Konto/Runner zusätzlich
   Sessions, Tokens, Schreibrechte und Workflow-Änderungen untersuchen.
4. Run-IDs, betroffene Versionen, öffentliche Fingerabdrücke und Zeitfenster sichern,
   aber kein Schlüsselmaterial in den Vorfallbericht kopieren. Betroffene Releases
   und Artefakte nach Beweissicherung zurückziehen; bereits heruntergeladene Kopien
   bleiben außerhalb der Kontrolle. Auf Runner-Cleanup nach Abbruch nicht blind
   vertrauen. Ein neuer PFX-Passwortwert macht einen gestohlenen Schlüssel nicht sicher.

### Einordnen und wiederherstellen

- **Nur Verlust, sicher kein fremder Zugriff:** intaktes Offline-Backup nach
  Abschnitt 2 wiederherstellen. Ohne Backup ist der private Schlüssel aus `.cer`
  oder GitHub Secrets nicht rekonstruierbar; neues Schlüsselpaar und unabhängiger
  Installer-Bootstrap sind erforderlich. Bei unklarer Ursache wie Kompromittierung
  behandeln. Auch ein restaurierter Schlüssel darf nach Ablauf nicht weiter signieren.
- **Kompromittierung oder begründeter Verdacht:** alten Schlüssel nicht mehr
  verwenden, auch nicht für ein Übergangspaket. Auf sauberem System neues
  Schlüsselpaar erzeugen, Ursache beseitigen und Abschnitt 4 per unabhängigen
  Installer durchführen. Neue Secrets erst nach abgesichertem Repo-/Kontozugriff
  einrichten. Vor erneuter Workflow-Aktivierung Änderungen prüfen; zunächst nur
  expliziten signierten `main`-Testbuild freigeben, nicht sofort einen Release-Tag.
- Freunde unabhängig kontaktieren: betroffene Downloads nicht starten, neues
  Zertifikat erst nach direktem Fingerabdruckabgleich vertrauen. In `certlm.msc`
  unter **Vertrauenswürdige Stammzertifizierungsstellen → Zertifikate** ausschließlich
  den bestätigten alten Thumbprint löschen. Falls manuell zusätzlich unter
  `certmgr.msc` (aktueller Benutzer) oder in TrustedPublisher installiert, auch
  dort nur diesen alten Eintrag entfernen. Niemals fremde Zertifikate nach Namen
  oder pauschal ganze Speicher löschen.
- **Kein zentraler Widerruf:** diese selbstsignierten Zertifikate besitzen hier
  keinen zentralen CRL-/OCSP-Widerruf; der Updater hat keine Notfall-Sperrliste.
  Secret-Löschung, Release-Rückzug und Root-Entfernung widerrufen seinen alten
  eingebauten Pin nicht. Ein alter Updater könnte weiterhin Angreiferpakete mit
  dem gestohlenen, noch gültigen Schlüssel akzeptieren. Bis zum unabhängig
  geprüften Austausch deshalb keine App-Updates über diesen Pfad durchführen.
  Bereits ausgeführte Schadsoftware erfordert eine eigene Systemprüfung;
  Zertifikatswechsel allein bereinigt sie nicht.

## Abschlussnachweis ohne Geheimnisse

Im Wartungs-/Vorfallticket festhalten: öffentliche alte/neue Fingerabdrücke,
Gültigkeitsdaten, Restore-Probe bestanden/fehlgeschlagen, Testlauf-IDs,
Bootstrap-Version, bestätigte Benachrichtigung der Empfänger und offene Migrationen.
CI-Schutz, Schlüssel-Backup und tatsächliche Vertrauensmigration getrennt abhaken.
Keine privaten Schlüssel, PFX-/Base64-Inhalte, Passwörter oder Secret-Screenshots
anhängen. Erst nach überprüftem Ergebnis den Release-Stopp aufheben.
