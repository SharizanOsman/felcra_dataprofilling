using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.UI.Shared.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;

namespace Felcra.Profiling.Web.Admin.Shared;

/// <summary>Common plumbing for admin pages: current role, data loading with error capture, and save notifications.</summary>
public abstract class AdminPageBase : ComponentBase
{
    [CascadingParameter] private Task<AuthenticationState> AuthState { get; set; } = null!;

    [Inject] protected ApiClient Api { get; set; } = null!;
    [Inject] protected IDialogService Dialogs { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    protected bool IsHq { get; private set; }
    protected bool IsLoading { get; private set; } = true;
    protected string? LoadError { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState;
        IsHq = state.User.IsInRole(Roles.HqAdmin);
        await ReloadAsync();
    }

    /// <summary>Loads the page's data; failures are shown via <see cref="LoadError"/>.</summary>
    protected abstract Task LoadAsync();

    protected async Task ReloadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            await LoadAsync();
        }
        catch (HttpRequestException ex)
        {
            LoadError = ex.StatusCode is System.Net.HttpStatusCode.NotFound
                ? T("This record was not found or you do not have access to it.")
                : T("Could not load data from the server. Please try again.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Shows a snackbar for a delete (or other non-dialog) call and reloads on success.</summary>
    protected async Task NotifyAsync<TValue>(ApiResult<TValue> result, string successMessage)
    {
        if (result.Succeeded)
        {
            Snackbar.Add(successMessage, Severity.Success);
            await ReloadAsync();
        }
        else
        {
            Snackbar.Add(result.FullError ?? T("Something went wrong."), Severity.Error);
        }
    }

    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// <summary>The URL being downloaded, so its button can show progress.</summary>
    protected string? Downloading { get; private set; }

    /// <summary>Fetches a report or other file and saves it in the browser.</summary>
    protected async Task DownloadAsync(string url)
    {
        if (Downloading is not null) return;
        Downloading = url;
        try
        {
            var result = await Api.DownloadAsync(url);
            if (result.Value is { } file)
                await JS.InvokeVoidAsync("felcra.downloadFile", file.FileName, file.ContentType, Convert.ToBase64String(file.Content));
            else
                Snackbar.Add(result.FullError ?? T("The file could not be downloaded."), Severity.Error);

        }
        finally
        {
            Downloading = null;
        }
    }

    protected async Task AfterSaveAsync(bool saved, string successMessage)
    {
        if (!saved) return;
        Snackbar.Add(successMessage, Severity.Success);
        await ReloadAsync();
    }
}
