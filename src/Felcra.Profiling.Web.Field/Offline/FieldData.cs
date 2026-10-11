using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Contracts.Records;
using Felcra.Profiling.Contracts.Sync;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Web.Field.Offline;

/// <summary>The field app's data on the phone: farm animals, reference lists, cached history and the outbox.</summary>
public sealed class FieldData(OfflineDb db)
{
    private const string ProductsKey = "products";
    private const string SessionsKey = "sessions";
    private const string RemindersKey = "reminders";
    private const string FeedsKey = "feeds";
    private const string CursorKey = "cursor";
    private const string LastSyncKey = "lastSync";
    private const string UserKey = "userId";

    private List<SyncAnimalDto>? _animals;

    // ---- Animals ------------------------------------------------------------------------------

    public async Task<List<SyncAnimalDto>> AnimalsAsync() => _animals ??= await db.GetAllAsync<SyncAnimalDto>(OfflineDb.Animals);

    public async Task<SyncAnimalDto?> AnimalAsync(Guid id) => (await AnimalsAsync()).FirstOrDefault(a => a.Id == id);

    public async Task<bool> HasDuplicateTagAsync(SyncAnimalDto animal) =>
        animal.TagNumber is { } tag && (await AnimalsAsync()).Any(o =>
            o.Id != animal.Id && o.FarmId == animal.FarmId && o.Status == AnimalStatus.Active && TagKey(o.TagNumber) == TagKey(tag));

    /// <summary>Animals whose tag or animal number contains the text; active animals first.</summary>
    public async Task<List<SyncAnimalDto>> SearchAsync(string text, int max = 30)
    {
        var key = TagKey(text);
        return (await AnimalsAsync())
            .Where(a => (a.TagNumber is not null && TagKey(a.TagNumber).Contains(key)) || a.AnimalNo.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Status != AnimalStatus.Active)
            .ThenBy(a => TagKey(a.TagNumber) != key)
            .ThenBy(a => a.TagNumber)
            .Take(max)
            .ToList();
    }

    private static string TagKey(string? tag) => (tag ?? "").Replace(" ", "").ToUpperInvariant();

    public async Task ApplyPullAsync(SyncPullDto pull)
    {
        if (pull.IsFull) await db.ClearAsync(OfflineDb.Animals);
        await db.BulkAsync(OfflineDb.Animals, pull.Animals.Select(a => (a.Id.ToString(), a)), pull.RemovedAnimalIds.Select(id => id.ToString()));
        await db.PutAsync(OfflineDb.Meta, ProductsKey, pull.TreatmentProducts);
        await db.PutAsync(OfflineDb.Meta, SessionsKey, pull.OpenCensusSessions);
        await db.PutAsync(OfflineDb.Meta, RemindersKey, pull.Reminders);
        await db.PutAsync(OfflineDb.Meta, FeedsKey, pull.FeedIngredients);
        await db.PutAsync(OfflineDb.Meta, CursorKey, pull.ServerTime);
        _animals = null;
    }

    // ---- Reference lists and sync state -------------------------------------------------------

    public async Task<List<TreatmentProductDto>> TreatmentProductsAsync() =>
        await db.GetAsync<List<TreatmentProductDto>>(OfflineDb.Meta, ProductsKey) ?? [];

    public async Task<List<CensusSessionDto>> OpenSessionsAsync(Guid farmId) =>
        (await db.GetAsync<List<CensusSessionDto>>(OfflineDb.Meta, SessionsKey) ?? []).Where(s => s.FarmId == farmId).ToList();

    /// <summary>Tasks as of the last sync (due within two weeks, plus overdue).</summary>
    public async Task<List<ReminderDto>> RemindersAsync() =>
        await db.GetAsync<List<ReminderDto>>(OfflineDb.Meta, RemindersKey) ?? [];

