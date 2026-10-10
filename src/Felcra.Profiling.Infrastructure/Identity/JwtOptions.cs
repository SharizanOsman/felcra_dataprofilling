using System.ComponentModel.DataAnnotations;

namespace Felcra.Profiling.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; set; } = "felcra-profiling";
    [Required] public string Audience { get; set; } = "felcra-profiling";

    /// <summary>HMAC-SHA256 key; at least 32 characters. Supply via user secrets / environment, never commit.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = "";

    [Range(1, 120)] public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Long-lived so field officers stay signed in while working offline.</summary>
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 30;
}
