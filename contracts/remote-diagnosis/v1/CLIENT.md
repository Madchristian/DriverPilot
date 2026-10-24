# Verhalten des Windows-Clients (DriverPilot)

Status: **umgesetzt** in `src/DriverPilot/RemoteDiagnosis` und durch `tests/RemoteDiagnosis` gegen die Schemas und Fixtures geprüft; noch nicht gegen einen laufenden Server getestet. Dieses Dokument sagt dem Server, was DriverPilot tatsächlich sendet und erwartet. Es ergänzt `README.md` und ändert keine Schemas. Weicht die Umsetzung später ab, wird zuerst dieses Dokument per Pull Request geändert.

## Verbindung

- Der Nutzer gibt beim Koppeln die **Serveradresse** und den **Einladungscode** ein. Die Adresse muss `https://<host>` sein, ohne Benutzerangabe, Query oder Fragment; ein Port ist erlaubt. Der Client hängt selbst `/api/v1` an. Es gibt keine fest eingebaute Adresse.
- Das Token ist an genau diese Herkunft gebunden und wird nie an eine andere gesendet.
- Jeder Request trägt `Accept: application/json` und `User-Agent: DriverPilot/<Version>`. Requests mit Body tragen `Content-Type: application/json; charset=utf-8` und nie `Content-Encoding`.
- Redirects (3xx) folgt der Client nicht; er behandelt sie als Fehler.
- Zeitlimit je Request: 30 Sekunden.
- TLS-Fehler werden nie übergangen; ein selbstsigniertes Zertifikat funktioniert nur, wenn Windows ihm bereits vertraut.

## Antworten

- Der Client liest jede Antwort **strikt**: Ein unbekanntes Feld, ein fehlendes Feld, ein falscher Typ oder ein unbekannter Enum-Wert macht die Antwort ungültig. Der Server darf Antworten deshalb nicht ohne neue Vertragsversion erweitern.
- Bei Fehlern wertet der Client `error.code` aus und zeigt einen eigenen deutschen Text je Code. `error.message` wird nie angezeigt; bei Codes ohne eigenen Text erscheint ein lokaler allgemeiner Hinweis.
- Eine Fehlerantwort ohne gültiges Fehlerobjekt (zum Beispiel eine HTML-Seite des Proxys) behandelt der Client anhand des Statuscodes: 401 wie `token_invalid`, 404 wie `not_found`, 429 und 5xx als vorübergehend, alles andere als endgültig. Der Server sollte trotzdem immer das Fehlerobjekt liefern, auch aus dem Proxy heraus.
- `Retry-After` wird als Sekundenzahl gelesen; die HTTP-Datumsform wird nicht unterstützt.

## Koppeln

- `POST /pairings/redeem` genau einmal je Nutzeraktion, ohne automatische Wiederholung.
- Der Client speichert `client_id`, `expires_at`, die Serveradresse und das Token; das Token mit Windows-DPAPI an das Benutzerkonto gebunden. Nach `expires_at` oder nach `token_invalid` verwirft er den Zugang und verlangt eine neue Einladung.

## Bericht

Der Client ruft `GET /capabilities` nach dem Koppeln und bei jedem Öffnen des Dialogs ab. Er sendet Berichte der Version `1.1` und nur, wenn `protocol_version` mit `1.` beginnt und `report_schema_versions` den Wert `1.1` enthält.

