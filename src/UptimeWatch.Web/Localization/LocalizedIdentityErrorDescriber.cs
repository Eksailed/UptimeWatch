using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace UptimeWatch.Web.Localization;

/// <summary>Identity error descriptions (shown on the register / password pages) in the current UI language.</summary>
public sealed class LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer) : IdentityErrorDescriber
{
    /// <summary>The error codes that have a text: the resource key of each is <c>Identity_&lt;Code&gt;</c>.</summary>
    public static readonly IReadOnlyList<string> Codes =
    [
        nameof(DefaultError), nameof(ConcurrencyFailure), nameof(PasswordMismatch), nameof(InvalidToken),
        nameof(RecoveryCodeRedemptionFailed), nameof(LoginAlreadyAssociated), nameof(InvalidUserName),
        nameof(InvalidEmail), nameof(DuplicateUserName), nameof(DuplicateEmail), nameof(InvalidRoleName),
        nameof(DuplicateRoleName), nameof(UserAlreadyHasPassword), nameof(UserLockoutNotEnabled),
        nameof(UserAlreadyInRole), nameof(UserNotInRole), nameof(PasswordTooShort),
        nameof(PasswordRequiresNonAlphanumeric), nameof(PasswordRequiresDigit), nameof(PasswordRequiresLower),
        nameof(PasswordRequiresUpper), nameof(PasswordRequiresUniqueChars)
    ];

    public override IdentityError DefaultError() => Describe(nameof(DefaultError));

    public override IdentityError ConcurrencyFailure() => Describe(nameof(ConcurrencyFailure));

    public override IdentityError PasswordMismatch() => Describe(nameof(PasswordMismatch));

    public override IdentityError InvalidToken() => Describe(nameof(InvalidToken));

    public override IdentityError RecoveryCodeRedemptionFailed() => Describe(nameof(RecoveryCodeRedemptionFailed));

    public override IdentityError LoginAlreadyAssociated() => Describe(nameof(LoginAlreadyAssociated));

    public override IdentityError InvalidUserName(string? userName) => Describe(nameof(InvalidUserName), userName);

    public override IdentityError InvalidEmail(string? email) => Describe(nameof(InvalidEmail), email);

    public override IdentityError DuplicateUserName(string userName) => Describe(nameof(DuplicateUserName), userName);

    public override IdentityError DuplicateEmail(string email) => Describe(nameof(DuplicateEmail), email);

    public override IdentityError InvalidRoleName(string? role) => Describe(nameof(InvalidRoleName), role);

    public override IdentityError DuplicateRoleName(string role) => Describe(nameof(DuplicateRoleName), role);

    public override IdentityError UserAlreadyHasPassword() => Describe(nameof(UserAlreadyHasPassword));

    public override IdentityError UserLockoutNotEnabled() => Describe(nameof(UserLockoutNotEnabled));

    public override IdentityError UserAlreadyInRole(string role) => Describe(nameof(UserAlreadyInRole), role);

    public override IdentityError UserNotInRole(string role) => Describe(nameof(UserNotInRole), role);

    public override IdentityError PasswordTooShort(int length) => Describe(nameof(PasswordTooShort), length);

    public override IdentityError PasswordRequiresNonAlphanumeric() => Describe(nameof(PasswordRequiresNonAlphanumeric));

    public override IdentityError PasswordRequiresDigit() => Describe(nameof(PasswordRequiresDigit));

    public override IdentityError PasswordRequiresLower() => Describe(nameof(PasswordRequiresLower));

    public override IdentityError PasswordRequiresUpper() => Describe(nameof(PasswordRequiresUpper));

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Describe(nameof(PasswordRequiresUniqueChars), uniqueChars);

    private IdentityError Describe(string code, params object?[] args) =>
        new() { Code = code, Description = localizer[$"Identity_{code}", args!].Value };
}
