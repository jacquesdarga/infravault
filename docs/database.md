# Base de données

Un coffre = un fichier SQLite, `vault.db`, dans le dossier de données. Rien d'autre n'est requis :
pas de serveur, pas de service, pas de compte.

## Création et intégrité

Le schéma est créé par EF Core (`EnsureCreated`) au premier démarrage, puis `VaultDbFactory.InitializeAsync`
applique :

```sql
PRAGMA journal_mode = WAL;      -- écritures atomiques, reprise propre après coupure
PRAGMA synchronous  = NORMAL;
PRAGMA quick_check;             -- refus de démarrer sur une base corrompue
```

Les clés étrangères sont actives (`ForeignKeys = true` dans la chaîne de connexion). Un
`quick_check` en échec lève une `IntegrityException` avec un message qui oriente vers la
restauration d'une sauvegarde.

> **Pas de migrations.** Le schéma porte la version `1` dans `settings` (`schema.version`). Il n'y a
> aucun mécanisme de migration : faire évoluer le schéma d'une version installée demandera d'en
> écrire un.

## Conventions

Les noms de tables et de colonnes sont en `snake_case`, convertis automatiquement depuis les noms
C# (`AssetTypeId` → `asset_type_id`, `EncryptedDek` → `encrypted_dek`). Les identifiants sont des
`GUID`. Les dates sont en UTC.

Une colonne dont le nom contient `encrypted` ne contient **jamais** de clair.

## Tables

### `vaults` — métadonnées cryptographiques

Une seule ligne. Ne contient ni mot de passe, ni clé utilisable telle quelle : uniquement deux
enveloppes de la même clé de données (DEK).

| Colonne | Rôle |
| --- | --- |
| `salt`, `kdf_algorithm`, `kdf_parameters` | Paramètres Argon2id de l'enveloppe « mot de passe » |
| `encrypted_dek`, `dek_nonce`, `dek_tag` | DEK chiffrée par la clé dérivée du Master Password |
| `recovery_salt`, `recovery_encrypted_dek`, `recovery_nonce`, `recovery_tag` | La même DEK, chiffrée par la clé dérivée de la clé de récupération |
| `audit_seal_sequence`, `audit_seal` | Scellé HMAC de la tête de la chaîne d'audit |
| `crypto_version` | Version du format cryptographique (1) |

Les deux enveloppes expliquent pourquoi changer le Master Password est instantané : seule
l'enveloppe est refaite, aucun secret n'est rechiffré.

### `assets` — l'inventaire

Table centrale. Les champs communs à tous les types sont des colonnes ; les champs propres à chaque
catégorie vivent dans `asset_properties`.

Colonnes notables : `name`, `hostname`, `ip_address`, `environment`, `status`, `tags`,
`internet_exposed`, `public_address`, `exposed_ports`.

Index sur `name`, `hostname`, `ip_address`, `asset_type_id` et `updated_at`.

`hostname` et `ip_address` sont **en clair** : ce sont les clés de recherche, et ce ne sont pas des
secrets. C'est un choix explicite, vérifié par un test.

### `asset_types` — les catégories

Les types système (serveur, hyperviseur, VM, stack, conteneur, base de données, équipement réseau,
domaine, ressource cloud…) sont créés avec le coffre et complétés au déverrouillage si une version
plus récente en apporte de nouveaux. `key` est unique. `field_schema` porte le schéma JSON des
champs spécifiques.

### `asset_properties` — les champs spécifiques

Une ligne par couple (ressource, clé). Unicité sur `(asset_id, property_key)`.

Une propriété marquée `is_sensitive` n'a pas de valeur en clair : seul `encrypted_value` est
renseigné.

### `asset_relationships` — le graphe

`source_asset_id`, `target_asset_id`, `relationship_type`, `ports`, `description`.

Deux garde-fous dans le schéma :

- unicité sur `(source, target, type)` — pas de relation en double ;
- contrainte `ck_relationship_not_self` — une ressource ne peut pas être reliée à elle-même.

