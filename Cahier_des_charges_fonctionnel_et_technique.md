# INFRAVAULT

## Cahier des charges fonctionnel et technique

**Version :** 1.0 – Spécification initiale  
**Type de produit :** Application Windows Desktop  
**Positionnement :** IT Infrastructure Management & Secure Password Vault  
**Architecture initiale :** Local-first, Offline-first  
**Évolution prévue :** Multi-utilisateur et synchronisation sécurisée

---

# PARTIE I — VISION ET OBJECTIFS

## 1. Présentation du projet

InfraVault est une application de bureau destinée aux administrateurs systèmes, réseaux, bases de données et professionnels de la cybersécurité.

Elle permet de centraliser, organiser, documenter et sécuriser les informations relatives aux infrastructures informatiques.

L'application combine trois domaines fonctionnels :

1. Infrastructure Asset Management : inventaire et organisation des ressources informatiques.
2. Password & Secrets Management : stockage sécurisé des identifiants et secrets.
3. IT Documentation Management : centralisation de la documentation et des interventions techniques.

L'application doit permettre de naviguer naturellement dans une infrastructure informatique, de retrouver rapidement les informations associées à une ressource et de gérer les secrets nécessaires à son administration.

## 2. Objectifs généraux

- Remplacer les fichiers TXT et documents non structurés contenant des informations sensibles.
- Centraliser les informations relatives aux serveurs et équipements.
- Organiser les infrastructures selon une représentation hiérarchique et relationnelle.
- Associer les identifiants aux ressources concernées.
- Sécuriser les mots de passe, clés SSH, tokens et autres secrets.
- Conserver un historique des modifications.
- Faciliter la recherche transversale dans l'ensemble de l'infrastructure.
- Permettre une évolution vers une utilisation collaborative.

## 3. Principes fondamentaux

### 3.1 Local-first

Les données doivent être disponibles localement, sans dépendance à un serveur distant.

### 3.2 Security by design

La sécurité doit être intégrée dès la conception et non ajoutée après le développement.

### 3.3 Offline-first

Toutes les fonctionnalités essentielles doivent fonctionner sans connexion Internet.

### 3.4 Extensibilité

L'architecture doit permettre d'ajouter de nouvelles catégories de ressources sans modifier profondément le système.

### 3.5 Separation of concerns

L'infrastructure, les secrets, la documentation et les mécanismes de sécurité doivent être séparés au niveau métier et architectural.

---

# PARTIE II — CAHIER DES CHARGES FONCTIONNEL

## 4. Module d'authentification et de gestion du coffre-fort

Ce module constitue le point d'entrée de l'application.

### 4.1 Création du coffre-fort

L'utilisateur doit pouvoir :

- Créer un nouveau coffre-fort.
- Définir un Master Password.
- Confirmer le Master Password.
- Générer une clé cryptographique principale.
- Définir une clé de récupération.
- Configurer les paramètres de verrouillage automatique.

Le Master Password ne doit jamais être enregistré en clair.

### 4.2 Déverrouillage

L'application doit permettre :

- La saisie du Master Password.
- La vérification du mot de passe.
- Le déverrouillage de la clé principale.
- Le déverrouillage du coffre.
- Le verrouillage manuel.
- Le verrouillage automatique après inactivité.

### 4.3 Gestion des sessions

Paramètres configurables :

- Délai d'inactivité avant verrouillage.
- Verrouillage à la fermeture de l'application.
- Verrouillage lors de la mise en veille Windows.
- Verrouillage manuel immédiat.
- Déverrouillage Windows Hello, éventuellement dans une version ultérieure.

Une session verrouillée ne doit plus permettre d'accéder aux secrets déchiffrés.

## 5. Module Infrastructure Manager

Ce module permet de créer et d'organiser les ressources informatiques.

### 5.1 Types de ressources

Le système doit prendre en charge les catégories suivantes :

| Catégorie | Exemples |
| --- | --- |
| Datacenter | Datacenter principal, site secondaire |
| Server | Serveur physique, serveur dédié |
| Hypervisor | Proxmox, VMware, Hyper-V |
| Virtual Machine | VM Linux, VM Windows |
| Container Platform | Docker, Kubernetes |
| Container | Conteneur applicatif |
| Network Device | Routeur, switch, firewall |
| Storage | NAS, SAN, stockage local |
| Database | PostgreSQL, MySQL, MongoDB |
| Application | GitLab, Keycloak, Laravel |
| Domain | Nom de domaine, sous-domaine |
| Cloud Resource | VM cloud, bucket, service cloud |
| Custom Resource | Ressource personnalisée |

