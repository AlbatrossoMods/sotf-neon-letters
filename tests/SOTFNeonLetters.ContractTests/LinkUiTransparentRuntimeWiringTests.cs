using Xunit;

public sealed class LinkUiTransparentRuntimeWiringTests
{
    [Fact]
    public void OwnedLinkUsesOfficialTextureSettersInOrder()
    {
        string source = File.ReadAllText(
            FindRepositoryFile("NeonLetterLinkUiTextureRuntime.cs"));
        int applyTexture = source.IndexOf(
            "linkUi.SetApplyTexture(true);",
            StringComparison.Ordinal);
        int primaryTexture = source.IndexOf(
            "linkUi.SetTexture(transparentTexture);",
            StringComparison.Ordinal);
        int outlineTexture = source.IndexOf(
            "linkUi.SetOutlineTexture(transparentTexture);",
            StringComparison.Ordinal);

        Assert.True(
            applyTexture >= 0 &&
            applyTexture < primaryTexture &&
            primaryTexture < outlineTexture);
    }

    [Fact]
    public void TransparentTextureIsOnePixelRgba32AndProcessLifetime()
    {
        string source = File.ReadAllText(
            FindRepositoryFile("NeonLetterLinkUiTextureRuntime.cs"));

        Assert.True(
            CountOccurrences(source, "new Texture2D(") == 1 &&
            source.Contains(
                "new Texture2D(\n" +
                "            1,\n" +
                "            1,\n" +
                "            TextureFormat.RGBA32,\n" +
                "            mipChain: false)",
                StringComparison.Ordinal) &&
            source.Contains(
                "texture.SetPixel(0, 0, Color.clear);",
                StringComparison.Ordinal) &&
            source.Contains(
                "texture.Apply(\n" +
                "            updateMipmaps: false,\n" +
                "            makeNoLongerReadable: true);",
                StringComparison.Ordinal) &&
            source.Contains(
                "texture.hideFlags = HideFlags.HideAndDontSave;",
                StringComparison.Ordinal) &&
            !source.Contains(
                "Object.Destroy",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OwnedLinkIsConfiguredBeforeItsHolderCanActivate()
    {
        string leaseSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterColorInteractionLeaseRuntime.cs"));
        string interactionSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterColorInteractionRuntime.cs"));
        int promptCreated = leaseSource.IndexOf(
            "SonsUiTools.CreateLinkUi(",
            StringComparison.Ordinal);
        int configured = leaseSource.IndexOf(
            "NeonLetterLinkUiTextureRuntime.ConfigureOwnedLinkUi(",
            StringComparison.Ordinal);
        int leaseReturned = leaseSource.IndexOf(
            "return lease;",
            configured,
            StringComparison.Ordinal);
        int leaseCreated = interactionSource.IndexOf(
            "ColorInteractionLease lease = CreateColorInteractionLease(",
            StringComparison.Ordinal);
        int holderActivated = interactionSource.IndexOf(
            "lease.Activate();",
            StringComparison.Ordinal);

        Assert.True(
            promptCreated >= 0 &&
            promptCreated < configured &&
            configured < leaseReturned &&
            leaseCreated >= 0 &&
            leaseCreated < holderActivated);
    }

    [Fact]
    public void CacheIsInitializedForTheMainThreadWithoutCreatingATexture()
    {
        string runtimeSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterLinkUiTextureRuntime.cs"));
        string colorSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterColorRuntime.cs"));
        string initializeMethod = ExtractMethod(
            colorSource,
            "public static void Initialize()",
            "internal static void Deinitialize()");

        Assert.True(
            initializeMethod.Contains(
                "NeonLetterLinkUiTextureRuntime.InitializeMainThread();",
                StringComparison.Ordinal) &&
            runtimeSource.Contains(
                "Environment.CurrentManagedThreadId",
                StringComparison.Ordinal) &&
            runtimeSource.Contains(
                "TransparentTextureCache.GetOrCreate(",
                StringComparison.Ordinal) &&
            !initializeMethod.Contains(
                "ConfigureOwnedLinkUi",
                StringComparison.Ordinal) &&
            !initializeMethod.Contains(
                "CreateTransparentTexture",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeCachesTheTextureFactoryDelegateOnce()
    {
        string source = File.ReadAllText(
            FindRepositoryFile("NeonLetterLinkUiTextureRuntime.cs"));

        Assert.True(
            source.Contains(
                "private static readonly Func<Texture2D>\n" +
                "        CreateTransparentTextureCallback =\n" +
                "            CreateTransparentTexture;",
                StringComparison.Ordinal) &&
            source.Contains(
                "TransparentTextureCache.GetOrCreate(\n" +
                "                CreateTransparentTextureCallback);",
                StringComparison.Ordinal) &&
            !source.Contains(
                "GetOrCreate(\n" +
                "                CreateTransparentTexture);",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NativeUseMechanismHasNoVisualHooksOrCustomPool()
    {
        string source = string.Join(
            "\n",
            new[]
            {
                "NeonLetterColorInteractionLeaseRuntime.cs",
                "NeonLetterColorInteractionRuntime.cs",
                "NeonLetterColorInteractionHarmony.cs",
                "NeonLetterLinkUiTextureRuntime.cs"
            }.Select(path => File.ReadAllText(FindRepositoryFile(path))));
        string[] forbiddenTokens =
        {
            "HarmonyPatch(\n    typeof(LinkUiElement)",
            "RequestUiElement",
            "RemoveElement",
            "ApplyTextureAndMaterial",
            "ReturnPooled",
            "UiElementManager",
            "DynamicInputIcon",
            "ManagedUpdate",
            "screen.neon",
            "customUiElementId"
        };

        Assert.True(
            source.Contains("\"screen.use\"", StringComparison.Ordinal) &&
            source.Contains(
                "private const string NativeUseAction = \"Use\";",
                StringComparison.Ordinal) &&
            source.Contains(
                "SonsInteractionTools.CreateInteraction<GenericInteraction>",
                StringComparison.Ordinal) &&
            forbiddenTokens.All(
                token => !source.Contains(token, StringComparison.Ordinal)));
    }

    private static int CountOccurrences(string source, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(
                   token,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static string ExtractMethod(
        string source,
        string startToken,
        string endToken)
    {
        int start = source.IndexOf(startToken, StringComparison.Ordinal);
        int end = source.IndexOf(endToken, start, StringComparison.Ordinal);
        return start >= 0 && end > start
            ? source.Substring(start, end - start)
            : string.Empty;
    }

    private static string FindRepositoryFile(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Unable to locate repository file '{fileName}'.");
    }
}