    /// <summary>Feed ingredients for the ration planner, as of the last sync.</summary>
    public async Task<List<FeedIngredientDto>> FeedIngredientsAsync() =>
        await db.GetAsync<List<FeedIngredientDto>>(OfflineDb.Meta, FeedsKey) ?? [];

    /// <summary>Bulls on a farm, for choosing the sire of a natural service.</summary>
    public async Task<List<SyncAnimalDto>> BullsAsync(Guid farmId) =>
        (await AnimalsAsync()).Where(a => a.FarmId == farmId && a.Sex == Sex.Male && a.Category == AnimalCategory.Bull && a.Status == AnimalStatus.Active)
            .OrderBy(a => a.TagNumber).ToList();

    public Task<DateTimeOffset?> CursorAsync() => db.GetAsync<DateTimeOffset?>(OfflineDb.Meta, CursorKey);

    public Task<DateTimeOffset?> LastSyncAsync() => db.GetAsync<DateTimeOffset?>(OfflineDb.Meta, LastSyncKey);

    public Task SetLastSyncAsync(DateTimeOffset time) => db.PutAsync(OfflineDb.Meta, LastSyncKey, time);

    public Task<string?> UserIdAsync() => db.GetAsync<string?>(OfflineDb.Meta, UserKey);

    /// <summary>A different user signed in on this phone: forget the previous user's farm data (not their outbox).</summary>
    public async Task SwitchUserAsync(string userId)
    {
        foreach (var store in new[] { OfflineDb.Animals, OfflineDb.Records, OfflineDb.Meta })
            await db.ClearAsync(store);
        await db.PutAsync(OfflineDb.Meta, UserKey, userId);
        _animals = null;
    }

    public async Task ClearAllAsync()
    {
        await db.ClearAllAsync();
        _animals = null;
    }

    // ---- Cached history (last version seen online) ---------------------------------------------

    public Task CacheDetailAsync(AnimalDto animal, AnimalRecordsDto records, AnimalBreedingDto breeding) => Task.WhenAll(
        db.PutAsync(OfflineDb.Records, $"animal:{animal.Id}", animal),
        db.PutAsync(OfflineDb.Records, $"records:{animal.Id}", records),
        db.PutAsync(OfflineDb.Records, $"breeding:{animal.Id}", breeding));

    public async Task<(AnimalDto? Animal, AnimalRecordsDto? Records, AnimalBreedingDto? Breeding)> CachedDetailAsync(Guid id) =>
        (await db.GetAsync<AnimalDto>(OfflineDb.Records, $"animal:{id}"),
         await db.GetAsync<AnimalRecordsDto>(OfflineDb.Records, $"records:{id}"),
         await db.GetAsync<AnimalBreedingDto>(OfflineDb.Records, $"breeding:{id}"));

    // ---- Outbox --------------------------------------------------------------------------------

    public async Task<List<OutboxItem>> OutboxAsync() =>
        (await db.GetAllAsync<OutboxItem>(OfflineDb.Outbox)).OrderBy(i => i.Key, StringComparer.Ordinal).ToList();

    public Task<int> OutboxCountAsync() => db.CountAsync(OfflineDb.Outbox);

    public Task EnqueueAsync(OutboxItem item) => db.PutAsync(OfflineDb.Outbox, item.Key, item);

    public Task UpdateAsync(OutboxItem item) => db.PutAsync(OfflineDb.Outbox, item.Key, item);

    public async Task RemoveAsync(OutboxItem item)
    {
        await db.DeleteAsync(OfflineDb.Outbox, item.Key);
        if (item.Kind == OutboxKind.Photo) await db.DeleteAsync(OfflineDb.Photos, item.Id.ToString());
    }

    public Task SavePhotoAsync(PendingPhoto photo) => db.PutAsync(OfflineDb.Photos, photo.Id.ToString(), photo);

    public Task<PendingPhoto?> PhotoAsync(Guid id) => db.GetAsync<PendingPhoto>(OfflineDb.Photos, id.ToString());
}