### 5.2 Gestion des serveurs

Pour chaque serveur, l'utilisateur peut renseigner :

- Nom du serveur.
- Hostname.
- Adresse IPv4.
- Adresse IPv6.
- FQDN.
- Système d'exploitation.
- Version du système.
- Fabricant.
- Modèle.
- Numéro de série.
- CPU.
- RAM.
- Stockage.
- Emplacement physique.
- Environnement : Production, Staging, Development, Testing.
- Statut : Active, Maintenance, Retired, Unknown.
- Date de création.
- Date de dernière modification.
- Description.

### 5.3 Gestion des machines virtuelles

Chaque machine virtuelle doit pouvoir être associée à son hyperviseur.

Informations :

- Nom.
- VM ID.
- Adresse IP.
- MAC Address.
- Système d'exploitation.
- CPU.
- RAM.
- Disque.
- Hyperviseur parent.
- Datacenter parent.
- Statut.
- Description.

Une machine virtuelle doit pouvoir contenir ou référencer plusieurs ressources :

- Comptes SSH.
- Bases de données.
- Applications.
- Services.
- Conteneurs.
- Documentation.
- Secrets.

### 5.4 Gestion des équipements réseau

Informations :

- Nom.
- Type d'équipement.
- Constructeur.
- Modèle.
- Adresse IP de management.
- Adresse MAC.
- Version firmware.
- Interfaces réseau.
- VLAN associés.
- Emplacement.
- Informations d'administration.

### 5.5 Gestion des bases de données

Une base de données doit être associée à son moteur et à son serveur hôte.

Informations :

- Nom de la base.
- Type de moteur.
- Version.
- Adresse du serveur.
- Port.
- Nom de l'instance.
- Base de données hébergée sur.
- Applications utilisatrices.
- Comptes associés.
- Description.

Exemples de moteurs :

- PostgreSQL.
- MySQL.
- MariaDB.
- Microsoft SQL Server.
- MongoDB.
- Redis.

## 6. Module Password & Secrets Manager

C'est l'un des modules les plus sensibles du système.

### 6.1 Types de secrets

L'application doit gérer :

- Username / Password.
- Comptes SSH.
- Clés SSH privées.
- Certificats.
- API Tokens.
- Database Credentials.
- VPN Credentials.
- Comptes administrateurs.
- Identifiants d'applications.
- Secrets personnalisés.

### 6.2 Informations d'un identifiant

Chaque entrée doit contenir :

- Nom de l'identifiant.
- Type.
- Username.
- Password ou secret.
- URL ou endpoint.
- Port.
- Ressource associée.
- Description.
- Tags.
- Date de création.
- Date de dernière modification.
- Date d'expiration éventuelle.
- Historique des versions.

### 6.3 Opérations disponibles

- Créer un identifiant.
- Modifier un identifiant.
- Supprimer un identifiant.
- Afficher ou masquer un secret.
- Copier le username.
- Copier le password.
- Générer un nouveau password.
- Définir la longueur du password.
- Définir les catégories de caractères.
- Consulter l'historique des modifications.
- Associer plusieurs ressources au même identifiant.

### 6.4 Gestion des changements de mots de passe

Chaque modification d'un secret doit pouvoir produire une nouvelle version.

Exemple :

| Version | Date | Description |
| --- | --- | --- |
| 1 | 01/09/2026 | Création initiale |
| 2 | 15/10/2026 | Rotation du mot de passe |
| 3 | 01/11/2026 | Modification après incident |

Les anciennes versions restent chiffrées.

L'utilisateur doit pouvoir restaurer une ancienne version après confirmation.

### 6.5 Générateur de mots de passe

Fonctionnalités :

- Longueur configurable.
- Lettres minuscules.
- Lettres majuscules.
- Chiffres.
- Caractères spéciaux.
- Exclusion de caractères ambigus.
- Génération cryptographiquement sécurisée.

Le générateur doit utiliser un générateur aléatoire cryptographique et non un générateur pseudo-aléatoire classique.

## 7. Module Documentation Manager

Chaque ressource doit pouvoir posséder sa documentation.

Fonctionnalités :

