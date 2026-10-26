using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AutoDubbing.Service.ContentPreparation;
using AutoDubbing.Service.Languages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AutoDubbing.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api")]
public sealed class ContentPreparationController(IContentPreparationService service, IPreparationRateGate rates,
    IOptions<ContentPreparationOptions> options, TimeProvider clock) : ControllerBase
{
    [HttpGet("content-preparations/settings")]
    [EndpointSummary("Read proximity settings; does not create content.")]
    public ActionResult<ProximitySettingsResponse> Settings(CancellationToken cancellationToken) =>
        Ok(new ProximitySettingsResponse(options.Value.PreparationRadiusMeters, options.Value.PlaybackRadiusMeters,
            options.Value.DwellSeconds));

    [HttpPost("pois/prepare-nearby")]
    [EndpointSummary("Queue source-language audio for Published POIs within the configured 500 m radius.")]
    [ProducesResponseType<NearbyPreparationResponse>(200)]
    [ProducesResponseType<NearbyPreparationResponse>(202)]
    [ProducesResponseType(429)]
    [ProducesResponseType(503)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    public async Task<ActionResult<NearbyPreparationResponse>> Nearby(PrepareNearbyRequest request, CancellationToken cancellationToken)
    {
        if (!Admit()) return LoadLimited();
        var response = await service.PrepareNearbyAsync(request.Latitude!.Value, request.Longitude!.Value, cancellationToken);
        SetRetry(response.Items);
        var pending = response.Items.FirstOrDefault(item => item.Status is "Queued" or "Processing");
        if (pending is not null) return Accepted(pending.StatusUrl, response);
        if (response.Items.Count > 0 && response.Items.All(item => item.Status is "QueueFull" or "QuotaExceeded"))
            return StatusCode(429, response);
        if (response.Items.Count > 0 && response.Items.All(item => item.Status == "Unavailable"))
            return StatusCode(response.Items.All(item => item.ErrorCode == "UnsupportedSourceContent") ? 422 : 503, response);
        return Ok(response);
    }

    [HttpPost("pois/{id:int}/prepare-content")]
    [EndpointSummary("Reuse or queue one requested POI language; anonymous, bounded and asynchronous.")]
    [ProducesResponseType<PreparationItemResponse>(200)]
    [ProducesResponseType<PreparationItemResponse>(202)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    [ProducesResponseType(429)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<PreparationItemResponse>> Prepare(int id, PrepareContentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Admit()) return LoadLimited();
        var response = await service.PrepareAsync(id, request.Language!, cancellationToken);
        if (response is null) return NotFound();
        SetRetry([response]);
        return response.Status switch
        {
            "Queued" or "Processing" => Accepted(response.StatusUrl, response),
            "QuotaExceeded" or "QueueFull" => StatusCode(429, response),
            "Unavailable" => StatusCode(response.ErrorCode == "UnsupportedSourceContent" ? 422 : 503, response),
            _ => Ok(response)
        };
    }

    [HttpGet("content-preparations/{id:guid}")]
    [EndpointSummary("Read a preparation job; polling never calls a content provider.")]
    [ProducesResponseType<PreparationItemResponse>(200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PreparationItemResponse>> Status(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.StatusAsync(id, cancellationToken);
        if (result is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        SetRetry([result]);
        return Ok(result);
    }

    private bool Admit()
    {
        Response.Headers.CacheControl = "no-store";
        if (rates.TryAcquire(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", out var retry)) return true;
        Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
        return false;
    }

    private ObjectResult LoadLimited() => Problem(statusCode: 429, title: "Preparation request limit exceeded.",
        extensions: new Dictionary<string, object?> { ["code"] = "RequestRateLimited" });

    private void SetRetry(IReadOnlyList<PreparationItemResponse> items)
    {
        if (items.Any(item => item.Status is "Queued" or "Processing"))
            Response.Headers.RetryAfter = "3";
        else
        {
            var deadlines = items.Where(item => item.RetryAt is not null && item.RetryAt < DateTimeOffset.MaxValue)
                .Select(item => item.RetryAt!.Value).ToArray();
            if (deadlines.Length > 0)
                Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling((deadlines.Min() - clock.GetUtcNow()).TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Untrusted client coordinates. The server chooses the radius and source language.</summary>
public sealed record PrepareNearbyRequest
{
    [Required, Range(-90d, 90d)] public double? Latitude { get; init; }
    [Required, Range(-180d, 180d)] public double? Longitude { get; init; }
}

/// <summary>One language code from GET /api/languages; no provider settings or secrets.</summary>
public sealed record PrepareContentRequest : IValidatableObject
{
    [Required, MaxLength(16)] public string? Language { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LanguageCatalog.Find(Language) is null)
            yield return new ValidationResult("Use a code from GET /api/languages.", [nameof(Language)]);
    }
}
