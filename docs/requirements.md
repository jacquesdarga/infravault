# Critères d'acceptation

Correspondance entre les critères du cahier des charges (partie X, §37 à 39) et les tests qui les
vérifient. Les critères non couverts sont signalés comme tels, sans contournement.

## État

| Projet de tests | Tests | Ce qu'il couvre |
| --- | ---: | --- |
| `InfraVault.Domain.Tests` | 110 | Objets valeur : clé de récupération, ports, tags, paramètres KDF |
| `InfraVault.Application.Tests` | 183 | Génération et robustesse des mots de passe, détection de fuite, audit, validateurs, temporisation |
| `InfraVault.Infrastructure.Tests` | 77 | Index FTS5, schéma SQLite, fichiers, rapport .docx |
| `InfraVault.Security.Tests` | 77 | AES-GCM, Argon2id, AAD, cycle de vie du coffre |
| `InfraVault.Integration.Tests` | 39 | Critères §37-38 de bout en bout |
| **Total** | **486** | 0 échec |

```bash
dotnet test InfraVault.slnx
```

## §37 — Sécurité

| Critère | Couvert par |
| --- | --- |
| Aucun secret stocké en clair dans la base | `SecurityAcceptanceTests.Aucun_secret_en_clair_dans_la_base` — témoins recherchés dans les octets bruts du fichier, WAL inclus |
| Aucun Master Password stocké en clair | `VaultLifecycleSecurityTests.Le_master_password_n_apparait_nulle_part_dans_le_fichier` |
| Un mauvais Master Password ne permet pas d'ouvrir le coffre | `Un_mauvais_mot_de_passe_laisse_le_coffre_ferme` |
| Toute altération d'un ciphertext doit être détectée | `SealedBoxTests.Tous_les_octets_sont_couverts_par_le_controle_d_integrite` — chaque octet de la boîte retourné un par un ; plus les altérations d'enveloppe, de sel et de tag au niveau du coffre |
| Les sauvegardes sont protégées | `Aucun_secret_en_clair_dans_une_sauvegarde` |
| Les logs ne contiennent aucun secret | **Partiel** — voir ci-dessous |
| Le verrouillage rend les secrets inaccessibles | `Le_verrouillage_rend_les_secrets_inaccessibles` (session et bout en bout) |
| Le changement du Master Password conserve l'accès aux données | `Le_changement_de_master_password_conserve_l_acces_aux_secrets` |
| La récupération fonctionne uniquement avec le mécanisme prévu | `La_recuperation_redonne_acces_aux_secrets`, `Une_cle_de_recuperation_deja_utilisee_ne_resservira_pas`, `Une_cle_de_recuperation_inventee_est_refusee` |

### « Les logs ne contiennent aucun secret » — couverture partielle

Le **journal d'audit** est testé : `Aucun_secret_dans_le_journal_d_audit` parcourt tous les
événements et vérifie qu'aucun témoin n'apparaît dans les libellés ou les détails. C'est le risque
principal, puisque l'audit est consultable et exportable, et que `AuditMetadata` tronque et assainit
tout ce qui y entre.

Les **fichiers Serilog** (`logs/infravault-<date>.log`) ne sont pas couverts : ils sont écrits par
la couche Presentation, qui n'est pas instanciée dans les tests. Vérification manuelle recommandée
avant livraison.

## §38 — Fonctionnel

| Critère | Couvert par |
| --- | --- |
| Création d'une ressource | `FunctionalAcceptanceTests.Creation_d_une_ressource` |
| Modification d'une ressource | `Modification_d_une_ressource` |
| Association d'un identifiant à une ressource | `Association_d_un_identifiant_a_une_ressource` |
| Association de plusieurs ressources à un identifiant | `Association_de_plusieurs_ressources_a_un_identifiant` |
| Navigation dans l'arborescence | `Navigation_dans_l_arborescence` — datacenter → hyperviseur → VM |
| Recherche par adresse IP | `Recherche_par_adresse_IP`, `Recherche_par_fragment_d_adresse_IP` |
| Recherche par hostname | `Recherche_par_hostname` |
| Consultation de l'historique | `Consultation_de_l_historique` et les deux tests d'événements de sécurité |
| Export et restauration d'une sauvegarde | `Export_et_restauration_d_une_sauvegarde` — la fiche est supprimée puis ramenée par la sauvegarde, secret compris |
| Utilisation sans Internet | **Non couvert** — voir ci-dessous |

### « Utilisation sans Internet » — non couvert

Aucun test ne le vérifie. L'application ne contient aucun client HTTP et n'ouvre aucune connexion
sortante, mais écrire un test qui « prouve » l'absence de réseau reviendrait à vérifier qu'un
appel qui n'existe pas n'a pas lieu — cela passerait toujours, y compris si un appel était ajouté
par un paquet tiers.

La vérification honnête est une inspection au pare-feu ou un relevé de connexions sur un poste de
test. Elle n'a pas été faite.

## §39 — Qualité

| Critère | État |
| --- | --- |
| Tests automatisés réussis | **Oui** — 486 tests, 0 échec, exécutés en CI sur Windows et Linux |
| Pas d'exception non gérée | **Partiel** — les chemins testés remontent des exceptions typées et attendues ; aucun audit exhaustif de l'interface n'a été fait |
| Pas de blocage de l'interface pendant les opérations longues | **Non couvert par les tests** — Argon2id est exécuté via `Task.Run`, mais le comportement de l'interface n'est pas testé automatiquement |
| Gestion correcte des erreurs de base de données | **Oui** — base corrompue, tronquée, fichier qui n'est pas du SQLite, violations de clé étrangère et de contrainte |
| Documentation technique disponible | **Oui** — ce dossier `docs/` |
| Installation et désinstallation fiables | **Sans objet en l'état** — il n'y a pas d'installeur ; la livraison est un dossier portable, la désinstallation consiste à le supprimer |

## Performance

Le cahier des charges demande une recherche par IP ou hostname en moins de 500 ms sur
10 000 ressources.

`Recherche_sous_500_ms_sur_10000_ressources` insère réellement 10 000 ressources, les indexe, fait
une requête de préchauffage, puis mesure une recherche par hostname et une recherche par adresse IP.
Les deux doivent passer sous 500 ms.

> Ce test mesure une machine autant qu'un logiciel. S'il échoue en CI sans qu'un changement de code
> le justifie, vérifier la charge de l'agent avant de soupçonner une régression.

## Ce qui reste à faire

- Vérification manuelle des fichiers Serilog avant livraison.
- Relevé de connexions réseau sur un poste de test.
- Mécanisme de migration de schéma, si une version 2 doit lire des coffres de version 1.
- Tests d'interface (WPF), absents : la couche Presentation n'est couverte par aucun test
  automatisé.
