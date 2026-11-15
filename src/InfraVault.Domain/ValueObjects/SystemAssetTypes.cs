using InfraVault.Shared.Constants;

namespace InfraVault.Domain.ValueObjects;

public sealed record AssetTypeDefinition(
    string Key, string Name, string Description, string Icon, string Color, IReadOnlyList<FieldDefinition> Fields);

/// <summary>
/// Catalogue des catégories système (§5.1 du cahier des charges) et de leurs champs.
/// Les entités « Server », « VirtualMachine », « DatabaseInstance », « NetworkDevice » et
/// « ApplicationResource » du modèle conceptuel sont matérialisées ici sous forme de schémas.
/// </summary>
public static class SystemAssetTypes
{
    private const string Hw = "Matériel";
    private const string Sys = "Système";
    private const string Net = "Réseau";
    private const string Loc = "Localisation";
    private const string Gen = "Général";

    private static FieldDefinition F(string key, string label, string group = Gen, FieldKind kind = FieldKind.Text,
        bool sensitive = false, string? placeholder = null, params string[] options)
        => new(key, label, kind, sensitive, group, options.Length > 0 ? options : null, placeholder);

    public static IReadOnlyList<AssetTypeDefinition> All { get; } =
    [
        new(AssetTypeKeys.Datacenter, "Datacenter", "Site physique : datacenter principal, site secondaire, salle serveur.",
            "building", "amber",
            [
                F("address", "Adresse", Loc, FieldKind.Multiline),
                F("provider", "Opérateur / hébergeur", Gen),
                F("contact", "Contact sur site", Gen),
                F("access_procedure", "Procédure d'accès", Loc, FieldKind.Multiline),
            ]),

        new(AssetTypeKeys.Server, "Serveur", "Serveur physique ou serveur dédié.",
            "server", "sky",
            [
                F("fqdn", "FQDN", Net, placeholder: "srv01.exemple.local"),
                F("ipv6", "Adresse IPv6", Net),
                F("os", "Système d'exploitation", Sys, placeholder: "Debian 13"),
                F("os_version", "Version du système", Sys),
                F("manufacturer", "Fabricant", Hw),
                F("model", "Modèle", Hw),
                F("serial_number", "Numéro de série", Hw),
                F("cpu", "CPU", Hw, placeholder: "2 × Xeon Silver 4310"),
                F("ram", "RAM", Hw, placeholder: "128 Go"),
                F("storage", "Stockage", Hw, placeholder: "4 × 1,92 To NVMe"),
                F("location", "Emplacement physique", Loc, placeholder: "Baie A3 · U12"),
            ]),

        new(AssetTypeKeys.Hypervisor, "Hyperviseur", "Proxmox, VMware ESXi, Hyper-V, KVM…",
            "layers", "violet",
            [
                F("platform", "Plateforme", Sys, FieldKind.Choice, options: ["Proxmox VE", "VMware ESXi", "Hyper-V", "KVM", "XCP-ng", "Nutanix AHV", "Autre"]),
                F("version", "Version", Sys),
                F("cluster", "Cluster", Sys),
                F("management_url", "URL d'administration", Net, FieldKind.Url, placeholder: "https://pve01:8006"),
                F("fqdn", "FQDN", Net),
                F("cpu", "CPU", Hw),
                F("ram", "RAM", Hw),
                F("storage", "Stockage", Hw),
                F("location", "Emplacement physique", Loc),
            ]),

        new(AssetTypeKeys.VirtualMachine, "Machine virtuelle", "VM Linux ou Windows rattachée à un hyperviseur.",
            "monitor", "teal",
            [
                F("vm_id", "VM ID", Sys, placeholder: "101"),
                F("os", "Système d'exploitation", Sys, placeholder: "Ubuntu 24.04"),
                F("os_version", "Version du système", Sys),
                F("mac_address", "Adresse MAC", Net, placeholder: "BC:24:11:xx:xx:xx"),
                F("fqdn", "FQDN", Net),
                F("cpu", "vCPU", Hw, placeholder: "8"),
                F("ram", "RAM", Hw, placeholder: "16 Go"),
                F("disk", "Disque", Hw, placeholder: "200 Go"),
            ]),

        new(AssetTypeKeys.ContainerPlatform, "Plateforme de conteneurs", "Docker, Kubernetes, Podman, Nomad…",
            "boxes", "sky",
            [
                F("platform", "Plateforme", Sys, FieldKind.Choice, options: ["Docker", "Kubernetes", "K3s", "Podman", "Docker Swarm", "Nomad", "OpenShift", "Autre"]),
                F("version", "Version", Sys),
                F("api_endpoint", "Endpoint d'API", Net, FieldKind.Url),
                F("nodes", "Nœuds", Sys, FieldKind.Multiline),
            ]),

        new(AssetTypeKeys.Stack, "Stack", "Ensemble de conteneurs déployés ensemble (projet Docker Compose, release Helm…).",
            "stack", "violet",
            [
                F("orchestrator", "Orchestration", Sys, FieldKind.Choice, options: ["Docker Compose", "Docker Swarm", "Helm", "Kustomize", "Portainer", "Autre"]),
                F("compose_path", "Fichier / chemin", Sys, placeholder: "/opt/stacks/gitlab/compose.yml"),
                F("network", "Réseau interne", Net, placeholder: "gitlab_net · 172.20.0.0/24"),
                F("repository", "Dépôt de code", Gen, FieldKind.Url),
            ]),

        new(AssetTypeKeys.Container, "Conteneur", "Conteneur applicatif.",
            "box", "lime",
            [
                F("image", "Image", Sys, placeholder: "registry.local/app"),
                F("image_tag", "Tag", Sys, placeholder: "1.4.2"),
                F("ports", "Ports publiés (hôte:conteneur)", Net, placeholder: "443:8443/tcp"),
                F("internal_ports", "Ports internes écoutés", Net, placeholder: "8443/tcp, 9090/tcp"),
                F("networks", "Réseaux", Net, placeholder: "gitlab_net"),
                F("volumes", "Volumes", Sys, FieldKind.Multiline),
                F("restart_policy", "Politique de redémarrage", Sys, FieldKind.Choice, options: ["no", "always", "unless-stopped", "on-failure"]),
            ]),

        new(AssetTypeKeys.NetworkDevice, "Équipement réseau", "Routeur, switch, firewall, point d'accès.",
            "network", "coral",
            [
                F("device_type", "Type d'équipement", Gen, FieldKind.Choice, options: ["Routeur", "Switch", "Firewall", "Point d'accès", "Load balancer", "VPN", "Autre"]),
                F("manufacturer", "Constructeur", Hw),
                F("model", "Modèle", Hw),
                F("mac_address", "Adresse MAC", Net),
                F("firmware_version", "Version firmware", Sys),
                F("interfaces", "Interfaces réseau", Net, FieldKind.Multiline, placeholder: "ge-0/0/0 — uplink WAN"),
                F("vlans", "VLAN associés", Net, placeholder: "10, 20, 134"),
                F("location", "Emplacement", Loc),
                F("admin_info", "Informations d'administration", "Administration", FieldKind.Multiline, sensitive: true),
            ]),

        new(AssetTypeKeys.Storage, "Stockage", "NAS, SAN, stockage local ou objet.",
            "drive", "amber",
            [
                F("storage_type", "Type", Gen, FieldKind.Choice, options: ["NAS", "SAN", "Stockage local", "Stockage objet", "Bande", "Autre"]),
                F("manufacturer", "Fabricant", Hw),
                F("model", "Modèle", Hw),
                F("capacity", "Capacité", Hw, placeholder: "48 To utiles"),
                F("raid_level", "Niveau RAID", Hw, placeholder: "RAID 6"),
                F("protocols", "Protocoles", Net, placeholder: "NFS, SMB, iSCSI"),
                F("location", "Emplacement", Loc),
            ]),

        new(AssetTypeKeys.Database, "Base de données", "PostgreSQL, MySQL, MongoDB… associée à son moteur et à son hôte.",
            "database", "teal",
            [
                F("engine", "Moteur", Sys, FieldKind.Choice, options: ["PostgreSQL", "MySQL", "MariaDB", "Microsoft SQL Server", "MongoDB", "Redis", "Oracle", "SQLite", "Autre"]),
                F("version", "Version", Sys),
                F("port", "Port", Net, FieldKind.Number, placeholder: "5432"),
                F("instance_name", "Nom de l'instance", Sys),
                F("database_name", "Nom de la base", Sys),
                F("charset", "Encodage / collation", Sys),
                F("backup_policy", "Politique de sauvegarde", Gen, FieldKind.Multiline),
            ]),

        new(AssetTypeKeys.Application, "Application", "GitLab, Keycloak, application métier…",
            "app", "rose",
            [
                F("version", "Version", Sys),
                F("url", "URL", Net, FieldKind.Url, placeholder: "https://gitlab.exemple.local"),
                F("port", "Port", Net, FieldKind.Number),
                F("stack", "Pile technique", Sys, placeholder: "Laravel 12 · PHP 8.4"),
                F("repository", "Dépôt de code", Gen, FieldKind.Url),
                F("owner", "Responsable", Gen),
            ]),

        new(AssetTypeKeys.Domain, "Domaine", "Nom de domaine ou sous-domaine.",
            "globe", "lime",
            [
                F("registrar", "Registrar", Gen),
                F("dns_provider", "Hébergeur DNS", Net),
                F("nameservers", "Serveurs de noms", Net, FieldKind.Multiline),
                F("expires_on", "Expiration du domaine", Gen, FieldKind.Date),
                F("tls_expires_on", "Expiration du certificat TLS", Gen, FieldKind.Date),
            ]),

        new(AssetTypeKeys.CloudResource, "Ressource cloud", "VM cloud, bucket, service managé.",
            "cloud", "sky",
            [
                F("provider", "Fournisseur", Gen, FieldKind.Choice, options: ["AWS", "Azure", "Google Cloud", "OVHcloud", "Scaleway", "Hetzner", "DigitalOcean", "Autre"]),
                F("region", "Région", Loc),
                F("resource_type", "Type de ressource", Gen, placeholder: "Bucket S3"),
                F("resource_id", "Identifiant de la ressource", Gen),
                F("account", "Compte / projet", Gen),
                F("console_url", "URL de la console", Net, FieldKind.Url),
            ]),

        new(AssetTypeKeys.Custom, "Ressource personnalisée", "Toute ressource qui n'entre dans aucune autre catégorie.",
            "cube", "slate", []),
    ];

    public static AssetTypeDefinition? Find(string key) => All.FirstOrDefault(t => t.Key == key);
}
