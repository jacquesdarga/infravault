# Contribuer à InfraVault

Merci de l'intérêt que vous portez au projet. Les contributions sont bienvenues : correctifs,
fonctionnalités, documentation, traductions, et surtout relecture critique du code
cryptographique.

**Vous avez trouvé une faille de sécurité ?** N'ouvrez pas d'issue. Lisez
[SECURITY.md](SECURITY.md).

## Avant de commencer

Pour un correctif évident — une faute de frappe, un message d'erreur confus, un cas limite manqué —
ouvrez directement une pull request.

Pour un changement important, **ouvrez une issue d'abord**. Une discussion de dix minutes évite
souvent un week-end de travail qui ne sera pas fusionné. C'est particulièrement vrai ici : le
projet a des contraintes qui ne sautent pas aux yeux (pas de migrations de schéma, une couche
cryptographique volontairement figée, aucune dépendance réseau).

## Construire

### Sur Windows avec le SDK .NET 10

```bash
dotnet build InfraVault.slnx
dotnet test InfraVault.slnx
```

### Depuis WSL, sans SDK .NET côté Windows

La couche WPF se compile sous Linux grâce à `EnableWindowsTargeting`, déjà présent dans
`Directory.Build.props`. **Ne compilez pas dans `/mnt/c`** : vous laisseriez des `bin/` et `obj/`
Linux dans une arborescence Windows. Copiez d'abord dans un dossier Linux :

```bash
rsync -a --exclude bin/ --exclude obj/ /mnt/c/chemin/vers/InfraVault/ ~/infravault-build/
cd ~/infravault-build
dotnet test InfraVault.slnx
```

L'application elle-même ne se lance que sous Windows — c'est du WPF. Sous Linux vous pouvez
compiler, exécuter les tests, et produire l'exécutable autonome, pas le voir tourner.

## Tests

```bash
dotnet test InfraVault.slnx
```

**486 tests, 0 échec, 0 warning.** C'est la condition d'entrée de toute pull request, et la CI le
vérifie sous Linux et sous Windows.

| Projet | Ce qu'on y met |
| --- | --- |
| `InfraVault.Domain.Tests` | Objets valeur, normalisation, bornes |
| `InfraVault.Application.Tests` | Logique métier pure, validateurs, politiques |
| `InfraVault.Infrastructure.Tests` | SQLite, index FTS5, fichiers, rapport .docx |
| `InfraVault.Security.Tests` | Primitives cryptographiques et cycle de vie du coffre |
| `InfraVault.Integration.Tests` | Critères d'acceptation de bout en bout |

Les trois derniers utilisent `tests/Shared/VaultHarness.cs`, qui monte un coffre jetable par test :
dossier temporaire, vraie base SQLite, services câblés comme en production, horloge pilotée et KDF
réduit à 8 Mio pour que les tests non cryptographiques restent rapides.

Deux choses à ne pas défaire sans les remplacer :

- **`DisableTestParallelization`** dans les trois projets qui utilisent le harnais.
  `SqliteConnection.ClearAllPools()` agit sur tout le processus ; en parallèle, les tests
  s'arracheraient leurs connexions.
- **Les contre-épreuves.** Le test « aucun secret en clair dans la base » vérifie aussi que le nom
  de la ressource et le hostname, eux, *sont* trouvables en clair. Sans cela, il passerait aussi le
  jour où la fonction de recherche d'octets serait cassée. Un test qui ne peut pas échouer ne
  prouve rien.

Écrivez des tests qui décrivent un comportement, pas une implémentation. Les noms sont en français
et en toutes lettres : `Une_enveloppe_alteree_rend_le_coffre_inouvrable`.

## Organisation du code

```
Presentation ──► Infrastructure ──► Application ──► Domain ──► Shared
```

**La règle de dépendance ne souffre aucune exception : une couche ne connaît que celles qui sont
sous elle.** `Application` définit des interfaces, `Infrastructure` les implémente. C'est ce qui
permet de tester toute la logique métier sans WPF.

Concrètement : pas de `Microsoft.Data.Sqlite` dans `Application`, pas de référence à WPF ailleurs
que dans `Presentation`, pas d'appel à `DateTime.UtcNow` en dehors de `IClock`.

[docs/architecture.md](docs/architecture.md) détaille le rôle de chaque projet.

## Conventions

- **Français** pour l'interface, les messages d'erreur, les commentaires et les noms de tests. Les
  noms de types et de membres restent en anglais, sauf le vocabulaire métier déjà en français.
- **`snake_case`** pour les colonnes SQL, converti automatiquement depuis les noms C#.
- `Nullable` et `ImplicitUsings` activés, analyseurs .NET au niveau `latest`. Un warning est une
  erreur à corriger, pas à supprimer.
- Les messages de `InfraVaultException` sont affichés tels quels à l'utilisateur : écrivez-les pour
  quelqu'un qui n'a pas lu le code.
- Les commentaires expliquent **pourquoi**, pas quoi. Le code dit déjà quoi.
- Les versions de paquets vivent dans `Directory.Build.props`, jamais en dur dans un `.csproj`.

