# Spécification — Emplacements physiques, hyperviseurs autonomes et hiérarchie générale

Statut : **à implémenter** (côté `src/`). Fait suite à
[rattachement-datacenter.md](rattachement-datacenter.md), dont il généralise le mécanisme à toutes
les ressources. Rien de ce qui suit n'est encore en place ; rédigé après lecture du code.

## Les trois besoins

1. **Hyperviseurs autonomes (Proxmox hors cluster).** Pouvoir créer un Proxmox seul et y rattacher
   des machines virtuelles, sans qu'un « cluster » soit nécessaire.
2. **Organiser par emplacement (localité / zone / bâtiment).** Pouvoir créer des ressources
   représentant un lieu, puis y rattacher serveurs, routeurs, baies… et, en se plaçant sur un lieu,
   retrouver tout ce qui s'y trouve.
3. **Généraliser la hiérarchie.** Le rattachement ne concerne pas que les serveurs vers un data
   center : **toute ressource** doit pouvoir avoir un parent approprié, dans un arbre unique.

## 1. Hyperviseur autonome — rien à créer, à clarifier

Le type **« Hyperviseur »** existe déjà (`AssetTypeKeys.Hypervisor`, `SystemAssetTypes.cs`). Il porte
déjà `platform` (« Proxmox VE », « VMware ESXi »…), `version`, **`cluster`**, `management_url`.

- **Proxmox singleton** = une ressource « Hyperviseur », champ `cluster` laissé vide.
- Une **VM** = type « Machine virtuelle », rattachée à l'hyperviseur par la relation **`RUNS`**
  (« exécute »). `RelationshipTypes.DefaultFor(hypervisor)` renvoie déjà `HOSTS` — **à corriger** :
  pour un hyperviseur, le défaut pertinent est `RUNS` vers une VM (voir §3, la matrice).
- Pas besoin d'un type « Cluster » pour le cas singleton. Si des clusters HA doivent un jour être
  modélisés comme entités, voir « Idées complémentaires ».

**Aucun nouveau type n'est requis pour ce besoin.** Il faut surtout que le rattachement d'une VM à
un hyperviseur soit possible depuis la boîte de dialogue (c'est l'objet de la spec précédente,
généralisée ici).

## 2. Emplacements physiques — un nouveau type « Emplacement »

Ajouter **un seul** type système, auto-imbriquable, plutôt que trois types figés : il couvre
localité, zone, bâtiment, salle, baie, sans multiplier les catégories.

### Nouveau type

- `AssetTypeKeys.Location = "location"` (dans `src/InfraVault.Shared/Constants/AppConstants.cs`).
- Définition dans `SystemAssetTypes.All` (`SystemAssetTypes.cs`) :

```
new(AssetTypeKeys.Location, "Emplacement", "Lieu physique : localité, zone, bâtiment, salle, baie.",
    "map-pin", "amber",
    [
        F("kind", "Niveau", Loc, FieldKind.Choice, options: ["Localité", "Zone", "Bâtiment", "Étage", "Salle", "Baie / rack"]),
        F("address", "Adresse", Loc, FieldKind.Multiline),
        F("coordinates", "Coordonnées GPS", Loc, placeholder: "12.3686, -1.5275"),
        F("contact", "Contact sur site", Gen),
        F("access_procedure", "Procédure d'accès", Loc, FieldKind.Multiline),
    ]),
```

Le champ **`kind`** donne la granularité (Localité → Zone → Bâtiment → Étage → Salle → Baie) sans
imposer de types distincts. Un emplacement peut en contenir un autre (Bâtiment `CONTAINS` Salle).

### Compatibilité des coffres existants

Pas de migration nécessaire. Les types système manquants sont **re-semés au déverrouillage**
(`AuthenticationService.OpenSessionAsync` appelle `SeedSystemTypes(db, existing)`), donc un coffre
déjà créé verra « Emplacement » apparaître automatiquement à la prochaine ouverture.

