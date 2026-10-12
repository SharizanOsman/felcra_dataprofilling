using System.Text;
using FluentValidation;
using FluentValidation.Resources;

namespace Felcra.Profiling.Contracts.Common;

/// <summary>
/// Validation messages in the current language. Call <see cref="Configure"/> once at startup (server and clients).
/// Custom messages use <c>WithMessage(_ => T("…"))</c> so they are translated per request, not once when the
/// (singleton) validator is built.
/// </summary>
public static class ValidationText
{
    public static void Configure()
    {
        ValidatorOptions.Global.LanguageManager = new BilingualLanguageManager();
        // Field names: "AreaHectares" → "Area hectares" → Malay. English keeps FluentValidation's own names.
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
            member is null || Language == English ? null : T(FieldName(member.Name));
    }

    /// <summary>"AreaHectares" → "Area hectares", the key for a field's name. Acronyms ("BCS") stay upper case.</summary>
    public static string FieldName(string propertyName)
    {
        var name = new StringBuilder(propertyName.Length + 4);
        for (var i = 0; i < propertyName.Length; i++)
        {
            var c = propertyName[i];
            if (i == 0 || !char.IsUpper(c))
            {
                name.Append(c);
                continue;
            }
            var previous = propertyName[i - 1];
            var nextIsLower = i + 1 < propertyName.Length && char.IsLower(propertyName[i + 1]);
            if (!char.IsUpper(previous) || nextIsLower) name.Append(' ');
            var inAcronym = (char.IsUpper(previous) && !nextIsLower) || (i + 1 < propertyName.Length && char.IsUpper(propertyName[i + 1]));
            name.Append(inAcronym ? c : char.ToLowerInvariant(c));
        }
        return name.ToString();
    }

    private sealed class BilingualLanguageManager : LanguageManager
    {
        public BilingualLanguageManager()
        {
            foreach (var (key, message) in Malay)
                AddTranslation(Localization.Text.Malay, key, message);
        }

        private static readonly Dictionary<string, string> Malay = new()
        {
            ["EmailValidator"] = "'{PropertyName}' bukan alamat e-mel yang sah.",
            ["GreaterThanOrEqualValidator"] = "'{PropertyName}' mestilah sekurang-kurangnya '{ComparisonValue}'.",
            ["GreaterThanValidator"] = "'{PropertyName}' mestilah lebih daripada '{ComparisonValue}'.",
            ["LengthValidator"] = "'{PropertyName}' mestilah antara {MinLength} hingga {MaxLength} aksara. Anda memasukkan {TotalLength} aksara.",
            ["MinimumLengthValidator"] = "'{PropertyName}' mestilah sekurang-kurangnya {MinLength} aksara. Anda memasukkan {TotalLength} aksara.",
            ["MaximumLengthValidator"] = "'{PropertyName}' mestilah tidak melebihi {MaxLength} aksara. Anda memasukkan {TotalLength} aksara.",
            ["LessThanOrEqualValidator"] = "'{PropertyName}' mestilah tidak melebihi '{ComparisonValue}'.",
            ["LessThanValidator"] = "'{PropertyName}' mestilah kurang daripada '{ComparisonValue}'.",
            ["NotEmptyValidator"] = "'{PropertyName}' wajib diisi.",
            ["NotEqualValidator"] = "'{PropertyName}' tidak boleh sama dengan '{ComparisonValue}'.",
            ["NotNullValidator"] = "'{PropertyName}' wajib diisi.",
            ["PredicateValidator"] = "'{PropertyName}' tidak memenuhi syarat yang ditetapkan.",
            ["AsyncPredicateValidator"] = "'{PropertyName}' tidak memenuhi syarat yang ditetapkan.",
            ["RegularExpressionValidator"] = "Format '{PropertyName}' tidak betul.",
            ["EqualValidator"] = "'{PropertyName}' mestilah sama dengan '{ComparisonValue}'.",
            ["ExactLengthValidator"] = "'{PropertyName}' mestilah tepat {MaxLength} aksara. Anda memasukkan {TotalLength} aksara.",
            ["InclusiveBetweenValidator"] = "'{PropertyName}' mestilah antara {From} hingga {To}. Anda memasukkan {PropertyValue}.",
            ["ExclusiveBetweenValidator"] = "'{PropertyName}' mestilah antara {From} hingga {To} (tidak termasuk). Anda memasukkan {PropertyValue}.",
            ["EmptyValidator"] = "'{PropertyName}' mestilah kosong.",
            ["NullValidator"] = "'{PropertyName}' mestilah kosong.",
            ["EnumValidator"] = "'{PropertyName}' tidak mempunyai nilai '{PropertyValue}'.",
            ["ScalePrecisionValidator"] = "'{PropertyName}' tidak boleh melebihi {ExpectedPrecision} digit, termasuk {ExpectedScale} tempat perpuluhan. Terdapat {Digits} digit dan {ActualScale} tempat perpuluhan.",
            ["Length_Simple"] = "'{PropertyName}' mestilah antara {MinLength} hingga {MaxLength} aksara.",
            ["MinimumLength_Simple"] = "'{PropertyName}' mestilah sekurang-kurangnya {MinLength} aksara.",
            ["MaximumLength_Simple"] = "'{PropertyName}' mestilah tidak melebihi {MaxLength} aksara.",
            ["ExactLength_Simple"] = "'{PropertyName}' mestilah tepat {MaxLength} aksara.",
            ["InclusiveBetween_Simple"] = "'{PropertyName}' mestilah antara {From} hingga {To}.",
        };
    }
}
