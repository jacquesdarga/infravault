# InfraVault

**Know your infrastructure. Secure your access. Control your information.**

Gestionnaire d'infrastructure et de secrets pour Windows. Un inventaire de vos machines, de leurs
relations et de leurs accès, dans un seul fichier chiffré, sur votre poste. Pas de serveur, pas de
compte, pas de connexion sortante.

---

## Ce que c'est

InfraVault répond à une situation courante : l'infrastructure est documentée dans un tableur, les
mots de passe sont dans un autre endroit, et le lien entre les deux n'existe que dans la tête de
celui qui les a mis en place.

Ici, les deux vivent ensemble :

- **Inventaire** — serveurs, hyperviseurs, VM, conteneurs, bases, équipements réseau, domaines,
  ressources cloud, avec leurs champs propres à chaque catégorie.
- **Relations** — ce qui héberge quoi, ce qui dépend de quoi, avec les ports des flux. L'arborescence
  et la cartographie en découlent.
- **Identifiants** — chiffrés, versionnés, rattachés aux ressources auxquelles ils donnent accès.
- **Documentation** — procédures et runbooks en Markdown, avec pièces jointes, rattachés aux
  ressources concernées.
- **Recherche** — par fragment de hostname ou d'adresse IP, sous 500 ms sur 10 000 ressources.
- **Audit** — journal scellé par chaîne de hachage, où toute modification ou suppression est
  détectable.

Tout est en français, interface comprise.

## Sécurité en une phrase

Le Master Password dérive une clé par **Argon2id** (64 Mio, 3 passes), qui chiffre une clé de
données tirée au hasard, qui chiffre les secrets en **AES-256-GCM** — chaque chiffré étant lié à sa
ligne et à son champ, de sorte qu'un chiffré recopié d'une fiche vers une autre devient illisible.

Le détail est dans [docs/security.md](docs/security.md), y compris ce que le produit **ne** protège
pas.

> **Le code n'a fait l'objet d'aucun audit de sécurité externe.** La conception est documentée et
> couverte par des tests, mais personne d'indépendant ne l'a revue. Tenez-en compte avant de
> confier à InfraVault les accès d'une infrastructure de production. Une relecture critique de la
> couche cryptographique est la contribution la plus utile que vous puissiez apporter au projet.

---

## Construire

### Sur un poste Windows avec le SDK .NET 10

```bash
dotnet build InfraVault.slnx
```

### Produire un exécutable autonome

```bash
dotnet publish src/InfraVault.Presentation/InfraVault.Presentation.csproj -c Release -r win-x64 --self-contained true -o publish
```

Le dossier `publish/` contient `InfraVault.exe` et tout ce dont il a besoin. Aucun runtime .NET n'est
requis sur la machine cible.

### Produire l'installeur Windows

