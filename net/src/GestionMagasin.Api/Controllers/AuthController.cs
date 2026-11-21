using GestionMagasin.Api.Data;
using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Domain;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Controllers;

[Route("api/auth")]
public class AuthController(
    AppDbContext db, CurrentUser current, AuditService audit,
    IPasswordHasher<User> hasher, IAntiforgery antiforgery) : ApiControllerBase
{
    public const int MinPasswordLength = 8;

    /// <summary>Publie le jeton CSRF (cookie lisible par Angular) pour l'identité courante.</summary>
    private void IssueXsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            Path = "/",
        });
    }

    /// <summary>État de session : utilisateur courant ou null. Initialise aussi le jeton CSRF.</summary>
    [HttpGet("me")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Me()
    {
        IssueXsrfToken();
        var user = await current.GetAsync();
        return Ok(new { user = user?.ToDto() });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult> Login(LoginRequest request)
    {
        var username = (request.Username ?? "").Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || !user.Active ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
            return Unauthorized(new ApiError("Identifiants invalides ou compte désactivé."));

        var principal = UserPrincipal.Create(user);
        await HttpContext.SignInAsync(UserPrincipal.CookieScheme, principal);
        HttpContext.User = principal;

        user.LastLoginAt = DateTime.UtcNow;
        await audit.AddAsync("LOGIN", "session", after: new { username = user.Username }, actor: user);
        await db.SaveChangesAsync();

        IssueXsrfToken(); // le jeton est lié à l'identité : on le renouvelle après connexion
        return Ok(new { user = user.ToDto() });
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        var user = await current.GetAsync();
        if (user is not null)
        {
            await audit.AddAsync("LOGOUT", "session", before: new { username = user.Username }, actor: user);
            await db.SaveChangesAsync();
        }
        await HttpContext.SignOutAsync(UserPrincipal.CookieScheme);
        HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
        IssueXsrfToken();
        return NoContent();
    }

    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await current.RequireAsync();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
            return Fail("Le mot de passe actuel est incorrect.");
        var password = request.NewPassword ?? "";
        if (password.Length < MinPasswordLength)
            return Fail($"Le nouveau mot de passe doit contenir au moins {MinPasswordLength} caractères.");
        if (password != request.PasswordConfirmation)
            return Fail("La confirmation du mot de passe ne correspond pas.");

        user.PasswordHash = hasher.HashPassword(user, password);
        user.UpdatedAt = DateTime.UtcNow;
        await audit.AddAsync("PASSWORD_CHANGE", "user", user.Id);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
