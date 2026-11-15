using System.Net;
using System.Text.RegularExpressions;
using FluentValidation;
using InfraVault.Application.DTOs;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;

namespace InfraVault.Application.Services;

public sealed partial class AssetInputValidator : AbstractValidator<AssetInput>
{
    [GeneratedRegex(@"^(?=.{1,253}$)([a-zA-Z0-9_]([a-zA-Z0-9_\-]{0,61}[a-zA-Z0-9_])?)(\.[a-zA-Z0-9_]([a-zA-Z0-9_\-]{0,61}[a-zA-Z0-9_])?)*$")]
    private static partial Regex HostnamePattern();

    public AssetInputValidator()
    {
        RuleFor(a => a.Name).NotEmpty().WithMessage("Le nom de la ressource est requis.")
            .MaximumLength(Limits.NameMaxLength).WithMessage("Le nom ne peut pas dépasser 120 caractères.");
        RuleFor(a => a.AssetTypeId).NotEmpty().WithMessage("La catégorie est requise.");
        RuleFor(a => a.Hostname)
            .Must(h => string.IsNullOrWhiteSpace(h) || HostnamePattern().IsMatch(h.Trim()))
            .WithMessage("Le hostname contient des caractères non valides.");
        RuleFor(a => a.IpAddress)
            .Must(ip => string.IsNullOrWhiteSpace(ip) || IsIpOrCidr(ip.Trim()))
            .WithMessage("L'adresse IP n'est pas valide (IPv4, IPv6 ou notation CIDR).");
        RuleFor(a => a.Description).MaximumLength(Limits.DescriptionMaxLength)
            .WithMessage("La description est trop longue.");
        RuleFor(a => a.Status).Must(s => AssetStatuses.All.Contains(s)).WithMessage("Statut inconnu.");
        RuleFor(a => a.Environment).NotEmpty().WithMessage("L'environnement est requis.");
    }

    public static bool IsIpOrCidr(string value)
    {
        var slash = value.IndexOf('/');
        var address = slash < 0 ? value : value[..slash];
        // IPAddress.TryParse accepte « 10 » ou « 10.1 » : on exige une forme complète.
        if (!IPAddress.TryParse(address, out var ip)) return false;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && address.Count(c => c == '.') != 3) return false;
        if (slash < 0) return true;
        var max = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return int.TryParse(value[(slash + 1)..], out var prefix) && prefix >= 0 && prefix <= max;
    }
}

public sealed class CredentialInputValidator : AbstractValidator<CredentialInput>
{
    public CredentialInputValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithMessage("Le nom de l'identifiant est requis.")
            .MaximumLength(Limits.NameMaxLength).WithMessage("Le nom ne peut pas dépasser 120 caractères.");
        RuleFor(c => c.Type).Must(t => CredentialTypes.All.Any(x => x.Key == t)).WithMessage("Type d'identifiant inconnu.");
        RuleFor(c => c.Port).InclusiveBetween(1, 65535).When(c => c.Port is not null)
            .WithMessage("Le port doit être compris entre 1 et 65535.");
        RuleFor(c => c.Secret).NotEmpty().When(c => c.Id is null)
            .WithMessage("Le mot de passe ou secret est requis.");
        RuleFor(c => c.Secret).Must(s => s is null || s.Length <= 64 * 1024)
            .WithMessage("Le secret est trop volumineux (64 Ko maximum).");
        RuleFor(c => c.Url).MaximumLength(2000).WithMessage("L'URL est trop longue.");
    }
}

public sealed class DocumentInputValidator : AbstractValidator<DocumentInput>
{
    public DocumentInputValidator()
    {
        RuleFor(d => d.Title).NotEmpty().WithMessage("Le titre du document est requis.")
            .MaximumLength(200).WithMessage("Le titre ne peut pas dépasser 200 caractères.");
        RuleFor(d => d.Content).Must(c => c is null || c.Length <= 2_000_000)
            .WithMessage("Le document est trop volumineux.");
        RuleFor(d => d.DocumentType).NotEmpty().WithMessage("Le type de document est requis.");
    }
}

public static class ValidationExtensions
{
    /// <summary>Valide et lève une <see cref="ValidationFailedException"/> lisible par l'utilisateur.</summary>
    public static void EnsureValid<T>(this IValidator<T> validator, T instance)
    {
        var result = validator.Validate(instance);
        if (!result.IsValid)
            throw new ValidationFailedException(result.Errors.Select(e => e.ErrorMessage).Distinct().ToList());
    }
}
