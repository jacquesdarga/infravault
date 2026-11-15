# Architecture

Ce document décrit l'organisation du code telle qu'elle est, pas telle qu'elle pourrait être.
Tout ce qui n'est pas implémenté est signalé comme tel.

## Vue d'ensemble

InfraVault est une application de bureau Windows (WPF, .NET 10) qui tient en un seul processus et
un seul fichier de données. Il n'y a ni serveur, ni service d'arrière-plan, ni appel réseau.

Cinq projets, empilés du plus abstrait au plus concret :

```
InfraVault.Presentation   WPF / MVVM — fenêtres, vues, ViewModels
        │
        ├──────────────► InfraVault.Infrastructure   SQLite, AES-GCM, Argon2id, fichiers, .docx
        │                        │
        └────────────────────────┴──► InfraVault.Application   cas d'utilisation, session, audit
                                              │
                                              ├──► InfraVault.Domain    entités, objets valeur
                                              └──► InfraVault.Shared    constantes, exceptions
```

La règle de dépendance ne souffre aucune exception : **une couche ne connaît que celles qui sont
sous elle**. `Application` définit des interfaces (`IVaultDb`, `IEncryptionService`,
`IKeyDerivationService`, `ISearchIndex`, `IFileStorage`, `IDatabaseFiles`, `IVaultSession`) que
`Infrastructure` implémente. C'est ce qui permet de tester toute la logique métier sans WPF, et de
remplacer SQLite ou Argon2id sans toucher aux cas d'utilisation.

## Les projets

### InfraVault.Shared

Constantes et exceptions, sans aucune dépendance. On y trouve les paramètres cryptographiques figés
(`CryptoConstants`), les listes de référence (types de ressources, types d'identifiants, types de
relations), les bornes (`Limits`) et la hiérarchie d'exceptions.

Toutes les exceptions héritent de `InfraVaultException`, dont le message est en français et
directement affichable à l'utilisateur. C'est un choix assumé : il n'y a pas de couche de
traduction entre l'erreur technique et le message d'interface.

### InfraVault.Domain

Entités persistées (`Vault`, `Asset`, `Credential`, `Documentation`, `AuditEvent`) et objets valeur
(`KdfParameters`, `PortList`, `TagList`, `RecoveryKey`, `FieldDefinition`).

Les objets valeur portent la logique de normalisation : `PortList.Normalize` transforme
« 443, 80/tcp; 8100-8000 » en « 443/tcp, 80/tcp, 8000-8100/tcp », `TagList` met les tags en
minuscules et déduplique. Cette normalisation est faite une fois, à l'écriture, pour que les
comparaisons et les filtres n'aient jamais à la refaire.

`AuditEvent.Canonical()` et `AuditEvent.ComputeHash()` vivent ici parce que la chaîne de hachage
du journal fait partie du modèle, pas de son stockage.

### InfraVault.Application

Les cas d'utilisation, tous enregistrés en singleton par `AddInfraVaultApplication()` :

| Service | Rôle |
| --- | --- |
| `AuthenticationService` | Création, déverrouillage, verrouillage, rotation du mot de passe, récupération |
| `VaultSession` | Détient la DEK en mémoire ; seule porte d'entrée pour chiffrer et déchiffrer |
| `InfrastructureService` | Ressources, propriétés, relations, arborescence |
| `CredentialService` | Identifiants, versions de secrets, liens vers les ressources |
| `DocumentationService` | Documents, versions, pièces jointes |
| `SearchService` | Recherche globale au-dessus de l'index FTS5 |
| `AuditService` | Écriture et vérification du journal |
| `BackupService` | Sauvegardes `.ivbak`, exports JSON et `.ivsec` |
| `TopologyService` | Cartographie : arbre, flux, expositions Internet |
| `DemoDataSeeder` | Jeu de données d'exemple proposé à la création |

`VaultSession` mérite une mention particulière : c'est le seul endroit du code qui détient la clé de
chiffrement en clair. Elle est exposée par `UseKey(...)`, qui passe la clé à un délégué et ne la
rend jamais sous forme de tableau conservable. `Lock()` efface la clé de la mémoire.

