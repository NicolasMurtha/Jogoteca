using System.Globalization;
using System.IO;
using System.Text;
using Ludoteca.Models;

namespace Ludoteca.Services;

public sealed class DataExportService(DatabaseService database)
{
    public async Task<string> ExportGamesCsvAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"jogos-{timestamp}.csv");

        var games = (await database.GetGamesAsync(null))
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "ID",
                "Nome",
                "Fonte",
                "ID externo",
                "Categoria",
                "Lancamento",
                "Ano",
                "Nota",
                "Review",
                "Listas",
                "Adicionado em",
                "Descricao",
                "Capa"
            }
        };

        foreach (var game in games)
        {
            var listNames = (await database.GetMembershipsAsync(game.Id))
                .Where(membership => membership.IsSelected)
                .Select(membership => membership.Name);

            rows.Add(
                new[]
                {
                    game.Id.ToString(CultureInfo.InvariantCulture),
                    game.Name,
                    FormatSource(game.Source),
                    game.ExternalId,
                    game.CategoryDisplay,
                    game.ReleaseDateDisplay,
                    game.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    game.UserRating?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
                    game.Review,
                    string.Join(", ", listNames),
                    FormatDateTime(game.AddedAt),
                    game.Description,
                    game.CoverDisplay
                });
        }

        await WriteCsvAsync(path, rows);
        return path;
    }

    private static async Task WriteCsvAsync(string path, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', row.Select(EscapeCsv)));
        }

        await File.WriteAllTextAsync(
            path,
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string EscapeCsv(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        if (!normalized.Contains(';') &&
            !normalized.Contains('"') &&
            !normalized.Contains('\n') &&
            !normalized.Contains('\r'))
        {
            return normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }

    private static string FormatDateTime(string value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var date)
            ? date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string FormatSource(string source)
    {
        return source.ToLowerInvariant() switch
        {
            "igdb" => "IGDB",
            "rawg" => "RAWG",
            _ => "MANUAL"
        };
    }
}
