using System.Net;
using System.Net.Http.Headers;
using MercadoPagoWebhookProxy;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = WebhookProxyPolicy.MaxBodyBytes;
    options.Listen(IPAddress.Loopback, 8787);
});

builder.Services.AddHttpClient("backend", client =>
{
    client.BaseAddress = new Uri("http://127.0.0.1:5278");
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

app.Run(async context =>
{
    if (!WebhookProxyPolicy.IsAllowedPath(context.Request.Path.Value ?? string.Empty))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (!WebhookProxyPolicy.IsAllowedMethod(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = HttpMethods.Post;
        return;
    }

    if (context.Request.ContentLength is > WebhookProxyPolicy.MaxBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("backend");
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"{WebhookProxyPolicy.Path}{context.Request.QueryString}");

    request.Content = new StreamContent(context.Request.Body);
    if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
    {
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
    }

    CopyHeader(context, request, "x-signature");
    CopyHeader(context, request, "x-request-id");
    CopyHeader(context, request, "accept");

    using var response = await client.SendAsync(
        request,
        HttpCompletionOption.ResponseHeadersRead,
        context.RequestAborted);

    context.Response.StatusCode = (int)response.StatusCode;
    if (response.Content.Headers.ContentType is { } contentType)
    {
        context.Response.ContentType = contentType.ToString();
    }

    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
});

await app.RunAsync();

static void CopyHeader(HttpContext context, HttpRequestMessage request, string name)
{
    if (context.Request.Headers.TryGetValue(name, out var value))
    {
        request.Headers.TryAddWithoutValidation(name, value.ToArray());
    }
}
