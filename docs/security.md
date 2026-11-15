# Sécurité

Ce document décrit la conception cryptographique telle qu'elle est implémentée. Chaque propriété
annoncée ici correspond à un test dans `tests/InfraVault.Security.Tests` ou
`tests/InfraVault.Integration.Tests`.

## Modèle de menace

Ce qu'InfraVault protège :

- **Vol du fichier de coffre.** Quelqu'un obtient `vault.db` ou une sauvegarde `.ivbak`. Sans le
  Master Password ou la clé de récupération, il n'en tire rien.
- **Altération du fichier.** Quelqu'un modifie un octet dans la base. L'altération est détectée, et
  le coffre refuse de s'ouvrir plutôt que de rendre des données fausses.
- **Déplacement de chiffrés.** Quelqu'un recopie le secret chiffré de la fiche A vers la fiche B
  pour s'octroyer un accès. Le chiffré devient illisible.
- **Devinette du mot de passe.** Les tentatives répétées sont ralenties et journalisées.

Ce qu'InfraVault ne protège **pas** :

- **Un poste compromis.** Un keylogger ou un malware ayant accès à la mémoire du processus pendant
  que le coffre est déverrouillé voit les secrets. La durée de session limitée et le verrouillage
  automatique réduisent la fenêtre, ils ne la ferment pas.
- **Un utilisateur qui exporte ses secrets** vers un fichier non protégé.
- **L'oubli simultané du Master Password et de la clé de récupération.** Il n'y a pas de porte
  dérobée. C'est voulu.

## Chiffrement par enveloppe

Le Master Password ne chiffre jamais les données directement.

```
Master Password ──Argon2id(sel, 64 Mio, 3 passes, 4 voies)──► KEK
                                                               │
                                               AES-256-GCM ────┤
                                                               ▼
Clé de récupération ──HKDF-SHA256──► KEK(récup) ──────────► DEK (256 bits, aléatoire)
                                                               │
                                                               ▼
                                            secrets, notes, documents, pièces jointes
```

- La **DEK** est tirée du CSPRNG à la création du coffre et ne change jamais.
- La **KEK** est dérivée du Master Password par Argon2id, avec un sel aléatoire de 128 bits.
- La DEK est stockée deux fois, chiffrée sous deux KEK différentes : celle du mot de passe et celle
  de la clé de récupération.

**Conséquence pratique :** changer le Master Password ne rechiffre aucun secret. Seule l'enveloppe
est refaite. L'opération est instantanée quelle que soit la taille du coffre, et ne peut pas échouer
à mi-parcours en laissant des données inaccessibles.

### Paramètres Argon2id

Par défaut : **64 Mio de mémoire, 3 passes, 4 voies**, sortie de 256 bits — au-delà du minimum
recommandé par l'OWASP pour Argon2id.

Ces paramètres sont stockés avec le coffre, en JSON, pour pouvoir être durcis plus tard sans casser
les coffres existants. Ils sont **bornés à la relecture** (`KdfParameters.EnsureValid`) : entre
8 Mio et 1 Gio, 1 à 64 passes, 1 à 16 voies, sortie de 32 octets exactement. Sans cette validation,
un fichier de coffre altéré pourrait réclamer plusieurs gigaoctets de RAM et transformer une
ouverture en déni de service.

## Format « boîte scellée »

Tout chiffré stocké en base suit ce format :

```
┌─────────┬───────────────┬──────────────┬──────────────────┐
│ version │ nonce (12 o.) │  tag (16 o.) │ chiffré (n o.)   │
│  1 o.   │   96 bits     │   128 bits   │                  │
└─────────┴───────────────┴──────────────┴──────────────────┘
```

AES-256-GCM. Le **nonce est tiré du CSPRNG à chaque chiffrement** — jamais de compteur, jamais de
réutilisation. Deux fiches portant le même mot de passe produisent deux chiffrés différents : la
base ne trahit pas les doublons.

L'octet de version permettra d'introduire un format v2 sans que la v1 tente de l'interpréter : une
version inconnue est refusée.

**Toute altération est détectée.** Un test retourne chaque octet de la boîte, un par un — version,
nonce, tag, chiffré — et exige une `IntegrityException` à chaque fois. Une boîte tronquée, une
mauvaise clé, un tag emprunté à un autre message : refus net. Il n'existe aucun chemin qui rende
des données déchiffrées fausses.

## Données associées (AAD)

Chaque chiffré est lié à son emplacement logique par des données associées authentifiées :

```
credential:<id>:secret        asset:<id>:property:<clé>
credential:<id>:username      documentation:<id>:content
credential:<id>:notes         attachment:<id>:content
```

L'AAD n'est pas chiffrée, elle est authentifiée : si elle ne correspond pas, le déchiffrement
échoue.

C'est ce qui rend inopérante l'attaque la plus simple contre un coffre local : ouvrir la base avec
un éditeur SQLite et recopier le secret chiffré d'une fiche vers une autre. Le chiffré déplacé
devient illisible, et le déplacement est donc détecté au lieu de donner accès.

## Index aveugle

Le nom d'utilisateur d'un identifiant est chiffré, donc non interrogeable. Pour permettre malgré
tout la recherche exacte, la base stocke `username_index` : le HMAC du username normalisé, sous une
clé dérivée de la DEK.