Les suppressions sont `Restrict` sur les deux extrémités : une ressource encore reliée ne peut pas
disparaître silencieusement. L'interface demande confirmation en listant les dépendances.

Les relations hiérarchiques (`CONTAINS`, `HOSTS`, `RUNS`) construisent l'arborescence ; les autres
(`USES`, `DEPENDS_ON`, `CONNECTS_TO`…) sont des flux.

### `credentials` et `credential_versions`

| En clair | Chiffré |
| --- | --- |
| `name`, `credential_type`, `url`, `port`, `tags`, `expires_at` | `username_encrypted`, `encrypted_secret`, `notes_encrypted` |

`username_index` est un **index aveugle** : le HMAC du username normalisé, sous une clé dérivée de
la DEK. Il permet de retrouver « tous les comptes `root` » sans jamais stocker `root` en clair, et
sans pouvoir remonter du HMAC vers la valeur.

Chaque modification d'un secret crée une ligne dans `credential_versions`. Les anciennes versions
restent chiffrées et restent consultables.

`credential_asset_links` associe un identifiant à N ressources (un compte de service partagé est le
cas normal, pas l'exception).

### `documentations`, `documentation_versions`, `attachments`

Le `title`, le `document_type` et les `tags` sont en clair — ils sont indexables. Le contenu
(`content_encrypted`) et les pièces jointes (nom **et** contenu) sont chiffrés.

### `audit_events` — le journal scellé

| Colonne | Rôle |
| --- | --- |
| `sequence` | Numéro d'ordre, unique |
| `event_type`, `entity_type`, `entity_id`, `result` | Quoi, sur quoi, succès ou échec |
| `metadata` | JSON `{label, detail}`, assaini et tronqué à 160 caractères |
| `previous_hash`, `hash` | Chaîne de hachage SHA-256 |

Chaque événement engage le précédent : `hash = SHA256(previous_hash || canonical)`. Modifier ou
supprimer une ligne casse la chaîne à partir de ce point, et `AuditService.VerifyAsync` renvoie le
numéro de séquence où la rupture commence.

`metadata` ne peut contenir qu'un libellé et un détail court, tous deux passés par
`AuditMetadata.Sanitize` (caractères de contrôle remplacés, troncature à 160 caractères). Il n'existe
volontairement aucun moyen d'y placer une valeur libre volumineuse — donc aucun moyen d'y faire
fuiter un secret.

### `settings`

Clé/valeur. `vault.settings` (JSON des préférences du coffre), `schema.version`, `auth.failures`
(compteur anti force brute, effacé à chaque ouverture réussie), `backup.last`.

### `users`

Un utilisateur local, créé avec le coffre. Prépare le mode équipe, qui n'existe pas encore.

## Index de recherche

Deux tables créées à la demande, hors EF Core :

- `search_index` — table virtuelle FTS5 (`entity_type`, `entity_id`, `category`, `icon`, `color`
  non indexés ; `title`, `subtitle`, `body`, `tags` indexés), tokenizer `trigram`.
- `search_map` — association `entity_id` → `rowid`, pour que la réindexation d'une entité soit en
  temps constant.

Le tokenizer trigramme est ce qui permet de chercher `10.42.0` ou `web-01` au milieu d'une valeur,
là où un tokenizer par mots exigerait le terme entier. Si la build SQLite ne le fournit pas, le code
bascule sur `unicode61` et la recherche par préfixe, sans échouer.

**L'index ne reçoit jamais de secret ni de contenu déchiffré.** Un test le vérifie en relisant le
contenu de `search_index` après avoir rempli un coffre de témoins reconnaissables.

## Sauvegardes

`DatabaseFiles.SnapshotAsync` utilise `VACUUM INTO`, qui produit un instantané cohérent même pendant
l'utilisation. `BackupService` en fait un fichier `.ivbak` chiffré.

La restauration (`ReplaceDatabaseAsync`) libère le pool de connexions, réinitialise l'index de
recherche, met l'ancienne base de côté sous `vault.db.avant-restauration-<horodatage>` — une
restauration malheureuse reste réparable — puis copie le fichier restauré en place.
