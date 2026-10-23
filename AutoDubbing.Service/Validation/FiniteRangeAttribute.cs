using System.ComponentModel.DataAnnotations;

namespace AutoDubbing.Service.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FiniteRangeAttribute(double minimum, double maximum) : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null
        || value is double number && double.IsFinite(number) && number >= minimum && number <= maximum;

    public override string FormatErrorMessage(string name) =>
        $"{name} phải là số hữu hạn trong [{minimum}, {maximum}].";
}