Deux fiches partageant le compte `root` ont le même index aveugle, ce qui permet de les regrouper.
Mais l'index ne permet pas de remonter à la valeur, et **deux coffres différents produisent des
index différents pour la même valeur** — comparer deux bases volées ne révèle pas qu'elles
contiennent les mêmes comptes.

## Clé de récupération

160 bits aléatoires, présentés en Base32 par groupes de 4 : `K7QF-2M4X-…`, 32 symboles.

L'alphabet exclut `0`, `1` et `8`, qui se confondent à la lecture avec `O`, `I` et `B` — et la
saisie les retraduit automatiquement, de même qu'elle ignore la casse, les tirets et les espaces.
Une clé recopiée à la main sur un papier doit fonctionner.

Elle est affichée **une seule fois**, à la création du coffre. Elle n'est stockée nulle part : la
base ne contient que la DEK chiffrée sous la clé dérivée de cette clé de récupération.

La récupération impose un nouveau Master Password et **remplace immédiatement la clé de
récupération utilisée**. Une clé déjà servie ne resservira pas.

## Anti force brute

Le délai est exponentiel, à partir du troisième échec :

| Échecs | 0–2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 et + |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Délai | aucun | 1 s | 2 s | 4 s | 8 s | 16 s | 32 s | **60 s** |

Les deux premières erreurs ne coûtent rien — se tromper de mot de passe est normal. Le plafond de
60 secondes est tout aussi délibéré : sans lui, `2^50` secondes fermeraient le coffre pour toujours
après une série de fautes de frappe.

Le compteur est persisté dans `settings` et **effacé à chaque ouverture réussie**. Chaque échec est
journalisé.

> Cette temporisation ralentit une attaque menée *à travers l'application*. Elle ne protège pas
> contre une attaque hors ligne sur le fichier volé — c'est le rôle d'Argon2id.

## Politique de Master Password

12 caractères minimum, et un score de robustesse d'au moins 2 sur 4. L'estimateur pénalise les mots
courants (français et anglais), les suites de clavier (`azerty`, `qwerty`, `qsdfgh`) et la
répétition de caractères.

Conséquence : `motdepasse12` fait bien 12 caractères et est refusé.

## Journal d'audit scellé

Chaque événement engage le précédent par une chaîne de hachage SHA-256 :

```
hash(n) = SHA256( hash(n-1) || canonical(n) )
```

`canonical` inclut le numéro de séquence, les identifiants, le type d'événement, l'horodatage, le
résultat et les métadonnées. La tête de chaîne est scellée par un HMAC sous une clé dérivée de la
DEK, stocké dans `vaults`.

Modifier une ligne ou en supprimer une casse la chaîne, et `AuditService.VerifyAsync` renvoie le
numéro de séquence à partir duquel elle est rompue. Les deux cas sont couverts par des tests.

**Aucun secret ne figure dans le journal.** Les métadonnées se limitent à un libellé et un détail
court, assainis et tronqués à 160 caractères. Il n'y a pas d'API permettant d'y écrire une valeur
libre.

## Verrouillage

`VaultSession` est le seul composant qui détient la DEK en clair. La clé n'est jamais rendue sous
forme de tableau conservable : `UseKey(...)` la passe à un délégué le temps d'une opération.

`Lock()` efface la clé de la mémoire (`CryptographicOperations.ZeroMemory`). Toute tentative de
déchiffrement après verrouillage lève `VaultLockedException`. Le verrouillage est déclenché
manuellement (Ctrl+L), par inactivité, et sur les événements système — verrouillage de session
Windows, mise en veille — selon les paramètres du coffre.

L'événement de verrouillage est écrit dans l'audit **avant** l'effacement de la clé, puisque le
scellé du journal a besoin de cette clé.

## Ce qui n'est pas en place

- **Pas de DPAPI**, pas de Windows Hello.
- **Protection mémoire partielle.** La DEK et ses sous-clés sont dans des tampons épinglés, effacés
  au verrouillage ; mais la DEK et la KEK transitent par des tableaux ordinaires pendant le
  déverrouillage et la rotation, et les secrets affichés existent sous forme de `string` .NET,
  impossibles à effacer. Le détail est dans [SECURITY.md](../SECURITY.md#mémoire).
- **Pas de signature des binaires**, pas d'installeur.
- **Pas de mode équipe.** Le partage d'un coffre entre plusieurs postes demande une conception
  cryptographique dédiée et ne doit pas être improvisé en posant la base SQLite sur un partage
  réseau.

## Vérification

```bash
dotnet test tests/InfraVault.Security.Tests/InfraVault.Security.Tests.csproj
dotnet test tests/InfraVault.Integration.Tests/InfraVault.Integration.Tests.csproj
```

Les tests d'intégration plantent des témoins reconnaissables dans le mot de passe, le nom
d'utilisateur, les notes et le contenu d'un document, puis relisent les **octets bruts** du fichier
SQLite — WAL rapatrié — pour vérifier qu'aucun n'y figure. Le même test vérifie que le nom de la
ressource et le hostname, eux, *sont* bien trouvables en clair : sans cette contre-épreuve, le test
passerait aussi si la recherche elle-même était cassée.
