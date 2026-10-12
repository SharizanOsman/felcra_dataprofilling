using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.Geo;
using Microsoft.AspNetCore.Mvc;

namespace Felcra.Profiling.Api.IntegrationTests;

/// <summary>The API answers in the language the app asks for; place search for the farm map.</summary>
[Collection(ApiCollection.Name)]
public class LanguageTests(ApiFactory factory)
{
    [Fact]
    public async Task Validation_errors_follow_Accept_Language()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var invalid = new FarmRequest { Code = "bad code!", Name = "", Latitude = 30, Longitude = 103 };

        var english = await (await hq.PostAsJsonAsync("/api/farms", invalid)).Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Latitude must be in Malaysia (0.5 to 7.5).", english!.Errors["Latitude"]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/farms") { Content = JsonContent.Create(invalid) };
        request.Headers.AcceptLanguage.ParseAdd("ms-MY");
        var malay = await (await hq.SendAsync(request)).Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Latitud mestilah di Malaysia (0.5 hingga 7.5).", malay!.Errors["Latitude"]);
        Assert.Contains("'Nama' wajib diisi.", malay.Errors["Name"]);
    }

    [Fact]
    public async Task Reports_are_written_in_the_requested_language()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail, "ms");
        hq.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ms");
        var bytes = await hq.GetByteArrayAsync("/api/reports/performance?from=2026-01-01&to=2026-01-31&format=xlsx");

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("Ringkasan", workbook.Worksheets.First().Name);
        Assert.Equal("Laporan prestasi ladang", workbook.Worksheets.First().Cell(1, 1).GetString());
        Assert.Contains(workbook.Worksheets, s => s.Name == "Mengikut kategori");
    }

    [Fact]
    public async Task Place_search_finds_places_and_reads_GPS_coordinates()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        hq.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ms");

        var places = await hq.GetFromJsonAsync<List<GeoPlaceDto>>("/api/geocode/search?q=Pagoh");
        Assert.Equal("Johor", Assert.Single(places!).State);
        Assert.Equal("ms", factory.Geocoder.Languages[^1]);

        // Coordinates or a Google Maps link: the point itself, named after the place it is in.
        var point = Assert.Single((await hq.GetFromJsonAsync<List<GeoPlaceDto>>(
            "/api/geocode/search?q=" + Uri.EscapeDataString("https://www.google.com/maps/@2.1501,102.7801,15z")))!);
        Assert.Equal((2.1501m, 102.7801m, "Pagoh", "coordinates"), (point.Latitude, point.Longitude, point.Name, point.Type));

        var named = await hq.GetFromJsonAsync<GeoPlaceDto>("/api/geocode/reverse?lat=2.1483&lon=102.7712");
        Assert.Equal("Pagoh", named!.Name);
        Assert.Equal(HttpStatusCode.NotFound, (await hq.GetAsync("/api/geocode/reverse?lat=5.0&lon=118.0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await hq.GetAsync("/api/geocode/search?q=a")).StatusCode);
    }

    [Fact]
    public async Task Place_search_reports_an_unavailable_service_and_is_for_managers_only()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var down = await hq.GetAsync("/api/geocode/search?q=offline");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);

        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.GetAsync("/api/geocode/search?q=Pagoh")).StatusCode);
    }
}
