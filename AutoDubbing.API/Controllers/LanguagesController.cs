using AutoDubbing.Service.Languages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoDubbing.API.Controllers;

[ApiController, Route("api/languages"), AllowAnonymous]
public sealed class LanguagesController : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<LanguageDto>> List() =>
        Ok(LanguageCatalog.All.Select(language => new LanguageDto(language.Code, language.DisplayName)).ToArray());
}
public sealed record LanguageDto(string Code, string DisplayName);