Avec [Inno Setup 6](https://jrsoftware.org/isinfo.php) installé, et une publication autonome déjà
produite dans `artifacts/InfraVault-1.0.0-win-x64` :

```bash
ISCC.exe installer\InfraVault.iss
```

`artifacts/installer/InfraVault-1.0.0-setup.exe` en résulte. Voir
[installer/README.md](installer/README.md) pour le détail, notamment la limite de signature.

### Depuis WSL, sans SDK .NET côté Windows

La compilation de la couche WPF fonctionne sous Linux grâce à `EnableWindowsTargeting`, déjà présent
dans `Directory.Build.props`. Copiez le projet dans un dossier **Linux** avant de compiler — compiler
dans `/mnt/c` laisserait des `bin/` et `obj/` Linux dans votre arborescence Windows :

```bash
rsync -a --exclude bin/ --exclude obj/ /mnt/c/chemin/vers/InfraVault/ ~/infravault-build/
cd ~/infravault-build
dotnet publish src/InfraVault.Presentation/InfraVault.Presentation.csproj -c Release -r win-x64 --self-contained true -o ~/infravault-publish
```

## Installer et lancer

Deux formes de livraison :

- **L'installeur** `InfraVault-1.0.0-setup.exe` — installation par utilisateur, sans droits
  administrateur, avec raccourci et désinstallation propre. **La désinstallation ne supprime jamais
  vos coffres** : ils restent dans `%LOCALAPPDATA%\InfraVault`, et le désinstalleur vous rappelle où.
- **Le dossier portable** — décompressez et lancez, rien à installer.

```
InfraVault.exe [--data-dir <dossier>]
```

Sans argument, les données vont dans `%LOCALAPPDATA%\InfraVault`. La variable d'environnement
`INFRAVAULT_DATA_DIR` a le même effet que `--data-dir`, qui l'emporte si les deux sont présents.

Le dossier de données contient :

| | |
| --- | --- |
| `vault.db` | Le coffre (SQLite chiffré) |
| `backups\` | Sauvegardes `.ivbak` |
| `logs\` | Journaux applicatifs, un fichier par jour |
| `preferences.json` | Préférences d'interface (taille de fenêtre, thème) |
| `tmp\` | Fichiers temporaires |

**Une seule instance par dossier de données.** Deux coffres distincts peuvent tourner côte à côte,
chacun avec son `--data-dir`.

Au premier lancement, l'application propose de créer un coffre et, si vous le souhaitez, de le
remplir avec un jeu de données d'exemple pour découvrir l'outil.

> **Notez votre clé de récupération.** Elle est affichée une seule fois, à la création. Sans elle et
> sans votre Master Password, le coffre est définitivement illisible. Il n'y a pas de porte dérobée.

## Raccourcis

| Raccourci | Action |
| --- | --- |
| `Ctrl+K` ou `Ctrl+F` | Recherche globale |
| `Ctrl+L` | Verrouiller le coffre |
| `Ctrl+N` | Nouvelle ressource |
| `Ctrl+1` … `Ctrl+5` | Tableau de bord, Infrastructure, Cartographie, Identifiants, Documentation |
| `Alt+←` | Retour |
| `Échap` | Fermer le dialogue en cours |

Les raccourcis globaux sont inactifs tant qu'un dialogue est ouvert ; `Échap` le ferme.

## Écrans

Création du coffre, déverrouillage (avec récupération et restauration), tableau de bord,
infrastructure (liste et fiche à six onglets), cartographie avec export PNG et Word `.docx`,
identifiants, documentation, recherche, audit, sauvegardes avec exports JSON et `.ivsec`,
paramètres. Thèmes clair et sombre.

---

## Développement

### Tests

```bash
dotnet test InfraVault.slnx
```

486 tests répartis en cinq projets. Ils tournent sous Linux comme sous Windows — la couche WPF n'est
couverte par aucun test. Le détail est dans [docs/requirements.md](docs/requirements.md), qui indique
aussi ce qui **n'est pas** couvert.

### Documentation technique

| | |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | Organisation des cinq projets, démarrage, règles de dépendance |
| [docs/security.md](docs/security.md) | Modèle de menace, chiffrement par enveloppe, format des chiffrés, audit scellé |
| [docs/database.md](docs/database.md) | Schéma SQLite, tables, index de recherche, sauvegardes |
| [docs/requirements.md](docs/requirements.md) | Critères d'acceptation et tests correspondants |

### Scripts

| | |
| --- | --- |
| `scripts/make-icon.py` | Regénère `src/InfraVault.Presentation/Resources/infravault.ico`. Dessin procédural, sans dépendance : `python scripts/make-icon.py` |

### Outil de capture

En configuration **Debug uniquement**, `InfraVault.exe --data-dir <dossier jetable> --snapshot <dossier>`
crée un coffre de démonstration, parcourt les écrans et écrit des captures PNG. Le code est encadré
par `#if DEBUG` et absent des binaires Release.

---

## Ce qui n'existe pas

Volontairement explicite, pour éviter les mauvaises surprises :

- **Installeur non signé.** Faute de certificat de signature de code, Windows SmartScreen
  affichera un avertissement au téléchargement. Vérifiez l'empreinte SHA-256 publiée à côté du
  fichier.
- **Pas de paquet MSIX ou MSI**, pas de distribution par le Microsoft Store ou winget.
- **Pas de migrations de schéma.** Le schéma est créé en version 1 ; faire évoluer un coffre
  existant demandera d'écrire un mécanisme de migration.
- **Pas de Windows Hello**, pas de DPAPI.
- **Pas de mode équipe ni de synchronisation.** Partager un coffre entre plusieurs postes demande
  une conception cryptographique dédiée — poser la base SQLite sur un partage réseau n'en est pas
  une.
- **Pas de signature des binaires.**

## Contribuer

Les contributions sont bienvenues : correctifs, fonctionnalités, documentation, traductions, et
surtout relecture critique du code cryptographique. [CONTRIBUTING.md](CONTRIBUTING.md) décrit
comment construire, tester et proposer une pull request, ainsi que les invariants de sécurité
qu'aucun changement ne doit casser.

**Une faille de sécurité ne s'ouvre pas en issue publique.** La marche à suivre est dans
[SECURITY.md](SECURITY.md).

## Licence

InfraVault est distribué sous **GNU General Public License v3.0 ou ultérieure** — voir
[LICENSE](LICENSE).

Vous êtes libre d'utiliser, d'étudier, de modifier et de redistribuer ce logiciel. En contrepartie,
toute version modifiée que vous distribuez doit l'être sous la même licence, sources comprises.

Ce n'est pas une formalité : InfraVault détient les mots de passe de ses utilisateurs, et un
gestionnaire de secrets ne vaut que par la possibilité d'inspecter ce qu'il fait. La GPL garantit
que tout logiciel dérivé d'InfraVault reste inspectable par ceux qui lui confient leurs accès.

```
InfraVault — gestionnaire d'infrastructure et de secrets
Copyright (C) 2026  Jacques DARGA

Ce programme est un logiciel libre : vous pouvez le redistribuer et/ou le modifier selon
les termes de la GNU General Public License telle que publiée par la Free Software
Foundation, soit la version 3 de la licence, soit (à votre choix) toute version ultérieure.

Ce programme est distribué dans l'espoir qu'il sera utile, mais SANS AUCUNE GARANTIE, ni
même la garantie implicite de QUALITÉ MARCHANDE ou d'ADÉQUATION À UN USAGE PARTICULIER.
Voir la GNU General Public License pour plus de détails.

Vous devriez avoir reçu une copie de la GNU General Public License avec ce programme.
Si ce n'est pas le cas, voir <https://www.gnu.org/licenses/>.
```
