; Script generated for Media Island Inno Setup Package
; Per-user Windows x64 installer for Media Island (.NET 8 WPF)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDir
  #error PublishDir must be supplied by tools/Build-Package.ps1
#endif

#ifndef OutputDir
  #define OutputDir "..\publish\dist"
#endif

#define AppName "Media Island"
#define AppPublisher "Media Island Contributors"
#define AppURL "https://github.com/truongsonphan187-cmd/Media-Island"
#define AppExeName "DynamicIslandMedia.exe"
#define AppId "{{8B51E168-5226-444F-A9F7-704983BF6C3E}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
; Single instance mutex for running process detection
AppMutex=DynamicIslandMedia_SingleInstance_Mutex
; Per-user install: {autopf} resolves to %LocalAppData%\Programs with PrivilegesRequired=lowest
DefaultDirName={autopf}\Media Island
DefaultGroupName={#AppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
OutputBaseFilename=MediaIsland-{#AppVersion}-win-x64-setup
OutputDir={#OutputDir}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
; Enforce Windows 10 Build 19041+ (required by net8.0-windows10.0.19041.0 target)
MinVersion=10.0.19041
; Restrict strictly to x64 Windows (AMD64 / Intel 64); ARM64 not tested/native
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
; No forced or automatic process termination; user must close app via tray
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
; Do not restore an old startup task after the user disabled startup inside the app.
UsePreviousTasks=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry"; Description: "Launch Media Island automatically on Windows startup"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\Assets\app_icon.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\Assets\app_icon.ico"; Tasks: desktopicon

[Registry]
; Set startup registry command if task selected (no unconditional uninsdeletevalue; scoped cleanup in Code)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DynamicIslandMedia"; ValueData: """{app}\{#AppExeName}"""; Tasks: startupentry

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Prevent installation or upgrade while application is running
function InitializeSetup(): Boolean;
begin
  Result := True;
  if CheckForMutexes('DynamicIslandMedia_SingleInstance_Mutex') then
  begin
    SuppressibleMsgBox('Media Island is currently running.' #13#10 #13#10 +
           'Please right-click the Media Island system tray icon, select "Exit", and run setup again.',
           mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

// Prevent uninstall while application is running
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if CheckForMutexes('DynamicIslandMedia_SingleInstance_Mutex') then
  begin
    SuppressibleMsgBox('Media Island is currently running.' #13#10 #13#10 +
           'Please right-click the Media Island system tray icon, select "Exit", and run uninstall again.',
           mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

// Scoped uninstall cleanup: delete startup entry ONLY if it points to this installation's executable
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  CurrentRunCmd: String;
  ExpectedRunCmd1: String;
  ExpectedRunCmd2: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DynamicIslandMedia', CurrentRunCmd) then
    begin
      ExpectedRunCmd1 := '"' + ExpandConstant('{app}\{#AppExeName}') + '"';
      ExpectedRunCmd2 := ExpandConstant('{app}\{#AppExeName}');
      // Only delete if the command matches this exact installation path
      if (CompareText(CurrentRunCmd, ExpectedRunCmd1) = 0) or (CompareText(CurrentRunCmd, ExpectedRunCmd2) = 0) then
      begin
        RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DynamicIslandMedia');
      end;
    end;
  end;
end;
