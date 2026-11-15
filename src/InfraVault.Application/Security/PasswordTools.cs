using System.Security.Cryptography;
using InfraVault.Application.DTOs;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;

namespace InfraVault.Application.Security;

/// <summary>Générateur de mots de passe reposant exclusivement sur le CSPRNG du système.</summary>
public static class PasswordGenerator
{
    public const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    public const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string DigitChars = "0123456789";
    public const string SymbolChars = "!@#$%^&*()-_=+[]{};:,.?/~";
    public const string AmbiguousChars = "0O1lI|`'\"5S2Z8B";
    public const int MinLength = 4;
    public const int MaxLength = 256;

    public static IReadOnlyList<string> Pools(PasswordGeneratorOptions options)
    {
        var pools = new List<string>(4);
        if (options.Lowercase) pools.Add(LowercaseChars);
        if (options.Uppercase) pools.Add(UppercaseChars);
        if (options.Digits) pools.Add(DigitChars);
        if (options.Symbols) pools.Add(SymbolChars);
        if (options.ExcludeAmbiguous)
            pools = pools.Select(p => new string(p.Where(c => !AmbiguousChars.Contains(c)).ToArray())).ToList();
        return pools.Where(p => p.Length > 0).ToList();
    }

    public static string Generate(PasswordGeneratorOptions options)
    {
        var pools = Pools(options);
        if (pools.Count == 0)
            throw new InfraVaultException("Sélectionnez au moins une catégorie de caractères.");
        var length = Math.Clamp(options.Length, Math.Max(MinLength, pools.Count), MaxLength);
        var all = string.Concat(pools);

        var chars = new char[length];
        // Au moins un caractère de chaque catégorie retenue, puis tirage uniforme sur l'ensemble.
        for (var i = 0; i < pools.Count; i++) chars[i] = Pick(pools[i]);
        for (var i = pools.Count; i < length; i++) chars[i] = Pick(all);
        RandomNumberGenerator.Shuffle(chars.AsSpan());

        var result = new string(chars);
        Array.Clear(chars);
        return result;
    }

    public static double EntropyBits(PasswordGeneratorOptions options)
    {
        var size = Pools(options).Sum(p => p.Length);
        return size == 0 ? 0 : Math.Max(options.Length, MinLength) * Math.Log2(size);
    }

    private static char Pick(string pool) => pool[RandomNumberGenerator.GetInt32(pool.Length)];
}

public sealed record PasswordStrength(int Score, double Bits, string Label);

/// <summary>Estimation pragmatique de la robustesse d'un mot de passe saisi par l'utilisateur.</summary>
public static class PasswordStrengthEstimator
{
    private static readonly string[] Common =
    [
        "password", "motdepasse", "passw0rd", "admin", "administrateur", "infravault", "azerty", "qwerty",
        "123456", "welcome", "bonjour", "letmein", "changeme", "secret", "root", "soleil", "iloveyou",
    ];

    private static readonly string[] Sequences =
        ["abcdefghijklmnopqrstuvwxyz", "0123456789", "azertyuiop", "qwertyuiop", "qsdfghjklm", "asdfghjkl"];

    public static PasswordStrength Estimate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return new(0, 0, "Vide");

        var pool = 0;
        if (password.Any(char.IsLower)) pool += 26;
        if (password.Any(char.IsUpper)) pool += 26;
        if (password.Any(char.IsDigit)) pool += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c))) pool += 33;
        var bits = password.Length * Math.Log2(Math.Max(pool, 2));

        var uniqueRatio = (double)password.Distinct().Count() / password.Length;
        bits *= Math.Clamp(uniqueRatio * 1.6, 0.25, 1.0);

        var lower = password.ToLowerInvariant();
        if (Common.Any(lower.Contains)) bits *= 0.45;
        if (HasSequence(lower)) bits *= 0.75;

        var score = bits switch { < 30 => 0, < 45 => 1, < 62 => 2, < 85 => 3, _ => 4 };
        var label = score switch { 0 => "Très faible", 1 => "Faible", 2 => "Correct", 3 => "Fort", _ => "Excellent" };
        return new(score, Math.Round(bits), label);
    }

    private static bool HasSequence(string lower)
    {
        foreach (var seq in Sequences)
            for (var i = 0; i + 4 <= seq.Length; i++)
                if (lower.Contains(seq.AsSpan(i, 4), StringComparison.Ordinal)) return true;
        return false;
    }
}

public static class MasterPasswordPolicy
{
    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < Limits.MasterPasswordMinLength)
            errors.Add($"Le mot de passe maître doit contenir au moins {Limits.MasterPasswordMinLength} caractères.");
        else if (PasswordStrengthEstimator.Estimate(password).Score < 2)
            errors.Add("Ce mot de passe maître est trop prévisible. Utilisez une phrase de passe plus longue ou plus variée.");
        return errors;
    }

    public static void Ensure(string? password)
    {
        var errors = Validate(password);
        if (errors.Count > 0) throw new ValidationFailedException(errors);
    }
}

/// <summary>
/// Détecte un secret probablement collé dans une note : la documentation ne doit pas
/// devenir un moyen de contourner le gestionnaire de secrets (§7).
/// </summary>
public static partial class SecretLeakDetector
{
    [System.Text.RegularExpressions.GeneratedRegex(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----|\b(?:password|passwd|pwd|mot ?de ?passe|mdp|secret|token|api[_-]?key)\b\s*[:=]\s*\S{6,}|\bAKIA[0-9A-Z]{16}\b|\bgh[pousr]_[A-Za-z0-9]{30,}\b|\bglpat-[A-Za-z0-9_\-]{20,}\b|\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex Pattern();

    public static bool LooksLikeSecret(string? content)
        => !string.IsNullOrEmpty(content) && Pattern().IsMatch(content);
}
