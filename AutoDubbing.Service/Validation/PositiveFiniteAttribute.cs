using System.ComponentModel.DataAnnotations;

namespace AutoDubbing.Service.Validation;

[AttributeUsage(AttributeTargets.Property)]
public sealed class PositiveFiniteAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null
        || value is double number && double.IsFinite(number) && number > 0;

    public override string FormatErrorMessage(string name) => $"{name} phải là số hữu hạn lớn hơn 0.";
}
