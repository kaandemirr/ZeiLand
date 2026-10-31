; ZEIGARN setup: ZeiLand plus, optionally, the PawnIO driver used for the CPU temperature.
; Built by build.ps1, which publishes the app to out\app and fetches PawnIO's official setup to vendor\.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6E0B3C1D-8B7A-4F2E-9C35-2D4A7E1F9B60}
AppName=ZeiLand
AppVersion={#AppVersion}
AppVerName=ZeiLand {#AppVersion}
AppPublisher=ZEIGARN
DefaultDirName={autopf}\ZEIGARN\ZeiLand
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=out
OutputBaseFilename=ZEIGARN-Setup-{#AppVersion}
SetupIconFile=..\ZeiLand\Assets\island.ico
UninstallDisplayIcon={app}\ZeiLand.exe
UninstallDisplayName=ZeiLand
VersionInfoCompany=ZEIGARN
VersionInfoProductName=ZeiLand
VersionInfoVersion={#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.17763
WizardStyle=modern
ShowLanguageDialog=no

; Turkish on a Turkish Windows, English everywhere else (the first entry is the fallback). Each comes with its
; terms to accept (summary, MIT license, privacy note); a silent update doesn't show them again.
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "license-en.txt"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"; LicenseFile: "license-tr.txt"

[CustomMessages]
turkish.TaskPawnIO=CPU sıcaklığı için PawnIO sürücüsünü kur (açık kaynak, imzalı)
english.TaskPawnIO=Install the PawnIO driver for the CPU temperature (open source, signed)
turkish.TaskAutostart=Windows ile başlat (oturum açılınca yönetici olarak, UAC sormadan)
english.TaskAutostart=Start with Windows (as administrator at sign-in, without a UAC prompt)
turkish.InstallingPawnIO=PawnIO sürücüsü kuruluyor...
english.InstallingPawnIO=Installing the PawnIO driver...
turkish.PawnIOFailed=PawnIO kurulamadı (kod %1). ZeiLand CPU sıcaklığı olmadan çalışır; sürücüyü daha sonra Ayarlar'dan kurabilirsin.
english.PawnIOFailed=PawnIO could not be installed (code %1). ZeiLand works without the CPU temperature; the driver can be installed later from Settings.
turkish.LaunchApp=ZeiLand'i şimdi başlat
english.LaunchApp=Launch ZeiLand now

[Tasks]
Name: "pawnio"; Description: "{cm:TaskPawnIO}"; Check: not IsPawnIOInstalled
Name: "autostart"; Description: "{cm:TaskAutostart}"

[Files]
Source: "out\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; Unmodified official PawnIO setup (GPL-2.0); run from the temp folder, never installed into {app}.
Source: "vendor\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: pawnio

[Icons]
Name: "{autoprograms}\ZeiLand"; Filename: "{app}\ZeiLand.exe"

[Run]
; The app writes the logon task itself (same definition as its own "start with Windows" switch).
Filename: "{app}\ZeiLand.exe"; Parameters: "--enable-autostart"; Flags: runhidden waituntilterminated; Tasks: autostart
; Unticked: remove a task left by an earlier install, so the choice here is what happens at sign-in.
Filename: "{app}\ZeiLand.exe"; Parameters: "--disable-autostart"; Flags: runhidden waituntilterminated; Tasks: not autostart
; With the logon task, starting it runs the island elevated straight away; otherwise the app asks itself.
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""ZeiLand"""; Description: "{cm:LaunchApp}"; Flags: postinstall runhidden nowait; Tasks: autostart
Filename: "{app}\ZeiLand.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait runasoriginaluser; Tasks: not autostart

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM ZeiLand.exe /F"; Flags: runhidden waituntilterminated; RunOnceId: "StopApp"
Filename: "{app}\ZeiLand.exe"; Parameters: "--disable-autostart"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveLogonTask"

[Code]
var
  RestartNeeded: Boolean;

function IsPawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

{ A running island (possibly elevated) locks its files; stop it before copying the new ones. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM ZeiLand.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('pawnio') then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('InstallingPawnIO');
    { 3010: installed, Windows wants a restart before the driver can load. }
    if not Exec(ExpandConstant('{tmp}\PawnIO_setup.exe'), '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, Code)
       or ((Code <> 0) and (Code <> 3010)) then
      MsgBox(FmtMessage(CustomMessage('PawnIOFailed'), [IntToStr(Code)]), mbInformation, MB_OK)
    else if Code = 3010 then
      RestartNeeded := True;
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RestartNeeded;
end;
