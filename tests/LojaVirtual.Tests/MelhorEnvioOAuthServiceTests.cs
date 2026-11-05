using System.Net;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using LojaVirtual.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class MelhorEnvioOAuthServiceTests
{
    [Fact]
    public async Task Valid_authorization_exchanges_code_and_persists_tokens_without_exposing_them()
    {
        await using var database = new TestDatabase();
        var handler = new OAuthHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                { "access_token": "access-test", "refresh_token": "refresh-test", "expires_in": 1800 }
                """)
        });
        var service = CreateService(database, handler);

        var authorizationUri = await service.CreateAuthorizationUriAsync();
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorizationUri.Query)["state"].ToString();

        await service.ExchangeAuthorizationCodeAsync(state, "code-test");

        var tokenStore = new MelhorEnvioTokenStore(database.Factory, database.DataProtection);
        var token = await tokenStore.GetAsync();
        Assert.Equal("access-test", token?.AccessToken);
        Assert.Equal("refresh-test", token?.RefreshToken);
        Assert.DoesNotContain("access-test", handler.LastRequestBody);
        Assert.DoesNotContain("refresh-test", handler.LastRequestBody);
        Assert.Contains("code-test", handler.LastRequestBody);
    }

    [Fact]
    public async Task Invalid_state_is_rejected_before_provider_call()
    {
        await using var database = new TestDatabase();
        var handler = new OAuthHandler(_ => throw new InvalidOperationException("provider não deveria ser chamado"));
        var service = CreateService(database, handler);

        var exception = await Assert.ThrowsAsync<ShippingOAuthException>(() =>
            service.ExchangeAuthorizationCodeAsync("invalid-state", "code-test"));

        Assert.Equal("oauth_invalid_state", exception.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Expired_state_is_rejected_and_removed()
    {
        await using var database = new TestDatabase();
        var service = CreateService(database, new OAuthHandler(_ => throw new InvalidOperationException()));
        var authorizationUri = await service.CreateAuthorizationUriAsync();
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorizationUri.Query)["state"].ToString();

        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            var entry = await context.Set<DistributedCacheEntry>().SingleAsync(item => item.Id.Contains(state));
            entry.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<ShippingOAuthException>(() =>
            service.ExchangeAuthorizationCodeAsync(state, "code-test"));

        Assert.Equal("oauth_invalid_state", exception.Code);
        await using var verify = await database.Factory.CreateDbContextAsync();
        Assert.DoesNotContain(await verify.Set<DistributedCacheEntry>().ToListAsync(), item => item.Id.Contains(state));
    }

    [Fact]
    public async Task State_cannot_be_reused()
    {
        await using var database = new TestDatabase();
        var handler = new OAuthHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"access_token\": \"access\", \"refresh_token\": \"refresh\" }")
        });
        var service = CreateService(database, handler);
        var authorizationUri = await service.CreateAuthorizationUriAsync();
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorizationUri.Query)["state"].ToString();

        await service.ExchangeAuthorizationCodeAsync(state, "code-test");
        var exception = await Assert.ThrowsAsync<ShippingOAuthException>(() =>
            service.ExchangeAuthorizationCodeAsync(state, "code-test"));

        Assert.Equal("oauth_invalid_state", exception.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Authorization_code_cannot_be_reused_with_another_state()
    {
        await using var database = new TestDatabase();
        var handler = new OAuthHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"access_token\": \"access\", \"refresh_token\": \"refresh\" }")
        });
        var service = CreateService(database, handler);
        var firstUri = await service.CreateAuthorizationUriAsync();
        var secondUri = await service.CreateAuthorizationUriAsync();
        var firstState = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(firstUri.Query)["state"].ToString();
        var secondState = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(secondUri.Query)["state"].ToString();

        await service.ExchangeAuthorizationCodeAsync(firstState, "code-test");
        var exception = await Assert.ThrowsAsync<ShippingOAuthException>(() =>
            service.ExchangeAuthorizationCodeAsync(secondState, "code-test"));

        Assert.Equal("oauth_code_reused", exception.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Provider_error_does_not_leak_provider_response()
    {
        await using var database = new TestDatabase();
        var handler = new OAuthHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{ \"error\": \"invalid_grant\", \"client_secret\": \"hidden\" }")
        });
        var service = CreateService(database, handler);
        var authorizationUri = await service.CreateAuthorizationUriAsync();
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorizationUri.Query)["state"].ToString();

        var exception = await Assert.ThrowsAsync<ShippingOAuthException>(() =>
            service.ExchangeAuthorizationCodeAsync(state, "code-test"));

        Assert.Equal("oauth_provider_error", exception.Code);
        Assert.Equal(400, exception.ProviderStatusCode);
        Assert.DoesNotContain("hidden", exception.Message);
    }

    [Fact]
    public async Task OAuth_controller_logs_only_safe_error_fields_and_keeps_response_generic()
    {
        const string state = "state-secret";
        const string code = "authorization-code-secret";
        const string token = "access-token-secret";
        var logger = new RecordingLogger<MelhorEnvioOAuthController>();
        var controller = new MelhorEnvioOAuthController(
            new ThrowingOAuthService(new ShippingOAuthException(
                "oauth_provider_error",
                "provider response must not be logged",
                502,
                providerStatusCode: 401)),
            logger);

        var result = await controller.Callback(state, code, null, CancellationToken.None);
        var response = Assert.IsType<ObjectResult>(result);
        var responseText = JsonSerializer.Serialize(response.Value);
        var logText = string.Join("\n", logger.Messages);

        Assert.Equal(503, response.StatusCode);
        Assert.Contains("oauth_failed", responseText);
        Assert.Contains("oauth_provider_error", logText);
        Assert.Contains("401", logText);
        Assert.DoesNotContain(state, logText);
        Assert.DoesNotContain(code, logText);
        Assert.DoesNotContain(token, logText);
        Assert.DoesNotContain("provider response must not be logged", logText);
    }

    private static MelhorEnvioOAuthService CreateService(
        TestDatabase database,
        HttpMessageHandler handler)
    {
        var dataProtection = database.DataProtection;
        var options = Options.Create(new ShippingOptions
        {
            BaseUrl = "https://sandbox.melhorenvio.com.br",
            ClientId = "client-test",
            ClientSecret = "secret-test",
            RedirectUri = "https://angel-acessorios-api-hml.onrender.com/api/integrations/melhorenvio/callback",
            AuthorizationUrl = "https://sandbox.melhorenvio.com.br/oauth/authorize",
            OAuthScope = "shipping-calculate",
            UserAgent = "Angel OAuth Test"
        });
        var tokenStore = new MelhorEnvioTokenStore(database.Factory, dataProtection);
        return new MelhorEnvioOAuthService(
            new HttpClient(handler),
            database.Factory,
            dataProtection,
            tokenStore,
            options);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public readonly IDbContextFactory<AppDbContext> Factory;
        public readonly IDataProtectionProvider DataProtection = DataProtectionProvider.Create("Angel OAuth Database Tests");

        public TestDatabase()
        {
            _connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            Factory = new TestDbContextFactory(options);
            using var context = new AppDbContext(options);
            context.Database.EnsureCreated();
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private sealed class OAuthHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private sealed class ThrowingOAuthService(ShippingOAuthException exception) : IMelhorEnvioOAuthService
    {
        public Task<Uri> CreateAuthorizationUriAsync(CancellationToken cancellationToken = default) =>
            throw exception;

        public Task ExchangeAuthorizationCodeAsync(string state, string code, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, null));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
