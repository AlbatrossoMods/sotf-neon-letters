using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace SOTFNeonLetters.Editor
{
    public static class BuildNeonAlphabet
    {
        private const string SourceModelPath =
            "Assets/GeneratedSource/NeonLetters_NoBackground.dae";
        private const string ExtensionSourceModelPath =
            "Assets/GeneratedSource/NeonLetters_Extended.glb";
        private const string SourceMaskPath =
            "Assets/GeneratedSource/NeonLetters_EmissionMask.png";
        private const string WireIngredientName = "Ingredient_Wire_Lead";
        private const string GeneratedAssetPath = "Assets/Generated";
        private const string GeneratedMaterialFolder = "Assets/Generated/Materials";
        private const string GeneratedPrefabFolder = "Assets/Generated/Prefabs";
        private const string GeneratedTextureFolder = "Assets/Generated/Textures";
        private const string LetterMaterialAssetPath =
            "Assets/Generated/Materials/NeonLetter_A_Cyan.mat";
        private const string WireMaterialAssetPath =
            "Assets/Generated/Materials/NeonLetter_Wire_Dark.mat";
        private const string LetterShaderName = "HDRP/Lit";
        private const string WireShaderName = "HDRP/Lit";
        private const string BaseColorProperty = "_BaseColor";
        private const string MetallicProperty = "_Metallic";
        private const string SmoothnessProperty = "_Smoothness";
        private const string EmissiveColorProperty = "_EmissiveColor";
        private const string EmissiveColorLdrProperty = "_EmissiveColorLDR";
        private const string EmissiveColorMapProperty = "_EmissiveColorMap";
        private const string UseEmissiveIntensityProperty = "_UseEmissiveIntensity";
        private const string EmissiveIntensityProperty = "_EmissiveIntensity";
        private const string EmissiveIntensityUnitProperty = "_EmissiveIntensityUnit";
        private const string EmissiveExposureWeightProperty = "_EmissiveExposureWeight";
        private const string DoubleSidedEnableProperty = "_DoubleSidedEnable";
        private const string DoubleSidedNormalModeProperty = "_DoubleSidedNormalMode";
        private const string CullModeProperty = "_CullMode";
        private const string CullModeForwardProperty = "_CullModeForward";
        private const string EmissiveColorMapKeyword = "_EMISSIVE_COLOR_MAP";
        private const string BundleName = "sotfneonletters";
        private const string BundleOutputPath = "Build/AssetBundles/Windows";
        private const int BookPageWidth = 1024;
        private const int BookPageHeight = 1024;
        private const int BookPageMipCount = 11;
        private const int BookIconSize = 128;
        private const int BookIconMipCount = 8;
        private const int IconMargin = 12;
        private const int TopCardLeft = 80;
        private const int TopCardRight = 352;
        private const int TopCardBottom = 512;
        private const int TopCardTop = 832;
        private const int BottomCardLeft = 80;
        private const int BottomCardRight = 352;
        private const int BottomCardBottom = 128;
        private const int BottomCardTop = 480;
        private const int CardMargin = 12;
        private const float NeonEmissiveIntensityNits = 600.0f;
        private const float SmallTargetHeight = 0.5f;

        private static readonly LetterDefinition[] Letters = CreateLetterDefinitions();

        public static void Build()
        {
            Debug.Log(
                $"SOTF Neon Letters: building {Letters.Length} Small letters at " +
                $"{SmallTargetHeight:F2} Unity units tall.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EnsureGeneratedAssets();

            GameObject sourceModel = LoadRequiredAsset<GameObject>(SourceModelPath);
            GameObject extensionSourceModel =
                LoadRequiredAsset<GameObject>(ExtensionSourceModelPath);
            LetterDefinition extensionScaleReference = Array.Find(
                Letters,
                letter => letter.AssetKey == "CYR_U0410");
            if (extensionScaleReference == null)
            {
                throw new InvalidOperationException(
                    "The extension catalog is missing its CYR_U0410 scale reference.");
            }
            Bounds extensionReferenceBounds = CalculateRenderedBounds(
                RequireSourceLetter(extensionSourceModel, extensionScaleReference)
                    .GetComponentsInChildren<Renderer>(true));
            float extensionUniformScale =
                SmallTargetHeight / extensionReferenceBounds.size.y;
            Texture2D emissionMask = LoadRequiredAsset<Texture2D>(SourceMaskPath);
            Material letterMaterial = CreateLetterMaterial(emissionMask);
            Material wireMaterial = CreateWireMaterial();
            var iconPixelsByLetter = new Dictionary<char, Color32[]>();

            foreach (LetterDefinition letter in Letters)
            {
                GameObject selectedSourceModel = letter.Source == NeonSymbolSource.LegacyDae
                    ? sourceModel
                    : extensionSourceModel;
                Transform sourceLetter = RequireSourceLetter(selectedSourceModel, letter);
                BuildPrefab(
                    letter,
                    sourceLetter,
                    letterMaterial,
                    wireMaterial,
                    letter.Source == NeonSymbolSource.ExtensionGlb
                        ? extensionUniformScale
                        : (float?)null);

                Color32[] iconPixels = CreateBookIconPixels(sourceLetter);
                iconPixelsByLetter.Add(letter.Letter, iconPixels);
                BuildTextureAsset(
                    letter.BookIconName,
                    letter.BookIconAssetPath,
                    BookIconSize,
                    BookIconSize,
                    BookIconMipCount,
                    iconPixels,
                    $"{letter.Letter} book icon");
            }

            for (int pageIndex = 0; pageIndex < Letters.Length / 2; pageIndex++)
            {
                LetterDefinition topLetter = Letters[pageIndex * 2];
                LetterDefinition bottomLetter = Letters[pageIndex * 2 + 1];
                string pageName = GetBookPageName(pageIndex);
                BuildTextureAsset(
                    pageName,
                    GetBookPageAssetPath(pageIndex),
                    BookPageWidth,
                    BookPageHeight,
                    BookPageMipCount,
                    CreateBookPagePixels(
                        iconPixelsByLetter[topLetter.Letter],
                        iconPixelsByLetter[bottomLetter.Letter]),
                    $"{topLetter.Letter}-{bottomLetter.Letter} book page");
            }

            AssetDatabase.SaveAssets();
            BuildAssetBundle();
        }

        private static void EnsureGeneratedAssets()
        {
            EnsureAssetFolder(GeneratedAssetPath);
            EnsureAssetFolder(GeneratedMaterialFolder);
            EnsureAssetFolder(GeneratedPrefabFolder);
            EnsureAssetFolder(GeneratedTextureFolder);
        }

        private static T LoadRequiredAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Could not import required {typeof(T).Name} at {assetPath}.");
            }

            return asset;
        }

        private static Transform RequireSourceLetter(
            GameObject sourceModel,
            LetterDefinition letter)
        {
            Transform sourceLetter = sourceModel.transform.Find(letter.SourceNodeName);
            if (sourceLetter == null)
            {
                throw new InvalidOperationException(
                    $"Source model does not contain the exact child '{letter.SourceNodeName}' " +
                    $"for {letter.Letter}.");
            }

            MeshFilter[] meshFilters = sourceLetter.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Source child '{letter.SourceNodeName}' does not contain a MeshFilter.");
            }

            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        $"Source child '{letter.SourceNodeName}' contains MeshFilter " +
                        $"'{meshFilter.name}' without a shared mesh.");
                }
            }

            if (sourceLetter.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                throw new InvalidOperationException(
                    $"Source child '{letter.SourceNodeName}' does not contain a renderer.");
            }

            return sourceLetter;
        }

        private static void BuildPrefab(
            LetterDefinition letter,
            Transform sourceLetter,
            Material letterMaterial,
            Material wireMaterial,
            float? prescribedUniformScale)
        {
            GameObject root = new GameObject(letter.PrefabName);

            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;

                GameObject geometry = UnityEngine.Object.Instantiate(
                    sourceLetter.gameObject,
                    root.transform,
                    false);
                geometry.name = letter.LetterIngredientName;
                geometry.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);

                Renderer[] renderers = geometry.GetComponentsInChildren<Renderer>(true);
                AssignMaterial(renderers, letterMaterial);

                Bounds initialBounds = CalculateRenderedBounds(renderers);
                if (initialBounds.size.y <= Mathf.Epsilon)
                {
                    throw new InvalidOperationException(
                        $"Source child '{letter.SourceNodeName}' has zero rendered height.");
                }

                float uniformScale = prescribedUniformScale ??
                    SmallTargetHeight / initialBounds.size.y;
                geometry.transform.localScale *= uniformScale;

                Bounds scaledBounds = CalculateRenderedBounds(renderers);
                geometry.transform.position += new Vector3(
                    -scaledBounds.center.x,
                    -scaledBounds.min.y,
                    -scaledBounds.center.z);

                Bounds letterBounds = CalculateRenderedBounds(renderers);
                CreateWireIngredient(root.transform, letterBounds, wireMaterial);

                root.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(root, letter.PrefabAssetPath, out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException(
                        $"Failed to save prefab at {letter.PrefabAssetPath}.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Material CreateLetterMaterial(Texture2D emissionMask)
        {
            Material material = CreateRequiredHdrpLitMaterial(
                LetterShaderName,
                "NeonLetter_A_Cyan",
                "letter");
            RequireMaterialProperties(
                material,
                BaseColorProperty,
                MetallicProperty,
                SmoothnessProperty,
                EmissiveColorProperty,
                EmissiveColorLdrProperty,
                EmissiveColorMapProperty,
                UseEmissiveIntensityProperty,
                EmissiveIntensityProperty,
                EmissiveIntensityUnitProperty,
                EmissiveExposureWeightProperty,
                DoubleSidedEnableProperty,
                DoubleSidedNormalModeProperty,
                CullModeProperty,
                CullModeForwardProperty);

            material.SetColor(BaseColorProperty, Color.white);
            material.SetFloat(MetallicProperty, 0.0f);
            material.SetFloat(SmoothnessProperty, 0.65f);
            material.SetTexture(EmissiveColorMapProperty, emissionMask);
            material.SetColor(EmissiveColorLdrProperty, Color.cyan);
            material.SetFloat(UseEmissiveIntensityProperty, 1.0f);
            material.SetFloat(EmissiveIntensityProperty, NeonEmissiveIntensityNits);
            material.SetFloat(EmissiveIntensityUnitProperty, 0.0f);
            material.SetFloat(EmissiveExposureWeightProperty, 1.0f);
            material.SetFloat(DoubleSidedEnableProperty, 1.0f);
            material.SetFloat(DoubleSidedNormalModeProperty, 1.0f);
            ValidateHdrpMaterial(material, "letter");
            ValidateDoubleSidedLetterMaterial(material);

            if (!material.IsKeywordEnabled(EmissiveColorMapKeyword))
            {
                throw new InvalidOperationException(
                    $"HDRP validation did not enable '{EmissiveColorMapKeyword}' for the neon " +
                    "letter emissive map.");
            }

            return SaveGeneratedAsset(material, LetterMaterialAssetPath);
        }

        private static Material CreateWireMaterial()
        {
            Material material = CreateRequiredHdrpLitMaterial(
                WireShaderName,
                "NeonLetter_Wire_Dark",
                "wire");
            RequireMaterialProperties(
                material,
                BaseColorProperty,
                MetallicProperty,
                SmoothnessProperty);

            material.SetColor(BaseColorProperty, new Color(0.025f, 0.03f, 0.035f, 1.0f));
            material.SetFloat(MetallicProperty, 0.15f);
            material.SetFloat(SmoothnessProperty, 0.2f);
            ValidateHdrpMaterial(material, "wire");

            return SaveGeneratedAsset(material, WireMaterialAssetPath);
        }

        private static Material CreateRequiredHdrpLitMaterial(
            string shaderName,
            string materialName,
            string materialRole)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Required HDRP shader '{shaderName}' for the {materialRole} material is " +
                    "unavailable. Install the pinned HDRP package; no fallback shader is allowed.");
            }

            var material = new Material(shader);
            material.name = materialName;
            return material;
        }

        private static void RequireMaterialProperties(
            Material material,
            params string[] properties)
        {
            foreach (string property in properties)
            {
                if (!material.HasProperty(property))
                {
                    throw new InvalidOperationException(
                        $"Shader '{material.shader.name}' is missing required HDRP property " +
                        $"'{property}' for material '{material.name}'.");
                }
            }
        }

        private static void ValidateHdrpMaterial(Material material, string materialRole)
        {
            if (!HDShaderUtils.ResetMaterialKeywords(material))
            {
                throw new InvalidOperationException(
                    $"HDRP 14 could not validate shader keywords for the {materialRole} material " +
                    $"'{material.name}' using shader '{material.shader.name}'.");
            }
        }

        private static void ValidateDoubleSidedLetterMaterial(Material material)
        {
            if (!Mathf.Approximately(material.GetFloat(DoubleSidedEnableProperty), 1.0f) ||
                !Mathf.Approximately(material.GetFloat(CullModeProperty), 0.0f) ||
                !Mathf.Approximately(material.GetFloat(CullModeForwardProperty), 0.0f))
            {
                throw new InvalidOperationException(
                    $"Neon letter material '{material.name}' must render both sides with " +
                    "culling disabled.");
            }
        }

        private static void CreateWireIngredient(
            Transform root,
            Bounds letterBounds,
            Material wireMaterial)
        {
            GameObject wireObject = new GameObject(WireIngredientName);
            wireObject.transform.SetParent(root, false);
            wireObject.transform.SetSiblingIndex(0);

            LineRenderer wire = wireObject.AddComponent<LineRenderer>();
            wire.sharedMaterial = wireMaterial;
            wire.useWorldSpace = false;
            wire.positionCount = 3;
            wire.numCapVertices = 6;
            wire.numCornerVertices = 4;
            wire.textureMode = LineTextureMode.Stretch;

            float wireWidth = Mathf.Clamp(letterBounds.size.y * 0.018f, 0.035f, 0.065f);
            float halfLength = Mathf.Clamp(letterBounds.size.x * 0.16f, 0.12f, 0.28f);
            float wireY = Mathf.Max(wireWidth * 0.75f, 0.03f);
            wire.startWidth = wireWidth;
            wire.endWidth = wireWidth;
            wire.SetPositions(
                new[]
                {
                    new Vector3(-halfLength, wireY, 0.0f),
                    new Vector3(0.0f, wireY + wireWidth * 0.3f, 0.0f),
                    new Vector3(halfLength, wireY, 0.0f)
                });
        }

        private static void AssignMaterial(Renderer[] renderers, Material material)
        {
            foreach (Renderer renderer in renderers)
            {
                int slotCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                var materials = new Material[slotCount];
                for (int index = 0; index < materials.Length; index++)
                {
                    materials[index] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static Bounds CalculateRenderedBounds(Renderer[] renderers)
        {
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException("Cannot calculate bounds without a renderer.");
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void BuildTextureAsset(
            string assetName,
            string assetPath,
            int width,
            int height,
            int expectedMipCount,
            Color32[] pixels,
            string assetDescription)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, true, false);
            texture.name = assetName;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            EditorUtility.CompressTexture(
                texture,
                TextureFormat.DXT1,
                TextureCompressionQuality.Best);

            if (texture.format != TextureFormat.DXT1)
            {
                throw new InvalidOperationException(
                    $"Generated {assetDescription} must use DXT1, but Unity produced " +
                    $"{texture.format}.");
            }

            if (texture.mipmapCount != expectedMipCount)
            {
                throw new InvalidOperationException(
                    $"Generated {width}x{height} {assetDescription} must have " +
                    $"{expectedMipCount} mip levels, but has {texture.mipmapCount}.");
            }

            texture.Apply(false, true);
            if (texture.isReadable)
            {
                throw new InvalidOperationException(
                    $"Generated {assetDescription} must release its CPU pixel copy.");
            }

            SaveGeneratedAsset(texture, assetPath);
        }

        private static T SaveGeneratedAsset<T>(T generatedAsset, string assetPath)
            where T : UnityEngine.Object
        {
            T existingAsset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existingAsset == null)
            {
                AssetDatabase.CreateAsset(generatedAsset, assetPath);
                return generatedAsset;
            }

            EditorUtility.CopySerialized(generatedAsset, existingAsset);
            EditorUtility.SetDirty(existingAsset);
            UnityEngine.Object.DestroyImmediate(generatedAsset);
            return existingAsset;
        }

        private static Color32[] CreateNeutralTexturePixels(int width, int height)
        {
            var pixels = new Color32[width * height];
            Vector2 center = new Vector2(width * 0.5f, height * 0.5f);
            float maximumRadius = Vector2.Distance(Vector2.zero, center);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), center) / maximumRadius;
                    byte neutral = (byte)Mathf.RoundToInt(
                        Mathf.Lerp(240.0f, 222.0f, Mathf.Clamp01(radius)));
                    pixels[y * width + x] = new Color32(
                        neutral,
                        (byte)(neutral - 3),
                        (byte)(neutral - 8),
                        byte.MaxValue);
                }
            }

            return pixels;
        }

        private static Color32[] CreateBookIconPixels(Transform sourceLetter)
        {
            Color32[] pixels = CreateNeutralTexturePixels(BookIconSize, BookIconSize);
            bool[] silhouette = RasterizeSourceSilhouette(sourceLetter);
            DrawSilhouetteGlow(pixels, silhouette);
            return pixels;
        }

        private static bool[] RasterizeSourceSilhouette(Transform sourceLetter)
        {
            MeshFilter[] meshFilters = sourceLetter.GetComponentsInChildren<MeshFilter>(true);
            var projectedMeshes = new List<ProjectedMesh>();
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            foreach (MeshFilter meshFilter in meshFilters)
            {
                Mesh mesh = meshFilter.sharedMesh;
                Vector3[] sourceVertices = mesh.vertices;
                var vertices = new Vector2[sourceVertices.Length];
                for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
                {
                    Vector3 point = meshFilter.transform.TransformPoint(sourceVertices[vertexIndex]);
                    Vector2 projected = new Vector2(-point.x, point.y);
                    vertices[vertexIndex] = projected;
                    minimum = Vector2.Min(minimum, projected);
                    maximum = Vector2.Max(maximum, projected);
                }

                projectedMeshes.Add(new ProjectedMesh(vertices, mesh.triangles));
            }

            Vector2 size = maximum - minimum;
            if (size.x <= Mathf.Epsilon || size.y <= Mathf.Epsilon)
            {
                throw new InvalidOperationException(
                    $"Source child '{sourceLetter.name}' has zero projected icon area.");
            }

            float drawableSize = BookIconSize - IconMargin * 2;
            float scale = Mathf.Min(drawableSize / size.x, drawableSize / size.y);
            Vector2 contentSize = size * scale;
            Vector2 offset = new Vector2(
                (BookIconSize - contentSize.x) * 0.5f,
                (BookIconSize - contentSize.y) * 0.5f);
            var silhouette = new bool[BookIconSize * BookIconSize];

            foreach (ProjectedMesh projectedMesh in projectedMeshes)
            {
                Vector2[] vertices = projectedMesh.Vertices;
                int[] triangles = projectedMesh.Triangles;
                for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
                {
                    Vector2 first = (vertices[triangles[triangleIndex]] - minimum) * scale + offset;
                    Vector2 second =
                        (vertices[triangles[triangleIndex + 1]] - minimum) * scale + offset;
                    Vector2 third =
                        (vertices[triangles[triangleIndex + 2]] - minimum) * scale + offset;
                    RasterizeTriangle(silhouette, first, second, third);
                }
            }

            return silhouette;
        }

        private static void RasterizeTriangle(
            bool[] silhouette,
            Vector2 first,
            Vector2 second,
            Vector2 third)
        {
            float area = Cross(second - first, third - first);
            if (Mathf.Abs(area) <= Mathf.Epsilon)
            {
                return;
            }

            int minimumX = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Min(first.x, Mathf.Min(second.x, third.x))),
                0,
                BookIconSize - 1);
            int maximumX = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(first.x, Mathf.Max(second.x, third.x))),
                0,
                BookIconSize - 1);
            int minimumY = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Min(first.y, Mathf.Min(second.y, third.y))),
                0,
                BookIconSize - 1);
            int maximumY = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(first.y, Mathf.Max(second.y, third.y))),
                0,
                BookIconSize - 1);

            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float firstEdge = Cross(second - first, point - first);
                    float secondEdge = Cross(third - second, point - second);
                    float thirdEdge = Cross(first - third, point - third);
                    bool hasNegative = firstEdge < 0.0f || secondEdge < 0.0f || thirdEdge < 0.0f;
                    bool hasPositive = firstEdge > 0.0f || secondEdge > 0.0f || thirdEdge > 0.0f;
                    if (!(hasNegative && hasPositive))
                    {
                        silhouette[y * BookIconSize + x] = true;
                    }
                }
            }
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static void DrawSilhouetteGlow(Color32[] pixels, bool[] silhouette)
        {
            const int glowRadius = 4;
            Color32 glow = new Color32(0, 218, 230, byte.MaxValue);
            for (int y = 0; y < BookIconSize; y++)
            {
                for (int x = 0; x < BookIconSize; x++)
                {
                    if (!silhouette[y * BookIconSize + x])
                    {
                        continue;
                    }

                    for (int offsetY = -glowRadius; offsetY <= glowRadius; offsetY++)
                    {
                        int targetY = y + offsetY;
                        if (targetY < 0 || targetY >= BookIconSize)
                        {
                            continue;
                        }

                        for (int offsetX = -glowRadius; offsetX <= glowRadius; offsetX++)
                        {
                            int targetX = x + offsetX;
                            if (targetX < 0 || targetX >= BookIconSize)
                            {
                                continue;
                            }

                            float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
                            if (distance > glowRadius)
                            {
                                continue;
                            }

                            float strength = (1.0f - distance / glowRadius) * 0.42f;
                            BlendPixel(
                                pixels,
                                targetY * BookIconSize + targetX,
                                glow,
                                strength);
                        }
                    }
                }
            }

            Color32 core = new Color32(0, 255, 255, byte.MaxValue);
            for (int index = 0; index < silhouette.Length; index++)
            {
                if (silhouette[index])
                {
                    pixels[index] = core;
                }
            }
        }

        private static void BlendPixel(
            Color32[] pixels,
            int index,
            Color32 source,
            float sourceAlpha)
        {
            Color32 destination = pixels[index];
            pixels[index] = new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(destination.r, source.r, sourceAlpha)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(destination.g, source.g, sourceAlpha)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(destination.b, source.b, sourceAlpha)),
                byte.MaxValue);
        }

        private static Color32[] CreateBookPagePixels(
            Color32[] topIconPixels,
            Color32[] bottomIconPixels)
        {
            Color32[] pagePixels = CreateNeutralTexturePixels(BookPageWidth, BookPageHeight);
            CopyIconToCard(
                pagePixels,
                topIconPixels,
                TopCardLeft,
                TopCardRight,
                TopCardBottom,
                TopCardTop);
            CopyIconToCard(
                pagePixels,
                bottomIconPixels,
                BottomCardLeft,
                BottomCardRight,
                BottomCardBottom,
                BottomCardTop);
            return pagePixels;
        }

        private static void CopyIconToCard(
            Color32[] pagePixels,
            Color32[] iconPixels,
            int cardLeft,
            int cardRight,
            int cardBottom,
            int cardTop)
        {
            int destinationLeft = cardLeft + CardMargin;
            int destinationRight = cardRight - CardMargin;
            int destinationBottom = cardBottom + CardMargin;
            int destinationTop = cardTop - CardMargin;
            int destinationWidth = destinationRight - destinationLeft;
            int destinationHeight = destinationTop - destinationBottom;

            for (int destinationY = 0; destinationY < destinationHeight; destinationY++)
            {
                int sourceY = destinationY * BookIconSize / destinationHeight;
                for (int destinationX = 0; destinationX < destinationWidth; destinationX++)
                {
                    int sourceX = destinationX * BookIconSize / destinationWidth;
                    Color32 source = iconPixels[sourceY * BookIconSize + sourceX];
                    if (!IsCyan(source))
                    {
                        continue;
                    }

                    int pageX = destinationLeft + destinationX;
                    int pageY = destinationBottom + destinationY;
                    pagePixels[pageY * BookPageWidth + pageX] = source;
                }
            }
        }

        private static bool IsCyan(Color32 pixel)
        {
            return pixel.g > 180 && pixel.b > 180 &&
                   pixel.g > pixel.r + 20 && pixel.b > pixel.r + 20;
        }

        private static void EnsureAssetFolder(string assetFolderPath)
        {
            string[] pathParts = assetFolderPath.Split('/');
            string currentPath = pathParts[0];

            for (int index = 1; index < pathParts.Length; index++)
            {
                string nextPath = $"{currentPath}/{pathParts[index]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, pathParts[index]);
                }

                currentPath = nextPath;
            }
        }

        private static void BuildAssetBundle()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            string outputDirectory = Path.Combine(projectRoot, BundleOutputPath);

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            Directory.CreateDirectory(outputDirectory);
            var assetNames = new List<string>();
            var addressableNames = new List<string>();

            foreach (LetterDefinition letter in Letters)
            {
                assetNames.Add(letter.PrefabAssetPath);
                addressableNames.Add(letter.PrefabName);
                assetNames.Add(letter.BookIconAssetPath);
                addressableNames.Add(letter.BookIconName);
            }

            for (int pageIndex = 0; pageIndex < Letters.Length / 2; pageIndex++)
            {
                string pageName = GetBookPageName(pageIndex);
                assetNames.Add(GetBookPageAssetPath(pageIndex));
                addressableNames.Add(pageName);
            }

            AssetBundleBuild[] buildMap =
            {
                new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = assetNames.ToArray(),
                    addressableNames = addressableNames.ToArray()
                }
            };

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                buildMap,
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);

            if (manifest == null)
            {
                throw new InvalidOperationException("Unity returned no asset bundle build result.");
            }

            string[] bundleNames = manifest.GetAllAssetBundles();
            if (bundleNames.Length != 1 ||
                !string.Equals(bundleNames[0], BundleName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Unity did not build exactly the requested asset bundle.");
            }

            string bundlePath = Path.Combine(outputDirectory, BundleName);
            FileInfo bundleFile = new FileInfo(bundlePath);
            if (!bundleFile.Exists || bundleFile.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Built bundle is missing or empty: {bundlePath}");
            }

            if (!BuildPipeline.GetCRCForAssetBundle(bundlePath, out _))
            {
                throw new InvalidOperationException(
                    $"Unity could not extract a CRC from the built bundle: {bundlePath}");
            }
        }

        private static string GetBookPageName(int pageIndex)
        {
            return $"NeonLetters_Small_Page_{pageIndex + 1:00}";
        }

        private static string GetBookPageAssetPath(int pageIndex)
        {
            return $"{GeneratedTextureFolder}/{GetBookPageName(pageIndex)}.asset";
        }

        private sealed class LetterDefinition
        {
            public LetterDefinition(NeonSymbolManifestEntry manifestEntry)
            {
                Letter = manifestEntry.Symbol;
                AssetKey = manifestEntry.AssetKey;
                SourceNodeName = manifestEntry.SourceNodeName;
                Source = manifestEntry.Source;
            }

            public char Letter { get; }
            public string AssetKey { get; }
            public string SourceNodeName { get; }
            public NeonSymbolSource Source { get; }
            public string PrefabName => $"NeonLetter_{AssetKey}_Small";
            public string PrefabAssetPath =>
                $"{GeneratedPrefabFolder}/{PrefabName}.prefab";
            public string LetterIngredientName => $"Ingredient_LightBulb_{AssetKey}";
            public string BookIconName => $"NeonLetter_{AssetKey}_Small_Icon";
            public string BookIconAssetPath =>
                $"{GeneratedTextureFolder}/{BookIconName}.asset";
        }

        private static LetterDefinition[] CreateLetterDefinitions()
        {
            IReadOnlyList<NeonSymbolManifestEntry> manifest = NeonSymbolManifest.All;
            var definitions = new LetterDefinition[manifest.Count];
            for (int index = 0; index < manifest.Count; index++)
            {
                definitions[index] = new LetterDefinition(manifest[index]);
            }

            return definitions;
        }

        private sealed class ProjectedMesh
        {
            public ProjectedMesh(Vector2[] vertices, int[] triangles)
            {
                Vertices = vertices;
                Triangles = triangles;
            }

            public Vector2[] Vertices { get; }
            public int[] Triangles { get; }
        }
    }
}
