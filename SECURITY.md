# Politique de sécurité

InfraVault conserve des mots de passe et des accès à de l'infrastructure. Une faille ici ne gâche
pas une fonctionnalité : elle expose les machines de quelqu'un. Les signalements sont pris au
sérieux et traités en priorité.

## Signaler une vulnérabilité

**N'ouvrez pas d'issue publique.** Une issue est visible de tous, y compris de ceux qui voudraient
exploiter la faille avant qu'un correctif n'existe.

Deux canaux, par ordre de préférence :

1. **GitHub — signalement privé.** Onglet *Security* du dépôt → *Report a vulnerability*. C'est le
   canal recommandé : l'échange reste privé jusqu'à publication, et l'historique est conservé.
2. **Par courriel** — `jacquesdarga@fasocivic.org`.

Si vous le souhaitez, chiffrez votre message ; indiquez-le et une clé publique vous sera fournie.

### Ce qui aide

Un bon signalement contient :

- la version ou le commit concerné ;
- ce que l'attaquant doit déjà posséder (le fichier `vault.db` ? un accès au poste ? rien ?) ;
- les étapes pour reproduire, aussi précises que possible ;
- l'impact tel que vous le voyez ;
- un correctif proposé, si vous en avez un — ce n'est pas attendu.

Un rapport imparfait vaut mieux qu'un rapport jamais envoyé. Si vous hésitez sur la gravité,
envoyez-le quand même.

### Ce à quoi vous pouvez vous attendre

Ce projet est maintenu par une équipe très réduite, sans astreinte. Plutôt que d'annoncer des
délais contractuels intenables, voici l'engagement réel :

- **accusé de réception** dès que possible, et au plus tard sous une semaine ;
- **évaluation** et retour sur la gravité retenue, avec les raisons ;
- **correctif** priorisé selon l'impact, les failles cryptographiques et les fuites de secrets
  passant devant tout le reste ;
- **crédit** à votre nom dans les notes de version, sauf si vous préférez rester anonyme.

Si vous n'avez pas de réponse sous une semaine, relancez — un message a pu se perdre.

## Périmètre

### Dans le périmètre

- Récupération d'un secret sans le Master Password ni la clé de récupération.
- Secret, nom d'utilisateur, note, contenu de document ou pièce jointe écrit en clair sur le
  disque — base, index de recherche, journal d'audit, sauvegarde, fichier temporaire ou journal
  applicatif.
- Altération d'un chiffré non détectée, ou déchiffrement rendant des données fausses au lieu
  d'échouer.
- Déplacement d'un chiffré d'une fiche vers une autre donnant accès à un secret.
- Contournement du verrouillage, ou secret encore lisible après `Lock()`.
- Falsification du journal d'audit sans que `VerifyAsync` la détecte.
- Contournement de la temporisation anti force brute.
- Affaiblissement de la dérivation de clé par un fichier de coffre fabriqué.
- Déni de service déclenché par un fichier de coffre ou une sauvegarde fabriqués.

### Hors périmètre

Ces points sont des limites assumées du modèle de menace, documentées dans
[docs/security.md](docs/security.md). Les signaler ne donnera pas lieu à un correctif :

- **Poste déjà compromis.** Un malware ou un keylogger ayant accès à la mémoire du processus
  pendant que le coffre est déverrouillé voit les secrets. Le verrouillage automatique réduit la
  fenêtre, il ne la ferme pas.
- **Attaque physique sur la mémoire** (cold boot, vidage mémoire par un administrateur local).
- **Utilisateur exportant volontairement ses secrets** vers un fichier non protégé.
- **Perte simultanée du Master Password et de la clé de récupération.** Il n'y a pas de porte
  dérobée, et il n'en sera pas ajouté.
- **Mot de passe maître faible** choisi malgré la politique.
- Absence de DPAPI, de Windows Hello, de signature des binaires : ce sont des fonctionnalités
  manquantes, pas des vulnérabilités. Ouvrez une issue normale.

## Limites connues

À lire avant de confier à InfraVault les accès d'une infrastructure de production. Ces limites sont
vérifiables dans le code ; elles sont listées ici pour que personne n'ait à les découvrir.

### Le vol du fichier révèle l'inventaire, pas les secrets

Sont **chiffrés** : nom d'utilisateur, secret et notes d'un identifiant, propriétés marquées
sensibles, contenu des documents, pièces jointes (nom et contenu).

Restent **en clair** dans `vault.db` : noms de ressources, hostnames, adresses IP, adresse publique
et ports exposés, environnement, tags, URL et port des identifiants, titres, types et tags des
documents, propriétés non sensibles, et l'index de recherche.

C'est un compromis assumé — ce sont les clés de recherche — mais il a une conséquence : quelqu'un
qui vole `vault.db` obtient **la cartographie de votre infrastructure** sans connaître aucun mot de
passe. Si la topologie elle-même est sensible, traitez le fichier comme confidentiel.

