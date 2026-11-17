using System.ComponentModel.DataAnnotations;
using AutoDubbing.Data.Entities;

namespace AutoDubbing.Service.Authentication;

public sealed class AdminLoginRequest
{
    [Required]
    [StringLength(AdminUser.UsernameMaxLength)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [StringLength(AdminAuthenticationService.PasswordMaxLength)]
    public string Password { get; init; } = string.Empty;
}

public sealed class AdminChangePasswordRequest
{
    [Required, StringLength(AdminAuthenticationService.PasswordMaxLength)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, StringLength(AdminAuthenticationService.PasswordMaxLength, MinimumLength = 8)]
    public string NewPassword { get; init; } = string.Empty;
}
