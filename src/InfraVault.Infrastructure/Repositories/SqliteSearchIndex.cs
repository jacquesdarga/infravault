using System.Text;
using InfraVault.Application.Interfaces;
using InfraVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace InfraVault.Infrastructure.Repositories;

/// <summary>
/// Index plein texte SQLite FTS5 (tokenizer trigramme : recherche par sous-chaîne, idéale pour les
/// hostnames et les adresses IP). Une table d'association entité → rowid rend les mises à jour O(1).
/// </summary>
public sealed class SqliteSearchIndex(VaultDbFactory factory) : ISearchIndex
{
    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _ready;
    private bool _trigram = true;

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        if (_ready) return connection;

        await _init.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready) return connection;
            await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS search_map (entity_id TEXT PRIMARY KEY, row_id INTEGER NOT NULL);", ct).ConfigureAwait(false);
            const string columns = "entity_type UNINDEXED, entity_id UNINDEXED, category UNINDEXED, icon UNINDEXED, color UNINDEXED, title, subtitle, body, tags";
            try
            {
                await ExecuteAsync(connection, $"CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5({columns}, tokenize='trigram');", ct).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                // SQLite sans tokenizer trigramme : repli sur la recherche par préfixe de mot.
                await ExecuteAsync(connection, $"CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5({columns}, tokenize='unicode61 remove_diacritics 2');", ct).ConfigureAwait(false);
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'search_index';";
            _trigram = ((await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) as string)?.Contains("trigram", StringComparison.OrdinalIgnoreCase) ?? false;
            _ready = true;
        }
        finally
        {
            _init.Release();
        }
        return connection;
    }

    /// <summary>À appeler après un remplacement du fichier de base (restauration).</summary>
    public void Reset() => _ready = false;

    public Task UpsertAsync(SearchDocument document, CancellationToken ct = default) => UpsertManyAsync([document], ct);

    public async Task UpsertManyAsync(IEnumerable<SearchDocument> documents, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using var find = connection.CreateCommand();
        find.Transaction = tx;
        find.CommandText = "SELECT row_id FROM search_map WHERE entity_id = $id;";
        var findId = find.Parameters.Add("$id", SqliteType.Text);

        await using var delete = connection.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM search_index WHERE rowid = $row;";
        var deleteRow = delete.Parameters.Add("$row", SqliteType.Integer);

        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO search_index (entity_type, entity_id, category, icon, color, title, subtitle, body, tags)
            VALUES ($type, $id, $category, $icon, $color, $title, $subtitle, $body, $tags);
            INSERT INTO search_map (entity_id, row_id) VALUES ($id, last_insert_rowid())
            ON CONFLICT(entity_id) DO UPDATE SET row_id = excluded.row_id;
            """;
        var p = new Dictionary<string, SqliteParameter>();
        foreach (var name in new[] { "$type", "$id", "$category", "$icon", "$color", "$title", "$subtitle", "$body", "$tags" })
            p[name] = insert.Parameters.Add(name, SqliteType.Text);

        foreach (var d in documents)
        {
            var id = d.EntityId.ToString("N");
            findId.Value = id;
            if (await find.ExecuteScalarAsync(ct).ConfigureAwait(false) is long row)
            {
                deleteRow.Value = row;
                await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            p["$type"].Value = d.EntityType;
            p["$id"].Value = id;
            p["$category"].Value = d.Category;
            p["$icon"].Value = (object?)d.Icon ?? DBNull.Value;
            p["$color"].Value = (object?)d.Color ?? DBNull.Value;
            p["$title"].Value = d.Title;
            p["$subtitle"].Value = d.Subtitle ?? "";
            p["$body"].Value = d.Body ?? "";
            p["$tags"].Value = d.Tags ?? "";
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM search_index WHERE rowid = (SELECT row_id FROM search_map WHERE entity_id = $id);
            DELETE FROM search_map WHERE entity_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", entityId.ToString("N"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await ExecuteAsync(connection, "DELETE FROM search_index; DELETE FROM search_map;", ct).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM search_map;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<SearchIndexHit>> QueryAsync(string text, int limit, CancellationToken ct = default)
    {
        var terms = (text ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8).ToList();
        if (terms.Count == 0) return [];

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        const string select = "SELECT entity_type, entity_id, category, title, subtitle, icon, color FROM search_index ";

        // Le tokenizer trigramme exige au moins 3 caractères par terme ; en-deçà, on bascule sur LIKE.
        var useMatch = !_trigram || terms.All(t => t.Length >= 3);
        if (useMatch)
        {
            var match = string.Join(" AND ", terms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\"" + (_trigram ? "" : "*")));
            cmd.CommandText = select + "WHERE search_index MATCH $q ORDER BY bm25(search_index, 0, 0, 0, 0, 0, 10.0, 4.0, 1.0, 3.0) LIMIT $limit;";
            cmd.Parameters.AddWithValue("$q", match);
        }
        else
        {
            var where = new StringBuilder();
            for (var i = 0; i < terms.Count; i++)
            {
                if (i > 0) where.Append(" AND ");
                where.Append($"(title LIKE $t{i} ESCAPE '\\' OR subtitle LIKE $t{i} ESCAPE '\\' OR body LIKE $t{i} ESCAPE '\\' OR tags LIKE $t{i} ESCAPE '\\')");
                cmd.Parameters.AddWithValue($"$t{i}", "%" + terms[i].Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            }
            cmd.CommandText = select + "WHERE " + where + " ORDER BY title LIMIT $limit;";
        }
        cmd.Parameters.AddWithValue("$limit", limit);

        var hits = new List<SearchIndexHit>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                hits.Add(new SearchIndexHit(reader.GetString(0), Guid.ParseExact(reader.GetString(1), "N"), reader.GetString(2),
                    reader.GetString(3), reader.IsDBNull(4) || reader.GetString(4).Length == 0 ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }
        catch (SqliteException)
        {
            // Requête FTS non interprétable (ponctuation exotique) : aucun résultat plutôt qu'une erreur.
            return [];
        }
        return hits;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
