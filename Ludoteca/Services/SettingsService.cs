using System.Security.Cryptography;
using System.Text;
using Ludoteca.Models;

namespace Ludoteca.Services;

public sealed class SettingsService(DatabaseService database)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Ludoteca.Local.Settings.v1");

    public async Task<AppSettings> LoadAsync()
    {
        var stored = await database.GetSettingsAsync();
        return new AppSettings
        {
            IgdbClientId = Unprotect(GetValue(stored, "igdb_client_id")),
            IgdbClientSecret = Unprotect(GetValue(stored, "igdb_client_secret")),
            RawgApiKey = Unprotect(GetValue(stored, "rawg_api_key")),
            Theme = NormalizeTheme(GetValue(stored, "app_theme")),
            HideApiWelcome = GetValue(stored, "hide_api_welcome") == "1"
        };
    }

    public Task SaveAsync(AppSettings settings)
    {
        IReadOnlyDictionary<string, string> values = new Dictionary<string, string>
        {
            ["igdb_client_id"] = Protect(settings.IgdbClientId),
            ["igdb_client_secret"] = Protect(settings.IgdbClientSecret),
            ["rawg_api_key"] = Protect(settings.RawgApiKey),
            ["app_theme"] = NormalizeTheme(settings.Theme),
            ["hide_api_welcome"] = settings.HideApiWelcome ? "1" : "0"
        };

        return database.SaveSettingsAsync(values);
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string NormalizeTheme(string value)
    {
        return string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase)
            ? "dark"
            : "light";
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(value.Trim());
        return Convert.ToBase64String(
            ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
