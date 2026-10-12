using System.Text.Json;
using Felcra.Profiling.Contracts.Sync;
using Felcra.Profiling.Web.Field.Offline;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Felcra.Profiling.Web.Field.Shared;

/// <summary>
/// Common behaviour for recording screens. The animal comes from the phone's copy of the farm data and
/// records are saved on the phone first, so recording works without a connection.
/// </summary>
public abstract class RecordPageBase : ComponentBase
{
    [Parameter] public Guid Id { get; set; }

    [Inject] protected FieldData Data { get; set; } = null!;
    [Inject] protected SyncService Sync { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    protected SyncAnimalDto? Animal { get; private set; }
    protected bool DuplicateTag { get; private set; }
    protected string? LoadError { get; private set; }
    protected string? SaveError { get; set; }
    protected bool Saving { get; private set; }

    protected override async Task OnParametersSetAsync()
    {
        Animal = await Data.AnimalAsync(Id);
        if (Animal is null && Sync.IsOnline)
        {
            // Not on the phone yet (e.g. registered since the last sync): fetch the latest farm data.
            await Sync.EnsureSyncedAsync();
            Animal = await Data.AnimalAsync(Id);
        }

        if (Animal is null)
        {
            LoadError = Sync.IsOnline
                ? T("This animal is not on any of your farms.")
                : T("This animal is not on this phone yet. Connect to the internet once to download your farm's animals.");
            return;
        }

        DuplicateTag = await Data.HasDuplicateTagAsync(Animal);
        await OnAnimalLoadedAsync();
    }

    protected virtual Task OnAnimalLoadedAsync() => Task.CompletedTask;

    /// <summary>Queues the record on the phone (it is sent when online) and returns to the animal page.</summary>
    protected async Task SaveAsync(Guid recordId, OutboxKind kind, object request, string summary, DateOnly date)
    {
        Saving = true;
        await Sync.EnqueueAsync(MakeItem(recordId, kind, JsonSerializer.Serialize(request, JsonSerializerOptions.Web), summary, date));
        Saving = false;

        Snackbar.Add(Sync.IsOnline ? T("{0} saved.", summary) : T("{0} saved on this phone — it will be sent when you are back online.", summary),
            Severity.Success);
        Navigation.NavigateTo($"a/{Id}", replace: true);
    }

    protected OutboxItem MakeItem(Guid recordId, OutboxKind kind, string payload, string summary, DateOnly date)
    {
        var now = DateTimeOffset.UtcNow;
        return new OutboxItem
        {
            Key = OutboxItem.MakeKey(now, recordId),
            Id = recordId,
            Kind = kind,
            AnimalId = Id,
            Payload = payload,
            Summary = summary,
            Date = date,
            QueuedAt = now,
        };
    }
}