- Création de notes.
- Édition de notes.
- Catégorisation.
- Tags.
- Historique des modifications.
- Association avec plusieurs ressources.
- Ajout de procédures techniques.
- Ajout de commandes.
- Ajout de liens externes.
- Ajout de fichiers joints.

Exemples :

- Procédure d'installation de PostgreSQL.
- Configuration Nginx.
- Configuration ModSecurity.
- Procédure de restauration.
- Configuration d'un cluster Proxmox.
- Procédure de rotation des certificats TLS.

Les documents peuvent contenir des blocs de code avec coloration syntaxique.

Attention : les notes ne doivent pas devenir un moyen de contourner le gestionnaire de secrets. Les secrets doivent être enregistrés dans les champs sécurisés dédiés.

## 8. Module Asset Relationship Manager

Ce module permet de représenter les relations entre les différentes ressources.

Exemples :

- Un serveur physique héberge plusieurs VMs.
- Une VM appartient à un hyperviseur.
- Une application utilise une base de données.
- Une application est hébergée sur une VM.
- Un équipement réseau connecte plusieurs équipements.
- Un compte SSH permet d'accéder à un serveur.

Les relations doivent être navigables dans les deux sens.

L'utilisateur doit pouvoir consulter les dépendances d'une ressource et retrouver les ressources qui dépendent d'elle.

## 9. Module Search Engine

La recherche globale est une fonctionnalité prioritaire.

L'utilisateur doit pouvoir rechercher :

- Un hostname.
- Une adresse IP.
- Un nom de serveur.
- Un username.
- Un nom de base de données.
- Un nom d'application.
- Un nom de secret.
- Une documentation.
- Un tag.

Les résultats doivent être regroupés par catégorie.

La recherche des secrets doit porter sur leurs métadonnées, pas sur leur contenu déchiffré par défaut.

## 10. Module Audit & History

L'application doit enregistrer les événements importants :

- Création d'une ressource.
- Modification d'une ressource.
- Suppression d'une ressource.
- Création d'un identifiant.
- Modification d'un secret.
- Consultation d'un secret.
- Copie d'un secret.
- Export.
- Import.
- Déverrouillage.
- Verrouillage.
- Échec d'authentification.

Chaque événement contient :

- Identifiant de l'événement.
- Type d'action.
- Type de ressource.
- Identifiant de la ressource.
- Date et heure UTC.
- Utilisateur.
- Résultat de l'opération.

**Règle absolue :** aucun mot de passe, secret, clé privée ou contenu déchiffré ne doit apparaître dans les journaux.

Les événements de sécurité doivent être protégés contre les modifications silencieuses.

## 11. Module Backup & Restore

Fonctionnalités :

- Sauvegarde manuelle.
- Sauvegarde automatique configurable.
- Export chiffré.
- Restauration.
- Vérification de l'intégrité des sauvegardes.
- Versionnement des sauvegardes.
- Export sélectif des ressources.

Les sauvegardes doivent être chiffrées indépendamment de la simple protection du disque local.

Formats proposés :

- Backup complet : archive chiffrée.
- Export de ressources : JSON.
- Export de secrets : JSON chiffré uniquement.

Aucun export de secrets en clair ne doit être proposé par défaut.

## 12. Module Administration

Paramètres :

- Paramètres du coffre.
- Politique de verrouillage.
- Paramètres de sécurité.
- Paramètres de sauvegarde.
- Gestion des catégories.
- Gestion des tags.
- Gestion des environnements.
- Gestion des préférences d'affichage.
- Gestion des extensions futures.

---

# PARTIE III — EXIGENCES NON FONCTIONNELLES

## 13. Sécurité

- Chiffrement des secrets au repos.
- Dérivation de clé résistante aux attaques par dictionnaire.
- Protection des clés cryptographiques.
- Gestion sécurisée du cycle de vie des clés.
- Aucun stockage de mots de passe en clair.
- Aucun secret dans les logs.
- Protection contre les modifications de données.
- Verrouillage automatique.
- Protection des sauvegardes.
- Contrôle des accès en mode équipe.
- Tests de sécurité réguliers.

## 14. Performance

Objectifs indicatifs :

- Ouverture de l'application : moins de 3 secondes sur une machine compatible.
- Recherche simple : moins de 500 ms sur un inventaire de 10 000 ressources.
- Navigation dans l'arborescence : fluide.
- Déchiffrement d'un secret : uniquement à la demande.
- Interface non bloquante pendant les opérations longues.

Ces valeurs devront être validées par des tests de performance.

## 15. Disponibilité

