using System.Diagnostics;
using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Security;
using InfraVault.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Application.Services;

/// <summary>
/// Recherche globale. Porte sur les métadonnées indexées (FTS5). Les usernames, chiffrés, sont
/// retrouvés par correspondance exacte via un index aveugle HMAC. Le contenu des documents n'est
/// parcouru que sur demande explicite, en mémoire, sans jamais être écrit en clair sur disque.
/// </summary>
public sealed class SearchService(IVaultDbFactory dbFactory, IVaultSession session, ISearchIndex index)
{
    public async Task<SearchResults> SearchAsync(string query, bool includeDocumentContent = false, int limit = 200,
        CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        query = (query ?? "").Trim();
        var watch = Stopwatch.StartNew();
        if (query.Length == 0) return new SearchResults(query, [], 0, TimeSpan.Zero);

        var hits = (await index.QueryAsync(query, limit, ct).ConfigureAwait(false))
            .Select(h => (h.Category, Hit: new SearchHit(h.EntityType, h.EntityId, h.Title, h.Subtitle, h.Icon, h.Color)))
            .ToList();
        var seen = hits.Select(h => (h.Hit.EntityType, h.Hit.EntityId)).ToHashSet();

        await using var db = dbFactory.Create();

        // Username : correspondance exacte par index aveugle, sans déchiffrer quoi que ce soit.
        var blind = session.BlindIndex(query);
        var byUsername = await db.Credentials.AsNoTracking().Where(c => c.UsernameIndex == blind)
            .Select(c => new { c.Id, c.Name, c.CredentialType }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var c in byUsername)
        {
            if (!seen.Add((EntityTypes.Credential, c.Id))) continue;
            hits.Insert(0, (SearchDocuments.CredentialCategory, new SearchHit(EntityTypes.Credential, c.Id, c.Name,
                $"{CredentialTypes.LabelOf(c.CredentialType)} · username correspondant", CredentialTypes.IconOf(c.CredentialType), "amber")));
        }

        if (includeDocumentContent)
        {
            var docs = db.Documentations.AsNoTracking().Select(d => new { d.Id, d.Title, d.DocumentType, d.ContentEncrypted }).AsAsyncEnumerable();
            await foreach (var d in docs.WithCancellation(ct).ConfigureAwait(false))
            {
                if (seen.Contains((EntityTypes.Documentation, d.Id))) continue;
                var content = session.OpenString(d.ContentEncrypted, Aad.DocumentContent(d.Id));
                var at = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                seen.Add((EntityTypes.Documentation, d.Id));
                hits.Add((SearchDocuments.DocumentationCategory, new SearchHit(EntityTypes.Documentation, d.Id, d.Title,
                    $"{d.DocumentType} · {Excerpt(content, at, query.Length)}", "file", "teal")));
            }
        }

        var groups = hits.GroupBy(h => h.Category)
            .Select(g => new SearchGroup(g.Key, g.Select(x => x.Hit).ToList()))
            .OrderBy(g => g.Category == SearchDocuments.CredentialCategory ? 1 : g.Category == SearchDocuments.DocumentationCategory ? 2 : 0)
            .ToList();
        watch.Stop();
        return new SearchResults(query, groups, hits.Count, watch.Elapsed);
    }

    private static string Excerpt(string content, int at, int length)
    {
        var start = Math.Max(0, at - 30);
        var end = Math.Min(content.Length, at + length + 50);
        var text = content[start..end].ReplaceLineEndings(" ");
        return (start > 0 ? "…" : "") + text.Trim() + (end < content.Length ? "…" : "");
    }
}
