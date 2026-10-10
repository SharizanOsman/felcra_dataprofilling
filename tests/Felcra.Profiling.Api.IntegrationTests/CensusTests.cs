using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CensusTests(ApiFactory factory)
{
    /// <summary>A slice of the Pagoh paper form: tick columns, untagged animals, a tag used twice, project in Catatan.</summary>
    private const string PagohStyleCsv = """
        BIL.,NO.TAG,P,I,AJ,AB,UMUR,BERAT,CATATAN
        1,0125,,,/,,1 Tahun,119 Kg,RP4
        3,178,/,,,,4 Tahun,317 Kg,RP4
        5,232,,/,,,6 Tahun,345 Kg,RP4
        30,08478,,,,/,8 Bulan,121 Kg,EPP5
        72,NO. TAG,,,,/,1 Tahun 6 Bulan,195 Kg,EPP5
        85,08478,,,,/,1 Tahun,161 Kg,RP4
        135,NO. TAG,,,,/,6 Bulan,,EPP5
        """;

    /// <summary>A new farm (linked to EPP5 and RP4) with an empty census session, so tests do not interfere.</summary>
    private async Task<(HttpClient Hq, CensusSessionDto Session)> NewSessionAsync()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var projects = (await hq.GetFromJsonAsync<List<ProjectDto>>("/api/projects"))!.Where(p => p.Code is "EPP5" or "RP4").Select(p => p.Id).ToList();
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("c"), Name = "Census farm", ProjectIds = projects }))
            .ReadAsync<FarmDto>();
        var session = await (await hq.PostAsJsonAsync("/api/census-sessions", new CensusSessionRequest
        {
            FarmId = farm.Id, Title = "Headcount July 2026", StartDate = new DateOnly(2026, 7, 2), EndDate = new DateOnly(2026, 7, 6),
            PicName = "Muhamad Alif", RecordedBulls = 11, RecordedDams = 61, RecordedMaleCalves = 28, RecordedFemaleCalves = 40,
        })).ReadAsync<CensusSessionDto>();
        return (hq, session);
    }

    private static MultipartFormDataContent CsvUpload(string csv, string fileName = "census.csv")
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task Preview_reports_what_the_import_will_do()
    {
        var (hq, session) = await NewSessionAsync();

        var preview = await (await hq.PostAsync($"/api/census-sessions/{session.Id}/import/preview", CsvUpload(PagohStyleCsv)))
            .ReadAsync<ImportPreviewDto>();

        Assert.True(preview.CanImport);
        Assert.Equal(7, preview.AnimalsToCreate);
        Assert.Equal(new CategoryCounts(1, 1, 1, 4), preview.Counts);
        Assert.Equal("RP4", preview.Rows[0].ProjectCode);
        Assert.Null(preview.Rows[0].Remarks);
        Assert.Contains(preview.Rows.Single(r => r.LineNo == 30).Warnings, w => w.Contains("appears 2 times"));
        Assert.Contains(preview.Rows.Single(r => r.LineNo == 72).Warnings, w => w.Contains("No ear tag"));
    }

    [Fact]
    public async Task Import_registers_animals_and_census_entries()
    {
        var (hq, session) = await NewSessionAsync();

        var result = await (await hq.PostAsync($"/api/census-sessions/{session.Id}/import", CsvUpload(PagohStyleCsv)))
            .ReadAsync<ImportResultDto>();
        Assert.Equal(new ImportResultDto(7, 0, 7, 0), result);

        var updated = await hq.GetFromJsonAsync<CensusSessionDto>($"/api/census-sessions/{session.Id}");
        Assert.Equal(new CategoryCounts(1, 1, 1, 4), updated!.Counted);

        var entries = (await hq.GetFromJsonAsync<List<CensusEntryDto>>($"/api/census-sessions/{session.Id}/entries"))!;
        Assert.Equal([1, 3, 5, 30, 72, 85, 135], entries.Select(e => e.LineNo!.Value));

        // Animal numbers follow the sheet's line order, so they can be matched against the paper form.
        var numbers = entries.Select(e => int.Parse(e.AnimalNo[1..])).ToList();
        Assert.Equal(numbers.Order(), numbers);
        Assert.Equal(numbers[0] + 6, numbers[^1]);
        Assert.True(entries.Single(e => e.LineNo == 30).HasDuplicateTag);

        // Birth date is estimated from the census age relative to the session start.
        var calf = await hq.GetFromJsonAsync<AnimalDto>($"/api/animals/{entries[0].AnimalId}");
        Assert.Equal(new DateOnly(2025, 7, 2), calf!.BirthDate);
        Assert.Equal(AgeEstimationMethod.Dentition, calf.BirthDateMethod);
        Assert.Single(calf.CensusHistory);

        var untagged = await hq.GetFromJsonAsync<PagedResult<AnimalListItemDto>>($"/api/animals?farmId={session.FarmId}&untaggedOnly=true");
        Assert.Equal(2, untagged!.TotalCount);
    }

    [Fact]
    public async Task Reimport_with_matching_updates_single_tags_and_refuses_ambiguous_ones()
    {
        var (hq, session) = await NewSessionAsync();
        (await hq.PostAsync($"/api/census-sessions/{session.Id}/import", CsvUpload(PagohStyleCsv))).EnsureSuccessStatusCode();

        var preview = await (await hq.PostAsync($"/api/census-sessions/{session.Id}/import/preview?matchExisting=true", CsvUpload(PagohStyleCsv)))
            .ReadAsync<ImportPreviewDto>();

        Assert.False(preview.CanImport);
        Assert.Equal(ImportRowAction.MatchExisting, preview.Rows.Single(r => r.TagNumber == "0125").Action);
        Assert.All(preview.Rows.Where(r => r.TagNumber == "08478"), r => Assert.Equal(ImportRowAction.Error, r.Action));

        var refused = await hq.PostAsync($"/api/census-sessions/{session.Id}/import?matchExisting=true", CsvUpload(PagohStyleCsv));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Without the ambiguous rows the re-import updates existing entries instead of duplicating animals.
        var unambiguous = string.Join('\n', PagohStyleCsv.Split('\n').Where(l => !l.Contains("08478")));
        var result = await (await hq.PostAsync($"/api/census-sessions/{session.Id}/import?matchExisting=true", CsvUpload(unambiguous)))
            .ReadAsync<ImportResultDto>();
        Assert.Equal(3, result.AnimalsMatched);
        Assert.Equal(3, result.EntriesUpdated);
    }

    [Fact]
    public async Task Unreadable_files_are_rejected_with_a_message()
    {
        var (hq, session) = await NewSessionAsync();

        var noHeader = await hq.PostAsync($"/api/census-sessions/{session.Id}/import/preview", CsvUpload("Name,Weight\nDaisy,300"));
        var wrongType = await hq.PostAsync($"/api/census-sessions/{session.Id}/import/preview", CsvUpload("x", "notes.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);
        Assert.Contains("Tag", await noHeader.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
    }

    [Fact]
    public async Task Downloaded_template_can_be_previewed()
    {
        var (hq, session) = await NewSessionAsync();
        var template = await hq.GetByteArrayAsync("/api/census-sessions/import-template");

        var content = new ByteArrayContent(template);
        var upload = new MultipartFormDataContent { { content, "file", "census-template.xlsx" } };
        var preview = await (await hq.PostAsync($"/api/census-sessions/{session.Id}/import/preview", upload)).ReadAsync<ImportPreviewDto>();

        var example = Assert.Single(preview.Rows);
        Assert.Equal(("0125", AnimalCategory.MaleCalf, 119m, "RP4"), (example.TagNumber, example.Category!.Value, example.WeightKg!.Value, example.ProjectCode));
    }

    [Fact]
    public async Task Entries_can_be_edited_and_closed_sessions_are_locked()
    {
        var (hq, session) = await NewSessionAsync();
        (await hq.PostAsync($"/api/census-sessions/{session.Id}/import", CsvUpload(PagohStyleCsv))).EnsureSuccessStatusCode();
        var entry = (await hq.GetFromJsonAsync<List<CensusEntryDto>>($"/api/census-sessions/{session.Id}/entries"))![1];

        var request = CensusEntryRequest.From(entry);
        request.Selected = true;
        request.BodyConditionScore = 3;
        var updated = await (await hq.PutAsJsonAsync($"/api/census-sessions/{session.Id}/entries/{entry.Id}", request)).ReadAsync<CensusEntryDto>();
        Assert.True(updated.Selected);
        Assert.Equal(1, (await hq.GetFromJsonAsync<CensusSessionDto>($"/api/census-sessions/{session.Id}"))!.SelectedCount);

        var current = await hq.GetFromJsonAsync<CensusSessionDto>($"/api/census-sessions/{session.Id}");
        var close = CensusSessionRequest.From(current!);
        close.IsClosed = true;
        (await hq.PutAsJsonAsync($"/api/census-sessions/{session.Id}", close)).EnsureSuccessStatusCode();

        request = CensusEntryRequest.From(updated);
        request.Selected = false;
        Assert.Equal(HttpStatusCode.BadRequest, (await hq.PutAsJsonAsync($"/api/census-sessions/{session.Id}/entries/{entry.Id}", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await hq.PostAsync($"/api/census-sessions/{session.Id}/import", CsvUpload(PagohStyleCsv))).StatusCode);
    }

    [Fact]
    public async Task Field_officer_records_entries_but_cannot_import()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var farmId = (await manager.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single().Id;
        var session = await (await manager.PostAsJsonAsync("/api/census-sessions", new CensusSessionRequest
        {
            FarmId = farmId, Title = TestHelpers.UniqueCode("Session "), StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 1),
        })).ReadAsync<CensusSessionDto>();
        var animal = await (await manager.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farmId, Category = AnimalCategory.Bull, Sex = Sex.Male,
        })).ReadAsync<AnimalDto>();

        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var entry = await (await officer.PostAsJsonAsync($"/api/census-sessions/{session.Id}/entries", new CensusEntryRequest
        {
            AnimalId = animal.Id, Category = AnimalCategory.Bull, WeightKg = 420,
        })).ReadAsync<CensusEntryDto>();
        Assert.Equal(420m, entry.WeightKg);

        var duplicate = await officer.PostAsJsonAsync($"/api/census-sessions/{session.Id}/entries", new CensusEntryRequest
        {
            AnimalId = animal.Id, Category = AnimalCategory.Bull,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await officer.PostAsync($"/api/census-sessions/{session.Id}/import/preview", CsvUpload(PagohStyleCsv))).StatusCode);
    }
}
