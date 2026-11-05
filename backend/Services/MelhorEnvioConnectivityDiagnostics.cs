using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Services;

public sealed record MelhorEnvioConnectivityDiagnostic(
    string Category,
    int? HttpStatusCode,
    long DurationMilliseconds,
    string? TlsVersion);

public interface IMelhorEnvioConnectivityDiagnostics
{
    Task<MelhorEnvioConnectivityDiagnostic> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class MelhorEnvioConnectivityDiagnostics : IMelhorEnvioConnectivityDiagnostics
{
    private const string SandboxHost = "sandbox.melhorenvio.com.br";
    private const int HttpsPort = 443;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MelhorEnvioConnectivityDiagnostics> _logger;

    public MelhorEnvioConnectivityDiagnostics(
        HttpClient httpClient,
        ILogger<MelhorEnvioConnectivityDiagnostics> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MelhorEnvioConnectivityDiagnostic> CheckAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string? tlsVersion = null;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(SandboxHost, cancellationToken);
            if (addresses.Length == 0)
            {
                return Complete("dns", null, stopwatch, tlsVersion);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Complete("timeout", null, stopwatch, tlsVersion);
        }
        catch (SocketException)
        {
            return Complete("dns", null, stopwatch, tlsVersion);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Complete("diagnostic_error", null, stopwatch, tlsVersion);
        }

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(SandboxHost, HttpsPort, cancellationToken);
            await using var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = SandboxHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cancellationToken);
            tlsVersion = sslStream.SslProtocol.ToString();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Complete("timeout", null, stopwatch, tlsVersion);
        }
        catch (AuthenticationException)
        {
            return Complete("tls", null, stopwatch, tlsVersion);
        }
        catch (SocketException)
        {
            return Complete("tcp", null, stopwatch, tlsVersion);
        }
        catch (IOException)
        {
            return Complete("tls", null, stopwatch, tlsVersion);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Complete("diagnostic_error", null, stopwatch, tlsVersion);
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                "https://sandbox.melhorenvio.com.br/",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return Complete("http", (int)response.StatusCode, stopwatch, tlsVersion);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Complete("timeout", null, stopwatch, tlsVersion);
        }
        catch (HttpRequestException exception)
        {
            return Complete(ClassifyHttpFailure(exception), null, stopwatch, tlsVersion);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Complete("diagnostic_error", null, stopwatch, tlsVersion);
        }
    }

    private MelhorEnvioConnectivityDiagnostic Complete(
        string category,
        int? httpStatusCode,
        Stopwatch stopwatch,
        string? tlsVersion)
    {
        stopwatch.Stop();
        var diagnostic = new MelhorEnvioConnectivityDiagnostic(
            category,
            httpStatusCode,
            stopwatch.ElapsedMilliseconds,
            tlsVersion);

        _logger.LogInformation(
            "Melhor Envio connectivity diagnostic. Category={Category}; HttpStatusCode={HttpStatusCode}; DurationMilliseconds={DurationMilliseconds}; TlsVersion={TlsVersion}",
            diagnostic.Category,
            diagnostic.HttpStatusCode,
            diagnostic.DurationMilliseconds,
            diagnostic.TlsVersion);

        return diagnostic;
    }

    private static string ClassifyHttpFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is WebException webException && webException.Status is
                WebExceptionStatus.ProxyNameResolutionFailure)
            {
                return "proxy";
            }

            if (current is AuthenticationException)
            {
                return "tls";
            }

            if (current is SocketException)
            {
                return "tcp";
            }
        }

        return "connection";
    }
}
