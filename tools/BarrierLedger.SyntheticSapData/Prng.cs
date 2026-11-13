namespace BarrierLedger.SyntheticSapData;

/// <summary>
/// SplitMix64. Implemented here instead of using System.Random so that the
/// sequence is fixed by this source file and cannot change with the runtime.
/// </summary>
internal sealed class Prng
{
    private ulong _state;

    public Prng(ulong seed) => _state = seed;

    public ulong NextU64()
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

    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        return (int)(NextU64() % (ulong)maxExclusive);
    }

    /// <summary>Uniform integer in [minInclusive, maxInclusive].</summary>
    public int Next(int minInclusive, int maxInclusive) =>
        minInclusive + Next(maxInclusive - minInclusive + 1);

    public bool Chance(int percent) => Next(100) < percent;

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
