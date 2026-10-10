using Felcra.Profiling.Application.Census;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Application.Tests;

public class CensusSheetParserTests
{
    [Theory]
    [InlineData("1 Tahun", 12)]
    [InlineData("1 Tahun 6 Bulan", 18)]
    [InlineData("8 Bulan", 8)]
    [InlineData("10 Bulan", 10)]
    [InlineData("2 tahun 6 bulan", 30)]
    [InlineData("1.5 years", 18)]
    [InlineData("2y 3m", 27)]
    [InlineData("3 thn", 36)]
    public void Parses_ages_in_malay_and_english(string text, int months) =>
        Assert.Equal(months, CensusSheetParser.ParseAgeMonths(text));

    [Theory]
    [InlineData("4")]
    [InlineData("old")]
    [InlineData("4 tahun lebih")]
    public void Unclear_ages_are_not_guessed(string text) =>
        Assert.Null(CensusSheetParser.ParseAgeMonths(text));

    [Theory]
    [InlineData("P", AnimalCategory.Bull)]
    [InlineData("Induk", AnimalCategory.Dam)]
    [InlineData("aj", AnimalCategory.MaleCalf)]
    [InlineData("Anak Betina", AnimalCategory.FemaleCalf)]
    [InlineData("Female calf", AnimalCategory.FemaleCalf)]
    public void Parses_category_codes_and_names(string text, AnimalCategory expected) =>
        Assert.Equal(expected, CensusSheetParser.ParseCategory(text));

    /// <summary>Layout of the FFISB paper form: two header rows, tick columns, Malay values, project code in Catatan.</summary>
    [Fact]
    public void Parses_the_ffisb_paper_form_layout()
    {
        string?[][] sheet =
        [
            ["BORANG BANCIAN DAN PROFILLING DATA", null, null, null, null, null, null, null, null],
            ["BIL.", "NO.TAG", "JENIS", null, null, null, "UMUR", "BERAT", "CATATAN"],
            [null, null, "P", "I", "AJ", "AB", null, null, null],
            ["1", "0125", "", "", "/", "", "1 Tahun", "119 Kg", "RP4"],
            ["3", "178", "/", "", "", "", "4 Tahun", "317 Kg", "RP4"],
            ["72", "NO. TAG", "", "", "", "/", "1 Tahun 6 Bulan", "195 Kg", "EPP5"],
            ["135", "NO. TAG", "", "", "", "/", "6 Bulan", "", "EPP5"],
            [null, null, null, null, null, null, null, null, null],
        ];

        var result = CensusSheetParser.Parse(sheet);

        Assert.Null(result.FatalError);
        Assert.Equal(4, result.Rows.Count);

        var calf = result.Rows[0];
        Assert.Equal((1, "0125", AnimalCategory.MaleCalf, 12, 119m, "RP4"),
            (calf.LineNo, calf.TagNumber, calf.Category, calf.AgeMonths, calf.WeightKg, calf.Remarks));
        Assert.Empty(calf.Errors);

        Assert.Equal(AnimalCategory.Bull, result.Rows[1].Category);

        var untagged = result.Rows[2];
        Assert.Null(untagged.TagNumber);
        Assert.Contains(untagged.Warnings, w => w.Contains("No ear tag"));

        Assert.Null(result.Rows[3].WeightKg);
        Assert.Contains(result.Rows[3].Warnings, w => w.Contains("No weight"));
    }

    [Fact]
    public void Parses_the_english_template_layout()
    {
        string?[][] sheet =
        [
            ["Line", "Tag No", "P", "I", "AJ", "AB", "Age", "Weight (kg)", "BCS", "Project", "Remarks", "Selected"],
            ["1", "A13 2340", "", "/", "", "", "6 years", "443", "3.5", "EPP5", "Good frame", "Y"],
        ];

        var row = Assert.Single(CensusSheetParser.Parse(sheet).Rows);

        Assert.Equal("A13 2340", row.TagNumber);
        Assert.Equal(AnimalCategory.Dam, row.Category);
        Assert.Equal(72, row.AgeMonths);
        Assert.Equal(3.5m, row.BodyConditionScore);
        Assert.Equal("EPP5", row.ProjectText);
        Assert.Equal("Good frame", row.Remarks);
        Assert.True(row.Selected);
    }

    [Fact]
    public void Accepts_a_single_category_column()
    {
        string?[][] sheet =
        [
            ["Tag", "Category", "Weight"],
            ["101", "I", "239"],
        ];

        Assert.Equal(AnimalCategory.Dam, Assert.Single(CensusSheetParser.Parse(sheet).Rows).Category);
    }

    [Fact]
    public void Reports_row_errors_without_stopping()
    {
        string?[][] sheet =
        [
            ["Tag", "P", "I", "AJ", "AB", "Weight"],
            ["1", "/", "/", "", "", "300"],
            ["2", "", "", "", "", "300"],
            ["3", "/", "", "", "", "heavy"],
            ["4", "/", "", "", "", "3000"],
            ["5", "/", "", "", "", "300"],
        ];

        var rows = CensusSheetParser.Parse(sheet).Rows;

        Assert.Contains("More than one", rows[0].Errors.Single());
        Assert.Contains("Category is missing", rows[1].Errors.Single());
        Assert.Contains("not a number", rows[2].Errors.Single());
        Assert.Contains("outside", rows[3].Errors.Single());
        Assert.Empty(rows[4].Errors);
    }

    [Fact]
    public void Warns_when_age_does_not_fit_the_category()
    {
        // Row 17 of the Pagoh census: a "male calf" recorded at 3 years old.
        string?[][] sheet =
        [
            ["Tag", "Jenis", "Umur", "Berat"],
            ["175", "AJ", "3 Tahun", "192"],
        ];

        var row = Assert.Single(CensusSheetParser.Parse(sheet).Rows);

        Assert.Contains(row.Warnings, w => w.Contains("calf"));
    }

    [Fact]
    public void Fails_clearly_without_a_tag_column()
    {
        string?[][] sheet = [["Name", "Weight"], ["Daisy", "300"]];

        Assert.Contains("Tag", CensusSheetParser.Parse(sheet).FatalError);
    }

    [Fact]
    public void Csv_reader_handles_quotes_and_semicolons()
    {
        var rows = CsvReader.Read("Tag;Remarks\r\n\"A13 2340\";\"thin; lame\"\r\n0125;\"said \"\"ok\"\"\"\r\n");

        Assert.Equal(new string?[] { "A13 2340", "thin; lame" }, rows[1]);
        Assert.Equal(new string?[] { "0125", "said \"ok\"" }, rows[2]);
    }
}
