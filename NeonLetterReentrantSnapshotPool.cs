#nullable enable

namespace SOTFNeonLetters;

internal sealed class NeonLetterReentrantSnapshotPool<T>
{
    private readonly List<List<T>> _buffers = new();
    private int _activeDepth;

    public int AllocatedBufferCount => _buffers.Count;

    public List<T> Rent()
    {
        if (_activeDepth == _buffers.Count)
        {
            _buffers.Add(new List<T>());
        }

        return _buffers[_activeDepth++];
    }

    public void Return(List<T> snapshot)
    {
        snapshot.Clear();
        _activeDepth--;
    }

    public bool IsReservedByOuterBuffer(T candidate)
    {
        int outerBufferCount = _activeDepth - 1;
        for (int depth = 0; depth < outerBufferCount; depth++)
        {
            List<T> buffer = _buffers[depth];
            for (int index = 0; index < buffer.Count; index++)
            {
                if (EqualityComparer<T>.Default.Equals(
                        buffer[index],
                        candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
