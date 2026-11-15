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

### Depuis WSL, sans SDK .NET côté Windows

La compilation de la couche WPF fonctionne sous Linux grâce à `EnableWindowsTargeting`, déjà présent
dans `Directory.Build.props`. Copiez le projet dans un dossier **Linux** avant de compiler — compiler
dans `/mnt/c` laisserait des `bin/` et `obj/` Linux dans votre arborescence Windows :

```bash
rsync -a --exclude bin/ --exclude obj/ /mnt/c/chemin/vers/InfraVault/ ~/infravault-build/
cd ~/infravault-build
dotnet publish src/InfraVault.Presentation/InfraVault.Presentation.csproj -c Release -r win-x64 --self-contained true -o ~/infravault-publish
```

## Lancer

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

- **Pas d'installeur** MSIX ou MSI. La livraison est un dossier portable ; la désinstallation
  consiste à le supprimer.
- **Pas de migrations de schéma.** Le schéma est créé en version 1 ; faire évoluer un coffre
  existant demandera d'écrire un mécanisme de migration.
- **Pas de Windows Hello**, pas de DPAPI.
- **Pas de mode équipe ni de synchronisation.** Partager un coffre entre plusieurs postes demande
  une conception cryptographique dédiée — poser la base SQLite sur un partage réseau n'en est pas
  une.
- **Pas de signature des binaires.**

## Licence

**À définir.** Aucune licence n'a été choisie pour ce projet. En l'absence de licence explicite,
tous les droits sont réservés par défaut : personne d'autre n'est autorisé à copier, modifier ou
redistribuer ce code. Si le projet doit être partagé, choisissez une licence et remplacez le fichier
[LICENSE](LICENSE).
