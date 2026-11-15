using InfraVault.Application.DTOs;
using InfraVault.Application.Security;
using InfraVault.Application.Services;
using InfraVault.Domain.Interfaces;
using InfraVault.Shared.Constants;

namespace InfraVault.Application.UseCases;

/// <summary>
/// Jeu de données d'exemple proposé à la création du coffre : une petite infrastructure réaliste
/// (datacenter, cluster Proxmox, VM, stack de conteneurs, flux et ports) pour découvrir l'application.
/// Les secrets d'exemple sont générés aléatoirement.
/// </summary>
public sealed class DemoDataSeeder(
    VaultService vault, InfrastructureService infrastructure, CredentialService credentials,
    DocumentationService documentation, IClock clock)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var types = (await vault.GetAssetTypesAsync(ct)).ToDictionary(t => t.Key, t => t.Id);

        async Task<Guid> Asset(string type, string name, Guid? parent = null, string? host = null, string? ip = null,
            string env = "Production", string status = AssetStatuses.Active, string? tags = null, string? description = null,
            string? icon = null, string? color = null, bool exposed = false, string? publicAddress = null,
            string? exposedPorts = null, params (string Key, string Value)[] props)
            => await infrastructure.SaveAssetAsync(new AssetInput
            {
                AssetTypeId = types[type], Name = name, ParentId = parent, Hostname = host, IpAddress = ip, Environment = env,
                Status = status, Tags = tags, Description = description, Icon = icon, IconColor = color,
                InternetExposed = exposed, PublicAddress = publicAddress, ExposedPorts = exposedPorts,
                Properties = props.ToDictionary(p => p.Key, p => (string?)p.Value),
            }, ct);

        Task Flow(Guid source, Guid target, string type, string? ports = null, string? description = null)
            => infrastructure.AddRelationshipAsync(source, target, type, description, ports, ct);

        // ── Site et réseau ──
        var dc = await Asset(AssetTypeKeys.Datacenter, "Datacenter Principal", tags: "site, prod",
            description: "Salle serveur principale. Deux arrivées électriques, climatisation redondante.",
            props: [("address", "Zone d'activités, bâtiment B"), ("provider", "Hébergement interne"), ("contact", "Équipe exploitation")]);

        var firewall = await Asset(AssetTypeKeys.NetworkDevice, "Firewall Périmètre", dc, "fw-edge-01", "10.52.0.1",
            tags: "réseau, sécurité", icon: "firewall", color: "coral", exposed: true, publicAddress: "portail.exemple.org",
            exposedPorts: "443/tcp, 80/tcp, 51820/udp",
            description: "Pare-feu de périmètre. Publie le portail (reverse proxy) et le VPN d'administration.",
            props: [("device_type", "Firewall"), ("manufacturer", "Netgate"), ("model", "6100"), ("firmware_version", "pfSense+ 25.07"),
                ("interfaces", "ix0 — WAN\nix1 — LAN serveurs (VLAN 134)\nix2 — DMZ (VLAN 20)"), ("vlans", "20, 134"),
                ("location", "Baie A1 · U40"), ("admin_info", "Accès d'administration uniquement depuis le VLAN 134, via le VPN.")]);

        var sw = await Asset(AssetTypeKeys.NetworkDevice, "Switch Cœur", dc, "sw-core-01", "10.52.0.2", tags: "réseau",
            props: [("device_type", "Switch"), ("manufacturer", "Aruba"), ("model", "CX 6300"), ("vlans", "10, 20, 134"), ("location", "Baie A1 · U38")]);

        var nas = await Asset(AssetTypeKeys.Storage, "NAS Storage", dc, "nas-01", "10.52.134.50", tags: "sauvegarde",
            description: "Cible des sauvegardes nocturnes.",
            props: [("storage_type", "NAS"), ("manufacturer", "Synology"), ("model", "RS2423+"), ("capacity", "48 To utiles"),
                ("raid_level", "RAID 6"), ("protocols", "NFS, SMB")]);

        // ── Virtualisation ──
        var cluster = await Asset(AssetTypeKeys.Hypervisor, "Proxmox Cluster", dc, "pve-cluster", "10.52.134.10", tags: "virtualisation",
            props: [("platform", "Proxmox VE"), ("version", "8.4"), ("cluster", "pve-prod"), ("management_url", "https://10.52.134.10:8006")]);

        var node1 = await Asset(AssetTypeKeys.Server, "Node 01", cluster, "pve-node01", "10.52.134.11", tags: "virtualisation",
            props: [("fqdn", "pve-node01.exemple.local"), ("os", "Proxmox VE"), ("os_version", "8.4"), ("manufacturer", "Dell"),
                ("model", "PowerEdge R660"), ("serial_number", "DX-48213"), ("cpu", "2 × Xeon Silver 4410Y"), ("ram", "256 Go"),
                ("storage", "6 × 1,92 To NVMe"), ("location", "Baie A3 · U12")]);

        var node2 = await Asset(AssetTypeKeys.Server, "Node 02", cluster, "pve-node02", "10.52.134.13", status: AssetStatuses.Maintenance,
            tags: "virtualisation", icon: "monitor", color: "violet",
            props: [("fqdn", "pve-node02.exemple.local"), ("os", "Proxmox VE"), ("os_version", "8.4"), ("manufacturer", "Dell"),
                ("model", "PowerEdge R660"), ("ram", "256 Go"), ("location", "Baie A3 · U14")]);

        // ── VM et services ──
        var gitlabVm = await Asset(AssetTypeKeys.VirtualMachine, "gitlab-srv", node1, "gitlab-srv", "10.52.134.12", tags: "devops",
            props: [("vm_id", "101"), ("os", "Ubuntu"), ("os_version", "24.04 LTS"), ("cpu", "8"), ("ram", "16 Go"), ("disk", "200 Go")]);
        var gitlab = await Asset(AssetTypeKeys.Application, "GitLab", gitlabVm, tags: "devops, ci", icon: "git", color: "coral",
            props: [("version", "18.2"), ("url", "https://gitlab.exemple.local"), ("port", "443"), ("owner", "Équipe DevOps")]);

        var dbVm = await Asset(AssetTypeKeys.VirtualMachine, "db-srv", node1, "db-srv", "10.52.134.20", tags: "données",
            props: [("vm_id", "102"), ("os", "Debian"), ("os_version", "13"), ("cpu", "8"), ("ram", "32 Go"), ("disk", "500 Go")]);
        var postgres = await Asset(AssetTypeKeys.Database, "PostgreSQL", dbVm, tags: "données",
            props: [("engine", "PostgreSQL"), ("version", "17.5"), ("port", "5432"), ("instance_name", "main"),
                ("backup_policy", "pg_basebackup nocturne vers le NAS, WAL archivés en continu.")]);

        var dockerVm = await Asset(AssetTypeKeys.VirtualMachine, "docker-srv", node1, "docker-srv", "10.52.134.30", tags: "conteneurs",
            props: [("vm_id", "103"), ("os", "Debian"), ("os_version", "13"), ("cpu", "12"), ("ram", "48 Go"), ("disk", "300 Go")]);
        var docker = await Asset(AssetTypeKeys.ContainerPlatform, "Docker Engine", dockerVm, tags: "conteneurs",
            props: [("platform", "Docker"), ("version", "28.3")]);

        var stack = await Asset(AssetTypeKeys.Stack, "Stack Portail", docker, tags: "conteneurs, portail",
            description: "Portail citoyen : reverse proxy, front, API et cache.",
            props: [("orchestrator", "Docker Compose"), ("compose_path", "/opt/stacks/portail/compose.yml"), ("network", "portail_net · 172.20.0.0/24")]);
        var traefik = await Asset(AssetTypeKeys.Container, "traefik", stack, tags: "proxy", icon: "router", color: "sky",
            props: [("image", "traefik"), ("image_tag", "3.4"), ("ports", "443:443/tcp, 80:80/tcp"), ("internal_ports", "443/tcp, 80/tcp"),
                ("networks", "portail_net"), ("restart_policy", "unless-stopped")]);
        var web = await Asset(AssetTypeKeys.Container, "portail-web", stack,
            props: [("image", "registry.exemple.local/portail/web"), ("image_tag", "2.8.1"), ("internal_ports", "8080/tcp"), ("networks", "portail_net")]);
        var api = await Asset(AssetTypeKeys.Container, "portail-api", stack,
            props: [("image", "registry.exemple.local/portail/api"), ("image_tag", "2.8.1"), ("internal_ports", "9000/tcp"), ("networks", "portail_net")]);
        var redis = await Asset(AssetTypeKeys.Container, "redis", stack, icon: "zap", color: "rose",
            props: [("image", "redis"), ("image_tag", "8-alpine"), ("internal_ports", "6379/tcp"), ("networks", "portail_net"),
                ("volumes", "redis_data:/data")]);
        var registry = await Asset(AssetTypeKeys.Container, "Docker Registry", docker, tags: "devops",
            props: [("image", "registry"), ("image_tag", "3"), ("ports", "5000:5000/tcp"), ("internal_ports", "5000/tcp")]);

        var domain = await Asset(AssetTypeKeys.Domain, "portail.exemple.org", tags: "portail",
            props: [("registrar", "Gandi"), ("dns_provider", "Cloudflare"),
                ("expires_on", clock.UtcNow.AddDays(212).ToString("yyyy-MM-dd")), ("tls_expires_on", clock.UtcNow.AddDays(41).ToString("yyyy-MM-dd"))]);

        // ── Flux et ports ──
        await Flow(firewall, traefik, RelationshipTypes.ConnectsTo, "443/tcp, 80/tcp", "NAT entrant vers le reverse proxy");
        await Flow(traefik, web, RelationshipTypes.ConnectsTo, "8080/tcp", "Routage HTTP du front");
        await Flow(traefik, api, RelationshipTypes.ConnectsTo, "9000/tcp", "Routage /api");
        await Flow(web, api, RelationshipTypes.Uses, "9000/tcp");
        await Flow(api, redis, RelationshipTypes.Uses, "6379/tcp", "Cache et sessions");
        await Flow(api, postgres, RelationshipTypes.Uses, "5432/tcp", "Base applicative");
        await Flow(gitlab, postgres, RelationshipTypes.Uses, "5432/tcp");
        await Flow(gitlab, registry, RelationshipTypes.Uses, "5000/tcp", "Publication des images");
        await Flow(dbVm, nas, RelationshipTypes.BacksUpTo, "2049/tcp", "Sauvegarde nocturne (NFS)");
        await Flow(firewall, sw, RelationshipTypes.ConnectsTo, description: "Trunk VLAN 20, 134");
        await Flow(sw, node1, RelationshipTypes.ConnectsTo, description: "2 × 25 GbE LACP");
        await Flow(sw, node2, RelationshipTypes.ConnectsTo, description: "2 × 25 GbE LACP");
        await Flow(domain, firewall, RelationshipTypes.DependsOn, "443/tcp", "Enregistrement A vers l'IP publique");

        // ── Identifiants (secrets aléatoires) ──
        var generator = new PasswordGeneratorOptions { Length = 24 };
        var now = clock.UtcNow;
        await credentials.SaveAsync(new CredentialInput
        {
            Name = "root — nœuds Proxmox", Type = CredentialTypes.Ssh, Username = "root", Secret = PasswordGenerator.Generate(generator),
            Port = 22, Tags = "ssh, virtualisation", AssetIds = [node1, node2], Notes = "Connexion par mot de passe désactivée hors console.",
        }, ct);
        await credentials.SaveAsync(new CredentialInput
        {
            Name = "PostgreSQL — superutilisateur", Type = CredentialTypes.Database, Username = "postgres",
            Secret = PasswordGenerator.Generate(generator), Url = "db-srv.exemple.local", Port = 5432, Tags = "données",
            ExpiresAt = now.AddDays(12), AssetIds = [postgres, dbVm],
        }, ct);
        await credentials.SaveAsync(new CredentialInput
        {
            Name = "GitLab — compte root", Type = CredentialTypes.Admin, Username = "root", Secret = PasswordGenerator.Generate(generator),
            Url = "https://gitlab.exemple.local", Tags = "devops", AssetIds = [gitlab],
        }, ct);
        await credentials.SaveAsync(new CredentialInput
        {
            Name = "Firewall — administration", Type = CredentialTypes.Admin, Username = "admin", Secret = PasswordGenerator.Generate(generator),
            Url = "https://10.52.0.1", Tags = "réseau, sécurité", ExpiresAt = now.AddDays(-3), AssetIds = [firewall],
        }, ct);
        await credentials.SaveAsync(new CredentialInput
        {
            Name = "Registry — jeton CI", Type = CredentialTypes.ApiToken, Username = "ci-runner",
            Secret = PasswordGenerator.Generate(new PasswordGeneratorOptions { Length = 40, Symbols = false }),
            Url = "https://registry.exemple.local", Port = 5000, Tags = "devops, ci", ExpiresAt = now.AddDays(25), AssetIds = [registry, gitlab],
        }, ct);
        await credentials.SaveAsync(new CredentialInput
        {
            Name = "VPN d'administration", Type = CredentialTypes.Vpn, Username = "ops", Secret = PasswordGenerator.Generate(generator),
            Url = "portail.exemple.org", Port = 51820, Tags = "réseau", AssetIds = [firewall],
        }, ct);

        // ── Documentation ──
        await documentation.SaveAsync(new DocumentInput
        {
            Title = "Procédure de restauration PostgreSQL", DocumentType = "Procédure", Tags = "données, sauvegarde",
            AssetIds = [postgres, dbVm, nas],
            Content = """
                # Restauration PostgreSQL

                Procédure à suivre pour restaurer l'instance **main** à partir de la sauvegarde nocturne stockée sur le NAS.

                ## Pré-requis

                - Accès SSH à `db-srv` (voir l'identifiant *PostgreSQL — superutilisateur* dans le coffre)
                - Montage NFS du NAS disponible sur `/mnt/backup`

                ## Étapes

                1. Arrêter le service et écarter l'ancien répertoire de données.
                2. Restaurer la sauvegarde de base.
                3. Rejouer les WAL jusqu'au point souhaité.

                ```bash
                sudo systemctl stop postgresql
                sudo mv /var/lib/postgresql/17/main /var/lib/postgresql/17/main.old
                sudo -u postgres tar -xzf /mnt/backup/pg/base-latest.tar.gz -C /var/lib/postgresql/17/main
                sudo systemctl start postgresql
                ```

                ## Vérification

                ```sql
                SELECT pg_is_in_recovery(), now() - pg_last_xact_replay_timestamp() AS retard;
                ```

                > Ne notez jamais de mot de passe ici : utilisez le gestionnaire d'identifiants.
                """,
        }, ct);
        await documentation.SaveAsync(new DocumentInput
        {
            Title = "Configuration Traefik du portail", DocumentType = "Configuration", Tags = "proxy, portail", AssetIds = [traefik, stack],
            Content = """
                # Traefik — Stack Portail

                Le reverse proxy termine TLS et route vers le front et l'API sur le réseau `portail_net`.

                ```yaml
                services:
                  traefik:
                    image: traefik:3.4
                    ports:
                      - "443:443"
                      - "80:80"
                    networks: [portail_net]
                  portail-web:
                    labels:
                      - traefik.http.routers.web.rule=Host(`portail.exemple.org`)
                      - traefik.http.services.web.loadbalancer.server.port=8080
                ```

                ## Flux

                - Internet → Firewall (443, 80) → traefik
                - traefik → portail-web (8080), portail-api (9000)
                """,
        }, ct);
        await documentation.SaveAsync(new DocumentInput
        {
            Title = "Rotation des certificats TLS", DocumentType = "Runbook", Tags = "sécurité", AssetIds = [domain, firewall],
            Content = """
                # Rotation des certificats TLS

                Les certificats publics sont renouvelés automatiquement (ACME). Ce runbook couvre le renouvellement manuel.

                ```bash
                certbot renew --dry-run
                certbot renew --deploy-hook "docker exec traefik kill -HUP 1"
                ```

                Après renouvellement, mettre à jour la date d'expiration sur la fiche du domaine.
                """,
        }, ct);
    }
}
