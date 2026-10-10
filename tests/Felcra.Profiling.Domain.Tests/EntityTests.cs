using Felcra.Profiling.Domain.Farms;

namespace Felcra.Profiling.Domain.Tests;

public class EntityTests
{
    [Fact]
    public void New_entities_get_unique_v7_ids()
    {
        var first = new Farm { Code = "A", Name = "A" };
        var second = new Farm { Code = "B", Name = "B" };

        Assert.Equal(7, first.Id.Version);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void New_farm_is_active_and_not_deleted()
    {
        var farm = new Farm { Code = "PAGOH", Name = "FELCRA Berhad Kawasan Pagoh" };

        Assert.True(farm.IsActive);
        Assert.False(farm.IsDeleted);
    }
}
