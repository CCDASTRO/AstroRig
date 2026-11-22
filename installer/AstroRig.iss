; From the repository root:
; dotnet publish AstroRig.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
; ISCC.exe installer/AstroRig.iss
#ifndef PublishDir
  #define PublishDir AddBackslash(SourcePath) + "..\artifacts\publish"
#endif
#define AppName "AstroRig"
#define AppExe "AstroRig.exe"
#define AppVersion GetVersionNumbersString(AddBackslash(PublishDir) + AppExe)
#define ShortVersion Copy(AppVersion, 1, RPos(".", AppVersion) - 1)

[Setup]
AppId={{436C5408-F663-4BAB-9B44-D3B3B9796B9E}
AppName={#AppName}
AppVersion={#ShortVersion}
AppPublisher=CCDASTRO
AppPublisherURL=https://github.com/CCDASTRO/AstroRig
AppSupportURL=https://github.com/CCDASTRO/AstroRig/issues
DefaultDirName={localappdata}\Programs\AstroRig
DefaultGroupName=AstroRig
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\AstroRig.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=.
OutputBaseFilename=AstroRig-{#ShortVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AstroRig"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\AstroRig"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch AstroRig"; Flags: nowait postinstall skipifsilent