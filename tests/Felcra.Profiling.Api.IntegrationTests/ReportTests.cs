using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using Felcra.Profiling.Api.Reports;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Contracts.Records;
using Felcra.Profiling.Contracts.Reports;
using Felcra.Profiling.Domain.Animals;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using PdfSharp.Pdf.IO;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReportTests(ApiFactory factory)
{
    private const string Census = """
        Line,Tag No,P,I,AJ,AB,Age,Weight (kg),BCS,Project,Remarks,Selected
        1,0125,,,/,,1 Tahun,119,,RP4,,
        2,178,/,,,,4 Tahun,317,3,RP4,,/
        3,232,,/,,,6 Tahun,345,3.5,RP4,Good udder,/
        4,08478,,,,/,8 Bulan,121,,EPP5,,
        5,08478,,,,/,1 Tahun,161,,RP4,,
        6,NO. TAG,,,,/,6 Bulan,,,EPP5,,
        """;

    private async Task<(HttpClient Hq, FarmDto Farm, CensusSessionDto Session)> ImportedSessionAsync()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var projects = (await hq.GetFromJsonAsync<List<ProjectDto>>("/api/projects"))!.Where(p => p.Code is "EPP5" or "RP4").Select(p => p.Id).ToList();
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest
        {
            Code = TestHelpers.UniqueCode("r"), Name = "Report farm", Address = "Kandang Tetap, Segamat", ProjectIds = projects, Capacity = 50,
        })).ReadAsync<FarmDto>();
        var session = await NewSessionAsync(hq, farm.Id);
        (await hq.PostAsync($"/api/census-sessions/{session.Id}/import", Upload(Encoding.UTF8.GetBytes(Census), "census.csv", "text/csv")))
            .EnsureSuccessStatusCode();
        return (hq, farm, session);
    }

    private static async Task<CensusSessionDto> NewSessionAsync(HttpClient hq, Guid farmId) =>
        await (await hq.PostAsJsonAsync("/api/census-sessions", new CensusSessionRequest
        {
            FarmId = farmId, Title = "Headcount July 2026", StartDate = new DateOnly(2026, 7, 2), EndDate = new DateOnly(2026, 7, 6),
            PicName = "Muhamad Alif", PicPosition = "Assistant Veterinary Executive", RecordedBulls = 1, RecordedDams = 2, RecordedMaleCalves = 1, RecordedFemaleCalves = 2,
            Notes = "Replace dams aged 8 years and above with heifers.",
        })).ReadAsync<CensusSessionDto>();

    private static MultipartFormDataContent Upload(byte[] content, string fileName, string contentType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<(byte[] Content, string? FileName, string? ContentType)> DownloadAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadAsByteArrayAsync(), response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName,
            response.Content.Headers.ContentType?.MediaType);
    }

    private static int PdfPages(byte[] pdf) => PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import).PageCount;

    [Fact]
    public async Task Census_form_pdf_has_summary_and_form_pages()
    {
        var (hq, farm, session) = await ImportedSessionAsync();

        var (pdf, name, type) = await DownloadAsync(hq, $"/api/reports/census-sessions/{session.Id}/form");

        Assert.Equal("application/pdf", type);
        Assert.Equal($"census-form-{farm.Code.ToLowerInvariant()}-2026-07-02.pdf", name);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(PdfPages(pdf) >= 2);
    }

    [Fact]
    public async Task Census_form_excel_marks_selected_animals_and_can_be_imported_again()
    {
        var (hq, _, session) = await ImportedSessionAsync();

        var (xlsx, name, _) = await DownloadAsync(hq, $"/api/reports/census-sessions/{session.Id}/form?format=xlsx");

        Assert.EndsWith(".xlsx", name);
        using (var workbook = new XLWorkbook(new MemoryStream(xlsx)))
        {
            var sheet = workbook.Worksheet("Census");
            var header = sheet.RowsUsed().First(r => r.Cell(2).GetString() == "Tag No").RowNumber();
            var rows = Enumerable.Range(header + 1, 6).Select(sheet.Row).ToList();
            Assert.Equal(["0125", "178", "232", "08478", "08478", "NO. TAG"], rows.Select(r => r.Cell(2).GetString()));
            Assert.Equal("/", rows[1].Cell(12).GetString());
            Assert.Equal(XLColor.FromHtml("#FFF59D"), rows[2].Cell(1).Style.Fill.BackgroundColor);
            Assert.Equal("1 year", rows[0].Cell(7).GetString());
            Assert.True(rows[3].Cell(2).HasComment);
        }

        // The export uses the import template's columns, so it reads back the same.
        var farm2 = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("r"), Name = "Copy farm" })).ReadAsync<FarmDto>();
        var copy = await NewSessionAsync(hq, farm2.Id);
        (await hq.PostAsync($"/api/census-sessions/{copy.Id}/import", Upload(xlsx, "export.xlsx", ReportFile.Xlsx))).EnsureSuccessStatusCode();
        var copied = await hq.GetFromJsonAsync<CensusSessionDto>($"/api/census-sessions/{copy.Id}");
        Assert.Equal(new CategoryCounts(1, 1, 1, 3), copied!.Counted);
        Assert.Equal(2, copied.SelectedCount);
    }

    [Fact]
    public async Task Selected_animals_list_in_both_formats()
    {
        var (hq, _, session) = await ImportedSessionAsync();

        var (xlsx, _, _) = await DownloadAsync(hq, $"/api/reports/census-sessions/{session.Id}/selected?format=xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var tags = workbook.Worksheet("Selected").Column(3).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("178", tags);
        Assert.Contains("232", tags);
        Assert.DoesNotContain("0125", tags);

        var (pdf, _, _) = await DownloadAsync(hq, $"/api/reports/census-sessions/{session.Id}/selected");
        Assert.Equal(1, PdfPages(pdf));
    }

    [Fact]
    public async Task Treatment_report_covers_the_period_and_farm()
    {
        var (hq, farm, _) = await ImportedSessionAsync();
        var animal = (await hq.GetFromJsonAsync<Contracts.Common.PagedResult<AnimalListItemDto>>($"/api/animals?farmId={farm.Id}&search=232"))!.Items.Single();
        var product = (await hq.GetFromJsonAsync<List<TreatmentProductDto>>("/api/treatment-products"))!.First(p => p.Name.StartsWith("FMD"));
        var day = new DateOnly(2026, 8, 14);
        (await hq.PostAsJsonAsync($"/api/animals/{animal.Id}/treatments", new TreatmentRecordRequest
        {
            TreatmentProductId = product.Id, GivenOn = day, Dose = 2, DoseUnit = "ml", BatchNumber = "B-77",
        })).EnsureSuccessStatusCode();

        var (xlsx, name, _) = await DownloadAsync(hq, $"/api/reports/treatments?farmId={farm.Id}&from=2026-08-01&to=2026-08-31&format=xlsx");

        Assert.Equal($"treatments-{farm.Code.ToLowerInvariant()}-2026-08.xlsx", name);
        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var row = workbook.Worksheet("Treatments").RowsUsed().Single(r => r.Cell(4).GetString() == "232");
        Assert.Equal(day.ToDateTime(TimeOnly.MinValue), row.Cell(1).GetDateTime());
        Assert.Equal("2 ml", row.Cell(8).GetString());
        Assert.Equal("B-77", row.Cell(10).GetString());

        // Outside the period: header only.
        var (empty, _, _) = await DownloadAsync(hq, $"/api/reports/treatments?farmId={farm.Id}&from=2026-09-01&to=2026-09-30&format=xlsx");
        using var emptyBook = new XLWorkbook(new MemoryStream(empty));
        Assert.DoesNotContain(emptyBook.Worksheet("Treatments").RowsUsed(), r => r.Cell(4).GetString() == "232");
    }

    [Fact]
    public async Task Performance_report_lists_animals_that_left_the_herd()
    {
        var (hq, farm, _) = await ImportedSessionAsync();
        var listed = (await hq.GetFromJsonAsync<Contracts.Common.PagedResult<AnimalListItemDto>>($"/api/animals?farmId={farm.Id}&search=178"))!.Items.Single();
        var animal = (await hq.GetFromJsonAsync<AnimalDto>($"/api/animals/{listed.Id}"))!;
        (await hq.PostAsJsonAsync($"/api/animals/{animal.Id}/status", new ChangeStatusRequest
        {
            Status = AnimalStatus.Sold, Date = new DateOnly(2026, 8, 20), Reason = "Sold at auction", Version = animal.Version,
        })).EnsureSuccessStatusCode();

        var (xlsx, _, _) = await DownloadAsync(hq, $"/api/reports/performance?farmId={farm.Id}&from=2026-08-01&to=2026-08-31&format=xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        Assert.Contains(workbook.Worksheet("Left the herd").RowsUsed(), r => r.Cell(4).GetString() == "178" && r.Cell(7).GetString() == "Sold at auction");
        var summary = workbook.Worksheet("Summary");
        Assert.Equal(5, summary.RowsUsed().Single(r => r.Cell(1).GetString() == "Active animals").Cell(2).GetValue<int>());

        var (pdf, _, _) = await DownloadAsync(hq, $"/api/reports/performance?farmId={farm.Id}");
        Assert.True(PdfPages(pdf) >= 1);
    }

    [Fact]
    public async Task Culling_candidates_and_all_farm_reports_download()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);

        var (pdf, name, _) = await DownloadAsync(hq, "/api/reports/culling-candidates");
        Assert.StartsWith("culling-candidates-all-farms-", name);
        Assert.True(PdfPages(pdf) >= 1);
        var (xlsx, _, _) = await DownloadAsync(hq, "/api/reports/culling-candidates?format=excel");
        Assert.NotNull(new XLWorkbook(new MemoryStream(xlsx)).Worksheet("Culling candidates"));
    }

    [Fact]
    public async Task Bad_requests_and_access_are_refused()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        Assert.Equal(HttpStatusCode.BadRequest, (await hq.GetAsync("/api/reports/performance?format=docx")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await hq.GetAsync("/api/reports/performance?from=2026-09-01&to=2026-08-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hq.GetAsync($"/api/reports/census-sessions/{Guid.NewGuid()}/form")).StatusCode);

        // A manager cannot report on another farm; officers have no reports.
        var otherFarm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("o"), Name = "Other farm" })).ReadAsync<FarmDto>();
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/reports/treatments?farmId={otherFarm.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/reports/treatments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/reports/monthly")).StatusCode);
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.GetAsync("/api/reports/culling-candidates")).StatusCode);
    }

    [Fact]
    public async Task Monthly_reports_are_emailed_once_per_month()
    {
        var period = new DateOnly(2001, 1, 1);
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<MonthlyReportJob>();
        var recipients = await job.RecipientsAsync(CancellationToken.None);
        Assert.Contains(recipients, r => r.Email == ApiFactory.AdminEmail && r.IsHq);
        Assert.Contains(recipients, r => r.Email == ApiFactory.ManagerEmail && r.FarmIds.Count > 0);
        Assert.DoesNotContain(recipients, r => r.Email == ApiFactory.OfficerEmail);

        Assert.True(await job.RunScheduledAsync(period, CancellationToken.None));
        Assert.False(await job.RunScheduledAsync(period, CancellationToken.None));

        var messages = Directory.GetFiles(factory.MailDirectory, "*.eml").Select(f => MimeMessage.Load(f))
            .Where(m => (m.Subject ?? "").Contains("January 2001")).ToList();
        Assert.Equal(recipients.Sum(r => r.IsHq ? 1 : r.FarmIds.Count), messages.Count);
        var toAdmin = messages.Single(m => m.To.Mailboxes.Single().Address == ApiFactory.AdminEmail);
        Assert.Contains("All farms", toAdmin.Subject);
        Assert.Equal(3, toAdmin.Attachments.Count());
        Assert.Contains("Active animals:", toAdmin.TextBody);

        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var status = await hq.GetFromJsonAsync<MonthlyReportStatusDto>("/api/reports/monthly");
        Assert.True(status!.EmailConfigured);
        Assert.False(status.Enabled);
        var run = status.RecentRuns.Single(r => r.Period == period);
        Assert.Equal(messages.Count, run.EmailsSent);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task Hq_can_send_a_test_email_to_themselves()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);

        var result = await (await hq.PostAsync("/api/reports/monthly/test", null)).ReadAsync<TestEmailResultDto>();

        Assert.Equal(ApiFactory.AdminEmail, result.SentTo);
        Assert.Equal(3, result.Attachments.Count);
        Assert.Contains(Directory.GetFiles(factory.MailDirectory, "*.eml").Select(f => MimeMessage.Load(f)),
            m => (m.Subject ?? "").StartsWith("[Test]") && m.To.Mailboxes.Single().Address == ApiFactory.AdminEmail);
    }
}
