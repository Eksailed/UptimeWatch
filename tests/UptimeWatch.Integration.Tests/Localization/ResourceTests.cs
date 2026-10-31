using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using UptimeWatch.Domain;
using UptimeWatch.Web;
using UptimeWatch.Web.Localization;

namespace UptimeWatch.Integration.Tests.Localization;

/// <summary>The ru (neutral) and en resource files stay in step, and every error code and literal key has a text.</summary>
public sealed partial class ResourceTests
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    private static readonly ResourceManager Manager =
        new("UptimeWatch.Web.Resources.SharedResource", typeof(SharedResource).Assembly);

    /// <summary>Russian is the neutral resource (the invariant set), English is the satellite assembly.</summary>
    private static Dictionary<string, string> Load(CultureInfo culture)
    {
        var set = Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static Dictionary<string, string> Russian => Load(CultureInfo.InvariantCulture);

    private static Dictionary<string, string> English => Load(En);

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static string[] PlaceholdersOf(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    [Fact]
    public void Both_files_have_the_same_keys()
    {
        var ru = Russian.Keys.ToHashSet();
        var en = English.Keys.ToHashSet();

        Assert.True(ru.Count > 100, "the resource file looks empty");
        Assert.Empty(ru.Except(en).Order());
        Assert.Empty(en.Except(ru).Order());
    }

    [Fact]
    public void No_value_is_empty()
    {
        Assert.Empty(Russian.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => "ru:" + p.Key));
        Assert.Empty(English.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => "en:" + p.Key));
    }

    [Fact]
    public void Both_languages_use_the_same_placeholders()
    {
        var ru = Russian;
        var en = English;

        var different = ru.Keys.Where(k => en.ContainsKey(k) && !PlaceholdersOf(ru[k]).SequenceEqual(PlaceholdersOf(en[k])))
            .Order().ToArray();

        Assert.Empty(different);
    }

    [Fact]
    public void Russian_texts_are_russian_and_english_texts_are_not_copies()
    {
        var ru = Russian;
        var en = English;

        // A value copied from one file to the other is a forgotten translation (except words that are the same in both).
        var copies = ru.Keys.Where(k => en.ContainsKey(k) && ru[k] == en[k] && ru[k].Any(char.IsLetter)
            && ru[k].Length > 12).Order().ToArray();

        Assert.Empty(copies);
        Assert.Equal("Не найдено", ru["Details_NotFound_Title"]);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Every_error_code_has_a_text(string culture)
    {
        var texts = culture == "ru" ? Russian : English;

        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            Assert.True(texts.TryGetValue(ErrorInfoLocalizer.Key(code), out var text) && text.Length > 0,
                $"no {culture} text for the error code {code}");
        }
    }

    [Fact]
    public void Every_resource_error_key_is_a_known_error_code()
    {
        var codes = Enum.GetNames<ErrorCode>().Select(n => "Error_" + n).ToHashSet();

        var unknown = Russian.Keys.Where(k => k.StartsWith("Error_", StringComparison.Ordinal) && !codes.Contains(k));

        Assert.Empty(unknown);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Error_info_is_localized_with_its_arguments(string culture)
    {
        var localizer = CreateLocalizer();
        var error = new ErrorInfo(ErrorCode.UrlTooLong, "fallback", 2048);

        using var _ = UseCulture(culture);
        var text = localizer.Text(error);

        Assert.Contains("2048", text);
        Assert.DoesNotContain("fallback", text);
        Assert.DoesNotContain("{0}", text);
        Assert.NotEqual(ErrorInfoLocalizer.Key(ErrorCode.UrlTooLong), text);
    }

    [Fact]
    public void Error_info_texts_differ_between_languages()
    {
        var localizer = CreateLocalizer();
        var error = new ErrorInfo(ErrorCode.UrlRequired, "fallback");

        string ru, en;
        using (UseCulture("ru")) { ru = localizer.Text(error); }
        using (UseCulture("en")) { en = localizer.Text(error); }

        Assert.NotEqual(ru, en);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Every_identity_error_has_a_text(string culture)
    {
        var texts = culture == "ru" ? Russian : English;

        foreach (var code in LocalizedIdentityErrorDescriber.Codes)
        {
            Assert.True(texts.ContainsKey("Identity_" + code), $"no {culture} text for the identity error {code}");
        }
    }

    [Fact]
    public void Identity_errors_come_in_the_current_language()
    {
        var describer = new LocalizedIdentityErrorDescriber(CreateLocalizer());

        string ru, en;
        using (UseCulture("ru")) { ru = describer.PasswordTooShort(6).Description; }
        using (UseCulture("en")) { en = describer.PasswordTooShort(6).Description; }

        Assert.Equal("PasswordTooShort", describer.PasswordTooShort(6).Code);
        Assert.Contains("6", ru);
        Assert.Contains("6", en);
        Assert.NotEqual(ru, en);
        Assert.Contains("Password", en, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_key_used_in_the_sources_exists()
    {
        var root = FindRepositoryRoot();
        var ru = Russian;
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "UptimeWatch.Web"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match match in UsedKey().Matches(File.ReadAllText(file)))
            {
                found.Add(match.Groups[1].Value);
            }
        }

        Assert.True(found.Count > 100, $"only {found.Count} keys found: the scan is broken");
        var missing = found.Where(k => !ru.ContainsKey(k)).ToArray();
        Assert.True(missing.Length == 0, "keys missing in the resource file: " + string.Join(", ", missing));
    }

    [GeneratedRegex(@"(?:L\[|L\.(?:Html|ErrorMessage)\(|Localized\w+\()\s*""([A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+)""")]
    private static partial Regex UsedKey();

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Validation_messages_follow_the_current_language(string culture)
    {
        var localizer = CreateLocalizer();
        var provider = new ServiceCollection().AddSingleton(localizer).BuildServiceProvider();
        var model = new Form { Email = "not an email", Password = "123", Confirm = "x" };
        var context = new ValidationContext(model, provider, null);
        var results = new List<ValidationResult>();

        using var _ = UseCulture(culture);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        var expected = new[]
        {
            localizer["Validation_Email_Invalid"].Value,
            localizer["Validation_Password_Length", 6, 100].Value,
            localizer["Validation_Password_Mismatch"].Value
        };
        Assert.Equal(expected.Order(), results.Select(r => r.ErrorMessage!).Order());
        Assert.DoesNotContain(results, r => r.ErrorMessage!.StartsWith("Validation_", StringComparison.Ordinal));
        Assert.Contains(results, r => r.ErrorMessage!.Contains("6", StringComparison.Ordinal));
    }

    [Fact]
    public void Required_message_is_translated()
    {
        var localizer = CreateLocalizer();
        var provider = new ServiceCollection().AddSingleton(localizer).BuildServiceProvider();
        var model = new Form();
        var results = new List<ValidationResult>();

        string ru, en;
        using (UseCulture("ru"))
        {
            Validator.TryValidateObject(model, new ValidationContext(model, provider, null), results, true);
            ru = results.First(r => r.MemberNames.Contains(nameof(Form.Email))).ErrorMessage!;
        }

        results.Clear();
        using (UseCulture("en"))
        {
            Validator.TryValidateObject(model, new ValidationContext(model, provider, null), results, true);
            en = results.First(r => r.MemberNames.Contains(nameof(Form.Email))).ErrorMessage!;
        }

        Assert.NotEqual(ru, en);
    }

    private sealed class Form
    {
        [LocalizedRequired("Validation_Email_Required")]
        [LocalizedEmailAddress("Validation_Email_Invalid")]
        public string? Email { get; set; }

        [LocalizedStringLength("Validation_Password_Length", 100, MinimumLength = 6)]
        public string? Password { get; set; }

        [LocalizedCompare("Validation_Password_Mismatch", nameof(Password))]
        public string? Confirm { get; set; }
    }

    private static IStringLocalizer<SharedResource> CreateLocalizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        return services.BuildServiceProvider().GetRequiredService<IStringLocalizer<SharedResource>>();
    }

    private static CultureScope UseCulture(string name) => new(name);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "UptimeWatch.Web")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
