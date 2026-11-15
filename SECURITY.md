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

À lire avant de déployer InfraVault sur quoi que ce soit d'important :

- **Le code n'a fait l'objet d'aucun audit de sécurité externe.** La conception cryptographique est
  documentée et couverte par des tests, mais personne d'indépendant ne l'a revue. Si vous avez les
  compétences pour le faire, c'est la contribution la plus utile que vous puissiez apporter.
- **La DEK vit dans le tas managé** pendant que le coffre est ouvert. Elle est effacée au
  verrouillage, mais .NET ne garantit pas qu'aucune copie ne subsiste après un déplacement par le
  ramasse-miettes.
- **Pas de migrations de schéma.** Un correctif qui imposerait un changement de schéma ne pourra
  pas être appliqué à un coffre existant sans qu'un mécanisme de migration soit écrit d'abord.
- **Pas de mode équipe.** Poser `vault.db` sur un partage réseau n'est pas une façon supportée de
  partager un coffre et n'offre aucune des garanties décrites ici.

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
