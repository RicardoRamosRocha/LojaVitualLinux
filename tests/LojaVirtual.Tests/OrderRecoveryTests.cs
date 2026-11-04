using System.Text.RegularExpressions;
using System.Net;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class OrderRecoveryTests
{
    [Fact]
    public async Task Unknown_order_or_email_returns_generic_result_without_sending()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var sender = new RecordingEmailSender();
        var service = new OrderRecoveryService(context, new FakeOrderAccessService(), sender);

        var result = await service.RequestAsync("unknown@example.com");

        Assert.True(result.Accepted);
        Assert.Empty(sender.Messages);
        Assert.Empty(await context.OrderAccessRecoveryTokens.ToListAsync());
    }

    [Fact]
    public async Task Matching_email_sends_one_time_token_and_persists_only_hash()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        SeedOrder(context, 22, "cliente@example.com");
        var sender = new RecordingEmailSender();
        var service = new OrderRecoveryService(context, new FakeOrderAccessService(), sender);

        await service.RequestAsync(" CLIENTE@example.com ");

        var message = Assert.Single(sender.Messages);
        var token = ExtractToken(message.HtmlBody);
        var stored = Assert.Single(await context.OrderAccessRecoveryTokens.ToListAsync());
        Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal(22, stored.OrderId);
        Assert.True(stored.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task Matching_email_creates_one_link_per_order_without_exposing_order_lookup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        SeedOrder(context, 22, "cliente@example.com");
        SeedOrder(context, 23, "cliente@example.com");
        var sender = new RecordingEmailSender();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:BaseUrl"] = "https://store.example.test"
            })
            .Build();
        var service = new OrderRecoveryService(context, new FakeOrderAccessService(), sender, configuration);

        await service.RequestAsync("cliente@example.com");

        var message = Assert.Single(sender.Messages);
        Assert.Equal(2, await context.OrderAccessRecoveryTokens.CountAsync());
        Assert.Contains("recoveryOrderId=22", message.HtmlBody);
        Assert.Contains("recoveryOrderId=23", message.HtmlBody);
        Assert.DoesNotContain("cliente@example.com", message.HtmlBody);
    }

    [Fact]
    public async Task Valid_token_emits_existing_order_access_cookie_once()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        SeedOrder(context, 22, "cliente@example.com");
        var sender = new RecordingEmailSender();
        var access = new FakeOrderAccessService();
        var service = new OrderRecoveryService(context, access, sender);
        await service.RequestAsync("cliente@example.com");
        var token = ExtractToken(Assert.Single(sender.Messages).HtmlBody);

        var http = new DefaultHttpContext();
        var result = await service.ConfirmAsync(22, token, http.Request, http.Response);

        Assert.True(result.Confirmed);
        Assert.Equal(1, access.PrepareCalls);
        Assert.True(access.CookieWritten);
        Assert.NotNull((await context.OrderAccessRecoveryTokens.AsNoTracking().SingleAsync()).UsedAtUtc);
    }

    [Fact]
    public async Task Invalid_expired_and_reused_tokens_are_rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        SeedOrder(context, 22, "cliente@example.com");
        var sender = new RecordingEmailSender();
        var access = new FakeOrderAccessService();
        var service = new OrderRecoveryService(context, access, sender);
        await service.RequestAsync("cliente@example.com");
        var token = ExtractToken(Assert.Single(sender.Messages).HtmlBody);
        var http = new DefaultHttpContext();

        Assert.False((await service.ConfirmAsync(22, "invalid-token", http.Request, http.Response)).Confirmed);
        Assert.True((await service.ConfirmAsync(22, token, http.Request, http.Response)).Confirmed);
        Assert.False((await service.ConfirmAsync(22, token, http.Request, http.Response)).Confirmed);

        await service.RequestAsync("cliente@example.com");
        var expiredToken = ExtractToken(sender.Messages[^1].HtmlBody);
        var recovery = await context.OrderAccessRecoveryTokens.OrderByDescending(item => item.Id).FirstAsync();
        recovery.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        Assert.False((await service.ConfirmAsync(22, expiredToken, http.Request, http.Response)).Confirmed);
    }

    [Fact]
    public async Task Resend_failure_keeps_generic_response_and_does_not_log_token()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        SeedOrder(context, 22, "cliente@example.com");
        var sender = new FailingEmailSender();
        var service = new OrderRecoveryService(context, new FakeOrderAccessService(), sender);

        var result = await service.RequestAsync("cliente@example.com");

        Assert.True(result.Accepted);
        Assert.Single(await context.OrderAccessRecoveryTokens.ToListAsync());
        Assert.DoesNotContain("token", sender.LastExceptionMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simultaneous_confirmation_can_consume_a_token_only_once()
    {
        const string connectionString = "Data Source=file:recovery-concurrency;Mode=Memory;Cache=Shared";
        await using var rootConnection = new SqliteConnection(connectionString);
        await rootConnection.OpenAsync();
        await using var rootContext = CreateContext(rootConnection);
        SeedOrder(rootContext, 22, "cliente@example.com");
        var sender = new RecordingEmailSender();
        var rootService = new OrderRecoveryService(rootContext, new FakeOrderAccessService(), sender);
        await rootService.RequestAsync("cliente@example.com");
        var token = ExtractToken(Assert.Single(sender.Messages).HtmlBody);

        await using var firstConnection = new SqliteConnection(connectionString);
        await using var secondConnection = new SqliteConnection(connectionString);
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstContext = CreateContext(firstConnection);
        await using var secondContext = CreateContext(secondConnection);

        var first = ConfirmSafely(new OrderRecoveryService(firstContext, new FakeOrderAccessService(), new RecordingEmailSender()), token);
        var second = ConfirmSafely(new OrderRecoveryService(secondContext, new FakeOrderAccessService(), new RecordingEmailSender()), token);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(result => result));
    }

    private static async Task<bool> ConfirmSafely(OrderRecoveryService service, string token)
    {
        try
        {
            var http = new DefaultHttpContext();
            return (await service.ConfirmAsync(22, token, http.Request, http.Response)).Confirmed;
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return false;
        }
    }

    [Fact]
    public void Recovery_rate_limiter_applies_to_client_and_identifier()
    {
        var limiter = new OrderRecoveryRateLimiter();
        for (var attempt = 0; attempt < 5; attempt++) Assert.True(limiter.TryAcquire("127.0.0.1", "22:user@example.com"));
        Assert.False(limiter.TryAcquire("127.0.0.1", "22:user@example.com"));
        Assert.True(limiter.TryAcquire("127.0.0.1", "23:user@example.com"));
    }

    [Fact]
    public async Task Resend_sender_uses_simulated_http_without_external_network()
    {
        var handler = new ResendHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendEmailSender(httpClient, Options.Create(new ResendOptions
        {
            Enabled = true,
            ApiKey = "test-only-key",
            From = "Angel <test@example.com>",
            ReplyTo = "reply@example.com"
        }));

        await sender.SendAsync(new TransactionalEmailMessage("recipient@example.com", "Subject", "<p>Body</p>"));

        Assert.Equal("/emails", handler.Path);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-only-key", handler.AuthorizationParameter);
        Assert.Contains("recipient@example.com", handler.Body);
    }

    private static AppDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static void SeedOrder(AppDbContext context, int id, string email)
    {
        context.Orders.Add(new Order { Id = id, Email = email, CustomerName = "Cliente", Status = "Pendente", Total = 10m });
        context.SaveChanges();
    }

    private static string ExtractToken(string html) =>
        Regex.Match(html, @"<strong>([^<]+)</strong>").Groups[1].Value;

    private sealed class RecordingEmailSender : ITransactionalEmailSender
    {
        public List<TransactionalEmailMessage> Messages { get; } = [];
        public Task SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEmailSender : ITransactionalEmailSender
    {
        public string LastExceptionMessage { get; private set; } = "provider failure";

        public Task SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken = default)
        {
            LastExceptionMessage = "provider failure";
            throw new TransactionalEmailException(LastExceptionMessage);
        }
    }

    private sealed class ResendHandler : HttpMessageHandler
    {
        public string Path { get; private set; } = string.Empty;
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath ?? string.Empty;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class FakeOrderAccessService : IOrderAccessCredentialService
    {
        public int PrepareCalls { get; private set; }
        public bool CookieWritten { get; private set; }

        public Task<PreparedOrderAccess> PrepareAsync(int orderId, HttpRequest request, CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            return Task.FromResult(new PreparedOrderAccess([], DateTime.UtcNow.AddDays(30), null));
        }

        public Task<IReadOnlyList<AuthorizedOrderAccess>> ResolveAsync(HttpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedOrderAccess>>([]);

        public Task RevokeAllAsync(HttpRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void WriteCookie(HttpResponse response, PreparedOrderAccess access) => CookieWritten = true;
        public void DeleteCookie(HttpResponse response) { }
    }
}
