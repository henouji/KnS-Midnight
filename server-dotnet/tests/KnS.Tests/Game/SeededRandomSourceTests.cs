using KnS.Game.Randomness;

namespace KnS.Tests.Game;

/// <summary>The seeded source must draw what the Python server's _SeededRandom draws (tools/parity_check.py relies on it).</summary>
public class SeededRandomSourceTests
{
    [Fact]
    public void Matches_the_python_sequence_for_seed_42()
    {
        // gamegw._SeededRandom(42): three raw draws, randint(0, 99), choices([1, 2, 3], [30, 25, 20]),
        // choices([5, 6, 7], k=4), then shuffle(list(range(6))).
        var random = new SeededRandomSource(42);

        Assert.Equal(13679457532755275413UL, random.NextUInt64());
        Assert.Equal(2949826092126892291UL, random.NextUInt64());
        Assert.Equal(5139283748462763858UL, random.NextUInt64());
        Assert.Equal(64, random.NextInclusive(0, 99));
        Assert.Equal(1, random.PickWeighted([1, 2, 3], [30L, 25L, 20L]));
        Assert.Equal([5, 6, 7, 6], random.PickMany([5, 6, 7], 4));

        var items = new List<int> { 0, 1, 2, 3, 4, 5 };
        random.Shuffle(items);
        Assert.Equal([0, 1, 3, 4, 5, 2], items);
    }
}