## Dépendances

Le projet étant sous GPL-3.0, **toute nouvelle dépendance doit être compatible GPL-3.0**. MIT,
BSD, Apache-2.0 et les licences GPL le sont. Une licence propriétaire, GPL-2.0 *seule* (sans la
clause « ou ultérieure ») ou à clause non commerciale ne l'est pas, et rendrait le projet
indistribuable.

Les dépendances actuelles, toutes compatibles :

| Licence | Paquets |
| --- | --- |
| MIT | EF Core et EF Core Sqlite, Microsoft.Data.Sqlite, Microsoft.Extensions.\*, Microsoft.Win32.SystemEvents, CommunityToolkit.Mvvm, Konscious.Security.Cryptography.Argon2 |
| Apache-2.0 | FluentValidation, Serilog et ses sinks, xunit |

Indiquez la licence du paquet dans la description de votre PR. Et demandez-vous d'abord si la
dépendance est nécessaire : le rapport `.docx` est généré sans bibliothèque OpenXML, et c'est très
bien ainsi.

## Invariants de sécurité

Toute modification doit les préserver. Ils sont couverts par des tests, et une PR qui les casse ne
sera pas fusionnée — même si les tests, eux, ont été ajustés.

1. **Aucun clair n'entre dans l'index de recherche ni dans le journal d'audit.** Les métadonnées
   d'audit passent par `AuditMetadata.Sanitize` et sont tronquées ; il n'existe pas d'API pour y
   écrire une valeur libre, et il ne doit pas en exister.
2. **Chaque chiffré est lié à son emplacement** par des données associées (`Aad`). Un secret
   recopié d'une fiche vers une autre doit devenir illisible.
3. **Le nonce AES-GCM vient du CSPRNG**, jamais d'un compteur.
4. **Les bornes de `KdfParameters.EnsureValid` ne sont pas relâchées.** Elles protègent contre un
   fichier de coffre fabriqué qui réclamerait des gigaoctets de RAM.
5. **Toute altération lève `IntegrityException`.** Aucun chemin ne doit renvoyer des données
   déchiffrées fausses.
6. **Une fois la session ouverte, seul `UseKey(...)` donne accès à la DEK**, et l'appelant ne la
   conserve jamais. Le déchiffrement des enveloppes reste confiné à `AuthenticationService` et à
   `BackupService.RestoreAsync` — ce sont les seuls endroits où la DEK existe hors de
   `VaultSession`, et ils l'effacent dans un `finally`.

### Faux positifs de scanners de secrets

`src/InfraVault.Presentation/Diagnostics/SnapshotRunner.cs` contient deux littéraux qui ressemblent
à des secrets : le mot de passe du coffre jetable servant aux captures, et une chaîne
`password = hunter2hunter2` qui sert à déclencher volontairement `SecretLeakDetector`. Le fichier
entier est encadré par `#if DEBUG` et absent des binaires Release. Si un scanner les signale, c'est
un faux positif — ne les « corrigez » pas, le second test cesserait de tester quoi que ce soit.

Si votre changement vous oblige à toucher à l'un de ces points, dites-le explicitement dans la
description de la PR. Ce n'est pas interdit — c'est à discuter avant, pas à découvrir pendant la
relecture.

## Pull requests

1. Une branche par sujet, depuis `main`.
2. `dotnet test InfraVault.slnx` vert, 0 warning.
3. Un message de commit qui dit **pourquoi**, pas seulement quoi.
4. La documentation mise à jour si le comportement change — `docs/` décrit le code tel qu'il est,
   pas tel qu'on aimerait qu'il soit.
5. Pas de reformatage massif mêlé à un changement fonctionnel : la relecture devient impossible.

## Où l'aide est la plus utile

Les manques sont connus et assumés. Si vous cherchez par où commencer :

- **Relecture de la conception cryptographique.** Personne d'indépendant ne l'a auditée. C'est la
  contribution la plus précieuse.
- **Migrations de schéma.** Le schéma est créé par `EnsureCreated` en version 1. Faire évoluer un
  coffre existant est aujourd'hui impossible.
- **Tests d'interface.** La couche WPF n'est couverte par aucun test automatisé.
- **Vérification de l'absence de trafic réseau.** Le critère « utilisation sans Internet » n'est
  pas testé, et un test naïf passerait toujours. Un relevé au pare-feu documenté serait utile.
- **Installeur** MSIX ou MSI — il n'y en a pas.
- **Traduction de l'interface.** Elle est entièrement en français.

## Licence

InfraVault est distribué sous **GNU General Public License v3.0** ([LICENSE](LICENSE)).

En proposant une contribution, vous acceptez qu'elle soit distribuée sous cette licence. Vous
conservez le droit d'auteur sur votre travail ; il n'y a pas de cession de droits à signer.

La GPL garantit que toute version modifiée d'InfraVault reste inspectable. Pour un logiciel qui
détient les mots de passe de ses utilisateurs, c'est le choix de licence qui protège ces
utilisateurs — pas une formalité.
