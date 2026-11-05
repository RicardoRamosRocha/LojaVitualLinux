using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LojaVirtual.Data;
using LojaVirtual.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Services;

public sealed record MelhorEnvioToken(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset? ExpiresAtUtc);

public interface IMelhorEnvioTokenStore
{
    Task<MelhorEnvioToken?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MelhorEnvioToken token, CancellationToken cancellationToken = default);
}

public interface IMelhorEnvioOAuthService
{
    Task<Uri> CreateAuthorizationUriAsync(CancellationToken cancellationToken = default);
    Task ExchangeAuthorizationCodeAsync(string state, string code, CancellationToken cancellationToken = default);
}

public sealed class MelhorEnvioTokenStore : IMelhorEnvioTokenStore
{
    private const string TokenKey = "angel:melhorenvio:oauth-token";
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDataProtector _protector;

    public MelhorEnvioTokenStore(
        IDbContextFactory<AppDbContext> contextFactory,
        IDataProtectionProvider dataProtectionProvider)
    {
        _contextFactory = contextFactory;
        _protector = dataProtectionProvider.CreateProtector("AngelAcessorios.MelhorEnvioOAuthToken.v1");
    }

    public async Task<MelhorEnvioToken?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entry = await context.Set<DistributedCacheEntry>()
            .SingleOrDefaultAsync(item => item.Id == TokenKey, cancellationToken);
        if (entry is null || entry.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            if (entry is not null)
            {
                context.Remove(entry);
                await context.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        try
        {
            var payload = _protector.Unprotect(entry.Value);
            return JsonSerializer.Deserialize<MelhorEnvioToken>(payload);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(MelhorEnvioToken token, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(token);
        var protectedPayload = _protector.Protect(payload);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var entry = await context.Set<DistributedCacheEntry>()
            .SingleOrDefaultAsync(item => item.Id == TokenKey, cancellationToken);
        if (entry is null)
        {
            entry = new DistributedCacheEntry { Id = TokenKey };
            context.Add(entry);
        }

        entry.Value = protectedPayload;
        // The access-token expiration must not expire this record: the refresh
        // token must remain available for a later renewal.
        entry.AbsoluteExpiration = null;
        entry.ExpiresAt = null;
        entry.SlidingExpiration = null;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class MelhorEnvioOAuthService : IMelhorEnvioOAuthService
{
    private const string StatePrefix = "angel:melhorenvio:oauth-state:";
    private const string CodePrefix = "angel:melhorenvio:oauth-code:";
    private readonly HttpClient _httpClient;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDataProtector _stateProtector;
    private readonly IMelhorEnvioTokenStore _tokenStore;
    private readonly ShippingOptions _options;

    public MelhorEnvioOAuthService(
        HttpClient httpClient,
        IDbContextFactory<AppDbContext> contextFactory,
        IDataProtectionProvider dataProtectionProvider,
        IMelhorEnvioTokenStore tokenStore,
        IOptions<ShippingOptions> options)
    {
        _httpClient = httpClient;
        _contextFactory = contextFactory;
        _stateProtector = dataProtectionProvider.CreateProtector("AngelAcessorios.MelhorEnvioOAuthState.v1");
        _tokenStore = tokenStore;
        _options = options.Value;
    }

    public async Task<Uri> CreateAuthorizationUriAsync(CancellationToken cancellationToken = default)
    {
        var clientId = Required(_options.ClientId, "Client ID");
        var redirectUri = RequiredHttps(_options.RedirectUri, "Redirect URI");
        var scope = Required(_options.OAuthScope, "OAuth scope");
        var authorizationUrl = RequiredHttps(
            string.IsNullOrWhiteSpace(_options.AuthorizationUrl)
                ? $"{_options.BaseUrl.TrimEnd('/')}/oauth/authorize"
                : _options.AuthorizationUrl,
            "Authorization URL");

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.OAuthStateLifetimeMinutes, 1, 15));
        await SaveStateAsync(state, expiresAtUtc, cancellationToken);

        var builder = new UriBuilder(authorizationUrl);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = state
        };
        builder.Query = string.Join("&", query.Select(item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        return builder.Uri;
    }

    public async Task ExchangeAuthorizationCodeAsync(
        string state,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ShippingOAuthException("oauth_invalid_request", "A autorização não pôde ser validada.", 400);
        }

        var stateRecord = await ConsumeStateAsync(state.Trim(), cancellationToken);
        if (stateRecord is null || stateRecord.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ShippingOAuthException("oauth_invalid_state", "A autorização não pôde ser validada.", 400);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ShippingOAuthException("oauth_provider_denied", "A autorização não pôde ser concluída.", 400);
        }

        if (!await TryClaimAuthorizationCodeAsync(code.Trim(), cancellationToken))
        {
            throw new ShippingOAuthException("oauth_code_reused", "A autorização não pôde ser concluída.", 400);
        }

        var clientId = Required(_options.ClientId, "Client ID");
        var clientSecret = Required(_options.ClientSecret, "Client Secret");
        var redirectUri = RequiredHttps(_options.RedirectUri, "Redirect URI");
        var tokenUrl = RequiredHttps($"{_options.BaseUrl.TrimEnd('/')}/oauth/token", "Token URL");

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["code"] = code.Trim()
            })
        };
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ShippingOAuthException("oauth_timeout", "O serviço de autorização está indisponível.", 504, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ShippingOAuthException("oauth_unavailable", "O serviço de autorização está indisponível.", 503, exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ShippingOAuthException(
                    "oauth_provider_error",
                    "O serviço de autorização rejeitou a solicitação.",
                    502,
                    providerStatusCode: (int)response.StatusCode);
            }

            MelhorEnvioTokenResponse? token;
            try
            {
                token = await response.Content.ReadFromJsonAsync<MelhorEnvioTokenResponse>(cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new ShippingOAuthException(
                    "oauth_invalid_response",
                    "O serviço de autorização retornou uma resposta inválida.",
                    502,
                    exception,
                    (int)response.StatusCode);
            }

            if (string.IsNullOrWhiteSpace(token?.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new ShippingOAuthException(
                    "oauth_invalid_response",
                    "O serviço de autorização retornou uma resposta inválida.",
                    502,
                    providerStatusCode: (int)response.StatusCode);
            }

            await _tokenStore.SaveAsync(new MelhorEnvioToken(
                token.AccessToken,
                token.RefreshToken,
                token.ExpiresIn is > 0
                    ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value)
                    : null), cancellationToken);
        }
    }

    private async Task SaveStateAsync(string state, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        var payload = _stateProtector.Protect(JsonSerializer.SerializeToUtf8Bytes(new OAuthStateRecord(expiresAtUtc)));
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Set<DistributedCacheEntry>().Add(new DistributedCacheEntry
        {
            Id = StatePrefix + state,
            Value = payload,
            AbsoluteExpiration = expiresAtUtc,
            ExpiresAt = expiresAtUtc
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<OAuthStateRecord?> ConsumeStateAsync(string state, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var entry = await context.Set<DistributedCacheEntry>()
            .SingleOrDefaultAsync(item => item.Id == StatePrefix + state, cancellationToken);
        if (entry is null) return null;

        context.Remove(entry);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (entry.ExpiresAt is not { } expiresAt || expiresAt <= DateTimeOffset.UtcNow) return null;
        try
        {
            return JsonSerializer.Deserialize<OAuthStateRecord>(_stateProtector.Unprotect(entry.Value));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private async Task<bool> TryClaimAuthorizationCodeAsync(string code, CancellationToken cancellationToken)
    {
        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var key = CodePrefix + codeHash;
        var existing = await context.Set<DistributedCacheEntry>()
            .SingleOrDefaultAsync(item => item.Id == key, cancellationToken);
        if (existing is not null && existing.ExpiresAt is { } existingExpiration && existingExpiration > DateTimeOffset.UtcNow)
        {
            return false;
        }

        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15);
        if (existing is not null)
        {
            existing.Value = [];
            existing.AbsoluteExpiration = expiresAtUtc;
            existing.ExpiresAt = expiresAtUtc;
        }
        else
        {
            context.Set<DistributedCacheEntry>().Add(new DistributedCacheEntry
            {
                Id = key,
                Value = [],
                AbsoluteExpiration = expiresAtUtc,
                ExpiresAt = expiresAtUtc
            });
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ShippingOAuthException("oauth_configuration_missing", $"Configuração OAuth incompleta: {name}.", 503)
            : value.Trim();

    private static string RequiredHttps(string? value, string name)
    {
        var normalized = Required(value, name);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ShippingOAuthException("oauth_configuration_invalid", $"Configuração OAuth inválida: {name}.", 503);
        }

        return uri.ToString().TrimEnd('/');
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private sealed record OAuthStateRecord(DateTimeOffset ExpiresAtUtc);

    private sealed class MelhorEnvioTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }
}

public sealed class ShippingOAuthException : Exception
{
    public ShippingOAuthException(
        string code,
        string message,
        int statusCode,
        Exception? innerException = null,
        int? providerStatusCode = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        ProviderStatusCode = providerStatusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public int? ProviderStatusCode { get; }
}