| Feld | Was der Client sendet |
| --- | --- |
| `scan_id` | Neue UUID je erfolgreichem Scan. |
| `scanned_at` | Abschluss des Scans laut PC-Uhr in UTC. |
| `observation_end` | Gleich `scanned_at`. |
| `observation_start` | `observation_end` minus genau 24 Stunden. |
| `previous_case_id` | ID des zuletzt gesendeten eigenen Falls, solange der Client ihn kennt und nicht gelöscht hat; sonst `null`. |
| `symptom.occurred_at` | In der ersten Version immer `null`. |
| `hardware.*` | Wert aus Windows; `null`, wenn leer, Platzhalter des Herstellers, zu lang oder mit Treffer bei einem Textmarker. |
| `hardware_details` | Ab Version 1.1. Jeder Bereich (`cpu`, `memory`, `gpus`, `storage`, `platform`) ist `null`, wenn seine Abfrage scheiterte; einzelne Werte sind `null`, wenn sie fehlen, außerhalb des Schemabereichs liegen oder einen Textmarker treffen. Keine Seriennummern. |
| `hardware_details.memory` | Aus `Win32_PhysicalMemory`/`Win32_PhysicalMemoryArray`: Steckplatz, Größe, SMBIOS-Typ, Nenn- und eingestellte Geschwindigkeit, Hersteller und Typnummer; höchstens 32 Module. |
| `hardware_details.gpus` | Nur physische PCI-Adapter aus `Win32_VideoController`, höchstens 4; Hersteller aus der PCI-Vendor-ID, VRAM aus `HardwareInformation.qwMemorySize` des Treibers. `telemetry` ist eine Momentaufnahme von `nvidia-smi` (nur NVIDIA, Abbruch nach 10 Sekunden); `[N/A]` wird `null`. |
| `hardware_details.storage` | Größe und freier Platz des Systemlaufwerks, Auslagerungsdatei und höchstens 16 Laufwerke aus `Get-PhysicalDisk` (Modell, Medium, Bus, Größe, Zustand laut Windows, Firmware). |
| `hardware_details.platform` | `%firmware_type%`, Secure Boot und HwSchMode aus der Registry, Laufzeit seit dem letzten Start. Der Energiesparplan erscheint nur als Windows-Standardplan oder `custom`; den frei vergebbaren Namen sendet der Client nie. |
| `devices` | Höchstens 64, in dieser Rangfolge: von Befunden referenzierte Geräte, Geräte mit Problemcode, dann Grafik, Netzwerk, Audio und Speichercontroller mit PCI- oder USB-Kennung. IDs `dev-01`, `dev-02`, … |
| `devices[].model` | Aus der Windows-Hardware-ID; `null`, wenn sie weder PCI noch USB ist. |
| `findings` | Höchstens 200 Ereignisgruppen (Quelle, Ereignis-ID, Gerät, Stunde), die schwersten und jüngsten zuerst. IDs `f-001`, `f-002`, … |
| `findings[].severity` | Aus der niedrigsten Windows-Ereignisstufe der Gruppe: 1 `critical`, 2 `error`, 3 `warning`, sonst `info`. |
| `findings[].first_seen` | Liegt ein Ereignis wegen der Scandauer wenige Sekunden vor `observation_start`, wird der Wert auf `observation_start` angehoben. |
| `collection.*` | `truncated`, wenn ein Limit (64 Geräte, 200 Gruppen, Abfragegrenze von Windows) gegriffen hat; `failed` bei Lesefehler; `unavailable`, wenn das WLAN-Protokoll fehlt oder deaktiviert ist. |
| `wow` | `null`, wenn der Nutzer keinen Spielkontext angibt. |
| `consent.accepted_at` | Zeitpunkt der lokalen Aktion „Vorschau prüfen / Zustimmung“ nach aktivierter Zustimmung. Vor dem separaten Senden-Klick vollständig im JSON sichtbar. |
| `consent.external_ai_allowed` | Nur `true`, wenn `/capabilities` die KI anbietet **und** der Nutzer sie ausdrücklich ankreuzt; die Option ist nicht vorausgewählt. |

- Der Body ist eingerücktes JSON; Umlaute stehen unverändert als UTF-8. Der Server darf keine bestimmte Formatierung oder Feldreihenfolge voraussetzen.
- Der Client prüft die Textmarker aus `rules.json` selbst und sendet keinen Bericht, der daran scheitern würde.
- **Entscheidung zur Transparenz (Schema unverändert):** Die lokale Zustimmungsaktion friert die endgültigen Bytes einschließlich aller vier `consent`-Felder und den Idempotency-Key ein, ohne Netzwerkaufruf. Erst der zweite, ausdrückliche Senden-Klick überträgt diese vorher sichtbaren Bytes. Der Entwurf davor enthält `consent: null` und ist nicht sendbar. Der Dateiexport ist exakt die gerade sichtbare Vorschau als UTF-8 ohne BOM, keine Neu-Serialisierung. Empfänger (vollständiger HTTPS-Endpunkt einschließlich Port), Kategorien/Anzahlen, Hinweisquelle/-version und Aufbewahrung stehen daneben. Bearbeitungen einschließlich KI-Wahl und Rücknahme der Zustimmung verwerfen die Freigabe; geänderte oder ungültige geladene Capabilities und ein Zugangwechsel ebenso. Während einer Übertragung sind Bearbeitungen gesperrt. Nach der lokalen Zustimmungsaktion sind Bytes und Idempotency-Key eingefroren. Bei Zeitüberschreitung, Verbindungsfehler, 429, 500 oder 503 wiederholt der Client erst nach erneuter Nutzeraktion, mit denselben Bytes und demselben Key. Nach einem App-Neustart oder einer Änderung entsteht ein neuer Bericht mit neuem Key.

## Fall

