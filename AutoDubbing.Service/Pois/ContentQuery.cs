using System.ComponentModel.DataAnnotations;

namespace AutoDubbing.Service.Pois;

public sealed class ContentQuery : IValidatableObject
{
    [Required(ErrorMessage = "Thiếu language.")]
    public string? Language { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Language is not null && !LanguageTags.TryNormalize(Language, out _))
        {
            yield return new ValidationResult("language phải là mã culture được .NET hỗ trợ, ví dụ vi, en, en-US.",
                [nameof(Language)]);
        }
    }
}
