using Felcra.Profiling.UI.Shared.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Felcra.Profiling.UI.Shared.Components;

public static class DialogExtensions
{
    private static readonly DialogOptions EditOptions = new() { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true };

    /// <summary>Opens <see cref="EditDialog{TModel}"/>; returns true when the user saved successfully.</summary>
    public static async Task<bool> EditAsync<TModel, TResult>(
        this IDialogService dialogs,
        string title,
        TModel model,
        RenderFragment<TModel> fields,
        Func<TModel, Task<ApiResult<TResult>>> save,
        Func<object, string, Task<IEnumerable<string>>>? validation = null)
        where TModel : class
    {
        var parameters = new DialogParameters<EditDialog<TModel>>
        {
            { d => d.Model, model },
            { d => d.Fields, fields },
            { d => d.Validation, validation },
            {
                d => d.Save, async m =>
                {
                    var result = await save(m);
                    return new ApiResult<object>(result.Value, result.Error, result.FieldErrors);
                }
            },
        };
        var dialog = await dialogs.ShowAsync<EditDialog<TModel>>(title, parameters, EditOptions);
        var result = await dialog.Result;
        return result is { Canceled: false };
    }

    public static async Task<bool> ConfirmDeleteAsync(this IDialogService dialogs, string what) =>
        await dialogs.ShowMessageBoxAsync(
            T("Delete"),
            T("Delete {0}? This cannot be undone from this screen.", what),
            yesText: T("Delete"),
            cancelText: T("Cancel")) == true;
}
