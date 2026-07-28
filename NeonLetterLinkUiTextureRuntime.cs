using Sons.Gui.Input;
using UnityEngine;

namespace SOTFNeonLetters;

internal static class NeonLetterLinkUiTextureRuntime
{
    private static readonly NeonLetterLinkUiTextureCache<Texture2D>
        TransparentTextureCache = new();
    private static readonly Func<Texture2D>
        CreateTransparentTextureCallback =
            CreateTransparentTexture;
    private static int _mainThreadId;

    internal static void InitializeMainThread()
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        if (_mainThreadId != 0 &&
            _mainThreadId != currentThreadId)
        {
            throw new InvalidOperationException(
                "The LinkUi texture runtime was initialized from a " +
                "different thread.");
        }

        _mainThreadId = currentThreadId;
    }

    internal static void ConfigureOwnedLinkUi(LinkUiElement linkUi)
    {
        ArgumentNullException.ThrowIfNull(linkUi);
        EnsureMainThread();
        Texture2D transparentTexture =
            TransparentTextureCache.GetOrCreate(
                CreateTransparentTextureCallback);
        linkUi.SetApplyTexture(true);
        linkUi.SetTexture(transparentTexture);
        linkUi.SetOutlineTexture(transparentTexture);
    }

    private static Texture2D CreateTransparentTexture()
    {
        var texture = new Texture2D(
            1,
            1,
            TextureFormat.RGBA32,
            mipChain: false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, Color.clear);
        texture.Apply(
            updateMipmaps: false,
            makeNoLongerReadable: true);
        return texture;
    }

    private static void EnsureMainThread()
    {
        if (_mainThreadId == 0 ||
            _mainThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "LinkUi textures can only be configured on the initialized " +
                "Unity main thread.");
        }
    }
}