- Der Client führt **einen** aktuellen Fall. Ein neuer Upload ersetzt die lokale Referenz; der alte Fall bleibt bis zum Ablauf auf dem Server, wenn der Nutzer ihn nicht vorher löscht.
- Polling nur bei geöffnetem Dialog, gemäß `poll_after_seconds`, mindestens 10 Sekunden; nach Fehlern 20, 40, 60 Sekunden mit bis zu 20 % Jitter. Der Client sendet `If-None-Match`, sobald er einen ETag hat.
- Ein Ergebnis zeigt der Client nur, wenn `case_id`, `scan_id` und `report_sha256` zu seinem gesendeten Bericht passen und es das Ergebnisschema samt Längengrenzen erfüllt. Sonst zeigt er eine Fehlermeldung statt des Inhalts.
- Quellenlinks werden als Text angezeigt und nicht automatisch geöffnet.
- Rückmeldung: `result_revision` ist die angezeigte Revision; `note` ist `null` oder der eingegebene Text nach Prüfung der Textmarker. Auch hier lokale vollständige JSON-Vorschau (einschließlich `outcome` und `result_revision`) vor separatem Senden; Text-/Auswahländerungen verwerfen Vorschau und Retry. Ein Revisionswechsel wird vor dem Senden erneut geprüft.
- Löschen sendet `DELETE` und entfernt bei 204 die lokale Referenz.
- Liegt lokal ein neuerer Scan vor als der des Falls, markiert der Client das Ergebnis sichtbar als zu einem älteren Scan gehörig.

## Lokaler Speicher und Freigabegrenzen

- Capabilities werden ohne Bearer-Token von der gekoppelten HTTPS-Herkunft geladen. Der Hinweis ist eine Serverangabe, keine unabhängig geprüfte oder separat signierte Datenschutzerklärung. Ein restaurierter Zugang/Fall ersetzt keinen erfolgreich geladenen, validierten Hinweis. Rückmeldung prüfen und senden benötigen beides: gültigen aktuellen Hinweis und freigegebene Fallrevision. Fehlgeschlagene Hinweis-Neuladung sperrt beide Aktionen und verwirft die vorherige Freigabe.
- Unverändertes erneutes Prüfen einer noch unklaren Rückmeldung behält Bytes und Idempotency-Key. Geänderte Inhalte benötigen eine ausdrückliche neue Prüfung; sie senden niemals stillschweigend die alte Rückmeldung. Nach Bearbeitung/neuer Freigabe kann eine zuvor angenommene Rückmeldung zusätzlich bestehen bleiben.
- Ein 502/504-Proxyfehler (auch HTML statt JSON) beweist nicht, dass nichts gespeichert wurde. Bei vorübergehenden Schreibfehlern bleiben die geprüften Bytes/Keys für eine ausdrückliche Wiederholung erhalten; es gibt keine automatische neue Schreibanfrage.
- Neue lokale Zugangs-/Fallzustände werden erst nach erfolgreichem Speichern übernommen. Nach bestätigter Serverannahme mit lokalem Speicherfehler bleiben Berichtsvorschau, Bytes und Key im offenen Dialog erhalten; unveränderte Wiederholung kann denselben angenommenen Fall wiederherstellen. Speicherfehler machen eine Serveraktion nicht rückgängig. Diese Wiederherstellung ist **nur im offenen Dialog** verfügbar, nicht über Prozessabbruch/Neustart hinweg; der Hinweis fordert deshalb zum Offenlassen auf.
- Dasselbe gilt für Löschen und lokales Entfernen: bei Speicherfehler bleibt der zuletzt gespeicherte Zustand zur Wiederholung erhalten. `token_invalid` deaktiviert den Zugang in der laufenden Sitzung auch dann, wenn seine lokale Entfernung fehlschlägt. Fehlerdetails, Pfade, Token und Diagnosedaten werden nicht in Fehlermeldungen übernommen. Pairing kann serverseitig bereits eine Einladung verbraucht haben, obwohl lokales Speichern scheitert; keine automatische erneute Einlösung.

## Transport und Abbruch

Die JSON-Vorschau umfasst den gesamten Diagnose-Body, nicht geheime Authentifizierungswerte. HTTPS übermittelt notwendigerweise Verbindungsmetadaten einschließlich IP an Server bzw. beteiligte Proxys. Das Bearer-Token steht nur im Authorization-Header, nicht in Vorschau oder Export; angezeigt wird seine Verwendung, niemals sein Wert. Auch Idempotency-Key, User-Agent (App-Version) und übliche HTTP-/TLS-Metadaten werden erklärt. Filter sind keine Garantie für perfekte Redaktion. Verbinden/Einladung, Capabilities und Status-Polling sind klar vom Bericht-Upload getrennt. Abbruch oder Schließen storniert einen laufenden Bericht-Request bestmöglich, kann eine serverseitige Annahme aber nicht rückgängig machen. Es folgt kein automatischer neuer Upload. Nur eine bewusste Wiederholung verwendet die geprüften Bytes mit demselben Key. Nach einem unklaren Ausgang und anschließender Bearbeitung kann eine neue Freigabe einen zweiten Fall erzeugen; der alte läuft serverseitig ab.

## Nicht im Client

Kein Upload ohne Klick, kein Hintergrunddienst, keine Ausführung von Inhalten aus Antworten, keine Treiberaktionen auf Serveranweisung. Der lokale Dateiexport funktioniert ohne Server.
