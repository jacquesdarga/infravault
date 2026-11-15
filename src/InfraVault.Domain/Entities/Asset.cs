namespace InfraVault.Domain.Entities;

/// <summary>
/// Ressource d'infrastructure. Table centrale de l'inventaire : les champs communs sont des colonnes,
/// les champs propres à chaque type vivent dans <see cref="AssetProperty"/> selon le schéma du type.
/// </summary>
public class Asset
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public Guid AssetTypeId { get; set; }
    public AssetType? AssetType { get; set; }

    public string Name { get; set; } = "";
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public string? Description { get; set; }
    public string Environment { get; set; } = "Production";
    public string Status { get; set; } = "Active";
    public string? Tags { get; set; }

    /// <summary>Exposition Internet : la ressource est joignable depuis l'extérieur.</summary>
    public bool InternetExposed { get; set; }
    /// <summary>Adresse ou nom public (IP publique, domaine).</summary>
    public string? PublicAddress { get; set; }
    /// <summary>Ports ouverts sur Internet, ex. « 443/tcp, 80/tcp ».</summary>
    public string? ExposedPorts { get; set; }

    /// <summary>Icône et couleur personnalisées ; null = celles du type.</summary>
    public string? Icon { get; set; }
    public string? IconColor { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<AssetProperty> Properties { get; set; } = [];
}

/// <summary>Catégorie de ressource. Les types système sont livrés avec l'application ; les autres sont créés par l'utilisateur.</summary>
public class AssetType
{
    public Guid Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Icon { get; set; } = "cube";
    public string Color { get; set; } = "slate";
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Schéma JSON des champs spécifiques (liste de FieldDefinition).</summary>
    public string FieldSchema { get; set; } = "[]";
}

/// <summary>Propriété spécifique d'une ressource. Une valeur sensible est chiffrée et n'existe qu'en <see cref="EncryptedValue"/>.</summary>
public class AssetProperty
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string PropertyKey { get; set; } = "";
    public string? PropertyValue { get; set; }
    public bool IsSensitive { get; set; }
    public byte[]? EncryptedValue { get; set; }
}

/// <summary>Relation orientée source → cible, navigable dans les deux sens.</summary>
public class AssetRelationship
{
    public Guid Id { get; set; }
    public Guid SourceAssetId { get; set; }
    public Guid TargetAssetId { get; set; }
    public string RelationshipType { get; set; } = "";
    /// <summary>Ports et protocoles du flux source → cible, ex. « 5432/tcp ».</summary>
    public string? Ports { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
