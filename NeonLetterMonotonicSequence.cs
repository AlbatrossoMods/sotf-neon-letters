#nullable enable

namespace SOTFNeonLetters;

internal sealed class NeonLetterMonotonicSequence
{
    private ulong _current;

    internal NeonLetterMonotonicSequence(ulong initialValue = 0)
    {
        _current = initialValue;
    }

    internal ulong Current => _current;

    internal ulong Advance()
    {
        if (_current == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The monotonic sequence is exhausted.");
        }

        _current++;
        return _current;
    }
}
