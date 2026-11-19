; DupliXon installer (Inno Setup 6). Build it with scripts\build-installer.ps1.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef FfmpegDir
  #error FfmpegDir must point to the folder that holds ffmpeg.exe
#endif

[Setup]
AppId={{6C2B7F0E-3D5A-4C1B-9E8F-D0B1A7C4E2F9}
AppName=DupliXon
AppVersion={#AppVersion}
AppVerName=DupliXon {#AppVersion}
AppPublisher=Nixon Ochoa
AppPublisherURL=https://github.com/NixonOchoa
AppSupportURL=https://github.com/NixonOchoa/DupliXon
AppCopyright=© 2026 Nixon Ochoa
DefaultDirName={autopf}\DupliXon
DefaultGroupName=DupliXon
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=DupliXon-Setup-{#AppVersion}
SetupIconFile=..\src\DupliXon.App\Assets\DupliXon.ico
UninstallDisplayIcon={app}\DupliXon.exe
UninstallDisplayName=DupliXon
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "virtualdisplay"; Description: "Instalar la pantalla virtual para usar el iPad como segunda pantalla (Parsec VDD, requiere Internet)"; GroupDescription: "Opcional:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\DupliXon.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#FfmpegDir}\ffmpeg.exe"; DestDir: "{app}\ffmpeg"; Flags: ignoreversion
Source: "{#FfmpegDir}\..\LICENSE"; DestDir: "{app}\ffmpeg"; DestName: "LICENSE-FFmpeg.txt"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DupliXon"; Filename: "{app}\DupliXon.exe"
Name: "{group}\Desinstalar DupliXon"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DupliXon"; Filename: "{app}\DupliXon.exe"; Tasks: desktopicon

[Run]
; Let devices on PRIVATE networks reach DupliXon (web page, signaling and WebRTC video).
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DupliXon"""; Flags: runhidden; StatusMsg: "Configurando el firewall de Windows..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""DupliXon"" dir=in action=allow program=""{app}\DupliXon.exe"" enable=yes profile=private"; Flags: runhidden
Filename: "winget"; Parameters: "install --id Parsec.ParsecVDD -e --accept-source-agreements --accept-package-agreements"; Tasks: virtualdisplay; StatusMsg: "Instalando la pantalla virtual..."; Flags: waituntilterminated
Filename: "{app}\DupliXon.exe"; Description: "{cm:LaunchProgram,DupliXon}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DupliXon"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"
