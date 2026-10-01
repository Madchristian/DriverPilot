# Ferndiagnose-API v1: Vertrag zwischen DriverPilot und Server

Status: **Entwurf zur gemeinsamen Bestätigung** durch Client- und Serverseite ([Issue #19](https://github.com/Madchristian/DriverPilot/issues/19), Abschnitt 5). Erst nach der Bestätigung ist er für beide Implementierungen verbindlich.

Der Vertrag beschreibt ausschließlich die Client-API unter `/api/v1`. Die Adminansicht, die Datenbank, der Modelladapter und der Betrieb sind Sache des Servers und hier nicht festgelegt. Alle Fixtures sind synthetisch; sie enthalten keine echten Zugangsdaten oder Diagnosedaten.

## Dateien

| Datei | Inhalt |
| --- | --- |
| `openapi.yaml` | Endpunkte, Header, Statuscodes und je Antwort die möglichen Fehlercodes (`x-error-codes`). |
| `schemas/*.schema.json` | JSON Schema 2020-12 je Nachricht. Muster sind ECMA-262-Regex. |
| `errors.json` | Fehlerkatalog: Code, HTTP-Status, Bedeutung, Wiederholbarkeit. |
| `states.json` | Fallzustände, erlaubte Übergänge und Invarianten. |
| `rules.json` | Limits, Zeit- und Verweisregeln sowie die Textmarker, die das Schema nicht ausdrücken kann. |
| `fixtures/valid/<schema>/` | Positivbeispiele je Schema. |
| `fixtures/invalid/<schema>/` | Negativbeispiele; erwarteter Fehler in `fixtures/scenario.json`. |
| `fixtures/scenario.json` | Gemeinsame Serverzeit und Hinweisversion der Fixtures, Erwartungen der Negativfälle. |
| `CLIENT.md` | Was der Windows-Client konkret sendet und erwartet; Orientierung für die Serverumsetzung. |
| `../validate_contract.py` | Prüft alles Obige gegeneinander; läuft in CI. |

## Transport

- Nur HTTPS, JSON in UTF-8 ohne BOM, `Content-Type: application/json` (optional `; charset=utf-8`). Keine Binärdaten oder Anhänge.
- Requests ohne `Content-Encoding`. Das Uploadlimit zählt die tatsächlich empfangenen Bytes, auch bei chunked Übertragung oder fehlender `Content-Length`.
- Zeitpunkte sind RFC 3339 in UTC mit `Z`. IDs sind UUIDs in Kleinschreibung.
- Der Body ist ein JSON-Objekt ohne doppelte Schlüssel. Unbekannte Felder werden abgelehnt, nie stillschweigend verworfen oder abgeschnitten.
- **Jedes Feld ist Pflicht.** Ein unbekannter oder nicht angegebener Wert ist ausdrücklich `null`; es gibt keine erfundenen Ersatzwerte.
- Längenangaben der Schemas zählen Unicode-Codepunkte.
- Das Token steht nur im Header `Authorization: Bearer …`, die Einladung nur im Body. Diagnoseinhalte, Tokens und Einladungen stehen nie in URLs.
- Der Client folgt keinen Redirects und sendet das Token an keinen anderen Host.
- Antworten mit Fall- oder Zugangsdaten tragen `Cache-Control: no-store`.
- `GET /capabilities` und `POST /pairings/redeem` sind ohne Token erreichbar; alles andere verlangt ein gültiges Token.

## Zugang

- `POST /pairings/redeem` verbraucht die Einmaleinladung atomar. Unbekannt, abgelaufen und bereits eingelöst sind nicht unterscheidbar (`invitation_invalid`).
- Fehlendes, unbekanntes, abgelaufenes und widerrufenes Token sind nicht unterscheidbar (`token_invalid`). Der Client beendet dann das Polling; es gibt kein Refresh-Token, sondern eine neue Einladung.
- Jeder Zugriff auf einen Fall ist an den authentifizierten Client gebunden. Unbekannte, fremde, gelöschte und abgelaufene Fälle liefern dieselbe 404-Antwort ohne ETag.

## Annahme eines Berichts (`POST /cases`)

Der Server prüft in dieser Reihenfolge und antwortet mit dem ersten zutreffenden Fehler:

1. Token (`token_invalid`), danach Anfragelimit (`rate_limited`).
2. `Content-Type` und `Content-Encoding` (415), Bodygröße (413).
3. Header `Idempotency-Key` (`idempotency_key_required`).
4. Idempotenz über die Body-Bytes, siehe unten (Wiederholung, `idempotency_key_conflict` oder `request_retired`).
5. JSON-Syntax (`invalid_json`).
6. `schema_version` (`unsupported_schema_version`).
7. Zustimmung: `consent` fehlt oder `upload_approved` ist nicht `true` (`consent_required`).
8. Übrige Schemaprüfung (`schema_violation`).
9. Version des Datenschutzhinweises (`privacy_notice_outdated`).
10. Zeitregeln aus `rules.json`: `clock_skew`, dann `scan_too_old`, dann `time_range_invalid`.
11. Verweisregeln (`reference_invalid`), `previous_case_id` (`previous_case_not_found`).
12. Textmarker (`text_rejected`).
13. Tageslimit des Clients (`daily_case_limit_reached`), globale Kapazität (`capacity_exhausted`).
14. Dauerhafte Transaktion für Fall und Arbeitsauftrag; scheitert sie, `service_unavailable`. Erst danach 202.

Der Server wartet im Upload nie auf ein Modell. Fehlermeldungen sind feste Texte je Code und geben keine Eingaben wieder.

### Idempotenz und `report_sha256`

- `report_sha256` ist der SHA-256 in Kleinschreibung über die **exakt empfangenen Body-Bytes**, nicht über neu serialisiertes JSON. Er bindet Ergebnis und Bericht aneinander; er authentifiziert und anonymisiert nichts.
- Byte-Fixture: `fixtures/valid/case-create-request/wow-crash.json` mit dem erwarteten Hash in `wow-crash.sha256`. Die Datei enthält Umlaute, Zeilenumbrüche im Freitext und kein abschließendes Zeilenende. `.gitattributes` nimmt die Fixtures von jeder Zeilenenden-Umwandlung aus.
- Ein Idempotency-Key gilt je Clientzugang und Endpunkt. Er wird erst mit einer Annahme (202 bzw. 201) gebunden; abgelehnte Requests binden ihn nicht.
- Gleicher Key und dieselben Bytes: dieselbe Antwort mit demselben Fall in seinem aktuellen Zustand, ohne neue Analyse. Gleicher Key und andere Bytes: `idempotency_key_conflict`. Parallele Requests sichert ein Datenbank-Constraint ab.
- Nach dem Löschen eines Falls bleibt ein inhaltsfreier Tombstone bis zum Ablauf des Clientzugangs; sein Key liefert `request_retired` und erzeugt keinen Fall neu.
- Der Client erzeugt die endgültigen Bytes im Moment der Zustimmung (`consent.accepted_at`) und verwendet danach für jede Wiederholung genau diese Bytes und denselben Key. Die Vorschau zeigt alle Felder außer `consent`. Jede Änderung an Freitext oder Scan verlangt eine neue Zustimmung, neue Bytes und einen neuen Key.

### Inhalt des Berichts

- Der Bericht ist eine eigene Projektion, nie ein serialisierter interner `Snapshot`. Keine rohen Ereignisnachrichten, Pfade, Instanz-IDs, Seriennummern oder Dumps.
- `collection` nennt je Bereich den Erfassungsstatus. Leere `devices` oder `findings` bedeuten nur bei `complete` „nichts gefunden“.
- Die Uhrzeitbasis ist die UTC-Uhr des Servers bei der Annahme. Der Client kann eine Abweichung am HTTP-Header `Date` erkennen und meldet `clock_skew` verständlich als falsch gestellte PC-Uhr.
- Textmarker (`rules.json`, `forbidden_text`) wenden Client und Server identisch an. Der Client setzt Hardwarefelder mit Treffer vor der Vorschau auf `null` und bittet bei Freitext um Umformulierung. Der Server lehnt bei einem Treffer den ganzen Upload ab. Die Marker sind konservative Heuristiken und keine Zusage von Anonymität; die Vorschau bleibt Pflicht.
- `external_ai_allowed=false` bedeutet ausschließlich manuelle Bearbeitung ohne Providerrequest. `true` erlaubt die KI nur, sie erzwingt sie nicht: Ohne aktivierten Anbieter wird der Fall manuell bearbeitet.
- `previous_case_id` verweist optional auf einen eigenen früheren Fall.

## Fallzustand und Polling (`GET /cases/{case_id}`)

- Zustände und Übergänge stehen in `states.json`. Der Client sieht nie einen ungeprüften Entwurf; `result` ist nur im Zustand `released` gesetzt.
- `poll_after_seconds` ist die früheste nächste Abfrage. `null` (bei `released` und `analysis_failed`) beendet das automatische Polling; manuelles Aktualisieren bleibt möglich, weil eine neue Revision freigegeben oder ein fehlgeschlagener Fall manuell übernommen werden kann.
- Der Client pollt nur bei geöffnetem Bereich, beginnt mit 10 Sekunden und weicht bei Fehlern exponentiell bis 60 Sekunden mit Jitter aus. `Retry-After` hat Vorrang. Bei 401 und 404 endet das Polling.
- `ETag` ist stark und opak. `If-None-Match` mit dem aktuellen Wert liefert 304 ohne Body.

## Ergebnis und Anzeige

- Ein Ergebnis ist an `case_id`, `scan_id` und `report_sha256` gebunden. Der Client zeigt es nur, wenn alle drei zu seinem gesendeten Bericht passen, und zeigt Scanzeit und Revision an.
- Alle Texte sind Plaintext. Der Client rendert kein HTML und kein Markdown; das Fixture `valid/result/ai-assisted.json` enthält dafür absichtlich `<script>` und Markdown-Syntax als wirkungslosen Text.
- Es gibt keine ausführbaren Felder. Der Client führt nichts aus einer Antwort aus und verwirft ein Ergebnis, das das Schema verletzt (`fixtures/invalid/result/`).
- Fakten belegen sich über IDs oder Feldzeiger des Berichts. Hypothesen tragen `proven: false`; Wahrscheinlichkeitswerte gibt es nicht.
- `sources` sind nur HTTPS-Links ohne Benutzerangabe und Port. Welche Hosts zulässig sind, prüft der Server vor der Freigabe; der Client öffnet einen Link erst nach bewusster Nutzeraktion.
- Eine freigegebene Revision ist unveränderlich. Rückmeldungen nennen die Revision, auf die sie sich beziehen.

## Löschen und Ablauf

- `DELETE` löscht Bericht, Entwürfe, Ergebnisse und Rückmeldungen und entwertet laufende Arbeit sofort. Die Antwort ist 204, auch für eine nicht sichtbare UUID; danach liefert `GET` 404.
- `expires_at` ist `received_at` plus Aufbewahrungsfrist und wird nie verlängert. Ab diesem Zeitpunkt ist der Fall gesperrt.

## Transportfälle ohne Datei-Fixture

Diese Fälle gehören in die Abnahmetests beider Seiten, lassen sich aber nicht als statische Datei ablegen:

| Fall | Erwartung |
| --- | --- |
| Body größer als 262144 Bytes, mit und ohne `Content-Length`, auch chunked | 413 `payload_too_large` |
| `Content-Encoding: gzip` | 415 `unsupported_content_encoding` |
| `Content-Type: text/plain` | 415 `unsupported_media_type` |
| Zwei parallele `POST /cases` mit gleichem Key und gleichen Bytes | genau ein Fall, genau ein Arbeitsauftrag |
| Wiederholung nach `DELETE` mit altem Key | 409 `request_retired` |
| Client B fragt Fall von Client A ab | dieselbe 404-Antwort wie für eine unbekannte ID |
| Redirect-Antwort des Servers oder Proxys | Client bricht ab und sendet das Token nicht weiter |

## Betriebsrahmen

Zielhost des Servers ist ein Raspberry Pi 4 mit 4 GB RAM. Der Vertrag ist darauf ausgelegt: kleine Bodies, feste Obergrenzen für Arrays und Texte, Textregeln ohne Lookaround (RE2-tauglich) und keine Operation, die im Request auf ein Modell wartet.

## Versionierung

- `protocol_version` und `schema_version` haben die Form `Major.Minor`. Der Server nimmt nur die in `/capabilities` genannten Berichtsversionen an.
- Jede Änderung an Schemas, Fehlercodes, Zuständen oder Regeln ist eine Vertragsänderung: zuerst hier per Pull Request abstimmen, dann implementieren. Keine stillen Anpassungen in Client oder Server.
- Neue Felder oder Enum-Werte brauchen eine neue Minor-Version, weil unbekannte Felder abgelehnt werden. Inkompatible Änderungen bekommen ein neues Verzeichnis `v2/`.

## Festlegungen, die Issue #19 offen lässt

Diese Punkte sind im Vertrag entschieden und brauchen die ausdrückliche Bestätigung beider Seiten:

1. `/capabilities` liefert den vollständigen Text des Datenschutzhinweises, damit der Client immer genau die Version anzeigt, der zugestimmt wird.
2. `hardware` enthält zusätzlich Hersteller, Board-Revision und `is_portable`; das Issue nennt nur Modell, BIOS, CPU, RAM und Windows.
3. `collection` ist ein zusätzliches Pflichtfeld für Erfassungslücken.
4. Gerätemodelle sind strukturiert (`bus`, `vendor_id`, `product_id`, `subsystem_id`) statt als Zeichenkette mit Backslash.
5. Ereignisquellen sind auf die zehn Quellen beschränkt, die DriverPilot heute liest. Anwendungsabstürze (zum Beispiel „Application Error“) wären eine Erweiterung in Version 1.1.
6. Eine nicht einlösbare Einladung liefert 401 `invitation_invalid`.
7. Ein überschrittenes Client-Tageslimit liefert 429, erschöpfte globale Kapazität 503.
8. Das Ergebnis hat zusätzlich `open_questions` und `sources`.
9. Höchstens zehn Rückmeldungen je Fall.
10. `analysis_failed` beendet das automatische Polling, ist serverseitig aber nicht endgültig.

## Prüfung

```
python3 -m venv .venv
.venv/bin/pip install -r contracts/remote-diagnosis/requirements.txt
.venv/bin/python contracts/remote-diagnosis/validate_contract.py
```

Der Validator enthält eine Referenzumsetzung der Annahmeregeln 6 bis 12 (ohne `previous_case_id`), damit jedes Negativ-Fixture nachweislich aus dem erwarteten Grund scheitert. Er ersetzt keine Tests gegen einen laufenden Server.