### Articulation avec le Datacenter

Le type « Datacenter » reste (il a des champs propres : opérateur, procédure d'accès). Un Datacenter
peut être **rattaché à un Emplacement** (ex. Bâtiment B `CONTAINS` Datacenter principal). La chaîne
physique complète devient :

```
Localité ⊃ Bâtiment ⊃ Salle ⊃ Baie ⊃ Datacenter ⊃ Serveur ⊃ (Hyperviseur ⊃ VM)
```

### Champ texte « Emplacement physique » existant

Les types Serveur/Hyperviseur/Stockage/Équipement réseau ont déjà un champ libre « Emplacement
physique » (ex. « Baie A3 · U12 »). **Le conserver** : il précise la position fine (U dans la baie),
là où le lien parent donne l'appartenance. Les deux sont complémentaires.

## 3. Hiérarchie générale — un parent optionnel pour toute ressource

Généralisation directe de la spec précédente. Au lieu de « serveur → data center », **toute
ressource peut avoir un parent hiérarchique optionnel**, et le type de relation est **déduit du
couple (type parent, type enfant)**.

### Principe : hiérarchie = contenance / emplacement ; le reste = relations latérales

- **L'arbre** (un seul parent par ressource) exprime la **contenance physique ou d'exécution** :
  un lieu contient des machines, un data center contient des serveurs, un hyperviseur exécute des
  VM, une stack contient des conteneurs.
- **Les liens logiques** (une application `USES` une base, un service `DEPENDS_ON` un autre) restent
  des **relations non hiérarchiques**, déjà gérées, et **multiples**. On ne les force pas dans
  l'arbre.

Cela résout le cas « une VM tourne sur l'hyperviseur DC-A mais sert le projet X » : l'arbre dit *où
elle s'exécute*, une relation `USES`/`DEPENDS_ON` dit *à quoi elle sert*.

### Matrice des parents éligibles et relation déduite

`RelationshipTypes.DefaultFor` doit devenir fonction **du couple** (parent, enfant), pas seulement du
parent. Proposition :

| Type enfant | Parents proposés | Relation |
|---|---|---|
| Emplacement | Emplacement (niveau au-dessus) | `CONTAINS` |
| Datacenter | Emplacement | `CONTAINS` |
| Serveur, Équipement réseau, Stockage | Emplacement, Datacenter | `CONTAINS` |
| Hyperviseur | Emplacement, Datacenter | `CONTAINS` |
| Machine virtuelle | Hyperviseur *(défaut)*, Datacenter, Emplacement | `RUNS` (sous hyperviseur) sinon `CONTAINS` |
| Plateforme de conteneurs | Serveur, Hyperviseur, Datacenter | `RUNS` |
| Conteneur | Stack *(défaut)*, Plateforme de conteneurs | `CONTAINS` / `RUNS` |
| Stack | Plateforme de conteneurs, Serveur | `RUNS` |
| Base de données, Application, Domaine, Ressource cloud | *(aucun parent par défaut)* — rattachables à un Serveur/Datacenter si l'utilisateur le souhaite | `CONTAINS` |

« Aucun parent par défaut » ≠ interdit : le champ reste proposé mais vide par défaut, car ces
ressources s'expriment surtout par des relations `USES`.

### Changements applicatifs

Reprendre la spec précédente (champ `UpdateParent`, application du parent en création **et** en
édition, garde-fous unicité + cycle), et :

- Remplacer `GetAttachableParentsAsync(parentTypeKey)` par
  **`GetEligibleParentsAsync(string childTypeKey)`** : renvoie, triés par type puis par nom, tous
  les parents éligibles selon la matrice. Le `ComboBox` les groupe par type (« Emplacements »,
  « Datacenters », « Hyperviseurs »…), avec l'entrée vide « — Aucun — » en tête.
- Étendre `DefaultFor` en `DefaultFor(parentTypeKey, childTypeKey)`.

### Cas particulier : stacks (Docker / Kubernetes) et conteneurs

Entièrement couvert par le modèle existant — à exposer, pas à créer :

- Le type **« Stack »** (`AssetTypeKeys.Stack`) existe, avec un champ **`orchestrator`** dont les
  options couvrent déjà les deux mondes : *Docker Compose* et *Docker Swarm* (stack Docker), *Helm*
  et *Kustomize* (stack Kubernetes), *Portainer*, *Autre*. **Pas besoin de types distincts
  « docker stack » / « k8s stack »** : c'est l'orchestration qui les différencie.
- Le type **« Conteneur »** (`AssetTypeKeys.Container`) existe (image, tag, ports publiés/internes,
  réseaux, volumes, politique de redémarrage).
- La relation **Stack `CONTAINS` Conteneur** est déjà le défaut (`DefaultFor(Stack)` → `CONTAINS`).

Chaînes typiques, qui s'inscrivent dans l'arbre unique :

```
Serveur ⊃ Plateforme de conteneurs (Docker)     ⊃ Stack (Compose)      ⊃ Conteneur
Serveur ⊃ Plateforme de conteneurs (Kubernetes)  ⊃ Stack (Helm release) ⊃ Conteneur (pod/service)
```

La plateforme de conteneurs est facultative : un stack peut être rattaché directement à un serveur
si l'utilisateur ne modélise pas la plateforme.

**Ce qu'il faut ajouter :**

1. Dans le sélecteur de parent généralisé, pour un **Conteneur**, proposer en priorité les **Stacks**
   (puis les plateformes de conteneurs). C'est déjà dans la matrice ci-dessus ; vérifier simplement
   que `CONTAINS` est bien la relation déduite pour (Stack, Conteneur).
2. Depuis la **fiche d'un Stack**, une action de confort **« Ajouter un conteneur »** qui ouvre la
   création d'un Conteneur avec le parent **pré-rempli sur ce stack** (réutilise `ParentId` +
   `UpdateParent` de la spec précédente). Évite de créer le conteneur puis de le rattacher en deux
   temps.