### InfraVault.Infrastructure

Les implémentations concrètes :

- **`VaultDbContext` / `VaultDbFactory`** — EF Core sur SQLite. Colonnes en `snake_case`, mode WAL,
  clés étrangères actives, contrôle d'intégrité au démarrage.
- **`AesGcmEncryptionService`** — AES-256-GCM, format « boîte scellée » décrit dans
  [security.md](security.md).
- **`Argon2KeyDerivationService`** — Argon2id (implémentation managée Konscious).
- **`SqliteSearchIndex`** — FTS5 avec tokenizer trigramme, repli automatique sur `unicode61` si le
  trigramme n'est pas disponible dans la build SQLite.
- **`FileStorageService`** — écritures atomiques (fichier temporaire puis renommage).
- **`DatabaseFiles`** — instantané `VACUUM INTO`, validation, remplacement du fichier.
- **`DocxArchitectureReportWriter`** — génère le rapport d'architecture en OpenXML, sans dépendance
  externe.
- **`WindowsSecurityService`** — réactions aux événements système (verrouillage de session, veille).

### InfraVault.Presentation

WPF en MVVM, avec `CommunityToolkit.Mvvm`. Thèmes clair et sombre (`Resources/Themes`). Les journaux
applicatifs passent par Serilog vers `logs/infravault-<date>.log`.

`Diagnostics/SnapshotRunner.cs` est encadré par `#if DEBUG` : c'est un outil de développement qui
parcourt les écrans et écrit des captures PNG. **Il est absent des binaires Release.**

## Démarrage

`App.OnStartup` enchaîne :

1. Lecture du dossier de données : `--data-dir <dossier>`, sinon la variable d'environnement
   `INFRAVAULT_DATA_DIR`, sinon `%LOCALAPPDATA%\InfraVault`.
2. Prise d'un mutex nommé dérivé du dossier de données — **une seule instance par dossier**. Deux
   coffres distincts peuvent donc tourner côte à côte.
3. Configuration de Serilog.
4. Construction du conteneur : `AddInfraVaultApplication()` puis `AddInfraVaultInfrastructure(dossier)`.
5. `IVaultDbFactory.InitializeAsync()` — création du schéma si besoin, `PRAGMA quick_check`.
6. Affichage de la fenêtre : création du coffre s'il n'existe pas, écran de déverrouillage sinon.

## Ce qui n'existe pas

À ne pas documenter comme existant, et à ne pas promettre :

- **Pas de migrations EF.** Le schéma est créé par `EnsureCreated`, version 1. Faire évoluer le
  schéma demandera d'écrire un mécanisme de migration.
- **Pas de paquet MSIX ou MSI.** La livraison se fait soit en dossier portable autonome, soit par
  l'installeur Inno Setup (`installer/`), qui n'est pas signé.
- **Pas de Windows Hello**, pas de DPAPI.
- **Pas de mode équipe ni de synchronisation.** C'est une phase ultérieure du cahier des charges et
  elle demandera une conception cryptographique propre, pas une base partagée sur un réseau.

## Tests

Cinq projets sous `tests/`, 486 tests. Voir [requirements.md](requirements.md) pour la
correspondance avec les critères d'acceptation.

`tests/Shared/VaultHarness.cs` est lié dans les trois projets qui en ont besoin
(`<Compile Include="..\Shared\VaultHarness.cs" />`) plutôt que dupliqué. Il monte un coffre jetable
par test : dossier temporaire, vraie base SQLite, services câblés comme en production, horloge
pilotée (`TestClock`) et KDF réduit à 8 Mio / 1 passe / 1 voie pour que les tests non
cryptographiques ne paient pas le coût d'Argon2id.

Les trois projets qui utilisent le harnais désactivent la parallélisation xUnit
(`CollectionBehavior(DisableTestParallelization = true)`) : `SqliteConnection.ClearAllPools()` agit
sur tout le processus et des tests parallèles s'arracheraient leurs connexions.