- Fonctionnement sans Internet.
- Conservation locale des données.
- Pas de dépendance à un service cloud.
- Récupération après fermeture inattendue.
- Intégrité de la base SQLite.

## 16. Ergonomie

- Interface moderne.
- Thème clair et sombre.
- Navigation arborescente.
- Recherche globale.
- Actions rapides.
- Raccourcis clavier.
- Affichage conditionnel des secrets.
- Interface adaptée aux écrans Windows.

---

# PARTIE IV — ARCHITECTURE TECHNIQUE

## 17. Stack technologique

| Couche | Technologie |
| --- | --- |
| Langage | C# |
| Runtime | .NET 10 LTS |
| Interface | WPF |
| Pattern UI | MVVM |
| DI | Microsoft.Extensions.DependencyInjection |
| Database | SQLite |
| ORM | Entity Framework Core |
| Cryptography | .NET Cryptography APIs |
| KDF | Argon2id |
| Password hashing | Argon2id |
| Logging | Serilog, sans secrets |
| Validation | FluentValidation |
| Tests unitaires | xUnit |
| Tests d'intégration | xUnit |
| Packaging | MSIX ou MSI |

Les dépendances cryptographiques doivent être choisies après vérification de leur maintenance, de leur compatibilité Windows et de leur implémentation des standards.

## 18. Architecture en couches

### Presentation Layer

Responsable de l'interface utilisateur.

Composants :

- DashboardView.
- InfrastructureExplorerView.
- ServerDetailsView.
- VirtualMachineView.
- CredentialsView.
- DocumentationView.
- SearchView.
- SettingsView.

### Application Layer

Responsable des cas d'utilisation.

Services :

- AuthenticationService.
- VaultService.
- InfrastructureService.
- CredentialService.
- DocumentationService.
- SearchService.
- AuditService.
- BackupService.

### Domain Layer

Contient les entités et règles métier.

- Vault.
- Asset.
- Server.
- VirtualMachine.
- DatabaseInstance.
- NetworkDevice.
- ApplicationResource.
- Credential.
- CredentialVersion.
- Documentation.
- AssetRelationship.
- AuditEvent.

### Infrastructure Layer

Responsable des implémentations techniques.

- SQLiteRepository.
- EncryptionService.
- KeyDerivationService.
- WindowsSecurityService.
- BackupRepository.
- FileStorageService.

## 19. Architecture logique

```text
                  INFRAVAULT
                       |
          +------------+------------+
          |                         |
     PRESENTATION              APPLICATION
          |                         |
       WPF/MVVM             Use Cases / Services
          |                         |
          +------------+------------+
                       |
                    DOMAIN
                       |
          +------------+------------+
          |                         |
      INFRASTRUCTURE            SECURITY
          |                         |
        SQLite                 Argon2id
        EF Core                AES-256-GCM
        File Storage           Key Management
        Backup                 Windows DPAPI
          |                         |
          +------------+------------+
                       |
                LOCAL STORAGE
```

Les dépendances doivent pointer vers le domaine et les abstractions, jamais vers les implémentations concrètes depuis le cœur métier.

---

# PARTIE V — ARCHITECTURE CRYPTOGRAPHIQUE

## 20. Modèle de chiffrement

InfraVault doit utiliser une architecture de chiffrement par enveloppe (Envelope Encryption).

Trois éléments fondamentaux :

1. Master Password : secret connu par l'utilisateur.
2. Key Encryption Key (KEK) : clé dérivée du Master Password.
3. Data Encryption Key (DEK) : clé aléatoire utilisée pour chiffrer les données.

### 20.1 Création du coffre

1. Générer un salt cryptographiquement aléatoire.
2. Générer une DEK aléatoire de 256 bits.
3. Dériver une KEK depuis le Master Password avec Argon2id.
4. Chiffrer la DEK avec la KEK en AES-256-GCM.
5. Enregistrer le salt, les paramètres KDF, le nonce, la DEK chiffrée et son tag d'authentification.
6. Détruire autant que possible les copies temporaires des clés.

### 20.2 Déverrouillage

1. Demander le Master Password.
2. Lire le salt.
3. Recalculer la KEK.
4. Déchiffrer la DEK.
5. Vérifier l'authenticité.
6. Ouvrir la session du coffre.

Si la vérification échoue, le coffre reste verrouillé.

### 20.3 Chiffrement des données

Chaque secret doit être chiffré avec AES-256-GCM.

