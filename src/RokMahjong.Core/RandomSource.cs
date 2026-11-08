namespace RokMahjong.Core;

public interface ISeededRandom
{
    int Next(int exclusiveUpperBound);
    void Shuffle<T>(IList<T> items);
}

/// <summary>A tiny platform-stable xorshift generator so a seed always produces the same deal.</summary>
public sealed class SeededRandom : ISeededRandom
{
    private uint _state;

    public SeededRandom(int seed)
    {
        _state = unchecked((uint)seed);
        if (_state == 0) _state = 0x9E3779B9u;
    }

    public int Next(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        return (int)(NextUInt() % (uint)exclusiveUpperBound);
    }

    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private uint NextUInt()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }
}
