; Corvite installer (Inno Setup 6). Built by .github/workflows/release.yml:
;   ISCC /DMyAppVersion=0.1.0 installer\Corvite.iss   (expects the published single file in ..\publish\Corvite.exe)
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{B7A1E0C2-4D3F-4C86-9A0E-5C7D2F1E8A44}
AppName=Corvite
AppVersion={#MyAppVersion}
AppPublisher=Corvite contributors
AppPublisherURL=https://github.com/gtyroumn/corvite
AppSupportURL=https://github.com/gtyroumn/corvite/issues
DefaultDirName={autopf}\Corvite
DefaultGroupName=Corvite
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Corvite-Setup
SetupIconFile=..\src\Corvite.App\Assets\corvite.ico
UninstallDisplayIcon={app}\Corvite.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
en.RestoreAsk=Restore Windows settings to their original values (from the Corvite backup) before uninstalling?
en.RestoreFail=Some settings could not be restored. You can restore them in the app while it is still installed.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\Corvite.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Corvite"; Filename: "{app}\Corvite.exe"
Name: "{autodesktop}\Corvite"; Filename: "{app}\Corvite.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Corvite.exe"; Description: "{cm:LaunchProgram,Corvite}"; Flags: nowait postinstall skipifsilent

[Code]
var
  LangPage: TInputOptionWizardPage;

// The installer itself is English only; this page chooses the language of the Corvite interface.
procedure InitializeWizard;
var
  Sys: Integer;
begin
  LangPage := CreateInputOptionPage(wpSelectTasks,
    'Program language', 'Choose the language of the Corvite interface',
    'You can change it later in the app with the RU / UA / EN buttons at the top.', True, False);
  LangPage.Add('Русский');
  LangPage.Add('Українська');
  LangPage.Add('English');
  Sys := GetUILanguage and $3FF;
  if Sys = $19 then LangPage.SelectedValueIndex := 0
  else if Sys = $22 then LangPage.SelectedValueIndex := 1
  else LangPage.SelectedValueIndex := 2;
end;

// Writes the chosen language to %ProgramData%\Corvite\config.json (keeps other settings if the file already exists).
procedure SaveLanguage;
var
  Lang, Dir, Path: string;
  Raw: AnsiString;
  Json: string;
begin
  case LangPage.SelectedValueIndex of
    0: Lang := 'ru';
    1: Lang := 'uk';
  else
    Lang := 'en';
  end;
  Dir := ExpandConstant('{commonappdata}\Corvite');
  ForceDirectories(Dir);
  Path := Dir + '\config.json';
  if FileExists(Path) and LoadStringFromFile(Path, Raw) then
  begin
    Json := String(Raw);
    StringChangeEx(Json, '"Language": "ru"', '"Language": "' + Lang + '"', True);
    StringChangeEx(Json, '"Language": "uk"', '"Language": "' + Lang + '"', True);
    StringChangeEx(Json, '"Language": "en"', '"Language": "' + Lang + '"', True);
    StringChangeEx(Json, '"Language":"ru"', '"Language":"' + Lang + '"', True);
    StringChangeEx(Json, '"Language":"uk"', '"Language":"' + Lang + '"', True);
    StringChangeEx(Json, '"Language":"en"', '"Language":"' + Lang + '"', True);
    SaveStringToFile(Path, Json, False);
  end
  else
    SaveStringToFile(Path, '{"Language": "' + Lang + '"}', False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then SaveLanguage;
end;

// On uninstall offer to roll Windows back to the settings saved on first launch.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if FileExists(ExpandConstant('{app}\Corvite.exe')) and
       (MsgBox(CustomMessage('RestoreAsk'), mbConfirmation, MB_YESNO) = IDYES) then
    begin
      if (not Exec(ExpandConstant('{app}\Corvite.exe'), '--restore-baseline', '', SW_HIDE, ewWaitUntilTerminated, Code))
         or (Code <> 0) then
        MsgBox(CustomMessage('RestoreFail'), mbInformation, MB_OK);
    end;
  end;
end;
