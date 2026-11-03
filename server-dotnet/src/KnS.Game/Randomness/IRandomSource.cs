namespace KnS.Game.Randomness;

/// <summary>The randomness behind drops, draws and options; see <see cref="RandomSourceExtensions"/>.</summary>
public interface IRandomSource
{
    /// <summary>A uniform integer in [0, <paramref name="exclusiveMax"/>).</summary>
    long NextBelow(long exclusiveMax);
}

/// <summary>The production source: <see cref="Random.Shared"/>.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    public long NextBelow(long exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return Random.Shared.NextInt64(exclusiveMax);
    }
}

/// <summary>
/// splitmix64 with plain modulo picks: the same sequence as the Python server's
/// <c>_SeededRandom</c> for the same seed, so the two servers can be compared draw for draw.
/// For testing only (modulo picks are slightly biased).
/// </summary>
public sealed class SeededRandomSource(ulong seed) : IRandomSource
{
    private readonly object _lock = new();
    private ulong _state = seed;

    public long NextBelow(long exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (long)(NextUInt64() % (ulong)exclusiveMax);
    }

    public ulong NextUInt64()
    {
        lock (_lock)
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
