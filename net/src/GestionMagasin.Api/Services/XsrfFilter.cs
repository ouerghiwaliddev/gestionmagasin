using GestionMagasin.Api.Dtos;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GestionMagasin.Api.Services;

/// <summary>
/// Protection CSRF des requêtes modifiantes (POST, PUT, PATCH, DELETE) : le jeton du cookie XSRF-TOKEN
/// doit être renvoyé dans l'en-tête X-XSRF-TOKEN (ce que fait HttpClient d'Angular automatiquement).
/// </summary>
public class XsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) ||
            HttpMethods.IsHead(context.HttpContext.Request.Method) ||
            HttpMethods.IsOptions(context.HttpContext.Request.Method) ||
            HttpMethods.IsTrace(context.HttpContext.Request.Method))
            return;

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new ApiError("Jeton de sécurité invalide ou expiré. Rechargez la page."));
        }
    }
}
