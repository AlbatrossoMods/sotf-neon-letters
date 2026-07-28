using SOTFNeonLetters;
using Xunit;

[Collection(AllocationSensitiveTestCollection.Name)]
public sealed class LinkUiTransparentTextureTests
{
    [Fact]
    public void MissingTextureFactoryIsRejected()
    {
        var cache = new NeonLetterLinkUiTextureCache<object>();

        Assert.Throws<ArgumentNullException>(
            () => cache.GetOrCreate(null!));
    }

    [Fact]
    public void TextureFactoryCannotPopulateTheCacheWithNull()
    {
        var cache = new NeonLetterLinkUiTextureCache<object>();

        Assert.Throws<InvalidOperationException>(
            () => cache.GetOrCreate(() => null!));
    }

    [Fact]
    public void RepeatedRequestsReuseTheFirstFactoryResult()
    {
        var cache = new NeonLetterLinkUiTextureCache<object>();
        int factoryCalls = 0;
        var createdTexture = new object();

        object first = cache.GetOrCreate(
            () =>
            {
                factoryCalls++;
                return createdTexture;
            });
        object second = cache.GetOrCreate(
            () =>
            {
                factoryCalls++;
                return new object();
            });

        Assert.Equal(
            (createdTexture, createdTexture, 1),
            (first, second, factoryCalls));
    }

    [Fact]
    public void WarmCacheHitsDoNotAllocateManagedFactoryObjects()
    {
        const int Iterations = 100_000;
        var cache = new NeonLetterLinkUiTextureCache<object>();
        var createdTexture = new object();
        Func<object> cachedFactory = () => createdTexture;
        cache.GetOrCreate(cachedFactory);
        long maximumAllocatedBytes = 0;

        for (int sample = 0; sample < 5; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                cache.GetOrCreate(cachedFactory);
            }

            maximumAllocatedBytes = Math.Max(
                maximumAllocatedBytes,
                GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.InRange(maximumAllocatedBytes, 0, 256);
    }
}
