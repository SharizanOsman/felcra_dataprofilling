using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Domain.Tests;

public class AnimalTests
{
    [Theory]
    [InlineData("NO. TAG")]
    [InlineData("no tag")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Placeholder_tags_mean_untagged(string? tag) => Assert.Null(Animal.NormaliseTag(tag));

    [Fact]
    public void Tags_keep_leading_zeros_and_collapse_spaces() =>
        Assert.Equal("A13 2340", Animal.NormaliseTag("  A13   2340 "));

    [Fact]
    public void Retag_closes_the_previous_tag_and_records_the_new_one()
    {
        var animal = new Animal { Category = AnimalCategory.Dam };
        animal.Retag("08478", new DateOnly(2026, 7, 2), "Census import");

        animal.Retag("08999", new DateOnly(2026, 9, 1), "Lost tag");

        Assert.Equal("08999", animal.TagNumber);
        Assert.Equal(2, animal.TagHistory.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), animal.TagHistory.Single(t => t.TagNumber == "08478").RemovedOn);
        Assert.Null(animal.TagHistory.Single(t => t.TagNumber == "08999").RemovedOn);
    }

    [Fact]
    public void Losing_a_tag_leaves_the_animal_untagged()
    {
        var animal = new Animal();
        animal.Retag("123", new DateOnly(2026, 1, 1), null);

        animal.Retag("NO. TAG", new DateOnly(2026, 2, 1), "Tag lost");

        Assert.Null(animal.TagNumber);
        Assert.All(animal.TagHistory, t => Assert.NotNull(t.RemovedOn));
    }

    [Theory]
    [InlineData(AnimalCategory.Bull, Sex.Male)]
    [InlineData(AnimalCategory.MaleCalf, Sex.Male)]
    [InlineData(AnimalCategory.Dam, Sex.Female)]
    [InlineData(AnimalCategory.FemaleCalf, Sex.Female)]
    public void Sex_follows_category(AnimalCategory category, Sex sex) => Assert.Equal(sex, Animal.SexOf(category));

    [Fact]
    public void Age_is_counted_in_whole_months()
    {
        var animal = new Animal { BirthDate = new DateOnly(2025, 1, 15) };

        Assert.Equal(17, animal.AgeInMonths(new DateOnly(2026, 7, 2)));
        Assert.Equal(18, animal.AgeInMonths(new DateOnly(2026, 7, 15)));
    }

    [Fact]
    public void Animal_numbers_are_zero_padded() => Assert.Equal("A000042", Animal.FormatAnimalNo(42));
}