3. Symétriquement, une action **« Ajouter un stack »** depuis une plateforme de conteneurs ou un
   serveur, parent pré-rempli.
4. Sur la fiche d'un Stack, **lister ses conteneurs** (ses enfants `CONTAINS`) — c'est la lecture
   réciproque de l'arbre, déjà disponible via les relations.

Rien de cryptographique ni de schéma à changer : uniquement du câblage UI au-dessus de la hiérarchie
générale.

## 4. « Retrouver tout ce qui est dans un bâtiment »

L'arbre unifie déjà toutes les relations hiérarchiques (`GetTreeAsync`). Une fois les emplacements
en place, se placer sur un Bâtiment et déplier donne déjà ses descendants. À ajouter pour que ce
soit direct :

- **Emplacement effectif (hérité).** Pour chaque ressource, calculer le plus proche ancêtre de type
  Emplacement/Datacenter en remontant l'arbre. Une VM affiche alors le bâtiment de l'hyperviseur sur
  lequel elle tourne, sans qu'on l'ait saisi deux fois. `AssetDetailDto.Path` fournit déjà la chaîne
  d'ancêtres — en dériver l'emplacement effectif.
- **Filtre « Emplacement » dans la liste des ressources.** Choisir un emplacement liste **tous ses
  descendants** (serveurs, routeurs, VM…), via l'emplacement effectif. C'est la réponse directe à
  « quand je vais sur le bâtiment, je veux retrouver toutes les ressources qui sont là ».
- **Vue arbre focalisée.** `TopologyService.BuildAsync(scopeRootId)` accepte déjà une racine : la
  réutiliser pour n'afficher que le sous-arbre d'un emplacement.

## 5. Interface

- Généraliser le sélecteur « Rattaché à (facultatif) » de la spec précédente à tous les types, peuplé
  par `GetEligibleParentsAsync(type)` et groupé par type.