Chaque opération utilise un nonce unique de 96 bits pour une même clé.

Le nonce ne doit jamais être réutilisé avec la même clé.

Le tag d'authentification doit être conservé.

Les métadonnées nécessaires à la recherche peuvent rester non chiffrées, mais doivent être limitées au strict nécessaire.

Les contenus sensibles doivent être chiffrés.

### 20.4 Rotation du Master Password

Le changement du Master Password doit :

1. Vérifier le Master Password actuel.
2. Déverrouiller la DEK.
3. Générer un nouveau salt.
4. Dériver une nouvelle KEK.
5. Chiffrer la DEK avec cette nouvelle KEK.
6. Mettre à jour les métadonnées cryptographiques.

Les secrets n'ont pas besoin d'être rechiffrés individuellement.

### 20.5 Récupération

Le système doit permettre de générer une clé de récupération aléatoire.

Cette clé doit être protégée et conservée séparément du poste de travail.

Elle ne doit pas être envoyée automatiquement à un serveur externe.

La perte du Master Password et de la clé de récupération entraîne la perte définitive de l'accès aux données.

### 20.6 Mémoire et presse-papiers

- Ne jamais afficher les secrets par défaut.
- Déchiffrer à la demande.
- Limiter la durée de conservation des données déchiffrées.
- Effacer les buffers sensibles lorsque cela est techniquement possible.
- Vider le presse-papiers après une durée configurable.
- Prévenir l'utilisateur que Windows Clipboard History et les gestionnaires de presse-papiers tiers peuvent conserver des copies.

---

# PARTIE VI — MODÈLE DE DONNÉES

## 21. Principes de conception

La base de données doit distinguer :

- Les ressources.
- Les types de ressources.
- Les relations.
- Les identifiants.
- Les versions des identifiants.
- Les documentations.
- Les utilisateurs.
- Les événements d'audit.
- Les informations cryptographiques du coffre.

Une ressource ne doit pas être obligatoirement représentée par un arbre parent-enfant strict.

Une ressource peut avoir plusieurs relations et plusieurs chemins de navigation.

## 22. Schéma conceptuel

```text
                    VAULT
                      |
          +-----------+-----------+
          |           |           |
        USERS       ASSETS    CREDENTIALS
                      |           |
              ASSET_RELATIONS     |
                      |       CREDENTIAL_VERSIONS
                      |
              DOCUMENTATIONS
                      |
                  ATTACHMENTS

                     AUDIT_EVENTS
```

## 23. Tables principales

### 23.1 vaults

| Champ | Type | Description |
| --- | --- | --- |
| id | UUID | Identifiant |
| name | TEXT | Nom du coffre |
| salt | BLOB | Salt Argon2id |
| kdf_algorithm | TEXT | Argon2id |
| kdf_parameters | TEXT | Paramètres KDF |
| encrypted_dek | BLOB | DEK chiffrée |
| dek_nonce | BLOB | Nonce |
| dek_tag | BLOB | Tag GCM |
| crypto_version | INTEGER | Version cryptographique |
| created_at | DATETIME | Création |

### 23.2 assets

Table centrale de l'inventaire.

| Champ | Type | Description |
| --- | --- | --- |
| id | UUID | Identifiant |
| vault_id | UUID | Coffre |
| asset_type_id | UUID | Type |
| name | TEXT | Nom |
| hostname | TEXT | Hostname |
| description | TEXT | Description chiffrée si sensible |
| environment | TEXT | Production, Staging... |
| status | TEXT | État |
| created_at | DATETIME | Création |
| updated_at | DATETIME | Modification |

### 23.3 asset_types

| Champ | Type |
| --- | --- |
| id | UUID |
| name | TEXT |
| description | TEXT |
| icon | TEXT |
| is_system | BOOLEAN |

### 23.4 asset_properties

Permet de gérer les propriétés spécifiques des différents types de ressources.

| Champ | Type |
| --- | --- |
| id | UUID |
| asset_id | UUID |
| property_key | TEXT |
| property_value | TEXT ou BLOB |
| is_sensitive | BOOLEAN |
| encryption_nonce | BLOB |
| encryption_tag | BLOB |

Exemples :

```text
Asset: GitLab VM

hostname       = gitlab-srv
ipv4           = 10.52.134.12
os             = Ubuntu 24.04
cpu             = 8
ram             = 16 GB
vm_id           = 101
```

Les valeurs sensibles doivent être chiffrées.

