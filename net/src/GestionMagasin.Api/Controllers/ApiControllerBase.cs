using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace GestionMagasin.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected BadRequestObjectResult Fail(string message) => BadRequest(new ApiError(message));
    protected NotFoundObjectResult Missing(string message = "Ressource introuvable.") => NotFound(new ApiError(message));

    /// <summary>Retourne le fichier du rapport au format demandé (« excel » ou « pdf »), sinon 404.</summary>
    protected IActionResult SendExport(IEnumerable<ExportSection> sections, string filename, string format) => format switch
    {
        "excel" => File(ExportService.BuildExcel(sections), ExportService.ExcelMime, $"{filename}.xlsx"),
        "pdf" => File(ExportService.BuildPdf(sections), ExportService.PdfMime, $"{filename}.pdf"),
        _ => NotFound(new ApiError("Format d'export inconnu.")),
    };
}
