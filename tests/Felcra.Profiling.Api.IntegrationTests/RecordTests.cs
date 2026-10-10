using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Contracts.Records;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.MasterData;
using Felcra.Profiling.Domain.Records;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RecordTests(ApiFactory factory)
{
    // Smallest valid PNG (1×1 pixel).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private async Task<(HttpClient Officer, AnimalDto Animal)> NewAnimalAsync()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var farmId = (await manager.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single().Id;
        var animal = await (await manager.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farmId, TagNumber = TestHelpers.UniqueCode("r"), Category = AnimalCategory.Dam, Sex = Sex.Female,
        })).ReadAsync<AnimalDto>();
        return (await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail), animal);
    }

    private static MultipartFormDataContent PhotoUpload(byte[] bytes, string name = "photo.png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "file", name } };
    }

    [Fact]
    public async Task Officer_records_a_weight_that_becomes_the_latest_weight()
    {
        var (officer, animal) = await NewAnimalAsync();

        var created = await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements",
            new MeasurementRequest { Type = MeasurementType.Weight, Value = 352.5m, Method = WeighingMethod.Scale });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var records = await officer.GetFromJsonAsync<AnimalRecordsDto>($"/api/animals/{animal.Id}/records");
        var weight = Assert.Single(records!.Measurements);
        Assert.Equal((352.5m, "Pagoh Field Officer"), (weight.Value, weight.RecordedBy));

        var list = await officer.GetFromJsonAsync<PagedResult<AnimalListItemDto>>($"/api/animals?ids={animal.Id}");
        Assert.Equal(352.5m, list!.Items.Single().LatestWeightKg);
    }

    [Fact]
    public async Task Sending_the_same_record_twice_stores_it_once()
    {
        var (officer, animal) = await NewAnimalAsync();
        var request = new MeasurementRequest { Type = MeasurementType.BodyCondition, Value = 3 };

        var first = await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements", request);
        var retry = await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(request.Id, (await retry.ReadAsync<MeasurementDto>()).Id);
        Assert.Single((await officer.GetFromJsonAsync<AnimalRecordsDto>($"/api/animals/{animal.Id}/records"))!.Measurements);
    }

    [Theory]
    [InlineData(MeasurementType.Weight, 2000)]
    [InlineData(MeasurementType.BodyCondition, 12)]
    [InlineData(MeasurementType.MilkYield, -1)]
    public async Task Implausible_values_are_rejected(MeasurementType type, decimal value)
    {
        var (officer, animal) = await NewAnimalAsync();

        var response = await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements", new MeasurementRequest { Type = type, Value = value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Treatment_uses_product_defaults_and_withdrawal_period()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var product = await (await hq.PostAsJsonAsync("/api/treatment-products", new TreatmentProductRequest
        {
            Name = TestHelpers.UniqueCode("Ivermectin "), Category = TreatmentCategory.Antiparasitic, Unit = "ml", DefaultDose = 5, WithdrawalDays = 28,
        })).ReadAsync<TreatmentProductDto>();
        var (officer, animal) = await NewAnimalAsync();

        var record = await (await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/treatments", new TreatmentRecordRequest
        {
            TreatmentProductId = product.Id, GivenOn = new DateOnly(2026, 10, 1), BatchNumber = "B123", NextDueOn = new DateOnly(2027, 4, 1),
        })).ReadAsync<TreatmentRecordDto>();

        Assert.Equal((5m, "ml"), (record.Dose, record.DoseUnit));
        Assert.Equal(new DateOnly(2026, 10, 29), record.WithdrawalUntil);
        Assert.Equal(product.Name, record.ProductName);
    }

    [Fact]
    public async Task Records_cannot_be_added_to_animals_that_left_the_farm()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var (officer, animal) = await NewAnimalAsync();
        (await manager.PostAsJsonAsync($"/api/animals/{animal.Id}/status",
            new ChangeStatusRequest { Status = AnimalStatus.Sold, Date = new DateOnly(2026, 9, 1), Version = animal.Version })).EnsureSuccessStatusCode();

        var response = await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements", new MeasurementRequest { Value = 300 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sold", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Observation_with_photo_can_be_viewed_back()
    {
        var (officer, animal) = await NewAnimalAsync();

        var photo = await (await officer.PostAsync($"/api/animals/{animal.Id}/photos", PhotoUpload(Png))).ReadAsync<PhotoDto>();
        var observation = await (await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/observations", new ObservationRequest
        {
            Category = ObservationCategory.LegsAndHooves, Notes = "Limping on left hind leg", PhotoIds = [photo.Id],
        })).ReadAsync<ObservationDto>();

        Assert.Equal([photo.Id], observation.PhotoIds);
        var image = await officer.GetAsync($"/api/photos/{photo.Id}");
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Non_images_and_other_animals_photos_are_rejected()
    {
        var (officer, animal) = await NewAnimalAsync();
        var (_, other) = await NewAnimalAsync();

        var notImage = await officer.PostAsync($"/api/animals/{animal.Id}/photos", PhotoUpload("<script>alert(1)</script>"u8.ToArray(), "x.png"));
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);

        var othersPhoto = await (await officer.PostAsync($"/api/animals/{other.Id}/photos", PhotoUpload(Png))).ReadAsync<PhotoDto>();
        var response = await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/observations",
            new ObservationRequest { Notes = "x", PhotoIds = [othersPhoto.Id] });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_managers_delete_records()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var (officer, animal) = await NewAnimalAsync();
        var weight = await (await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements", new MeasurementRequest { Value = 280 }))
            .ReadAsync<MeasurementDto>();

        Assert.Equal(HttpStatusCode.Forbidden, (await officer.DeleteAsync($"/api/measurements/{weight.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/measurements/{weight.Id}")).StatusCode);
        Assert.Empty((await officer.GetFromJsonAsync<AnimalRecordsDto>($"/api/animals/{animal.Id}/records"))!.Measurements);
    }

    [Fact]
    public async Task Animals_on_other_farms_cannot_be_recorded()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("z"), Name = "Elsewhere" })).ReadAsync<FarmDto>();
        var animal = await (await hq.PostAsJsonAsync("/api/animals", new AnimalRequest { FarmId = farm.Id, Category = AnimalCategory.Bull, Sex = Sex.Male }))
            .ReadAsync<AnimalDto>();
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);

        var response = await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements", new MeasurementRequest { Value = 400 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
