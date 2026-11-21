using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMagasin.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMagasin.Tests;

/// <summary>
/// Application lancée en mémoire avec une base PostgreSQL temporaire dédiée (créée puis supprimée).
/// Serveur : variable TEST_PG_CONNECTION (défaut : localhost, utilisateur postgres, mot de passe postgres).
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private static readonly string ServerConnection =
        Environment.GetEnvironmentVariable("TEST_PG_CONNECTION") ?? "Host=localhost;Username=postgres;Password=postgres";

    private readonly string _dbName = $"gestionmagasin_test_{Guid.NewGuid():N}";

    public const string AdminPassword = "AdminPass123!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(ServerConnection) { Database = _dbName, Pooling = false };
        builder.UseSetting("ConnectionStrings:Default", cs.ToString());
        builder.UseSetting("Admin:Username", "admin");
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Admin:DisplayName", "Admin");
    }

    public T Db<T>(Func<AppDbContext, T> action)
    {
        using var scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public ApiClient NewClient() => new(this);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            var admin = new Npgsql.NpgsqlConnectionStringBuilder(ServerConnection) { Database = "postgres", Pooling = false };
            using var connection = new Npgsql.NpgsqlConnection(admin.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
            command.ExecuteNonQuery();
        }
        catch (Npgsql.NpgsqlException) { /* serveur indisponible : rien à nettoyer */ }
    }
}

/// <summary>Client HTTP avec cookies et jeton CSRF (cookie XSRF-TOKEN renvoyé dans X-XSRF-TOKEN), comme Angular.</summary>
public sealed class ApiClient : IDisposable
{
    private sealed class CookieHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public Dictionary<string, string> Cookies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Cookies.Count > 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", Cookies.Select(c => $"{c.Key}={c.Value}")));
            var response = await base.SendAsync(request, ct);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var header in values)
                {
                    var pair = header.Split(';')[0];
                    var i = pair.IndexOf('=');
                    if (i <= 0) continue;
                    var name = pair[..i];
                    var value = pair[(i + 1)..];
                    if (header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) || value.Length == 0)
                        Cookies.Remove(name);
                    else
                        Cookies[name] = value;
                }
            }
            return response;
        }
    }

    private readonly CookieHandler _handler;
    private readonly HttpClient _http;

    public ApiClient(AppFactory factory)
    {
        _handler = new CookieHandler(factory.Server.CreateHandler());
        _http = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") };
    }

    private string? Xsrf => _handler.Cookies.GetValueOrDefault("XSRF-TOKEN") is { } t ? Uri.UnescapeDataString(t) : null;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, bool withXsrf)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (withXsrf && Xsrf is { } token) request.Headers.Add("X-XSRF-TOKEN", token);
        return await _http.SendAsync(request);
    }

    public Task<HttpResponseMessage> Get(string url) => SendAsync(HttpMethod.Get, url, null, false);
    public Task<HttpResponseMessage> Post(string url, object? body = null) => SendAsync(HttpMethod.Post, url, body ?? new { }, true);
    public Task<HttpResponseMessage> PostWithoutXsrf(string url, object body) => SendAsync(HttpMethod.Post, url, body, false);
    public Task<HttpResponseMessage> Put(string url, object body) => SendAsync(HttpMethod.Put, url, body, true);
    public Task<HttpResponseMessage> Delete(string url) => SendAsync(HttpMethod.Delete, url, null, true);

    public async Task<ApiClient> LoginAsync(string username = "admin", string password = AppFactory.AdminPassword)
    {
        await Get("/api/auth/me"); // initialise le jeton CSRF
        var response = await Post("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return this;
    }

    public async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public async Task<JsonElement> GetJson(string url)
    {
        var response = await Get(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    public async Task<int> PostId(string url, object body)
    {
        var response = await Post(url, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("id").GetInt32();
    }

    public void Dispose() => _http.Dispose();
}
