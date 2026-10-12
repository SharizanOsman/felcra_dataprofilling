using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Felcra.Profiling.UI.Shared;

/// <summary>
/// Malay for MudBlazor's own texts (button labels for screen readers, "Required", conversion errors…).
/// In English, or for a key not listed here, MudBlazor uses its built-in English.
/// </summary>
internal sealed class MalayMudLocalizer : MudLocalizer
{
    private static readonly Dictionary<string, string> Translations = new()
    {
        ["MudFormComponent_Required"] = "Wajib diisi",
        ["MudInput_Clear"] = "Kosongkan",
        ["MudInput_Increment"] = "Tambah",
        ["MudInput_Decrement"] = "Kurang",
        ["MudDialog_Close"] = "Tutup",
        ["MudAlert_Close"] = "Tutup",
        ["MudChip_Close"] = "Tutup",
        ["MudSnackbar_Close"] = "Tutup",
        ["MudSelect_SelectAll"] = "Pilih semua",
        ["MudTable_Loading"] = "Memuatkan…",
        ["MudTable_SelectAllRows"] = "Pilih semua baris",
        ["MudTable_SelectRow"] = "Pilih baris",
        ["MudTablePager_FirstPage"] = "Halaman pertama",
        ["MudTablePager_PreviousPage"] = "Halaman sebelumnya",
        ["MudTablePager_NextPage"] = "Halaman seterusnya",
        ["MudTablePager_LastPage"] = "Halaman terakhir",
        ["MudBaseDatePicker_Open"] = "Buka pemilih tarikh",
        ["MudBaseDatePicker_NextMonth"] = "Bulan seterusnya",
        ["MudBaseDatePicker_PrevMonth"] = "Bulan sebelumnya",
        ["MudBaseDatePicker_NextYear"] = "Tahun seterusnya",
        ["MudBaseDatePicker_PrevYear"] = "Tahun sebelumnya",
        ["MudBreadcrumbs_Label"] = "Laluan halaman",
        ["MudNavGroup_ToggleExpand"] = "Kembang atau kuncup {0}",
        ["MudTabs_ScrollLeft"] = "Tatal ke kiri",
        ["MudTabs_ScrollRight"] = "Tatal ke kanan",
        ["MudFileUpload_Button"] = "Pilih fail",
        ["MudFileUpload_DragAndDrop"] = "Seret dan lepas fail di sini",
        ["Converter_InvalidNumber"] = "Bukan nombor yang sah",
        ["Converter_InvalidDateTime"] = "Bukan tarikh yang sah",
        ["Converter_InvalidBoolean"] = "Bukan nilai ya/tidak yang sah",
        ["Converter_ConversionFailed"] = "Nilai tidak dapat ditukar",
    };

    public override LocalizedString this[string key] =>
        Language == Malay && Translations.TryGetValue(key, out var text)
            ? new LocalizedString(key, text)
            : new LocalizedString(key, key, resourceNotFound: true);

    public override LocalizedString this[string key, params object[] arguments] =>
        this[key] is { ResourceNotFound: false } found
            ? new LocalizedString(key, string.Format(found.Value, arguments))
            : new LocalizedString(key, key, resourceNotFound: true);
}
