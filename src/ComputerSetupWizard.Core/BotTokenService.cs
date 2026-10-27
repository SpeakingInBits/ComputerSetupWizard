using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ComputerSetupWizard.Core;

public sealed record BotToken(
    string Token,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, string> Permissions,
    long InstallationId,
    string RepositorySelection);

/// <summary>Everything about the bot setup except the token itself, so it is safe to show.</summary>
public sealed record BotInfo(
    string AppName,
    string AppSlug,
    string KeyPath,
    long InstallationId,
    string RepositorySelection,
    string Permissions,
    DateTimeOffset TokenExpires,
    string BotName,
    string BotEmail);

/// <summary>
/// Gets SpeakingInBits-Bot installation tokens (valid ~1 hour) by signing an app JWT with the
/// machine's private key. Replaces scripts/Get-BotToken.ps1 without needing OpenSSL.
/// </summary>
public sealed class BotTokenService
{
    static readonly HttpClient Http = CreateClient();

    public BotTokenService(string? keyPath = null)
    {
        KeyPath = keyPath ?? BotConfig.FindNewestKey()
            ?? throw new FileNotFoundException($"No bot private key found. Save it in {BotConfig.KeyDirectory}.");
    }

    public string KeyPath { get; }

    public async Task<BotToken> GetTokenAsync(CancellationToken ct = default)
    {
        var jwt = await CreateJwtAsync(ct);

        using var installation = await SendAsync(HttpMethod.Get, $"orgs/{BotConfig.Org}/installation", jwt, ct);
        var installationId = installation.RootElement.GetProperty("id").GetInt64();
        var selection = installation.RootElement.GetProperty("repository_selection").GetString() ?? "";

        using var token = await SendAsync(HttpMethod.Post, $"app/installations/{installationId}/access_tokens", jwt, ct);
        var root = token.RootElement;
        var permissions = root.GetProperty("permissions").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");

        return new BotToken(
            root.GetProperty("token").GetString()!,
            root.GetProperty("expires_at").GetDateTimeOffset(),
            permissions,
            installationId,
            selection);
    }

    public async Task<BotInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct);

        using var app = await SendAsync(HttpMethod.Get, "app", await CreateJwtAsync(ct), ct);
        var appName = app.RootElement.GetProperty("name").GetString() ?? "";
        var slug = app.RootElement.GetProperty("slug").GetString() ?? "";

        var botLogin = $"{slug}[bot]";
        using var bot = await SendAsync(HttpMethod.Get, $"users/{Uri.EscapeDataString(botLogin)}", jwt: null, ct);
        var botId = bot.RootElement.GetProperty("id").GetInt64();

        return new BotInfo(
            appName,
            slug,
            KeyPath,
            token.InstallationId,
            token.RepositorySelection,
            string.Join(", ", token.Permissions.Select(p => $"{p.Key}={p.Value}")),
            token.ExpiresAt,
            botLogin,
            $"{botId}+{botLogin}@users.noreply.github.com");
    }

    /// <summary>Builds an RS256 app JWT, backdated a minute for clock drift and valid for nine more.</summary>
    internal static string CreateAppJwt(string pem, string appId, DateTimeOffset now)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);

        var seconds = now.ToUnixTimeSeconds();
        var header = Base64Url.EncodeToString("""{"alg":"RS256","typ":"JWT"}"""u8);
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $$"""{"iat":{{seconds - 60}},"exp":{{seconds + 540}},"iss":"{{appId}}"}"""));
        var signingInput = $"{header}.{payload}";
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    async Task<string> CreateJwtAsync(CancellationToken ct) =>
        CreateAppJwt(await File.ReadAllTextAsync(KeyPath, ct), BotConfig.AppId, DateTimeOffset.UtcNow);

    static async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? jwt, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (jwt is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub returned {(int)response.StatusCode} for /{path}: {ErrorMessage(body)}");

        return JsonDocument.Parse(body);
    }

    static string ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? body : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ComputerSetupWizard");
        return client;
    }
}
