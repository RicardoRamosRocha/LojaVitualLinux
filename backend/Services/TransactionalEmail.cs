using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Services;

public sealed class ResendOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string? ReplyTo { get; set; }
}

public sealed record TransactionalEmailMessage(
    string To,
    string Subject,
    string HtmlBody);

public interface ITransactionalEmailSender
{
    Task SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class TransactionalEmailException : Exception
{
    public TransactionalEmailException(string message) : base(message) { }
}

public sealed class ResendEmailSender : ITransactionalEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;

    public ResendEmailSender(HttpClient httpClient, IOptions<ResendOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.From))
        {
            throw new TransactionalEmailException("O envio transacional de e-mail não está configurado.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendEmailRequest
            {
                From = _options.From,
                To = [message.To],
                Subject = message.Subject,
                Html = message.HtmlBody,
                ReplyTo = string.IsNullOrWhiteSpace(_options.ReplyTo) ? null : [_options.ReplyTo]
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new TransactionalEmailException("O provedor de e-mail não está disponível.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransactionalEmailException("O provedor de e-mail excedeu o tempo limite.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TransactionalEmailException("O provedor de e-mail não aceitou a mensagem.");
            }
        }
    }

    private sealed class ResendEmailRequest
    {
        [JsonPropertyName("from")]
        public string From { get; init; } = string.Empty;

        [JsonPropertyName("to")]
        public string[] To { get; init; } = [];

        [JsonPropertyName("subject")]
        public string Subject { get; init; } = string.Empty;

        [JsonPropertyName("html")]
        public string Html { get; init; } = string.Empty;

        [JsonPropertyName("reply_to")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? ReplyTo { get; init; }
    }
}
