using System.Globalization;
using System.Text.Json;
using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;
using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Controllers;

[Route("api/admin/users")]
[Authorize(Policy = "Admin")]
public class UsersController(
    AppDbContext db, CurrentUser current, AuditService audit, IPasswordHasher<User> hasher) : ApiControllerBase
{
    private const string LastAdminMessage = "Le dernier administrateur actif doit être conservé.";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> List() =>
        Ok((await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync()).Select(u => u.ToDto()));

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(UserCreateRequest request)
    {
        var username = (request.Username ?? "").Trim();
        var displayName = (request.DisplayName ?? "").Trim();
        if (username.Length == 0 || username.Length > 80 || displayName.Length == 0 || displayName.Length > 120 ||
            !Roles.IsValid(request.Role))
            return Fail("Les informations du compte sont invalides.");
        if ((request.Password ?? "").Length < AuthController.MinPasswordLength)
            return Fail($"Le mot de passe doit contenir au moins {AuthController.MinPasswordLength} caractères.");
        if (await db.Users.AnyAsync(u => u.Username == username))
            return Fail("Ce nom d’utilisateur existe déjà.");

        var user = new User { Username = username, DisplayName = displayName, Role = request.Role!, Active = true };
        user.PasswordHash = hasher.HashPassword(user, request.Password!);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.AddAsync("USER_CREATE", "user", user.Id, after: AuditService.UserSnapshot(user));
        await db.SaveChangesAsync();
        return Ok(user.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UserUpdateRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return Missing("Utilisateur introuvable.");
        var displayName = (request.DisplayName ?? "").Trim();
        if (displayName.Length == 0 || displayName.Length > 120 || !Roles.IsValid(request.Role))
            return Fail("Les informations du compte sont invalides.");
        if (user.Id == current.Id && request.Role != Roles.Admin)
            return Fail("Vous ne pouvez pas retirer votre propre rôle administrateur.");
        if (user.Role == Roles.Admin && request.Role != Roles.Admin && user.Active &&
            await db.Users.CountAsync(u => u.Role == Roles.Admin && u.Active) <= 1)
            return Fail(LastAdminMessage);

        var before = AuditService.UserSnapshot(user);
        user.DisplayName = displayName;
        user.Role = request.Role!;
        user.UpdatedAt = DateTime.UtcNow;
        await audit.AddAsync("USER_UPDATE", "user", user.Id, before, AuditService.UserSnapshot(user));
        await db.SaveChangesAsync();
        return Ok(user.ToDto());
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<ActionResult> ResetPassword(int id, PasswordRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return Missing("Utilisateur introuvable.");
        if ((request.Password ?? "").Length < AuthController.MinPasswordLength)
            return Fail($"Le mot de passe doit contenir au moins {AuthController.MinPasswordLength} caractères.");

        user.PasswordHash = hasher.HashPassword(user, request.Password!);
        user.UpdatedAt = DateTime.UtcNow;
        await audit.AddAsync("PASSWORD_RESET", "user", user.Id, after: new { username = user.Username });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<ActionResult<UserDto>> Deactivate(int id)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return Missing("Utilisateur introuvable.");
        if (user.Id == current.Id) return Fail("Vous ne pouvez pas désactiver votre propre compte.");
        if (!user.Active) return Fail("Ce compte est déjà désactivé.");
        if (user.Role == Roles.Admin && await db.Users.CountAsync(u => u.Role == Roles.Admin && u.Active) <= 1)
            return Fail(LastAdminMessage);

        var before = AuditService.UserSnapshot(user);
        user.Active = false;
        user.UpdatedAt = DateTime.UtcNow;
        await audit.AddAsync("USER_DEACTIVATE", "user", user.Id, before, AuditService.UserSnapshot(user));
        await db.SaveChangesAsync();
        return Ok(user.ToDto());
    }

    [HttpPost("{id:int}/activate")]
    public async Task<ActionResult<UserDto>> Activate(int id)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return Missing("Utilisateur introuvable.");
        var before = AuditService.UserSnapshot(user);
        user.Active = true;
        user.UpdatedAt = DateTime.UtcNow;
        await audit.AddAsync("USER_ACTIVATE", "user", user.Id, before, AuditService.UserSnapshot(user));
        await db.SaveChangesAsync();
        return Ok(user.ToDto());
    }
}

[Route("api/admin/audit")]
[Authorize(Policy = "Admin")]
public class AuditController(AppDbContext db) : ApiControllerBase
{
    private const int PageSize = 30;

    [HttpGet]
    public async Task<ActionResult> List(
        [FromQuery] int? actorId, [FromQuery] string? action,
        [FromQuery] string? dateFrom, [FromQuery] string? dateTo, [FromQuery] int page = 1)
    {
        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();
        if (actorId is int a) query = query.Where(l => l.ActorId == a);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);

        if (!TryDate(dateFrom, out var from) || !TryDate(dateTo, out var to))
            return Fail("La plage de dates est invalide.");
        if (from is DateTime f) query = query.Where(l => l.CreatedAt >= f);
        if (to is DateTime t) { var end = t.AddDays(1); query = query.Where(l => l.CreatedAt < end); }

        page = Math.Max(page, 1);
        var total = await query.CountAsync();
        var pages = Math.Max((int)Math.Ceiling(total / (double)PageSize), 1);
        page = Math.Min(page, pages);
        var logs = await query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();

        var items = logs.Select(l => new AuditLogDto(
            l.Id, l.ActorId, l.ActorUsername, l.Action, l.TargetType, l.TargetId, ParseDetails(l.Details), l.CreatedAt)).ToList();
        var users = (await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync()).Select(u => new { u.Id, u.Username });
        var actions = await db.AuditLogs.Select(l => l.Action).Distinct().OrderBy(x => x).ToListAsync();
        return Ok(new { logs = new PageDto<AuditLogDto>(items, page, pages, total, PageSize), users, actions });
    }

    private static JsonElement ParseDetails(string details)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(details) ? "{}" : details).RootElement.Clone(); }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    private static bool TryDate(string? value, out DateTime? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return false;
        date = DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return true;
    }
}
