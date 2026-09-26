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
CreateAppDir=no
Uninstallable=no
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
Source: "{#PayloadDir}\*"; DestDir: "{tmp}\DriverPilotPayload"; Flags: ignoreversion deleteafterinstall

[Run]
Filename: "{autopf}\DriverPilot\DriverPilot.exe"; Description: "DriverPilot öffnen"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  if CheckForMutexes('Local\DriverPilot.Desktop') then
    Result := 'Bitte DriverPilot zuerst über das Symbol im Infobereich beenden und anschließend erneut versuchen.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  ScriptPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    ScriptPath := ExpandConstant('{tmp}\DriverPilotPayload\Setup.ps1');
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
      '', SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('Die Windows-Einrichtung konnte nicht gestartet werden.');
    if Code <> 0 then
      RaiseException('Die Windows-Einrichtung wurde nicht abgeschlossen (Code ' + IntToStr(Code) + '). Bitte vorhandene DriverPilot-Prozesse beenden und das Setup erneut starten.');
  end;
end;
