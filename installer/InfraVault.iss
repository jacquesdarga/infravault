; Installeur InfraVault — Inno Setup 6
;
; Construction :
;   1. publier l'application     (voir installer/README.md)
;   2. ISCC.exe installer\InfraVault.iss
;
; Le résultat est produit dans artifacts\installer\.

#define AppName        "InfraVault"
#define AppVersion     "1.0.0"
#define AppPublisher   "Jacques DARGA"
#define AppUrl         "https://github.com/jacquesdarga/infravault"
#define AppExe         "InfraVault.exe"
#define SourceDir      "..\artifacts\InfraVault-1.0.0-win-x64"

[Setup]
; Identifiant stable : c'est lui qui permet à une version ultérieure de
; remplacer celle-ci au lieu de s'installer à côté. Ne jamais le changer.
AppId={{D0BB02E1-3696-47F3-B1A9-C7AFE80FAB6C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} — gestionnaire d'infrastructure et de secrets

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; Installation par utilisateur : aucune élévation requise, ce qui est cohérent
; avec un coffre dont les données vivent déjà dans %LOCALAPPDATA%.
; Un administrateur peut forcer une installation pour toute la machine
; (/ALLUSERS en ligne de commande, ou le choix proposé au lancement).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog

; Application 64 bits uniquement.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; La licence GPL-3.0 est présentée avant installation, comme il se doit.
LicenseFile=..\LICENSE
SetupIconFile=..\src\InfraVault.Presentation\Resources\infravault.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}

OutputDir=..\artifacts\installer
OutputBaseFilename=InfraVault-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Restart Manager : si InfraVault tourne, l'utilisateur est invité à le fermer
; plutôt que de se heurter à un fichier verrouillé.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Tout le dossier publié autonome : aucun runtime .NET n'est requis sur la machine cible.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Messages]
fr.WelcomeLabel2=Ceci va installer [name/ver] sur votre ordinateur.%n%nInfraVault conserve votre inventaire et vos secrets dans un fichier chiffré, sur ce poste uniquement. Aucune connexion réseau n'est utilisée.
en.WelcomeLabel2=This will install [name/ver] on your computer.%n%nInfraVault keeps your inventory and secrets in an encrypted file, on this machine only. No network connection is used.

[CustomMessages]
fr.DonneesConservees=Vos coffres et vos sauvegardes n'ont pas été supprimés.%n%nIls se trouvent dans :%n%1%n%nSupprimez ce dossier vous-même si vous voulez vraiment effacer vos secrets — cette opération est irréversible et aucune récupération n'est possible.
en.DonneesConservees=Your vaults and backups have not been deleted.%n%nThey are located in:%n%1%n%nDelete this folder yourself if you really want to erase your secrets — this cannot be undone and no recovery is possible.

[Code]
// La désinstallation ne touche JAMAIS aux données de l'utilisateur.
// Supprimer %LOCALAPPDATA%\InfraVault détruirait des mots de passe
// irrécupérables : il n'existe aucune sauvegarde ailleurs, par conception.
// L'utilisateur est informé de l'endroit où elles restent.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DossierDonnees: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DossierDonnees := ExpandConstant('{localappdata}\{#AppName}');
    if DirExists(DossierDonnees) then
      MsgBox(FmtMessage(CustomMessage('DonneesConservees'), [DossierDonnees]),
             mbInformation, MB_OK);
  end;
end;
