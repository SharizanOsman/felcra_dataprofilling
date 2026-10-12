using FluentValidation;

namespace Felcra.Profiling.UI.Shared.Forms;

/// <summary>Adapts a FluentValidation validator to MudForm's per-field <c>Validation</c> parameter.</summary>
public static class FormValidation
{
    public static Func<object, string, Task<IEnumerable<string>>> For<T>(IValidator<T> validator) =>
        async (model, propertyName) =>
        {
            var result = await validator.ValidateAsync(
                ValidationContext<T>.CreateWithOptions((T)model, options => options.IncludeProperties(propertyName)));
            return result.IsValid ? [] : result.Errors.Select(e => e.ErrorMessage);
        };
}

public static class EnumText
{
    /// <summary>"DiagnosticTest" → "Diagnostic test" (or its Malay).</summary>
    public static string Humanize<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        var chars = new List<char>(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                chars.Add(' ');
                chars.Add(char.ToLowerInvariant(name[i]));
            }
            else
            {
                chars.Add(name[i]);
            }
        }
        return T(new string([.. chars]));

    }
}
