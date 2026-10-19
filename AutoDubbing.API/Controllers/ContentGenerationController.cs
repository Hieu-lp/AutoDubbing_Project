using System.ComponentModel.DataAnnotations;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.Languages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoDubbing.API.Controllers;

[ApiController]
[Route("api/admin/pois")]
[Authorize]
public sealed class ContentGenerationController(IContentGenerationService service) : ControllerBase
{
    [HttpPost("{id:int}/generate-content")]
    public async Task<ActionResult<GenerationBatchResult>> Generate(int id, GenerateContentRequest request,
        CancellationToken cancellationToken)
    {
        if (!service.IsConfigured)
            return Problem(statusCode: 503, title: "Chưa cấu hình Gemini hoặc Supabase cho chức năng sinh nội dung.");
        try
        {
            var result = await service.GenerateAsync(id, request.Languages!, request.BatchSize, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (GenerationException error) when (error.Code is "UnsupportedSourceContent" or "SourceTooLong")
        { return Problem(statusCode: 422, title: "Nội dung gốc phải là tiếng Việt, không rỗng và tối đa 6000 ký tự."); }
    }
}

public sealed class GenerateContentRequest : IValidatableObject
{
    [Required, MinLength(1), MaxLength(64)]
    public string[]? Languages { get; init; }
    [Range(1, 3)]
    public int BatchSize { get; init; } = 3;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Languages is null) yield break;
        if (Languages.Any(code => LanguageCatalog.Find(code) is null))
            yield return new ValidationResult("languages chỉ nhận mã trong GET /api/languages.", [nameof(Languages)]);
        if (Languages.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Languages.Length)
            yield return new ValidationResult("languages không được trùng nhau.", [nameof(Languages)]);
    }
}
