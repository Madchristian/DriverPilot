; ExecAndLogOutput was introduced in Inno Setup 6.3.0. CI must provision
; 6.3.0 or newer; do not silently fall back to Exec and lose backend errors.
#if Ver < 0x06030000
  #error Inno Setup 6.3.0 or newer is required for backend stdout/stderr logging
#endif

#ifndef AppVersion
  #error AppVersion must be provided by Build-Release.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be provided by Build-Release.ps1
#endif

[Setup]
AppId=DriverPilot.Package
AppName=DriverPilot
AppVersion={#AppVersion}
AppPublisher=DriverPilot
WizardStyle=modern
SetupIconFile=../assets/branding/DriverPilot.ico
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultDirName={autopf}\DriverPilot.Uninstall
CreateAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
Uninstallable=yes
UninstallFilesDir={autopf}\DriverPilot.Uninstall
UninstallDisplayIcon={autopf}\DriverPilot\DriverPilot.exe
UninstallDisplayName=DriverPilot
OutputDir=../artifacts/dist
OutputBaseFilename=DriverPilot-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
RestartIfNeededByRun=no
CloseApplications=no
DisableWelcomePage=no
SetupLogging=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{tmp}\DriverPilotPayload"; Flags: dontcopy
; Keep the uninstall engine outside the strictly authenticated, directory-swapped app package.
Source: "{#PayloadDir}\Uninstall.ps1"; DestDir: "{autopf}\DriverPilot.Uninstall"; Flags: ignoreversion

[Run]
Filename: "{autopf}\DriverPilot\DriverPilot.exe"; Description: "DriverPilot öffnen"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
var
  RemoveSettings, RemoveLogs, RemoveBackups: Boolean;

function HasUninstallOption(const Option: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Option) = 0 then Result := True;
end;

function InitializeUninstall(): Boolean;
var
  Settings, Logs, Backups: Boolean;
begin
  Result := False;
  Settings := False;
  Logs := False;
  Backups := False;
  if UninstallSilent then
  begin
    Settings := HasUninstallOption('/REMOVESETTINGS');
    Logs := HasUninstallOption('/REMOVELOGS');
    Backups := HasUninstallOption('/REMOVEBACKUPS');
  end
  else
  begin
    if MsgBox('DriverPilot entfernen? Laufende GUI bitte zuerst im Infobereich beenden. Eine laufende Wartung wird nicht abgebrochen; in diesem Fall wird die Deinstallation zurueckgestellt.' + #13#10 + 'Zertifikatsvertrauen und installierte Treiber bleiben unveraendert.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then Exit;
    Settings := MsgBox('Einstellungen einschliesslich Wartungsregeln, eigener Codex-Anmeldung und lokalem Ferndiagnose-Zugang dauerhaft loeschen? Serverfaelle bleiben unveraendert. Standard: Nein (behalten).', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    Logs := MsgBox('Diagnoseprotokolle, Chat, gespeicherte Scans und Wartungsnachweise dauerhaft loeschen? Standard: Nein (behalten).', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    Backups := MsgBox('Treibersicherungen unter ProgramData\DriverPilot\Backups dauerhaft loeschen? Dies entfernt den manuellen Rueckweg zu gesicherten Treibern. Standard: Nein (behalten).', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
  RemoveSettings := Settings;
  RemoveLogs := Logs;
  RemoveBackups := Backups;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Arguments: String;
  Code: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Log('DriverPilot removed. Unselected data, owner receipt, unknown files and unverifiable incomplete staging may remain; certificate trust is unchanged.');
    if not UninstallSilent then
      MsgBox('DriverPilot wurde entfernt. Nicht ausgewaehlte Daten, die Besitzerzuordnung und unbekannte Dateien bleiben erhalten. Unvollstaendige, nicht pruefbare Update-Zwischenordner werden aus Sicherheitsgruenden ebenfalls aufbewahrt. Zertifikatsvertrauen und installierte Treiber bleiben unveraendert.', mbInformation, MB_OK);
    Exit;
  end;
  if CurUninstallStep <> usUninstall then Exit;
  { Run only AFTER Inno's own final confirmation. Exceptions at usUninstall are fatal,
    before Inno deletes any file or the Apps entry. }
  Arguments := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{autopf}\DriverPilot.Uninstall\Uninstall.ps1') + '"';
  if RemoveSettings then Arguments := Arguments + ' -RemoveSettings';
  if RemoveLogs then Arguments := Arguments + ' -RemoveLogs';
  if RemoveBackups then Arguments := Arguments + ' -RemoveBackups';
  if not ExecAndLogOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments,
    ExpandConstant('{sys}'), SW_HIDE, ewWaitUntilTerminated, Code, nil) then
    RaiseException('Die Deinstallation konnte nicht gestartet werden. Der Deinstallationseintrag bleibt erhalten.');
  if Code <> 0 then
  begin
    Log('DriverPilot uninstall backend failed with code ' + IntToStr(Code));
    RaiseException('Deinstallation nicht abgeschlossen (Code ' + IntToStr(Code) + '). GUI im Infobereich beenden, laufende Wartung abwarten und als urspruenglicher Installationsbenutzer erneut versuchen. Bei weiterem Fehler die Anleitung zur Deinstallation verwenden. Der Deinstallationseintrag bleibt erhalten.');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  if FileExists(ExpandConstant('{commonappdata}\DriverPilot\UninstallPending')) or
    DirExists(ExpandConstant('{commonappdata}\DriverPilot\UninstallPending')) then
  begin
    Result := 'Eine DriverPilot-Deinstallation ist noch nicht abgeschlossen. Zuerst die Deinstallation erneut ausfuehren; der vorhandene Wiederherstellungshelfer bleibt unveraendert.';
    Exit;
  end;
  if CheckForMutexes('Local\DriverPilot.Desktop') then
    Result := 'Bitte DriverPilot zuerst über das Symbol im Infobereich beenden und anschließend erneut versuchen.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  ScriptPath: String;
begin
  { ssInstall exceptions are fatal BEFORE Inno copies its helper/registration.
    ssPostInstall exceptions are only reported and cannot signal setup failure. }
  if CurStep = ssInstall then
  begin
    ExtractTemporaryFiles('{tmp}\DriverPilotPayload\*');
    ScriptPath := ExpandConstant('{tmp}\DriverPilotPayload\Setup.ps1');
    if not ExecAndLogOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
      ExpandConstant('{sys}'), SW_HIDE, ewWaitUntilTerminated, Code, nil) then
      RaiseException('Die Windows-Einrichtung konnte nicht gestartet werden.');
    if Code <> 0 then
      RaiseException('Die Windows-Einrichtung wurde nicht abgeschlossen (Code ' + IntToStr(Code) + '). Bitte vorhandene DriverPilot-Prozesse beenden und das Setup erneut starten.');
  end;
end;
