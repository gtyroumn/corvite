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
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.RestoreAsk=Вернуть настройки Windows к исходным значениям (из резервной копии Corvite) перед удалением?
uk.RestoreAsk=Повернути налаштування Windows до вихідних значень (з резервної копії Corvite) перед видаленням?
en.RestoreAsk=Restore Windows settings to their original values (from the Corvite backup) before uninstalling?
ru.RestoreFail=Не удалось вернуть часть настроек. Их можно вернуть вручную в приложении, пока оно установлено.
uk.RestoreFail=Не вдалося повернути частину налаштувань. Їх можна повернути вручну в застосунку, поки він встановлений.
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