### 23.5 asset_relationships

| Champ | Type |
| --- | --- |
| id | UUID |
| source_asset_id | UUID |
| target_asset_id | UUID |
| relationship_type | TEXT |
| description | TEXT |

Exemples de relations :

```text
Proxmox Node 01
    |
    +-- HOSTS --> GitLab VM
    |
    +-- HOSTS --> Registry VM

GitLab VM
    |
    +-- RUNS --> GitLab Application
    |
    +-- USES --> PostgreSQL Database
```

### 23.6 credentials

| Champ | Type |
| --- | --- |
| id | UUID |
| vault_id | UUID |
| name | TEXT |
| credential_type | TEXT |
| username_encrypted | BLOB |
| username_nonce | BLOB |
| username_tag | BLOB |
| encrypted_secret | BLOB |
| secret_nonce | BLOB |
| secret_tag | BLOB |
| notes_encrypted | BLOB |
| created_at | DATETIME |
| updated_at | DATETIME |
| expires_at | DATETIME |

### 23.7 credential_asset_links

Permet d'associer un identifiant à plusieurs ressources.

| Champ | Type |
| --- | --- |
| credential_id | UUID |
| asset_id | UUID |
| relationship_type | TEXT |

### 23.8 credential_versions

| Champ | Type |
| --- | --- |
| id | UUID |
| credential_id | UUID |
| encrypted_secret | BLOB |
| nonce | BLOB |
| authentication_tag | BLOB |
| version_number | INTEGER |
| change_reason | TEXT |
| created_at | DATETIME |

### 23.9 documentations

| Champ | Type |
| --- | --- |
| id | UUID |
| vault_id | UUID |
| title | TEXT |
| content_encrypted | BLOB |
| nonce | BLOB |
| authentication_tag | BLOB |
| document_type | TEXT |
| created_at | DATETIME |
| updated_at | DATETIME |

### 23.10 documentation_asset_links

| Champ | Type |
| --- | --- |
| documentation_id | UUID |
| asset_id | UUID |

### 23.11 audit_events

| Champ | Type |
| --- | --- |
| id | UUID |
| vault_id | UUID |
| user_id | UUID nullable |
| event_type | TEXT |
| entity_type | TEXT |
| entity_id | UUID nullable |
| timestamp | DATETIME |
| result | TEXT |
| metadata | TEXT |

Les métadonnées d'audit doivent être filtrées afin d'éviter toute fuite de données sensibles.

### 23.12 users

| Champ | Type |
| --- | --- |
| id | UUID |
| username | TEXT |
| display_name | TEXT |
| status | TEXT |
| created_at | DATETIME |

Cette table prépare l'évolution vers le mode équipe.

### 23.13 attachments

| Champ | Type |
| --- | --- |
| id | UUID |
| documentation_id | UUID |
| filename_encrypted | BLOB |
| content_encrypted | BLOB |
| nonce | BLOB |
| authentication_tag | BLOB |
| mime_type | TEXT |
| created_at | DATETIME |

## 24. Contraintes et index

- Toutes les tables doivent utiliser des identifiants uniques.
- Les clés étrangères doivent être activées.
- Les suppressions doivent être contrôlées.
- Les relations doivent éviter les doublons.
- Les références croisées doivent être validées.
- Les index doivent être prévus sur les noms, types, adresses IP et identifiants de relations.

La recherche doit utiliser SQLite FTS5 pour les métadonnées et les contenus de documentation indexables.

Les index de recherche ne doivent jamais recevoir les mots de passe ou les secrets déchiffrés.

Pour les données sensibles nécessitant une recherche exacte, une stratégie d'index HMAC avec clé dédiée pourra être étudiée ultérieurement.

---

# PARTIE VII — INTERFACE UTILISATEUR

## 25. Structure principale

L'interface sera organisée en trois zones :

```text
+----------------------------------------------------------+
| InfraVault             Search              User / Lock   |
+--------------------+-------------------------------------+
|                    |                                     |
| Infrastructure     |             Main Content            |
|                    |                                     |
| Datacenters        |             Dashboard               |
|   Proxmox          |                                     |
|     Node 01        |                                     |
|       GitLab       |                                     |
|       Registry     |                                     |
|                    |                                     |
| Applications       |                                     |
| Databases          |                                     |
| Network Devices    |                                     |
| Credentials        |                                     |
| Documentation      |                                     |
|                    |                                     |
+--------------------+-------------------------------------+
| Status / Vault Locked or Unlocked / Backup Status         |
+----------------------------------------------------------+
```

