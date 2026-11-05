using LojaVirtual.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class MelhorEnvioHttpClientTests
{
    [Fact]
    public void Connectivity_http_client_can_be_created_with_valid_user_agent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient<IMelhorEnvioConnectivityDiagnostics, MelhorEnvioConnectivityDiagnostics>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                MelhorEnvioConnectivityDiagnostics.ConnectivityUserAgent);
        });

        using var provider = services.BuildServiceProvider();

        var diagnostics = provider.GetRequiredService<IMelhorEnvioConnectivityDiagnostics>();

        Assert.NotNull(diagnostics);
    }
}
