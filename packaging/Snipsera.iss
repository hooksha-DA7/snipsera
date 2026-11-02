#ifndef AppDir
  #error AppDir is required
#endif
[Setup]
AppId={{AE5C6F25-7A71-4A81-AFC2-7442D18AE306}
AppName=Snipsera
AppVersion={#ReleaseVersion}
VersionInfoVersion={# GetVersionNumbersString(AddBackslash(AppDir) + "Snipsera.exe")}
AppPublisher=hooksha-DA7
AppPublisherURL=https://github.com/hooksha-DA7/snipsera
AppSupportURL=https://github.com/hooksha-DA7/snipsera/issues
DefaultDirName={localappdata}\Programs\Snipsera
DefaultGroupName=Snipsera
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile={#AppDir}\LICENSE
SetupIconFile=..\src\Framelet\Assets\Snipsera.ico
UninstallDisplayIcon={app}\Snipsera.exe
OutputDir={#OutputDir}
OutputBaseFilename=Snipsera-{#ReleaseVersion}-setup-win-x64
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Snipsera

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
[Files]
Source: "{#AppDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Snipsera"; Filename: "{app}\Snipsera.exe"
Name: "{autodesktop}\Snipsera"; Filename: "{app}\Snipsera.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Snipsera.exe"; Description: "Open Snipsera"; Flags: nowait postinstall skipifsilent unchecked
[Code]
function InitializeSetup(): Boolean;
var
  Installed: Cardinal;
  ErrorCode: Integer;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and (Installed = 1);
  if not Result then
    if MsgBox('Screen recording needs the Microsoft Visual C++ x64 runtime. Install it, then run this installer again. Open the official download page?', mbInformation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://aka.ms/vs/17/release/vc_redist.x64.exe', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;