## 26. Dashboard

Le tableau de bord présente :

- Nombre de ressources.
- Nombre de serveurs.
- Nombre de VMs.
- Nombre d'applications.
- Nombre de bases de données.
- Nombre d'identifiants.
- Ressources récemment modifiées.
- Identifiants arrivant à expiration.
- Dernière sauvegarde.
- Événements récents.

Les secrets ne doivent pas apparaître directement dans les widgets.

## 27. Resource Details

Chaque ressource possède une vue détaillée organisée en onglets :

1. Overview.
2. Properties.
3. Credentials.
4. Relationships.
5. Documentation.
6. History.

## 28. Credential Details

Une fiche d'identifiant doit présenter :

- Nom.
- Type.
- Username masqué ou affichable.
- Password masqué.
- Bouton Reveal.
- Bouton Copy.
- Bouton Edit.
- Historique.
- Ressources associées.
- Expiration éventuelle.

L'affichage du secret doit être limité dans le temps et nécessiter une action explicite.

---

# PARTIE VIII — ORGANISATION DU DÉVELOPPEMENT

## 29. Structure du repository Git

```text
InfraVault/
|
+-- src/
|   |
|   +-- InfraVault.Presentation/
|   |   +-- Views/
|   |   +-- ViewModels/
|   |   +-- Controls/
|   |   +-- Resources/
|   |
|   +-- InfraVault.Application/
|   |   +-- Interfaces/
|   |   +-- Services/
|   |   +-- UseCases/
|   |   +-- DTOs/
|   |
|   +-- InfraVault.Domain/
|   |   +-- Entities/
|   |   +-- Enums/
|   |   +-- ValueObjects/
|   |   +-- Interfaces/
|   |
|   +-- InfraVault.Infrastructure/
|   |   +-- Persistence/
|   |   +-- Repositories/
|   |   +-- Cryptography/
|   |   +-- Windows/
|   |   +-- Backup/
|   |
|   +-- InfraVault.Shared/
|       +-- Constants/
|       +-- Exceptions/
|
+-- tests/
|   |
|   +-- InfraVault.Domain.Tests/
|   +-- InfraVault.Application.Tests/
|   +-- InfraVault.Infrastructure.Tests/
|   +-- InfraVault.Security.Tests/
|   +-- InfraVault.Integration.Tests/
|
+-- docs/
|   +-- architecture/
|   +-- security/
|   +-- database/
|   +-- requirements/
|
+-- scripts/
|
+-- README.md
+-- LICENSE
```

## 30. Conventions de développement

- Respect de SOLID.
- Architecture Clean Architecture.
- MVVM pour l'interface.
- Injection de dépendances.
- Repository Pattern lorsque pertinent.
- Async/Await pour les opérations longues.
- DTO pour les échanges entre couches.
- Validation systématique des entrées.
- Gestion centralisée des exceptions.
- Tests unitaires et d'intégration.
- Analyse statique du code.
- Aucune donnée sensible dans les logs.

## 31. Git et CI/CD

Le projet doit utiliser Git et un pipeline CI.

Étapes proposées :

1. Restore dependencies.
2. Build.
3. Unit tests.
4. Integration tests.
5. Security tests.
6. Static analysis.
7. Package Windows release.

Les secrets de signature et les clés de publication doivent être protégés dans le pipeline.

---

# PARTIE IX — ROADMAP

## 32. Phase 1 : Foundation & Secure Vault

Objectif : construire le socle sécurisé.

Fonctionnalités :

- Création du coffre.
- Master Password.
- Dérivation Argon2id.
- Génération de DEK.
- Chiffrement AES-256-GCM.
- Déverrouillage.
- Verrouillage.
- Gestion des identifiants.
- Générateur de mots de passe.
- Base SQLite.
- Sauvegarde chiffrée.

Livrable : application Windows fonctionnelle permettant de stocker et récupérer des identifiants en sécurité.

## 33. Phase 2 : Infrastructure Management

Fonctionnalités :

- Gestion des datacenters.
- Gestion des serveurs.
- Gestion des hyperviseurs.
- Gestion des VMs.
- Gestion des équipements réseau.
- Gestion des bases de données.
- Gestion des applications.
- Arborescence.
- Relations entre ressources.

Livrable : inventaire informatique navigable.

## 34. Phase 3 : Documentation & Audit

