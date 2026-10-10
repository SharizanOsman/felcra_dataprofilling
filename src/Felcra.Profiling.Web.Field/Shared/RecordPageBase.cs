using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.UI.Shared.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Felcra.Profiling.Web.Field.Shared;

/// <summary>Common behaviour for recording screens: load the animal, save, return to the animal page.</summary>
public abstract class RecordPageBase : ComponentBase
{
    [Parameter] public Guid Id { get; set; }

    [Inject] protected ApiClient Api { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    protected AnimalDto? Animal { get; private set; }
    protected string? LoadError { get; private set; }
    protected string? SaveError { get; set; }
    protected bool Saving { get; private set; }

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            Animal = await Api.GetAsync<AnimalDto>($"api/animals/{Id}");
            await OnAnimalLoadedAsync();
        }
        catch (HttpRequestException ex)
        {
            LoadError = ex.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "This animal is not on any of your farms."
                : "Could not load the animal. Check your connection.";
        }
    }

    protected virtual Task OnAnimalLoadedAsync() => Task.CompletedTask;

    /// <summary>Posts the record; on success shows a confirmation and returns to the animal page.</summary>
    protected async Task SaveAsync<T>(string path, object request, string successMessage)
    {
        Saving = true;
        SaveError = null;
        var result = await Api.PostAsync<T>($"api/animals/{Id}/{path}", request);
        Saving = false;

        if (!result.Succeeded)
        {
            SaveError = result.FullError;
            return;
        }
        Snackbar.Add(successMessage, Severity.Success);
        Navigation.NavigateTo($"a/{Id}", replace: true);
    }
}
