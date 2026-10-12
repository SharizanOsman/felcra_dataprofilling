using System.Globalization;
using System.Text.RegularExpressions;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Localization;
using FluentValidation;

namespace Felcra.Profiling.Localization.Tests;

/// <summary>Every text the code passes to T("…") has a Malay translation with the same placeholders.</summary>
public partial class TranslationTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Every_text_in_the_code_has_a_Malay_translation()
    {
        var missing = KeysInCode().Where(k => !MalayText.Entries.ContainsKey(k)).Order().ToList();
        Assert.True(missing.Count == 0, "Add to MalayText:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Translations_keep_the_placeholders()
    {
        var wrong = MalayText.Pairs
            .Where(p => !Placeholders(p.English).SequenceEqual(Placeholders(p.Malay)))
            .Select(p => $"{p.English} → {p.Malay}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void Each_text_is_translated_once()
    {
        var duplicates = MalayText.Pairs.GroupBy(p => p.English).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, string.Join("\n", duplicates));
        Assert.DoesNotContain(MalayText.Pairs, p => string.IsNullOrWhiteSpace(p.Malay));
    }

    [Fact]
    public void Every_enum_value_shown_as_words_has_a_translation()
    {
        var enums = typeof(Felcra.Profiling.Domain.Animals.Animal).Assembly.GetTypes().Where(t => t.IsEnum && t.IsPublic);
        var missing = enums.SelectMany(e => Enum.GetNames(e))
            .Select(ValidationText.FieldName)
            .Where(w => !MalayText.Entries.ContainsKey(w))
            .Distinct().ToList();
        Assert.True(missing.Count == 0, "Add to MalayText.Words:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_validated_field_has_a_Malay_name()
    {
        var validators = typeof(FarmRequestValidator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IValidator).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null);
        var ignored = new HashSet<string> { "P256dh", "Auth" }; // push subscription keys: never typed by a person
        var missing = validators
            .SelectMany(t => ((IValidator)Activator.CreateInstance(t)!).CreateDescriptor().GetMembersWithValidators().Select(g => g.Key))
            .Select(name => name.Split('.')[^1])
            .Where(name => name.Length > 0 && !ignored.Contains(name))
            .Select(ValidationText.FieldName)
            .Where(f => !MalayText.Entries.ContainsKey(f))
            .Distinct().Order().ToList();
        Assert.True(missing.Count == 0, "Add to MalayText.Words:\n" + string.Join("\n", missing));
    }

    private static IEnumerable<string> KeysInCode()
    {
        var files = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains("Migrations") && !f.Contains("Felcra.Profiling.Localization"));
        foreach (var file in files)
        {
            // Comments may mention T("…") as an example.
            var code = string.Join('\n', File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("@*")));
            foreach (Match m in Direct().Matches(code)) yield return Unescape(m.Groups[1].Value);
            foreach (Match m in Ternary().Matches(code))
            {
                yield return Unescape(m.Groups[1].Value);
                yield return Unescape(m.Groups[2].Value);
            }
        }
    }

    private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static List<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).Order().ToList();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Felcra.Profiling.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(""""\bT\(\s*"((?:[^"\\]|\\.)*)"""")]
    private static partial Regex Direct();

    [GeneratedRegex(""""\bT\(\s*[^"\n;]+?\?\s*"((?:[^"\\]|\\.)*)"\s*:\s*"((?:[^"\\]|\\.)*)"""")]
    private static partial Regex Ternary();

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();
}

/// <summary>Switching languages, and validation messages following the current language.</summary>
public class LanguageTests
{
    [Fact]
    public void Text_follows_the_UI_culture_and_falls_back_to_English()
    {
        using (Text.Use(Text.Malay))
        {
            Assert.Equal(Text.Malay, Text.Language);
            Assert.Equal("Ladang", Text.T("Farms"));
            Assert.Equal("{0} ekor", Text.T("{0} head"));
            Assert.Equal("12 ekor", Text.T("{0} head", 12));
            Assert.Equal("Not a known text", Text.T("Not a known text"));
        }
        using (Text.Use(Text.English))
        {
            Assert.Equal("Farms", Text.T("Farms"));
            Assert.Equal("12 head", Text.T("{0} head", 12));
        }
    }

    [Theory]
    [InlineData("ms-MY,ms;q=0.9,en;q=0.8", "ms")]
    [InlineData("ms", "ms")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Parse_reads_the_first_preference(string? header, string? expected) => Assert.Equal(expected, Text.Parse(header));

    [Fact]
    public void Validation_messages_are_translated_when_validating_not_when_the_validator_is_built()
    {
        ValidationText.Configure();
        var validator = new FarmRequestValidator(); // built once, like the server's singletons
        var request = new FarmRequest { Code = "bad code!", Name = "", Latitude = 20, Longitude = 103 };

        using (Text.Use(Text.Malay))
        {
            var errors = validator.Validate(request).Errors.Select(e => e.ErrorMessage).ToList();
            Assert.Contains("Kod hanya boleh mengandungi huruf, digit, '-' dan '_'.", errors);
            Assert.Contains("'Nama' wajib diisi.", errors);
            Assert.Contains("Latitud mestilah di Malaysia (0.5 hingga 7.5).", errors);
        }
        using (Text.Use(Text.English))
        {
            var errors = validator.Validate(request).Errors.Select(e => e.ErrorMessage).ToList();
            Assert.Contains("Code may only contain letters, digits, '-' and '_'.", errors);
            Assert.Contains("'Name' must not be empty.", errors);
        }
    }

    [Theory]
    [InlineData("AreaHectares", "Area hectares")]
    [InlineData("TagNumber", "Tag number")]
    [InlineData("BCS", "BCS")]
    [InlineData("DiagnosticTest", "Diagnostic test")]
    [InlineData("Name", "Name")]
    public void FieldName_turns_property_names_into_words(string property, string expected) =>
        Assert.Equal(expected, ValidationText.FieldName(property));

    [Fact]
    public void Use_restores_the_previous_culture()
    {
        var before = CultureInfo.CurrentUICulture;
        using (Text.Use(Text.Malay)) Assert.Equal("ms-MY", CultureInfo.CurrentUICulture.Name);
        Assert.Equal(before, CultureInfo.CurrentUICulture);
    }
}
