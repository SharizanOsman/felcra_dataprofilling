#:package ClosedXML
// Builds the UAT tracking workbook from the test-case tables in docs/UAT.md:
//   dotnet run tools/uat-workbook.cs     (from the repository root)
using System.Text.RegularExpressions;
using ClosedXML.Excel;

var source = args.Length > 0 ? args[0] : "docs/UAT.md";
var output = args.Length > 1 ? args[1] : "docs/uat/FELCRA-UAT-test-cases.xlsx";

// Rows "| UAT-01 | M | Role | FR-… | Steps | Expected |" under "### 2.x Area" headings.
var cases = new List<string[]>();
var area = "";
foreach (var line in File.ReadLines(source))
{
    if (Regex.Match(line, @"^### 2\.\d+ (.+)$") is { Success: true } heading) area = heading.Groups[1].Value.Trim();
    if (!line.StartsWith("| UAT-")) continue;
    var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim().Replace("**", "")).ToArray();
    cases.Add([cells[0], area, cells[1], cells[2], cells[3], cells[4], cells[5]]);
}
if (cases.Count == 0) throw new InvalidOperationException($"No test cases found in {source}.");

using var book = new XLWorkbook();
var header = XLColor.FromHtml("#1B5E20");

// ---- Test cases ----
var sheet = book.Worksheets.Add("Test cases");
string[] columns = ["ID", "Area", "Priority", "Role", "Requirement", "Steps", "Expected result", "Tester", "Date", "Result", "Defect ID", "Comments"];
for (var c = 0; c < columns.Length; c++) sheet.Cell(1, c + 1).Value = columns[c];
var head = sheet.Range(1, 1, 1, columns.Length);
head.Style.Font.Bold = true;
head.Style.Font.FontColor = XLColor.White;
head.Style.Fill.BackgroundColor = header;
for (var r = 0; r < cases.Count; r++)
    for (var c = 0; c < 7; c++) sheet.Cell(r + 2, c + 1).Value = cases[r][c];
var last = cases.Count + 1;
sheet.Range(2, 10, last, 10).CreateDataValidation().List("\"Pass,Fail,Blocked,Not run\"", true);
sheet.Range(2, 9, last, 9).Style.DateFormat.Format = "d mmm yyyy";
sheet.Columns(1, 5).AdjustToContents();
sheet.Column(6).Width = 60;
sheet.Column(7).Width = 55;
foreach (var c in new[] { 8, 9, 10, 11 }) sheet.Column(c).Width = 13;
sheet.Column(12).Width = 40;
var body = sheet.Range(2, 1, last, columns.Length);
body.Style.Alignment.WrapText = true;
body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
sheet.Range(1, 1, last, columns.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
sheet.Range(1, 1, last, columns.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
var result = sheet.Range(2, 10, last, 10);
result.AddConditionalFormat().WhenEquals("\"Pass\"").Fill.SetBackgroundColor(XLColor.FromHtml("#C8E6C9"));
result.AddConditionalFormat().WhenEquals("\"Fail\"").Fill.SetBackgroundColor(XLColor.FromHtml("#FFCDD2"));
result.AddConditionalFormat().WhenEquals("\"Blocked\"").Fill.SetBackgroundColor(XLColor.FromHtml("#FFE0B2"));
sheet.SheetView.FreezeRows(1);
sheet.RangeUsed()!.SetAutoFilter();

// ---- Defects ----
var defects = book.Worksheets.Add("Defects");
string[] defectColumns = ["Defect ID", "Test ID", "Severity", "Summary", "Steps / expected / actual", "Reported by", "Date", "Status", "Fixed in version", "Re-tested by", "Notes"];
for (var c = 0; c < defectColumns.Length; c++) defects.Cell(1, c + 1).Value = defectColumns[c];
var dHead = defects.Range(1, 1, 1, defectColumns.Length);
dHead.Style.Font.Bold = true;
dHead.Style.Font.FontColor = XLColor.White;
dHead.Style.Fill.BackgroundColor = header;
defects.Range(2, 3, 300, 3).CreateDataValidation().List("\"Critical,High,Medium,Low\"", true);
defects.Range(2, 8, 300, 8).CreateDataValidation().List("\"Open,Fixed,Re-test passed,Accepted (punch list),Rejected\"", true);
defects.Range(2, 7, 300, 7).Style.DateFormat.Format = "d mmm yyyy";
for (var c = 1; c <= defectColumns.Length; c++) defects.Column(c).Width = c is 4 or 5 or 11 ? 45 : 15;
defects.SheetView.FreezeRows(1);

// ---- Summary (formulas follow the testers' entries) ----
var summary = book.Worksheets.Add("Summary");
summary.Cell(1, 1).Value = "FELCRA Cattle Profiling — UAT summary";
summary.Cell(1, 1).Style.Font.Bold = true;
summary.Cell(1, 1).Style.Font.FontSize = 14;
summary.Cell(3, 1).Value = "Result";
summary.Cell(3, 2).Value = "All";
summary.Cell(3, 3).Value = "Must";
var row = 4;
var range = $"'Test cases'!$J$2:$J${last}";
var priority = $"'Test cases'!$C$2:$C${last}";
foreach (var r in new[] { "Pass", "Fail", "Blocked", "Not run" })
{
    summary.Cell(row, 1).Value = r;
    summary.Cell(row, 2).FormulaA1 = r == "Not run"
        ? $"COUNTIF({range},\"Not run\")+COUNTBLANK({range})"
        : $"COUNTIF({range},\"{r}\")";
    summary.Cell(row, 3).FormulaA1 = r == "Not run"
        ? $"COUNTIFS({priority},\"M\",{range},\"Not run\")+COUNTIFS({priority},\"M\",{range},\"\")"
        : $"COUNTIFS({priority},\"M\",{range},\"{r}\")";
    row++;
}
summary.Cell(row, 1).Value = "Total";
summary.Cell(row, 2).FormulaA1 = $"COUNTA('Test cases'!$A$2:$A${last})";
summary.Cell(row, 3).FormulaA1 = $"COUNTIF({priority},\"M\")";
summary.Row(row).Style.Font.Bold = true;
row += 2;
summary.Cell(row, 1).Value = "Open Critical / High defects";
summary.Cell(row, 2).FormulaA1 = "COUNTIFS(Defects!$C:$C,\"Critical\",Defects!$H:$H,\"Open\")+COUNTIFS(Defects!$C:$C,\"High\",Defects!$H:$H,\"Open\")";
summary.Cell(row + 1, 1).Value = "Exit criteria: every Must case run, no open Critical or High defects (docs/UAT.md §1).";
summary.Cell(row + 1, 1).Style.Font.Italic = true;
summary.Range(3, 1, 3, 3).Style.Font.Bold = true;
summary.Column(1).Width = 32;
summary.Columns(2, 3).Width = 10;
summary.SetTabActive();

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
book.SaveAs(output);
Console.WriteLine($"{cases.Count} test cases → {output}");
