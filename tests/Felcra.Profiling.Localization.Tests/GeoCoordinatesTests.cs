using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.Geo;

namespace Felcra.Profiling.Localization.Tests;

public class GeoCoordinatesTests
{
    [Theory]
    [InlineData("2.148, 102.771", 2.148, 102.771)]
    [InlineData("2.148 102.771", 2.148, 102.771)]
    [InlineData(" (2.1483216, 102.7712345) ", 2.148322, 102.771235)]
    [InlineData("https://www.google.com/maps/@2.1483,102.7712,15z", 2.1483, 102.7712)]
    [InlineData("https://www.google.com/maps/search/?api=1&query=2.1483%2C102.7712", 2.1483, 102.7712)]
    [InlineData("https://maps.google.com/?q=2.1483,102.7712", 2.1483, 102.7712)]
    [InlineData("2°08'53.9\"N 102°46'16.3\"E", 2.148306, 102.771194)]
    [InlineData("2° 8' N, 102° 46' E", 2.133333, 102.766667)]
    public void Reads_what_people_paste(string text, double lat, double lon)
    {
        Assert.True(GeoCoordinates.TryParse(text, out var latitude, out var longitude));
        Assert.Equal((decimal)lat, latitude);
        Assert.Equal((decimal)lon, longitude);
    }

    [Theory]
    [InlineData("Pagoh")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("95, 200")]
    public void Rejects_anything_else(string? text) => Assert.False(GeoCoordinates.TryParse(text, out _, out _));

    [Theory]
    [InlineData("Johor", "Johor")]
    [InlineData("Penang", "Pulau Pinang")]
    [InlineData("Wilayah Persekutuan Kuala Lumpur", "Kuala Lumpur")]
    [InlineData("negeri sembilan", "Negeri Sembilan")]
    [InlineData("Singapore", null)]
    [InlineData(null, null)]
    public void States_are_normalised(string? state, string? expected) => Assert.Equal(expected, MalaysianStates.Normalize(state));
}
