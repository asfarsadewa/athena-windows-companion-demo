#ifndef MyAppVersion
#define MyAppVersion "0.1.0"
#endif

#ifndef SourceDir
#error SourceDir must be defined by the build script.
#endif

#ifndef OutputDir
#error OutputDir must be defined by the build script.
#endif

#ifndef IconFile
#error IconFile must be defined by the build script.
#endif

#define MyAppName "Athena Companion"
#define MyAppPublisher "asfarsadewa"
#define MyAppExeName "AthenaCompanion.exe"

[Setup]
AppId={{8C314624-02DA-4F26-A499-1F9727B14C72}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Athena Companion
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=AthenaCompanionSetup-{#MyAppVersion}
SetupIconFile={#IconFile}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