- Dans la boîte de dialogue, pour une **VM**, pré-orienter le sélecteur vers les hyperviseurs ; pour
  un **serveur/routeur**, vers emplacements et data centers.
- Ajouter le **filtre par emplacement** dans la liste des ressources, et rendre le fil d'Ariane
  (`Path`) cliquable pour remonter l'arbre.
- Permettre de **créer un emplacement à la volée** depuis le sélecteur (« + Nouvel emplacement »),
  pour ne pas obliger à sortir du formulaire.

## Idées complémentaires (optionnelles, à arbitrer)

- **Type « Cluster » (plus tard).** Si des clusters Proxmox/Kubernetes HA doivent exister comme
  entités, un type « Cluster » qui `CONTAINS` des hyperviseurs/nœuds. Inutile pour le cas singleton ;
  le champ texte `cluster` de l'hyperviseur suffit à noter l'appartenance d'ici là.
- **Compteurs sur les conteneurs logiques.** Sur un emplacement, un data center ou un hyperviseur :
  afficher le nombre de ressources contenues, et pour un hyperviseur la somme des vCPU/RAM des VM —
  un petit tableau de capacité. À faire après la hiérarchie.
- **Carte / coordonnées.** Les coordonnées GPS d'un Emplacement permettraient une vue carte des sites
  — simple bonus, non prioritaire.
- **Déplacement de sous-arbre.** Re-rattacher un emplacement ou un data center déplace tout son
  contenu (les relations pointent sur les nœuds, donc c'est automatique) — juste à vérifier par un
  test.

## Récapitulatif des changements

| Couche | Fichier | Changement |
|---|---|---|
| Constantes | `Shared/Constants/AppConstants.cs` | `AssetTypeKeys.Location` ; `RelationshipTypes.DefaultFor(parent, child)` |
| Domaine | `Domain/ValueObjects/SystemAssetTypes.cs` | définition du type « Emplacement » (champ `kind`, adresse, GPS…) |
| Application | `Services/InfrastructureService.cs` | `GetEligibleParentsAsync(childType)` selon la matrice ; emplacement effectif ; filtre par emplacement ; parent éditable (via la spec précédente) |
| Présentation | `AssetViews.xaml` + ViewModels | sélecteur parent généralisé et groupé ; filtre par emplacement ; fil d'Ariane cliquable ; création d'emplacement à la volée |
| Tests | `tests/InfraVault.Integration.Tests` | voir ci-dessous |

## Critères d'acceptation (tests à ajouter)

En plus de ceux de la spec précédente (je peux les écrire une fois le code posé) :

1. Un coffre existant voit apparaître le type « Emplacement » après déverrouillage (re-seed).
2. Emplacement `CONTAINS` Emplacement (Bâtiment contient Salle) — imbrication valide.
3. Rattacher un serveur à un bâtiment, un routeur au même bâtiment → les deux ressortent quand on
   liste le contenu du bâtiment.
4. Une VM rattachée à un hyperviseur a l'emplacement effectif = celui de l'hyperviseur (hérité).
5. Rattacher une VM à un hyperviseur crée une relation `RUNS`, pas `HOSTS`.
6. Le sélecteur de parent d'une VM propose les hyperviseurs ; celui d'un serveur propose
   emplacements et data centers.
7. Déplacer un bâtiment sous une autre localité emporte tout son contenu dans l'arbre.
8. Pas de cycle, un seul parent hiérarchique (rappel de la spec précédente).
9. Créer un Stack (orchestration Docker Compose) puis lui ajouter des conteneurs → relation
   `CONTAINS` stack → conteneur ; les conteneurs apparaissent sous le stack dans l'arbre.
10. Créer un Stack (orchestration Helm) sous une plateforme Kubernetes, lui ajouter des conteneurs →
    chaîne Plateforme `RUNS` Stack `CONTAINS` Conteneur.
11. L'action « Ajouter un conteneur » depuis la fiche d'un stack pré-remplit le parent sur ce stack.
