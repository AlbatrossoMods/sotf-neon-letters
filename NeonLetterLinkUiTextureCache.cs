#nullable enable

namespace SOTFNeonLetters;

internal sealed class NeonLetterLinkUiTextureCache<TTexture>
    where TTexture : class
{
    private TTexture? _texture;

    internal TTexture GetOrCreate(Func<TTexture> createTexture)
    {
        ArgumentNullException.ThrowIfNull(createTexture);
        if (_texture != null)
        {
            return _texture;
        }

        TTexture texture = createTexture();
        if (texture == null)
        {
            throw new InvalidOperationException(
                "The LinkUi texture factory returned null.");
        }

        _texture = texture;
        return texture;
    }
}
