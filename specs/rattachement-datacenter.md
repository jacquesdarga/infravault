# Spécification — Rattacher une ressource à un data center (facultatif)

Statut : **à implémenter** (côté `src/`). Rédigé après lecture du code existant ; aucune des
fonctionnalités décrites ici n'est encore en place dans l'interface.

## Besoin

Dans la boîte de dialogue « Modifier la ressource » (et « Nouvelle ressource »), une ressource de
type **Serveur** — et plus généralement toute ressource — doit pouvoir être **rattachée à un data
center, ou pas**. Le rattachement est :

- **facultatif** : une ressource sans data center est normale et valide ;
- **souple** : ce n'est pas une dépendance forte ; détacher ne supprime rien, ne casse rien ;
- **modifiable** : on peut rattacher, changer de data center, ou détacher à tout moment, y compris
  en édition (aujourd'hui ce n'est possible qu'à la création).

Aujourd'hui, la boîte de dialogue ne propose aucun champ pour cela (voir la capture : Nom,
Catégorie, Hostname, IP, Environnement, Statut, Tags, exposition Internet, puis les propriétés du
type — rien pour le parent).

## Ce qui existe déjà (à réutiliser, ne pas réinventer)

- **`AssetInput.ParentId` (Guid?)** et **`AssetInput.ParentRelationType` (string?)** existent déjà
  dans `src/InfraVault.Application/DTOs/Dtos.cs`.
- **`InfrastructureService.SaveAssetAsync`** applique déjà un parent, mais **uniquement à la
  création** : `if (created && input.ParentId is { } parentId)` (lignes ~173-184). En édition, ces
  champs sont ignorés.
- La relation parent↔enfant est une **`AssetRelationship`** hiérarchique. `RelationshipTypes` définit
  `CONTAINS`, `HOSTS`, `RUNS` comme hiérarchiques, et `RelationshipTypes.DefaultFor(parentTypeKey)`
  renvoie la relation par défaut selon le type du parent — pour un **data center**, c'est
  `CONTAINS` (branche `_ => Contains`).
- **`InfrastructureService.GetTreeAsync`** construit déjà l'arborescence à partir de ces relations,
  et **`BuildPathAsync`** calcule le chemin ancêtres (déjà renvoyé dans `AssetDetailDto.Path`).
- Le schéma SQLite garantit déjà l'unicité `(source, target, type)` et interdit l'auto-relation
  (`ck_relationship_not_self`). Les suppressions hiérarchiques sont `Restrict`.
- Les événements d'audit **`RelationshipCreated`** et **`RelationshipDeleted`** existent déjà.

Autrement dit : la donnée et l'arborescence sont prêtes. Il manque **l'édition du parent** et
**son exposition dans l'UI**.

## Le vrai écart à combler

1. **Lecture** : la boîte de dialogue ne connaît pas le data center actuel de la ressource, donc ne
   peut pas le pré-sélectionner. `AssetDetailDto` ne porte pas le parent courant.
2. **Écriture** : `SaveAssetAsync` n'applique le parent qu'à la création — pas de rattachement, de
   changement ni de détachement en édition.
3. **Choix** : aucune liste des data centers disponibles n'est fournie à l'UI pour peupler le
   sélecteur.

## Changements proposés

### 1. Application — lister les parents candidats

Ajouter à `InfrastructureService` une méthode qui renvoie les data centers (au minimum) comme
candidats au rattachement :

```csharp
Task<IReadOnlyList<AssetSummaryDto>> GetAttachableParentsAsync(
    string parentTypeKey = AssetTypeKeys.Datacenter, CancellationToken ct = default);
```

Implémentation : réutiliser la logique de `ListAssetsAsync` filtrée sur le type demandé, triée par
nom. Par défaut, data centers ; le paramètre laisse la porte ouverte à d'autres conteneurs
(hyperviseur, baie…) plus tard sans changer la signature.

### 2. DTO — exposer le parent courant

Étendre `AssetDetailDto` (ou `AssetSummaryDto` renvoyé dedans) avec le parent hiérarchique courant,
pour que le formulaire le pré-remplisse :

```csharp
public Guid? ParentId        // le data center (ou autre parent hiérarchique) actuel, sinon null
public string? ParentName    // pour l'affichage
public string? ParentRelationType
```

`GetAssetAsync` le renseigne : chercher l'éventuelle `AssetRelationship` hiérarchique dont
`TargetAssetId == id` (il doit y en avoir **au plus une**, cf. §4).

### 3. Application — appliquer le parent en création ET en édition

Le point délicat : en édition, il faut distinguer « l'utilisateur ne touche pas au parent » de
« l'utilisateur détache » (`ParentId = null`). Un `Guid?` seul ne suffit pas.

**Option recommandée — un intent explicite.** Ajouter à `AssetInput` :

```csharp
public bool UpdateParent { get; init; }   // false = ne pas toucher aux relations (défaut)
```

- `UpdateParent == false` (défaut) → comportement actuel inchangé ; les appels existants (import,
  seeder, édits programmatiques) ne risquent pas d'effacer un rattachement par inadvertance.
