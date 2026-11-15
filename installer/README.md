# Installeur Windows

`InfraVault.iss` produit `InfraVault-<version>-setup.exe` avec [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Construire

L'installeur empaquète une publication autonome ; il faut donc la produire d'abord.

```bash
dotnet publish src/InfraVault.Presentation/InfraVault.Presentation.csproj \
  -c Release -r win-x64 --self-contained true \
  -o artifacts/InfraVault-1.0.0-win-x64
```

Puis, depuis la racine du dépôt :

```bash
ISCC.exe installer\InfraVault.iss
```

Le résultat est écrit dans `artifacts/installer/`. Sur ce poste de développement, Inno Setup est
installé par utilisateur ; `ISCC.exe` se trouve alors dans
`%LOCALAPPDATA%\Programs\Inno Setup 6\` et non dans `Program Files`.

Le chemin de la publication est figé dans `#define SourceDir` en tête du script : si vous changez
le numéro de version, changez les deux.

## Ce que fait l'installeur

- **Installation par utilisateur**, sans élévation. C'est cohérent avec une application dont les
  données vivent déjà dans `%LOCALAPPDATA%\InfraVault`. Un administrateur peut forcer une
  installation pour toute la machine avec `/ALLUSERS`, ou via le choix proposé au lancement.
- **Présente la licence GPL-3.0** avant installation.
- **Raccourci menu Démarrer**, raccourci bureau en option.
- **Ferme proprement InfraVault** s'il tourne, via le Restart Manager de Windows, au lieu de buter
  sur un fichier verrouillé.
- **Français et anglais.**

## Ce que l'installeur ne fait pas, délibérément

**La désinstallation ne touche jamais à vos données.** `%LOCALAPPDATA%\InfraVault` — coffres,
sauvegardes, journaux — survit à la désinstallation, et le désinstalleur affiche son emplacement.

Ce n'est pas un oubli. Un coffre InfraVault n'a aucune copie ailleurs : c'est le principe même du
produit. Un désinstalleur qui proposerait une case « supprimer aussi mes données » finirait par
être coché par quelqu'un qui voulait seulement réinstaller, et ses mots de passe seraient perdus
sans recours. Supprimer ce dossier reste un geste délibéré de l'utilisateur.

## Limite connue : l'installeur n'est pas signé

Il n'y a pas de certificat de signature de code. Conséquence concrète : au téléchargement,
**Windows SmartScreen affichera un avertissement** du type « Windows a protégé votre ordinateur »,
et l'utilisateur devra cliquer sur *Informations complémentaires* puis *Exécuter quand même*.

C'est normal pour un binaire non signé et sans réputation établie, mais c'est exactement le genre
d'avertissement qu'on apprend aux gens à ignorer. Si InfraVault doit être distribué au-delà d'un
cercle restreint, un certificat de signature de code est à prévoir.

En attendant, publiez l'empreinte SHA-256 du fichier à côté du téléchargement, pour que les
utilisateurs puissent vérifier ce qu'ils ont reçu :

```powershell
Get-FileHash artifacts\installer\InfraVault-1.0.0-setup.exe -Algorithm SHA256
```

## Identifiant d'application

`AppId` est un GUID fixe dans le script. C'est lui qui permet à une version ultérieure de
**remplacer** celle qui est installée plutôt que de s'ajouter à côté. Ne le changez jamais : deux
versions d'InfraVault installées en parallèle, chacune croyant être seule, est précisément la
situation à éviter.
