using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;
using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Base de données (PostgreSQL) ----------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default est obligatoire (variable ConnectionStrings__Default).");
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

// ---------- Services applicatifs ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// ---------- Authentification par cookie (HttpOnly, SameSite=Strict) + CSRF ----------
builder.Services
    .AddAuthentication(UserPrincipal.CookieScheme)
    .AddCookie(UserPrincipal.CookieScheme, options =>
    {
        options.Cookie.Name = ".GestionMagasin.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // L'API répond par des codes HTTP, jamais par une redirection.
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        // Compte désactivé ou rôle modifié : pris en compte dès la requête suivante.
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var idText = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = int.TryParse(idText, out var id) ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id) : null;
            if (user is null || !user.Active)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(UserPrincipal.CookieScheme);
                return;
            }
            ctx.ReplacePrincipal(UserPrincipal.Create(user));
        };
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("Admin", p => p.RequireRole(Roles.Admin));

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN"; // en-tête envoyé automatiquement par HttpClient d'Angular
    options.Cookie.Name = ".GestionMagasin.Antiforgery";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services
    .AddControllers(options => options.Filters.Add<XsrfFilter>())
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = _ =>
            new BadRequestObjectResult(new ApiError("Les valeurs saisies sont invalides.")));

var app = builder.Build();

// ---------- Initialisation de la base et du premier administrateur ----------
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (args is ["import-legacy", var legacyPath])
    {
        Console.WriteLine(await LegacyImporter.ImportAsync(db, legacyPath));
        return;
    }

    if (!await db.Users.AnyAsync())
    {
        var username = (app.Configuration["Admin:Username"] ?? "").Trim();
        var password = app.Configuration["Admin:Password"] ?? "";
        if (username.Length == 0 || password.Length < 8)
            throw new InvalidOperationException(
                "Aucun utilisateur : Admin:Username et Admin:Password (8 caractères minimum) sont obligatoires " +
                "(variables d'environnement Admin__Username et Admin__Password).");
        var admin = new User
        {
            Username = username,
            DisplayName = (app.Configuration["Admin:DisplayName"] ?? "").Trim() is { Length: > 0 } name ? name : username,
            Role = Roles.Admin,
            Active = true,
        };
        admin.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }
}

// ---------- Pipeline ----------
app.UseDefaultFiles();
app.UseStaticFiles();   // avant UseRouting : sinon le fallback SPA masque les fichiers statiques
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// Application Angular (routes côté client), sans capturer les URL de l'API.
app.MapFallbackToFile("/{**path:regex(^(?!api/).*$)}", "index.html").AllowAnonymous();

app.Run();

public partial class Program;