Fonctionnalités :

- Documentation technique.
- Notes.
- Pièces jointes chiffrées.
- Historique.
- Audit.
- Recherche globale.
- Recherche avancée.

Livrable : plateforme de documentation et de gestion des infrastructures.

## 35. Phase 4 : Advanced Security

Fonctionnalités :

- Clé de récupération.
- Windows Hello.
- Politiques de verrouillage.
- Détection des anomalies locales.
- Audit renforcé.
- Vérification de l'intégrité des sauvegardes.
- Tests de pénétration.
- Revue cryptographique.

Livrable : version renforcée pour un usage professionnel.

## 36. Phase 5 : Team & Synchronization

Fonctionnalités :

- Serveur central.
- Comptes utilisateurs.
- Gestion des rôles.
- Partage de coffres.
- Gestion des permissions par ressource.
- Synchronisation.
- Journal d'audit centralisé.
- Gestion des appareils autorisés.

Livrable : InfraVault Team.

Cette phase nécessitera une conception cryptographique spécifique. Il ne faudra pas simplement placer une base SQLite partagée sur un réseau.

---

# PARTIE X — CRITÈRES D'ACCEPTATION

## 37. Sécurité

- Aucun secret stocké en clair dans la base.
- Aucun Master Password stocké en clair.
- Un mauvais Master Password ne permet pas d'ouvrir le coffre.
- Toute altération d'un ciphertext doit être détectée.
- Les sauvegardes sont protégées.
- Les logs ne contiennent aucun secret.
- Le verrouillage rend les secrets inaccessibles.
- Le changement du Master Password conserve l'accès aux données.
- La récupération fonctionne uniquement avec le mécanisme prévu.

## 38. Fonctionnel

- Création d'une ressource.
- Modification d'une ressource.
- Association d'un identifiant à une ressource.
- Association de plusieurs ressources à un identifiant.
- Navigation dans l'arborescence.
- Recherche par adresse IP.
- Recherche par hostname.
- Consultation de l'historique.
- Export et restauration d'une sauvegarde.
- Utilisation sans Internet.

## 39. Qualité

- Tests automatisés réussis.
- Pas d'exception non gérée.
- Pas de blocage de l'interface pendant les opérations longues.
- Gestion correcte des erreurs de base de données.
- Documentation technique disponible.
- Installation et désinstallation fiables.

---

# PARTIE XI — RISQUES ET MESURES DE MAÎTRISE

| Risque | Mesure |
| --- | --- |
| Perte du Master Password | Clé de récupération |
| Vol du fichier SQLite | Chiffrement |
| Malware local | Durée de session limitée, protection Windows |
| Fuite dans les logs | Filtrage et tests |
| Corruption de la base | Sauvegardes et intégrité |
| Mauvaise gestion des clés | Architecture cryptographique dédiée |
| Dépendance à une bibliothèque vulnérable | Audit des dépendances |
| Synchronisation conflictuelle | Architecture dédiée en phase équipe |
| Mauvaise suppression de ressources | Suppression contrôlée et vérification des dépendances |

---

# PARTIE XII — PRINCIPES À NE PAS COMPROMETTRE

1. InfraVault ne doit jamais stocker les mots de passe récupérables sous forme de hash.
2. Les secrets doivent être chiffrés avec un algorithme authentifié.
3. Le Master Password ne doit jamais être conservé en clair.
4. Le chiffrement doit être implémenté dans un module isolé et testé.
5. Les opérations cryptographiques ne doivent pas être dispersées dans les ViewModels.
6. Les secrets ne doivent jamais apparaître dans les logs.
7. Les sauvegardes doivent être chiffrées.
8. La base de données ne doit pas être directement exposée à des utilisateurs distants.
9. La synchronisation future doit utiliser une architecture sécurisée conçue pour cet usage.
10. Le système doit rester utilisable hors ligne.

---

# CONCLUSION

InfraVault ambitionne de devenir un outil de référence pour les administrateurs systèmes et réseaux qui souhaitent centraliser leurs infrastructures, leurs identifiants et leur documentation technique.

La première version doit privilégier la sécurité, la simplicité et la fiabilité.

Le produit sera développé progressivement, en commençant par un coffre-fort local sécurisé, puis en intégrant la gestion des ressources, leurs relations, la documentation et enfin les fonctionnalités collaboratives.

**Principe directeur : Know your infrastructure. Secure your access. Control your information.**