- `UpdateParent == true` → appliquer `ParentId` :
  - `ParentId == null` → **détacher** : supprimer la relation hiérarchique entrante existante, s'il
    y en a une.
  - `ParentId == value` → **rattacher/changer** : supprimer l'éventuelle relation hiérarchique
    entrante existante, puis créer `parent --CONTAINS--> ressource` (type via
    `ParentRelationType` s'il est hiérarchique, sinon `RelationshipTypes.DefaultFor(parentType)`).

La boîte de dialogue envoie toujours `UpdateParent = true` et le `ParentId` choisi (ou null).

*Alternative* : une méthode dédiée `ReparentAsync(Guid assetId, Guid? parentId)` appelée par la
boîte de dialogue après `SaveAssetAsync`. Plus isolée, plus facile à tester unitairement, mais deux
allers-retours. Les deux approches sont acceptables ; l'intent explicite garde tout dans un seul
enregistrement.

Émettre `RelationshipCreated` / `RelationshipDeleted` dans l'audit selon le cas (déjà disponibles).

### 4. Garde-fous

- **Un seul parent hiérarchique** par ressource. Avant d'ajouter un parent, retirer l'éventuel
  parent hiérarchique existant. L'arborescence (`GetTreeAsync`) suppose un parent unique.
- **Pas d'auto-rattachement** : `ParentId != asset.Id` (déjà bloqué en base, mais le refuser tôt
  avec un message clair).
- **Pas de cycle** : interdire de rattacher une ressource à l'un de ses propres descendants (sinon
  l'arbre devient infini). Vérifier que `ParentId` n'est pas dans la descendance de la ressource
  (réutiliser un parcours de `GetTreeAsync` / une requête récursive sur les relations
  hiérarchiques). Message : « Un data center ne peut pas être rattaché à l'une de ses propres
  ressources. »
- Le parent choisi doit exister ; sinon `ValidationFailedException` (déjà le cas à la création).

### 5. Présentation (WPF) — le champ dans la boîte de dialogue

Dans `src/InfraVault.Presentation` (vue `AssetViews.xaml` + le ViewModel d'édition de ressource) :

- Ajouter un champ **« Rattaché à (facultatif) »** — un `ComboBox` listant les data centers via
  `GetAttachableParentsAsync`, avec une **première entrée vide** « — Aucun — ».
- Le placer naturellement après Environnement/Statut/Tags, avant le bloc « Propriétés — Serveur ».
- En édition, pré-sélectionner le data center courant depuis `AssetDetailDto.ParentId`.
- À l'enregistrement, renseigner `UpdateParent = true` et `ParentId` (null si « Aucun »).
- Détail utile : si aucun data center n'existe encore, afficher le champ désactivé avec un libellé
  « Aucun data center — créez-en un d'abord », plutôt qu'un sélecteur vide déroutant.

## Hors périmètre (à ne pas faire ici)

- Pas de rattachement multiple (une ressource = au plus un data center).
- Pas de déplacement en masse, pas de glisser-déposer dans l'arbre : une autre évolution.
- Le lien reste souple : aucune cascade, aucune règle métier nouvelle sur la suppression.

## Critères d'acceptation (tests à ajouter)

À couvrir dans `tests/InfraVault.Integration.Tests` (je peux les écrire une fois le code en place) :

1. Créer un serveur sans data center → aucune relation hiérarchique entrante.
2. Créer un serveur avec data center → relation `CONTAINS` data center → serveur ; le serveur
   apparaît sous le data center dans `GetTreeAsync`.
3. Éditer un serveur sans parent pour le rattacher → relation créée ; `GetAssetAsync` renvoie le
   bon `ParentId`.
4. Changer de data center → l'ancienne relation disparaît, la nouvelle existe (toujours un seul
   parent).
5. Détacher (`ParentId = null`, `UpdateParent = true`) → plus de relation hiérarchique entrante ;
   la ressource redevient racine.
6. `UpdateParent = false` → une édition qui ne touche pas au parent conserve le rattachement
   existant (non-régression de l'import et du seeder).
7. Rattacher un data center à l'une de ses propres ressources → refusé (cycle).
8. Se rattacher à soi-même → refusé.

## Résumé pour l'implémentation

| Couche | Fichier | Changement |
|---|---|---|
| DTO | `DTOs/Dtos.cs` | `AssetInput.UpdateParent` ; `AssetDetailDto.ParentId/ParentName/ParentRelationType` |
| Application | `Services/InfrastructureService.cs` | `GetAttachableParentsAsync` ; parent appliqué en édition (set/change/clear) ; garde-fous cycle/unicité ; parent courant renseigné dans `GetAssetAsync` |
| Présentation | `AssetViews.xaml` + ViewModel | `ComboBox` « Rattaché à (facultatif) », entrée vide, pré-remplissage, envoi de `UpdateParent`/`ParentId` |
| Tests | `tests/InfraVault.Integration.Tests` | les 8 critères ci-dessus |
