using System.Security.Claims;
using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Services;

/// <summary>Utilisateur authentifié de la requête (chargé depuis la base, une seule fois par requête).</summary>
public class CurrentUser(IHttpContextAccessor accessor, AppDbContext db)
{
    private User? _user;
    private bool _loaded;

    public int? Id =>
        int.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task<User?> GetAsync()
    {
        if (_loaded) return _user;
        _loaded = true;
        if (Id is int id) _user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        return _user;
    }

    public async Task<User> RequireAsync() =>
        await GetAsync() ?? throw new InvalidOperationException("Aucun utilisateur authentifié.");
}

public static class UserPrincipal
{
    public const string CookieScheme = "GestionMagasin.Cookie";

    public static ClaimsPrincipal Create(User user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
            ],
            CookieScheme);
        return new ClaimsPrincipal(identity);
    }
}