### Mémoire

La DEK et ses deux sous-clés sont détenues dans des tampons **épinglés**
(`GC.AllocateArray(pinned: true)`) que le ramasse-miettes ne déplace pas, et remis à zéro au
verrouillage. Les limites sont ailleurs :

- Pendant le déverrouillage, la création, la rotation du mot de passe et la restauration, la DEK et
  la KEK transitent par des tableaux ordinaires, non épinglés. Ils sont effacés ensuite, mais le
  ramasse-miettes a pu en laisser une copie entre-temps.
- Le mot de passe maître, les secrets affichés ou édités et le contenu des documents existent sous
  forme de `string` .NET : immuables, donc impossibles à effacer. Ils subsistent en mémoire jusqu'à
  leur collecte.
- **Rien ne protège contre un processus malveillant tournant sous le même compte Windows** :
  lecture de la mémoire du processus, enregistreur de frappe.

### Journal d'audit

La chaîne de hachage détecte toute modification ou suppression intermédiaire. Le scellé HMAC, lui,
n'est posé que **coffre ouvert** :

- Les événements écrits coffre verrouillé — typiquement les échecs d'authentification — sont
  chaînés mais non scellés jusqu'au déverrouillage suivant.
- La suppression des **derniers** événements, postérieurs au dernier scellé, n'est pas détectable :
  la chaîne restante reste cohérente.
- Un attaquant qui détient la DEK peut recalculer la chaîne **et** le scellé, donc réécrire
  l'historique intégralement. Le journal protège contre la falsification par un tiers, pas contre
  celui qui a déjà la clé.

### Temporisation anti force brute

Le compteur d'échecs est stocké dans `vault.db` (`auth.failures`). Il ralentit les tentatives faites
**à travers l'application**. Il n'offre aucune protection contre une attaque hors ligne sur une copie
du fichier : celle-ci n'est freinée que par Argon2id (64 Mio, 3 passes, 4 voies) et par la force du
mot de passe maître.

### Index aveugle

Le HMAC du nom d'utilisateur est **déterministe**. Sans la clé, on ne remonte pas à la valeur, mais
deux identifiants partageant le même nom d'utilisateur portent le même index, visible dans le
fichier. Un observateur apprend donc que plusieurs fiches utilisent le même compte, sans savoir
lequel.

### Sauvegardes

L'en-tête d'un fichier `.ivbak` est authentifié mais **en clair** : nom du coffre, date de création,
nombre de ressources, d'identifiants et de documents, et les enveloppes de la DEK.

Surtout : une sauvegarde se déchiffre avec le mot de passe maître **en vigueur au moment où elle a
été créée**. Changer le mot de passe maître ne protège pas les sauvegardes antérieures. Si vous
changez de mot de passe parce que l'ancien a fuité, détruisez aussi les anciennes sauvegardes.

### Presse-papiers

Lors d'une copie, l'application demande au système d'exclure la valeur de l'historique du
presse-papiers et du presse-papiers cloud, et l'efface après le délai configuré. **Ce sont des
demandes**, pas des garanties : un gestionnaire de presse-papiers tiers peut les ignorer et
conserver la valeur.

### Autres

- **Aucun audit de sécurité externe.** La conception est documentée et couverte par des tests, mais
  personne d'indépendant ne l'a revue. Si vous avez les compétences, c'est la contribution la plus
  utile que vous puissiez apporter.
- **Argon2id provient du paquet tiers `Konscious.Security.Cryptography.Argon2`** (implémentation
  managée). C'est une dépendance cryptographique externe, et un point qu'un relecteur voudra
  examiner.
- **Pas de migrations de schéma.** Un correctif imposant un changement de schéma ne pourra pas être
  appliqué à un coffre existant tant qu'un mécanisme de migration n'aura pas été écrit.
- **Pas de mode équipe.** Poser `vault.db` sur un partage réseau n'est pas supporté et n'offre
  aucune des garanties décrites ici.

## Versions supportées

Il n'existe pas encore de politique de versions publiées. Les correctifs sont appliqués sur la
branche `main`. Si vous utilisez une version construite vous-même, indiquez le commit dans votre
signalement.

## Pour les contributeurs

Quelques invariants que toute modification doit préserver. Ils sont couverts par des tests, et une
PR qui les casse ne sera pas fusionnée :

- Aucun clair n'entre dans l'index de recherche ni dans le journal d'audit.
- Chaque chiffré est lié à son emplacement par des données associées (`Aad`).
- Les bornes de `KdfParameters.EnsureValid` ne sont pas relâchées.
- Le nonce AES-GCM vient du CSPRNG, jamais d'un compteur.
- Toute altération d'un chiffré lève `IntegrityException` — jamais de données erronées renvoyées
  silencieusement.

Voir [CONTRIBUTING.md](CONTRIBUTING.md).
