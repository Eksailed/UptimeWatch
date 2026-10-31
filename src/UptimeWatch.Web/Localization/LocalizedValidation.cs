using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Localization;

namespace UptimeWatch.Web.Localization;

/// <summary>
/// DataAnnotations attributes whose message is a resource key, looked up in the language of the current request or
/// circuit when the validation fails. The localizer comes from the <see cref="ValidationContext"/> service provider,
/// which Blazor's DataAnnotationsValidator fills. Without a provider the key itself is the message.
/// </summary>
internal static class LocalizedValidation
{
    public static ValidationResult? Localize(ValidationResult? result, ValidationContext context, string key, params object[] args)
    {
        if (result is null || result == ValidationResult.Success)
        {
            return result;
        }

        var localizer = (IStringLocalizer<SharedResource>?)context.GetService(typeof(IStringLocalizer<SharedResource>));
        var message = localizer is null ? key : localizer[key, args].Value;
        return new ValidationResult(message, result.MemberNames);
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class LocalizedRequiredAttribute(string key) : RequiredAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        LocalizedValidation.Localize(base.IsValid(value, validationContext), validationContext, key);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class LocalizedEmailAddressAttribute(string key) : DataTypeAttribute(DataType.EmailAddress)
{
    private static readonly EmailAddressAttribute Inner = new();

    public override bool IsValid(object? value) => Inner.IsValid(value);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        LocalizedValidation.Localize(base.IsValid(value, validationContext), validationContext, key);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class LocalizedPhoneAttribute(string key) : DataTypeAttribute(DataType.PhoneNumber)
{
    private static readonly PhoneAttribute Inner = new();

    public override bool IsValid(object? value) => Inner.IsValid(value);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        LocalizedValidation.Localize(base.IsValid(value, validationContext), validationContext, key);
}

/// <summary>Message arguments: {0} = minimum length (set it with MinimumLength), {1} = maximum length.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class LocalizedStringLengthAttribute(string key, int maximumLength) : StringLengthAttribute(maximumLength)
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        LocalizedValidation.Localize(base.IsValid(value, validationContext), validationContext, key, MinimumLength, MaximumLength);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class LocalizedCompareAttribute(string key, string otherProperty) : CompareAttribute(otherProperty)
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        LocalizedValidation.Localize(base.IsValid(value, validationContext), validationContext, key);
}
