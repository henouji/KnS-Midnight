namespace KnS.Game.Randomness;

/// <summary>
/// The picks the game rules make. Each one draws exactly as the Python server's seeded source
/// does, in the same order, which is what keeps seeded runs of the two servers identical.
/// </summary>
public static class RandomSourceExtensions
{
    /// <summary>A uniform integer in [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public static long NextInclusive(this IRandomSource random, long min, long max) =>
        min + random.NextBelow(max - min + 1);

    public static T Pick<T>(this IRandomSource random, IReadOnlyList<T> items) =>
        items[(int)random.NextBelow(items.Count)];

    /// <summary><paramref name="count"/> uniform picks, with repeats.</summary>
    public static List<T> PickMany<T>(this IRandomSource random, IReadOnlyList<T> items, long count)
    {
        var picks = new List<T>();
        for (long i = 0; i < count; i++)
        {
            picks.Add(random.Pick(items));
        }

        return picks;
    }

    /// <summary>One pick, each item as likely as its share of the total weight.</summary>
    public static T PickWeighted<T>(this IRandomSource random, IReadOnlyList<T> items, IReadOnlyList<long> weights)
    {
        long roll = random.NextBelow(weights.Sum());
        long cumulative = 0;
        for (int i = 0; i < items.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
            {
                return items[i];
            }
        }

        throw new InvalidOperationException("Weights do not cover the roll.");
    }

    /// <summary>Fisher-Yates, from the last element down.</summary>
    public static void Shuffle<T>(this IRandomSource random, IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = (int)random.NextBelow(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
