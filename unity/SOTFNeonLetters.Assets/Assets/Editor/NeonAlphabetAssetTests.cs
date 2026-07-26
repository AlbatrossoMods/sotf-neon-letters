using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SOTFNeonLetters.Editor
{
    public static class NeonAlphabetAssetTests
    {
        private const string SourceModelPath =
            "Assets/GeneratedSource/NeonLetters_NoBackground.dae";
        private const string ExtensionSourceModelPath =
            "Assets/GeneratedSource/NeonLetters_Extended.glb";
        private const string SourceEmissionMaskPath =
            "Assets/GeneratedSource/NeonLetters_EmissionMask.png";
        private const string GeneratedPrefabFolder = "Assets/Generated/Prefabs";
        private const string GeneratedTextureFolder = "Assets/Generated/Textures";
        private const string WireIngredientName = "Ingredient_Wire_Lead";
        private const string BundleRelativePath = "Build/AssetBundles/Windows/sotfneonletters";
        private const string BuildStartedEpochVariable =
            "SOTF_NEON_UNITY_BUILD_STARTED_EPOCH";
        private const string LetterShaderName = "HDRP/Lit";
        private const float TargetHeight = 0.5f;
        private const float GeometryTolerance = 0.01f;
        private const float MinimumLetterWidth = 0.03f;
        private const float MaximumLetterWidth = 0.60f;
        private const float MinimumLetterDepth = 0.10f;
        private const float MaximumLetterDepth = 0.14f;
        private const float MinimumRuntimeColliderDepth = 0.08f;
        private const float MaximumExtensionColliderDepth = TargetHeight * 0.45f;
        private const float EmissiveIntensityNits = 600.0f;
        private const int ExpectedSymbolCount = 80;
        private const int BookPageCount = 40;
        private const int BookIconSize = 128;
        private const int ExpectedBundleAssetCount = 200;
        private const int CanonicalSignatureSize = 32;
        private const float MaximumCanonicalSignatureDistance = 0.15f;
        private const int RecipeCardMargin = 12;

        private static readonly PixelRegion TopRecipeCard =
            new PixelRegion(80, 352, 512, 832);
        private static readonly PixelRegion BottomRecipeCard =
            new PixelRegion(80, 352, 128, 480);

        private static readonly LetterCase[] Letters = CreateCases();

        public static void Run()
        {
            var failures = new List<string>();
            RunTest(
                "shared catalog contains every supported neon symbol",
                () => AssertEqual(
                    ExpectedSymbolCount,
                    Letters.Length,
                    "generated symbol test-case count"),
                failures);
            RunTest("canonical source nodes resolve exactly once", TestSourceLetterNodes, failures);
            RunTest(
                "canonical emission mask contains luminous pixels",
                TestCanonicalEmissionMask,
                failures);

            foreach (LetterCase letter in Letters)
            {
                LetterCase captured = letter;
                RunTest(
                    $"Small {captured.Letter} generated prefab",
                    () => TestPrefab(captured),
                    failures);
                RunTest(
                    $"Small {captured.Letter} generated icon",
                    () => TestBookIcon(captured),
                    failures);
            }

            RunTest("legacy A-Z icon silhouettes are unique", TestIconSilhouettesAreUnique, failures);
            RunTest(
                "Small B prefab and icon face forward",
                TestAsymmetricBOrientation,
                failures);
            RunTest(
                "Small Cyrillic Б prefab faces forward",
                TestAsymmetricCyrillicBeOrientation,
                failures);
            RunTest(
                "extension bounds are suitable for close-fit runtime colliders",
                TestExtensionColliderEnvelopes,
                failures);
            RunTest(
                "legacy A-Z asset identities remain unchanged",
                TestLegacyAssetIdentity,
                failures);

            for (int pageIndex = 0; pageIndex < BookPageCount; pageIndex++)
            {
                int capturedPageIndex = pageIndex;
                RunTest(
                    $"Small {Letters[pageIndex * 2].Letter}-" +
                    $"{Letters[pageIndex * 2 + 1].Letter} two-recipe page",
                    () => TestBookPage(capturedPageIndex),
                    failures);
            }

            RunTest("fresh Windows bundle has exact symbol manifest", TestBundleArtifact, failures);
            RunTest(
                "Windows bundle resolves all non-readable SonsSdk texture references",
                TestRuntimeAssetReferences,
                failures);

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"SOTF Neon Letters Unity alphabet asset tests failed ({failures.Count}):\n- " +
                    string.Join("\n- ", failures));
            }

            Debug.Log("SOTF Neon Letters: all Small symbol Unity asset tests passed.");
        }

        private static void RunTest(
            string testName,
            Action test,
            ICollection<string> failures)
        {
            try
            {
                test();
                Debug.Log($"PASS: {testName}");
            }
            catch (Exception exception)
            {
                failures.Add($"{testName}: {exception.Message}");
            }
        }

        private static void TestSourceLetterNodes()
        {
            var sourceMeshes = new HashSet<Mesh>();

            foreach (LetterCase letter in Letters)
            {
                GameObject sourceModel =
                    LoadRequiredAsset<GameObject>(letter.SourceModelPath);
                Transform sourceLetter = RequireDirectChild(
                    sourceModel.transform,
                    letter.SourceNodeName);
                MeshFilter[] meshFilters = sourceLetter.GetComponentsInChildren<MeshFilter>(true);
                Renderer[] renderers = sourceLetter.GetComponentsInChildren<Renderer>(true);
                if (meshFilters.Length == 0 || renderers.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"source node '{letter.SourceNodeName}' for {letter.Letter} has no visible mesh");
                }

                foreach (MeshFilter meshFilter in meshFilters)
                {
                    Mesh mesh = meshFilter.sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0 || mesh.triangles.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"source node '{letter.SourceNodeName}' for {letter.Letter} " +
                            "contains an empty mesh");
                    }

                    if (!sourceMeshes.Add(mesh))
                    {
                        throw new InvalidOperationException(
                            $"source node '{letter.SourceNodeName}' for {letter.Letter} " +
                            "reuses another letter's mesh");
                    }
                }
            }

            AssertEqual(ExpectedSymbolCount, sourceMeshes.Count, "unique source mesh count");
        }

        private static void TestPrefab(LetterCase letter)
        {
            GameObject prefab = LoadRequiredAsset<GameObject>(letter.PrefabAssetPath);
            AssertEqual(letter.PrefabName, prefab.name, $"{letter.Letter} prefab name");
            AssertEqual(2, prefab.transform.childCount, $"{letter.Letter} top-level ingredient count");
            AssertEqual(
                WireIngredientName,
                prefab.transform.GetChild(0).name,
                $"{letter.Letter} first ingredient");
            AssertEqual(
                letter.LetterIngredientName,
                prefab.transform.GetChild(1).name,
                $"{letter.Letter} second ingredient");

            Transform wire = RequireDirectChild(prefab.transform, WireIngredientName);
            LineRenderer lineRenderer = wire.GetComponent<LineRenderer>();
            if (lineRenderer == null || lineRenderer.sharedMaterial == null)
            {
                throw new InvalidOperationException(
                    $"{letter.Letter} wire ingredient is not a visible materialized line");
            }

            Transform generatedLetter = RequireDirectChild(
                prefab.transform,
                letter.LetterIngredientName);
            AssertApproximately(
                180.0f,
                generatedLetter.localEulerAngles.y,
                GeometryTolerance,
                $"{letter.Letter} prefab forward-facing Y rotation");
            MeshFilter[] generatedMeshFilters =
                generatedLetter.GetComponentsInChildren<MeshFilter>(true);
            Renderer[] generatedRenderers =
                generatedLetter.GetComponentsInChildren<Renderer>(true);
            if (generatedMeshFilters.Length == 0 || generatedRenderers.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{letter.Letter} letter ingredient has no visible mesh");
            }

            AssertUsesExpectedSourceMeshes(letter, generatedMeshFilters);
            AssertLetterGeometry(letter, generatedRenderers);
            AssertLetterMaterial(letter, generatedRenderers);
        }

        private static void AssertUsesExpectedSourceMeshes(
            LetterCase letter,
            MeshFilter[] generatedMeshFilters)
        {
            GameObject sourceModel =
                LoadRequiredAsset<GameObject>(letter.SourceModelPath);
            Transform sourceLetter = RequireDirectChild(sourceModel.transform, letter.SourceNodeName);
            Mesh[] expectedMeshes = sourceLetter
                .GetComponentsInChildren<MeshFilter>(true)
                .Select(meshFilter => meshFilter.sharedMesh)
                .ToArray();
            Mesh[] actualMeshes = generatedMeshFilters
                .Select(meshFilter => meshFilter.sharedMesh)
                .ToArray();

            AssertEqual(
                expectedMeshes.Length,
                actualMeshes.Length,
                $"{letter.Letter} generated mesh count");
            foreach (Mesh expectedMesh in expectedMeshes)
            {
                int matches = actualMeshes.Count(actualMesh => actualMesh == expectedMesh);
                AssertEqual(
                    1,
                    matches,
                    $"{letter.Letter} use of source mesh '{expectedMesh.name}'");
            }
        }

        private static void AssertLetterGeometry(LetterCase letter, Renderer[] renderers)
        {
            Bounds bounds = CalculateRenderedBounds(renderers);
            AssertFinitePositive(bounds.size.x, $"{letter.Letter} collider-fit width");
            AssertFinitePositive(bounds.size.y, $"{letter.Letter} collider-fit height");
            AssertFinitePositive(bounds.size.z, $"{letter.Letter} collider-fit depth");
            if (letter.Source == NeonSymbolSource.LegacyDae)
            {
                AssertApproximately(
                    TargetHeight,
                    bounds.size.y,
                    GeometryTolerance,
                    $"{letter.Letter} height");
            }
            AssertApproximately(
                0.0f,
                bounds.min.y,
                GeometryTolerance,
                $"{letter.Letter} bottom pivot");
            AssertApproximately(
                0.0f,
                bounds.center.x,
                GeometryTolerance,
                $"{letter.Letter} horizontal center");
            AssertApproximately(
                0.0f,
                bounds.center.z,
                GeometryTolerance,
                $"{letter.Letter} depth center");

            if (letter.Source == NeonSymbolSource.LegacyDae &&
                (bounds.size.x < MinimumLetterWidth || bounds.size.x > MaximumLetterWidth))
            {
                throw new InvalidOperationException(
                    $"{letter.Letter} width must fit the I-to-W range " +
                    $"[{MinimumLetterWidth:F2}, {MaximumLetterWidth:F2}], " +
                    $"but is {bounds.size.x:F4}");
            }

            if (letter.Source == NeonSymbolSource.LegacyDae &&
                (bounds.size.z < MinimumLetterDepth || bounds.size.z > MaximumLetterDepth))
            {
                throw new InvalidOperationException(
                    $"{letter.Letter} depth must stay thin and non-zero in " +
                    $"[{MinimumLetterDepth:F2}, {MaximumLetterDepth:F2}], " +
                    $"but is {bounds.size.z:F4}");
            }

            GameObject sourceModel =
                LoadRequiredAsset<GameObject>(letter.SourceModelPath);
            Transform sourceLetter = RequireDirectChild(sourceModel.transform, letter.SourceNodeName);
            Bounds sourceBounds = CalculateRenderedBounds(
                sourceLetter.GetComponentsInChildren<Renderer>(true));
            float sourceScale = ResolveExpectedSourceScale(letter, sourceBounds);
            AssertApproximately(
                sourceBounds.size.y * sourceScale,
                bounds.size.y,
                GeometryTolerance,
                $"{letter.Letter} source height ratio");
            AssertApproximately(
                sourceBounds.size.x * sourceScale,
                bounds.size.x,
                GeometryTolerance,
                $"{letter.Letter} source width ratio");
            AssertApproximately(
                sourceBounds.size.z * sourceScale,
                bounds.size.z,
                GeometryTolerance,
                $"{letter.Letter} source depth ratio");
        }

        private static float ResolveExpectedSourceScale(LetterCase letter, Bounds sourceBounds)
        {
            if (letter.Source == NeonSymbolSource.LegacyDae)
            {
                return TargetHeight / sourceBounds.size.y;
            }

            LetterCase reference = Letters.Single(candidate => candidate.Letter == 'А');
            GameObject extensionModel = LoadRequiredAsset<GameObject>(ExtensionSourceModelPath);
            Bounds referenceBounds = CalculateRenderedBounds(
                RequireDirectChild(extensionModel.transform, reference.SourceNodeName)
                    .GetComponentsInChildren<Renderer>(true));
            return TargetHeight / referenceBounds.size.y;
        }

        private static void AssertLetterMaterial(LetterCase letter, Renderer[] renderers)
        {
            int materialCount = 0;
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    materialCount++;
                    if (material == null)
                    {
                        throw new InvalidOperationException(
                            $"{letter.Letter} renderer '{renderer.name}' has a null material slot");
                    }

                    AssertEqual(
                        LetterShaderName,
                        material.shader.name,
                        $"{letter.Letter} letter shader");
                    AssertMaterialProperty(material, "_EmissiveColorMap");
                    AssertMaterialProperty(material, "_EmissiveColorLDR");
                    AssertMaterialProperty(material, "_EmissiveIntensity");
                    AssertMaterialProperty(material, "_DoubleSidedEnable");
                    AssertMaterialProperty(material, "_CullMode");
                    AssertMaterialProperty(material, "_CullModeForward");

                    Texture actualEmissionMask = material.GetTexture("_EmissiveColorMap");
                    if (actualEmissionMask == null)
                    {
                        throw new InvalidOperationException(
                            $"{letter.Letter} letter material has no emission mask");
                    }

                    Texture2D expectedEmissionMask =
                        LoadRequiredAsset<Texture2D>(SourceEmissionMaskPath);
                    AssertEqual(
                        expectedEmissionMask,
                        actualEmissionMask as Texture2D,
                        $"{letter.Letter} canonical emission mask reference");
                    AssertEqual(
                        SourceEmissionMaskPath,
                        AssetDatabase.GetAssetPath(actualEmissionMask),
                        $"{letter.Letter} canonical emission mask path");

                    Color glow = material.GetColor("_EmissiveColorLDR");
                    AssertApproximately(0.0f, glow.r, 0.01f, $"{letter.Letter} emission red");
                    AssertApproximately(1.0f, glow.g, 0.01f, $"{letter.Letter} emission green");
                    AssertApproximately(1.0f, glow.b, 0.01f, $"{letter.Letter} emission blue");
                    AssertApproximately(
                        EmissiveIntensityNits,
                        material.GetFloat("_EmissiveIntensity"),
                        0.1f,
                        $"{letter.Letter} emission intensity");
                    AssertApproximately(
                        1.0f,
                        material.GetFloat("_DoubleSidedEnable"),
                        0.01f,
                        $"{letter.Letter} double-sided flag");
                    AssertApproximately(
                        0.0f,
                        material.GetFloat("_CullMode"),
                        0.01f,
                        $"{letter.Letter} cull mode");
                    AssertApproximately(
                        0.0f,
                        material.GetFloat("_CullModeForward"),
                        0.01f,
                        $"{letter.Letter} forward cull mode");

                    if (!material.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"))
                    {
                        throw new InvalidOperationException(
                            $"{letter.Letter} material does not enable the emission-map keyword");
                    }
                }
            }

            if (materialCount == 0)
            {
                throw new InvalidOperationException(
                    $"{letter.Letter} renderers contain no material slots");
            }
        }

        private static void TestBookIcon(LetterCase letter)
        {
            Texture2D icon = LoadRequiredAsset<Texture2D>(letter.BookIconAssetPath);
            AssertTexture(icon, letter.BookIconName, 128, 128, 8, $"{letter.Letter} book icon");
            Color32[] pixels = ReadSerializedDxt1Pixels(icon);
            AssertLightCorners(pixels, icon.width, icon.height, $"{letter.Letter} book icon");

            PixelBounds cyanBounds = FindCyanBounds(
                pixels,
                icon.width,
                icon.height,
                new PixelRegion(0, icon.width, 0, icon.height));
            if (cyanBounds.Count < 40)
            {
                throw new InvalidOperationException(
                    $"{letter.Letter} icon must contain a visible cyan glyph, " +
                    $"found {cyanBounds.Count} pixels");
            }

            AssertBoundsInsideRegion(
                cyanBounds,
                new PixelRegion(8, icon.width - 8, 8, icon.height - 8),
                $"{letter.Letter} icon glyph");
        }

        private static void TestIconSilhouettesAreUnique()
        {
            var ownersBySignature = new Dictionary<ulong, char>();
            foreach (LetterCase letter in Letters.Take(26))
            {
                Texture2D icon = LoadRequiredAsset<Texture2D>(letter.BookIconAssetPath);
                ulong signature = CalculateCyanSignature(
                    ReadSerializedDxt1Pixels(icon),
                    icon.width,
                    icon.height,
                    new PixelRegion(0, icon.width, 0, icon.height));
                if (ownersBySignature.TryGetValue(signature, out char existingLetter))
                {
                    throw new InvalidOperationException(
                        $"icons {existingLetter} and {letter.Letter} have identical cyan silhouettes");
                }

                ownersBySignature.Add(signature, letter.Letter);
            }
        }

        private static void TestAsymmetricBOrientation()
        {
            LetterCase letterB = Letters.Single(letter => letter.Letter == 'B');
            GameObject prefab = LoadRequiredAsset<GameObject>(letterB.PrefabAssetPath);
            Transform generatedLetter = RequireDirectChild(
                prefab.transform,
                letterB.LetterIngredientName);
            AssertApproximately(
                180.0f,
                generatedLetter.localEulerAngles.y,
                GeometryTolerance,
                "B prefab forward-facing Y rotation");

            Texture2D icon = LoadRequiredAsset<Texture2D>(letterB.BookIconAssetPath);
            Color32[] pixels = ReadSerializedDxt1Pixels(icon);
            var silhouette = new bool[pixels.Length];
            for (int index = 0; index < pixels.Length; index++)
            {
                silhouette[index] = IsCyan(pixels[index]);
            }

            AssertVerticalStemIsOnLeft(
                silhouette,
                icon.width,
                icon.height,
                "B book icon");
        }

        private static void TestAsymmetricCyrillicBeOrientation()
        {
            LetterCase letter = Letters.Single(candidate => candidate.Letter == 'Б');
            GameObject prefab = LoadRequiredAsset<GameObject>(letter.PrefabAssetPath);
            Transform generatedLetter = RequireDirectChild(
                prefab.transform,
                letter.LetterIngredientName);
            bool[] actualSilhouette = RasterizeFrontProjection(generatedLetter, false);

            GameObject sourceModel = LoadRequiredAsset<GameObject>(letter.SourceModelPath);
            Transform sourceLetter = RequireDirectChild(
                sourceModel.transform,
                letter.SourceNodeName);
            bool[] expectedSilhouette = RasterizeFrontProjection(sourceLetter, true);

            float distance = CalculateSilhouetteDistance(
                actualSilhouette,
                expectedSilhouette);
            if (distance > 0.05f)
            {
                throw new InvalidOperationException(
                    $"Cyrillic Б prefab front projection differs from its readable source " +
                    $"silhouette: distance {distance:F4}, maximum 0.0500");
            }

            AssertVerticalStemIsOnLeft(
                actualSilhouette,
                BookIconSize,
                BookIconSize,
                "Cyrillic Б prefab front projection");
        }

        private static void TestExtensionColliderEnvelopes()
        {
            foreach (LetterCase letter in Letters.Where(
                         candidate => candidate.Source == NeonSymbolSource.ExtensionGlb))
            {
                GameObject prefab = LoadRequiredAsset<GameObject>(letter.PrefabAssetPath);
                Transform generatedLetter = RequireDirectChild(
                    prefab.transform,
                    letter.LetterIngredientName);
                Bounds visualBounds = CalculateRenderedBounds(
                    generatedLetter.GetComponentsInChildren<Renderer>(true));
                Vector3 colliderSize = new Vector3(
                    visualBounds.size.x,
                    visualBounds.size.y,
                    Mathf.Max(visualBounds.size.z, MinimumRuntimeColliderDepth));
                Bounds colliderBounds = new Bounds(visualBounds.center, colliderSize);

                AssertFiniteVector(visualBounds.center, $"{letter.Letter} visual center");
                AssertFiniteVector(colliderSize, $"{letter.Letter} runtime collider size");
                AssertApproximately(
                    visualBounds.size.x,
                    colliderBounds.size.x,
                    GeometryTolerance,
                    $"{letter.Letter} collider width fit");
                AssertApproximately(
                    visualBounds.size.y,
                    colliderBounds.size.y,
                    GeometryTolerance,
                    $"{letter.Letter} collider height fit");
                AssertApproximately(
                    Mathf.Max(visualBounds.size.z, MinimumRuntimeColliderDepth),
                    colliderBounds.size.z,
                    GeometryTolerance,
                    $"{letter.Letter} collider depth fit");
                AssertApproximately(
                    0.0f,
                    visualBounds.center.x,
                    GeometryTolerance,
                    $"{letter.Letter} collider horizontal center");
                AssertApproximately(
                    0.0f,
                    visualBounds.center.z,
                    GeometryTolerance,
                    $"{letter.Letter} collider depth center");

                if (!colliderBounds.Contains(visualBounds.min) ||
                    !colliderBounds.Contains(visualBounds.max))
                {
                    throw new InvalidOperationException(
                        $"{letter.Letter} runtime collider must contain the complete visual bounds");
                }

                if (colliderSize.z > MaximumExtensionColliderDepth)
                {
                    throw new InvalidOperationException(
                        $"{letter.Letter} runtime collider depth {colliderSize.z:F4} exceeds " +
                        $"the {MaximumExtensionColliderDepth:F4} close-fit limit");
                }
            }
        }

        private static void TestLegacyAssetIdentity()
        {
            for (int index = 0; index < 26; index++)
            {
                char letter = (char)('A' + index);
                LetterCase symbol = Letters[index];
                AssertEqual(letter, symbol.Letter, $"legacy symbol at index {index}");
                AssertEqual(
                    $"NeonLetter_{letter}_Small",
                    symbol.PrefabName,
                    $"legacy {letter} prefab name");
                AssertEqual(
                    $"NeonLetter_{letter}_Small_Icon",
                    symbol.BookIconName,
                    $"legacy {letter} icon name");
                AssertEqual(
                    $"NeonLetters_Small_Page_{index / 2 + 1:00}",
                    GetBookPageName(index / 2),
                    $"legacy {letter} page name");
            }
        }

        private static void AssertVerticalStemIsOnLeft(
            bool[] silhouette,
            int width,
            int height,
            string description)
        {
            int leftMaximum = 0;
            int rightMaximum = 0;
            for (int x = 0; x < width; x++)
            {
                int occupiedPixels = 0;
                for (int y = 0; y < height; y++)
                {
                    if (silhouette[y * width + x])
                    {
                        occupiedPixels++;
                    }
                }

                if (x < width / 2)
                {
                    leftMaximum = Math.Max(leftMaximum, occupiedPixels);
                }
                else
                {
                    rightMaximum = Math.Max(rightMaximum, occupiedPixels);
                }
            }

            if (leftMaximum <= rightMaximum)
            {
                throw new InvalidOperationException(
                    $"{description} is mirrored: the continuous vertical stem must be on the " +
                    $"left, but left/right column occupancy is {leftMaximum}/{rightMaximum}");
            }
        }

        private static bool[] RasterizeFrontProjection(Transform geometry, bool mirrorX)
        {
            MeshFilter[] meshFilters = geometry.GetComponentsInChildren<MeshFilter>(true);
            var meshes = new List<ProjectedMesh>();
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            foreach (MeshFilter meshFilter in meshFilters)
            {
                Vector3[] sourceVertices = meshFilter.sharedMesh.vertices;
                var vertices = new Vector2[sourceVertices.Length];
                for (int index = 0; index < sourceVertices.Length; index++)
                {
                    Vector3 point = meshFilter.transform.TransformPoint(sourceVertices[index]);
                    vertices[index] = new Vector2(mirrorX ? -point.x : point.x, point.y);
                    minimum = Vector2.Min(minimum, vertices[index]);
                    maximum = Vector2.Max(maximum, vertices[index]);
                }

                meshes.Add(new ProjectedMesh(vertices, meshFilter.sharedMesh.triangles));
            }

            Vector2 size = maximum - minimum;
            if (size.x <= Mathf.Epsilon || size.y <= Mathf.Epsilon)
            {
                throw new InvalidOperationException(
                    $"{geometry.name} has no finite front-projected silhouette");
            }

            float drawableSize = BookIconSize - 24.0f;
            float scale = Mathf.Min(drawableSize / size.x, drawableSize / size.y);
            Vector2 contentSize = size * scale;
            Vector2 offset = new Vector2(
                (BookIconSize - contentSize.x) * 0.5f,
                (BookIconSize - contentSize.y) * 0.5f);
            var silhouette = new bool[BookIconSize * BookIconSize];

            foreach (ProjectedMesh mesh in meshes)
            {
                for (int index = 0; index < mesh.Triangles.Length; index += 3)
                {
                    Vector2 first =
                        (mesh.Vertices[mesh.Triangles[index]] - minimum) * scale + offset;
                    Vector2 second =
                        (mesh.Vertices[mesh.Triangles[index + 1]] - minimum) * scale + offset;
                    Vector2 third =
                        (mesh.Vertices[mesh.Triangles[index + 2]] - minimum) * scale + offset;
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
                    bool hasNegative = firstEdge < 0.0f || secondEdge < 0.0f ||
                        thirdEdge < 0.0f;
                    bool hasPositive = firstEdge > 0.0f || secondEdge > 0.0f ||
                        thirdEdge > 0.0f;
                    if (!(hasNegative && hasPositive))
                    {
                        silhouette[y * BookIconSize + x] = true;
                    }
                }
            }
        }

        private static float CalculateSilhouetteDistance(bool[] actual, bool[] expected)
        {
            AssertEqual(expected.Length, actual.Length, "front silhouette length");
            int difference = 0;
            int union = 0;
            for (int index = 0; index < actual.Length; index++)
            {
                if (actual[index] || expected[index])
                {
                    union++;
                }

                if (actual[index] != expected[index])
                {
                    difference++;
                }
            }

            return union == 0 ? 1.0f : (float)difference / union;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static void TestBookPage(int pageIndex)
        {
            LetterCase topLetter = Letters[pageIndex * 2];
            LetterCase bottomLetter = Letters[pageIndex * 2 + 1];
            string pageName = GetBookPageName(pageIndex);
            string pagePath = GetBookPageAssetPath(pageIndex);
            Texture2D page = LoadRequiredAsset<Texture2D>(pagePath);
            AssertTexture(page, pageName, 1024, 1024, 11, $"{topLetter.Letter}-{bottomLetter.Letter} page");

            Color32[] pixels = ReadSerializedDxt1Pixels(page);
            AssertLightCorners(
                pixels,
                page.width,
                page.height,
                $"{topLetter.Letter}-{bottomLetter.Letter} page");
            PixelBounds topBounds = FindCyanBounds(
                pixels,
                page.width,
                page.height,
                TopRecipeCard);
            PixelBounds bottomBounds = FindCyanBounds(
                pixels,
                page.width,
                page.height,
                BottomRecipeCard);

            AssertRecipeCardGlyph(topLetter, topBounds, TopRecipeCard, "top");
            AssertRecipeCardGlyph(bottomLetter, bottomBounds, BottomRecipeCard, "bottom");
            AssertRecipeCardMatchesCanonicalIcon(
                topLetter,
                pixels,
                page.width,
                page.height,
                TopRecipeCard,
                "top");
            AssertRecipeCardMatchesCanonicalIcon(
                bottomLetter,
                pixels,
                page.width,
                page.height,
                BottomRecipeCard,
                "bottom");

            ulong topSignature = CalculateCyanSignature(
                pixels,
                page.width,
                page.height,
                TopRecipeCard);
            ulong bottomSignature = CalculateCyanSignature(
                pixels,
                page.width,
                page.height,
                BottomRecipeCard);
            if (topSignature == bottomSignature)
            {
                throw new InvalidOperationException(
                    $"{topLetter.Letter}-{bottomLetter.Letter} page uses the same glyph " +
                    "in both recipe cards");
            }
        }

        private static void AssertRecipeCardGlyph(
            LetterCase letter,
            PixelBounds bounds,
            PixelRegion card,
            string slot)
        {
            if (bounds.Count < 200)
            {
                throw new InvalidOperationException(
                    $"{letter.Letter} {slot} recipe card must contain visible cyan art, " +
                    $"found {bounds.Count} pixels");
            }

            AssertBoundsInsideRegion(
                bounds,
                card.Inset(4),
                $"{letter.Letter} {slot} recipe-card glyph");
        }

        private static void AssertRecipeCardMatchesCanonicalIcon(
            LetterCase expectedLetter,
            Color32[] pagePixels,
            int pageWidth,
            int pageHeight,
            PixelRegion card,
            string slot)
        {
            byte[] actualSignature = CreateNormalizedCyanSignature(
                pagePixels,
                pageWidth,
                pageHeight,
                card.Inset(RecipeCardMargin));

            Texture2D expectedIcon =
                LoadRequiredAsset<Texture2D>(expectedLetter.BookIconAssetPath);
            byte[] expectedSignature = CreateNormalizedCyanSignature(
                ReadSerializedDxt1Pixels(expectedIcon),
                expectedIcon.width,
                expectedIcon.height,
                new PixelRegion(0, expectedIcon.width, 0, expectedIcon.height));
            float distance = CalculateSignatureDistance(actualSignature, expectedSignature);
            if (distance > MaximumCanonicalSignatureDistance)
            {
                throw new InvalidOperationException(
                    $"{expectedLetter.Letter} {slot} recipe card differs too much from its " +
                    $"canonical icon: distance {distance:F4}, maximum " +
                    $"{MaximumCanonicalSignatureDistance:F4}");
            }
        }

        private static byte[] CreateNormalizedCyanSignature(
            Color32[] pixels,
            int width,
            int height,
            PixelRegion region)
        {
            PixelBounds bounds = FindCyanBounds(pixels, width, height, region);
            if (bounds.Count == 0)
            {
                throw new InvalidOperationException(
                    "cannot create a canonical signature without cyan pixels");
            }

            int contentWidth = bounds.MaximumX - bounds.MinimumX + 1;
            int contentHeight = bounds.MaximumY - bounds.MinimumY + 1;
            var signature = new byte[CanonicalSignatureSize * CanonicalSignatureSize];
            for (int signatureY = 0; signatureY < CanonicalSignatureSize; signatureY++)
            {
                int minimumY = bounds.MinimumY +
                    signatureY * contentHeight / CanonicalSignatureSize;
                int maximumYExclusive = bounds.MinimumY +
                    (signatureY + 1) * contentHeight / CanonicalSignatureSize;
                maximumYExclusive = Math.Max(minimumY + 1, maximumYExclusive);

                for (int signatureX = 0; signatureX < CanonicalSignatureSize; signatureX++)
                {
                    int minimumX = bounds.MinimumX +
                        signatureX * contentWidth / CanonicalSignatureSize;
                    int maximumXExclusive = bounds.MinimumX +
                        (signatureX + 1) * contentWidth / CanonicalSignatureSize;
                    maximumXExclusive = Math.Max(minimumX + 1, maximumXExclusive);

                    int cyanPixels = 0;
                    int sampledPixels = 0;
                    for (int y = minimumY; y < maximumYExclusive; y++)
                    {
                        for (int x = minimumX; x < maximumXExclusive; x++)
                        {
                            sampledPixels++;
                            if (IsCyan(pixels[y * width + x]))
                            {
                                cyanPixels++;
                            }
                        }
                    }

                    signature[signatureY * CanonicalSignatureSize + signatureX] =
                        (byte)Mathf.RoundToInt(255.0f * cyanPixels / sampledPixels);
                }
            }

            return signature;
        }

        private static float CalculateSignatureDistance(byte[] first, byte[] second)
        {
            AssertEqual(first.Length, second.Length, "canonical signature length");
            long totalDifference = 0;
            for (int index = 0; index < first.Length; index++)
            {
                totalDifference += Math.Abs(first[index] - second[index]);
            }

            return totalDifference / (255.0f * first.Length);
        }

        private static void TestCanonicalEmissionMask()
        {
            Texture2D canonicalMask =
                LoadRequiredAsset<Texture2D>(SourceEmissionMaskPath);
            AssertEqual(
                SourceEmissionMaskPath,
                AssetDatabase.GetAssetPath(canonicalMask),
                "canonical emission mask path");

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("could not resolve Unity project root");
            string absoluteMaskPath = Path.Combine(
                projectRoot,
                SourceEmissionMaskPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absoluteMaskPath))
            {
                throw new InvalidOperationException(
                    $"canonical emission mask file is missing: {absoluteMaskPath}");
            }

            var readableMask = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!ImageConversion.LoadImage(
                        readableMask,
                        File.ReadAllBytes(absoluteMaskPath),
                        false))
                {
                    throw new InvalidOperationException(
                        $"canonical emission mask could not be decoded: {absoluteMaskPath}");
                }

                bool containsLuminousPixel = readableMask
                    .GetPixels32()
                    .Any(pixel => pixel.r > 0 || pixel.g > 0 || pixel.b > 0);
                if (!containsLuminousPixel)
                {
                    throw new InvalidOperationException(
                        "canonical emission mask is completely black");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readableMask);
            }
        }

        private static void TestBundleArtifact()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("could not resolve Unity project root");
            string bundlePath = Path.Combine(projectRoot, BundleRelativePath);
            FileInfo bundle = new FileInfo(bundlePath);
            if (!bundle.Exists || bundle.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Windows bundle is missing or empty: {bundlePath}");
            }

            AssertBundleWasBuiltForThisRun(bundle);
            if (!BuildPipeline.GetCRCForAssetBundle(bundlePath, out uint crc) || crc == 0)
            {
                throw new InvalidOperationException("Windows bundle has no valid CRC");
            }

            string manifestPath = $"{bundlePath}.manifest";
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    $"Windows bundle manifest is missing: {manifestPath}");
            }

            HashSet<string> expectedAssetPaths = CreateExpectedBundleAssetPaths();
            List<string> actualAssetPaths = ReadManifestAssetPaths(manifestPath);
            var actualAssetSet = new HashSet<string>(actualAssetPaths, StringComparer.Ordinal);

            if (actualAssetPaths.Count != actualAssetSet.Count)
            {
                throw new InvalidOperationException(
                    "Windows bundle manifest contains duplicate asset paths");
            }

            string[] missing = expectedAssetPaths.Except(actualAssetSet).OrderBy(path => path).ToArray();
            string[] unexpected = actualAssetSet.Except(expectedAssetPaths).OrderBy(path => path).ToArray();
            if (actualAssetSet.Count != ExpectedBundleAssetCount ||
                missing.Length > 0 ||
                unexpected.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Windows bundle manifest must contain exactly {ExpectedBundleAssetCount} " +
                    "symbol assets (80 prefabs, 80 icons, 40 pages); " +
                    $"found {actualAssetSet.Count}; missing [{string.Join(", ", missing)}]; " +
                    $"unexpected [{string.Join(", ", unexpected)}]");
            }
        }

        private static void TestRuntimeAssetReferences()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("could not resolve Unity project root");
            string bundlePath = Path.Combine(projectRoot, BundleRelativePath);
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                throw new InvalidOperationException(
                    $"Windows bundle could not be loaded for runtime-name checks: {bundlePath}");
            }

            try
            {
                foreach (LetterCase letter in Letters)
                {
                    RequireBundleAsset<GameObject>(bundle, letter.PrefabName);
                    Texture2D icon =
                        RequireBundleAsset<Texture2D>(bundle, letter.BookIconName);
                    AssertTexture(
                        icon,
                        letter.BookIconName,
                        128,
                        128,
                        8,
                        $"{letter.Letter} bundled book icon");
                }

                for (int pageIndex = 0; pageIndex < BookPageCount; pageIndex++)
                {
                    string pageName = GetBookPageName(pageIndex);
                    Texture2D page =
                        RequireBundleAsset<Texture2D>(bundle, pageName);
                    AssertTexture(
                        page,
                        pageName,
                        1024,
                        1024,
                        11,
                        $"{pageName} bundled book page");
                }
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static T RequireBundleAsset<T>(AssetBundle bundle, string assetName)
            where T : UnityEngine.Object
        {
            T asset = bundle.LoadAsset<T>(assetName);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Windows bundle does not resolve '{assetName}' as {typeof(T).Name}");
            }

            return asset;
        }

        private static void AssertBundleWasBuiltForThisRun(FileInfo bundle)
        {
            string epochText = Environment.GetEnvironmentVariable(BuildStartedEpochVariable);
            if (string.IsNullOrWhiteSpace(epochText) ||
                !long.TryParse(epochText, out long buildStartedEpoch))
            {
                throw new InvalidOperationException(
                    $"test runner did not set {BuildStartedEpochVariable}");
            }

            DateTime buildStartedUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(buildStartedEpoch);
            if (bundle.LastWriteTimeUtc < buildStartedUtc)
            {
                throw new InvalidOperationException(
                    $"Windows bundle is stale: written {bundle.LastWriteTimeUtc:O}, " +
                    $"test build started {buildStartedUtc:O}");
            }
        }

        private static HashSet<string> CreateExpectedBundleAssetPaths()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (LetterCase letter in Letters)
            {
                paths.Add(letter.PrefabAssetPath);
                paths.Add(letter.BookIconAssetPath);
            }

            for (int pageIndex = 0; pageIndex < BookPageCount; pageIndex++)
            {
                paths.Add(GetBookPageAssetPath(pageIndex));
            }

            AssertEqual(ExpectedBundleAssetCount, paths.Count, "expected bundle manifest set size");
            return paths;
        }

        private static List<string> ReadManifestAssetPaths(string manifestPath)
        {
            var paths = new List<string>();
            bool readingAssets = false;
            foreach (string line in File.ReadAllLines(manifestPath))
            {
                if (string.Equals(line, "Assets:", StringComparison.Ordinal))
                {
                    readingAssets = true;
                    continue;
                }

                if (!readingAssets)
                {
                    continue;
                }

                if (!line.StartsWith("- ", StringComparison.Ordinal))
                {
                    break;
                }

                paths.Add(line.Substring(2));
            }

            return paths;
        }

        private static string GetBookPageName(int pageIndex)
        {
            return $"NeonLetters_Small_Page_{pageIndex + 1:00}";
        }

        private static string GetBookPageAssetPath(int pageIndex)
        {
            return $"{GeneratedTextureFolder}/{GetBookPageName(pageIndex)}.asset";
        }

        private static T LoadRequiredAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"required {typeof(T).Name} asset is missing: {assetPath}");
            }

            return asset;
        }

        private static Transform RequireDirectChild(Transform root, string childName)
        {
            Transform match = null;
            int matchCount = 0;
            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                Transform child = root.GetChild(childIndex);
                if (!string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    continue;
                }

                match = child;
                matchCount++;
            }

            if (matchCount != 1)
            {
                throw new InvalidOperationException(
                    $"'{root.name}' must have exactly one direct child '{childName}', " +
                    $"but found {matchCount}");
            }

            return match;
        }

        private static Bounds CalculateRenderedBounds(Renderer[] renderers)
        {
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException("cannot calculate bounds without renderers");
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void AssertTexture(
            Texture2D texture,
            string expectedName,
            int expectedWidth,
            int expectedHeight,
            int expectedMipCount,
            string description)
        {
            AssertEqual(expectedName, texture.name, $"{description} name");
            AssertEqual(expectedWidth, texture.width, $"{description} width");
            AssertEqual(expectedHeight, texture.height, $"{description} height");
            AssertEqual(TextureFormat.DXT1, texture.format, $"{description} format");
            AssertEqual(expectedMipCount, texture.mipmapCount, $"{description} mip count");
            AssertEqual(false, texture.isReadable, $"{description} CPU readability");
            AssertEqual(FilterMode.Bilinear, texture.filterMode, $"{description} filter mode");
            AssertEqual(TextureWrapMode.Clamp, texture.wrapMode, $"{description} wrap mode");
            AssertEqual(1, texture.anisoLevel, $"{description} anisotropic level");

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(assetPath))
            {
                AssertSerializedTextureField(
                    assetPath,
                    "m_IsReadable",
                    "0",
                    $"{description} serialized CPU readability");
                AssertSerializedTextureField(
                    assetPath,
                    "m_StreamingMipmaps",
                    "0",
                    $"{description} streaming mipmaps");
                AssertSerializedTextureField(
                    assetPath,
                    "m_ColorSpace",
                    "0",
                    $"{description} color space");
            }
        }

        private static Color32[] ReadSerializedDxt1Pixels(Texture2D texture)
        {
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new InvalidOperationException(
                    $"Texture '{texture.name}' has no serialized asset path.");
            }

            string payloadHex = ReadSerializedTextureField(
                assetPath,
                "_typelessdata");
            if (payloadHex.Length % 2 != 0)
            {
                throw new InvalidOperationException(
                    $"Texture '{texture.name}' has an odd-length serialized payload.");
            }

            var payload = new byte[payloadHex.Length / 2];
            for (int index = 0; index < payload.Length; index++)
            {
                payload[index] = (byte)(
                    ReadHexNibble(payloadHex[index * 2]) << 4 |
                    ReadHexNibble(payloadHex[index * 2 + 1]));
            }

            var readableTexture = new Texture2D(
                texture.width,
                texture.height,
                TextureFormat.DXT1,
                texture.mipmapCount > 1,
                false);
            try
            {
                readableTexture.LoadRawTextureData(payload);
                readableTexture.Apply(false, false);
                return readableTexture.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readableTexture);
            }
        }

        private static int ReadHexNibble(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            throw new InvalidOperationException(
                $"Serialized texture payload contains invalid hex character '{value}'.");
        }

        private static void AssertSerializedTextureField(
            string assetPath,
            string fieldName,
            string expectedValue,
            string description)
        {
            AssertEqual(
                expectedValue,
                ReadSerializedTextureField(assetPath, fieldName),
                description);
        }

        private static string ReadSerializedTextureField(
            string assetPath,
            string fieldName)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("could not resolve Unity project root");
            string absolutePath = Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
            string prefix = $"  {fieldName}: ";
            string line = File.ReadLines(absolutePath)
                .SingleOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
            if (line == null)
            {
                throw new InvalidOperationException(
                    $"Texture asset '{assetPath}' has no serialized field '{fieldName}'.");
            }

            return line.Substring(prefix.Length);
        }

        private static void AssertLightCorners(
            Color32[] pixels,
            int width,
            int height,
            string description)
        {
            AssertLightBackground(pixels[0], $"{description} bottom-left corner");
            AssertLightBackground(pixels[width - 1], $"{description} bottom-right corner");
            AssertLightBackground(
                pixels[(height - 1) * width],
                $"{description} top-left corner");
            AssertLightBackground(
                pixels[height * width - 1],
                $"{description} top-right corner");
        }

        private static void AssertLightBackground(Color32 pixel, string description)
        {
            if (pixel.r < 190 || pixel.g < 190 || pixel.b < 180)
            {
                throw new InvalidOperationException(
                    $"{description} must remain light, " +
                    $"got RGB({pixel.r}, {pixel.g}, {pixel.b})");
            }
        }

        private static PixelBounds FindCyanBounds(
            Color32[] pixels,
            int width,
            int height,
            PixelRegion region)
        {
            region.AssertFits(width, height);
            var bounds = new PixelBounds(width, height);
            for (int y = region.MinimumY; y < region.MaximumYExclusive; y++)
            {
                for (int x = region.MinimumX; x < region.MaximumXExclusive; x++)
                {
                    if (IsCyan(pixels[y * width + x]))
                    {
                        bounds.Include(x, y);
                    }
                }
            }

            return bounds;
        }

        private static ulong CalculateCyanSignature(
            Color32[] pixels,
            int width,
            int height,
            PixelRegion region)
        {
            region.AssertFits(width, height);
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            unchecked
            {
                for (int y = region.MinimumY; y < region.MaximumYExclusive; y++)
                {
                    for (int x = region.MinimumX; x < region.MaximumXExclusive; x++)
                    {
                        hash ^= IsCyan(pixels[y * width + x]) ? (byte)1 : (byte)0;
                        hash *= prime;
                    }
                }
            }

            return hash;
        }

        private static bool IsCyan(Color32 pixel)
        {
            return pixel.g > 180 && pixel.b > 180 &&
                   pixel.g > pixel.r + 20 && pixel.b > pixel.r + 20;
        }

        private static void AssertBoundsInsideRegion(
            PixelBounds bounds,
            PixelRegion region,
            string description)
        {
            if (bounds.MinimumX < region.MinimumX ||
                bounds.MaximumX >= region.MaximumXExclusive ||
                bounds.MinimumY < region.MinimumY ||
                bounds.MaximumY >= region.MaximumYExclusive)
            {
                throw new InvalidOperationException(
                    $"{description} must fit inside " +
                    $"x[{region.MinimumX}, {region.MaximumXExclusive}) " +
                    $"y[{region.MinimumY}, {region.MaximumYExclusive}), but spans " +
                    $"x[{bounds.MinimumX}, {bounds.MaximumX}] " +
                    $"y[{bounds.MinimumY}, {bounds.MaximumY}]");
            }
        }

        private static void AssertMaterialProperty(Material material, string propertyName)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"material '{material.name}' has no required property '{propertyName}'");
            }
        }

        private static void AssertApproximately(
            float expected,
            float actual,
            float tolerance,
            string description)
        {
            if (Mathf.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    $"{description}: expected {expected:F4} +/- {tolerance:F4}, " +
                    $"got {actual:F4}");
            }
        }

        private static void AssertFinitePositive(float actual, string description)
        {
            if (float.IsNaN(actual) || float.IsInfinity(actual) || actual <= 0.0f)
            {
                throw new InvalidOperationException(
                    $"{description} must be finite and positive, got {actual}");
            }
        }

        private static void AssertFiniteVector(Vector3 actual, string description)
        {
            if (float.IsNaN(actual.x) || float.IsInfinity(actual.x) ||
                float.IsNaN(actual.y) || float.IsInfinity(actual.y) ||
                float.IsNaN(actual.z) || float.IsInfinity(actual.z))
            {
                throw new InvalidOperationException(
                    $"{description} must contain only finite values, got {actual}");
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"{description}: expected '{expected}', got '{actual}'");
            }
        }

        private static LetterCase[] CreateCases()
        {
            IReadOnlyList<NeonSymbolManifestEntry> manifest = NeonSymbolManifest.All;
            var cases = new LetterCase[manifest.Count];
            for (int index = 0; index < manifest.Count; index++)
            {
                cases[index] = new LetterCase(manifest[index]);
            }

            return cases;
        }

        private sealed class LetterCase
        {
            public LetterCase(NeonSymbolManifestEntry manifestEntry)
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
            public string SourceModelPath => Source == NeonSymbolSource.LegacyDae
                ? NeonAlphabetAssetTests.SourceModelPath
                : ExtensionSourceModelPath;
            public string PrefabName => $"NeonLetter_{AssetKey}_Small";
            public string PrefabAssetPath =>
                $"{GeneratedPrefabFolder}/{PrefabName}.prefab";
            public string LetterIngredientName => $"Ingredient_LightBulb_{AssetKey}";
            public string BookIconName => $"NeonLetter_{AssetKey}_Small_Icon";
            public string BookIconAssetPath =>
                $"{GeneratedTextureFolder}/{BookIconName}.asset";
        }

        private sealed class PixelBounds
        {
            public PixelBounds(int width, int height)
            {
                MinimumX = width;
                MinimumY = height;
                MaximumX = -1;
                MaximumY = -1;
            }

            public int MinimumX { get; private set; }
            public int MinimumY { get; private set; }
            public int MaximumX { get; private set; }
            public int MaximumY { get; private set; }
            public int Count { get; private set; }

            public void Include(int x, int y)
            {
                MinimumX = Math.Min(MinimumX, x);
                MinimumY = Math.Min(MinimumY, y);
                MaximumX = Math.Max(MaximumX, x);
                MaximumY = Math.Max(MaximumY, y);
                Count++;
            }
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

        private readonly struct PixelRegion
        {
            public PixelRegion(
                int minimumX,
                int maximumXExclusive,
                int minimumY,
                int maximumYExclusive)
            {
                MinimumX = minimumX;
                MaximumXExclusive = maximumXExclusive;
                MinimumY = minimumY;
                MaximumYExclusive = maximumYExclusive;
            }

            public int MinimumX { get; }
            public int MaximumXExclusive { get; }
            public int MinimumY { get; }
            public int MaximumYExclusive { get; }

            public PixelRegion Inset(int pixels)
            {
                return new PixelRegion(
                    MinimumX + pixels,
                    MaximumXExclusive - pixels,
                    MinimumY + pixels,
                    MaximumYExclusive - pixels);
            }

            public void AssertFits(int width, int height)
            {
                if (MinimumX < 0 || MinimumY < 0 ||
                    MaximumXExclusive > width || MaximumYExclusive > height ||
                    MinimumX >= MaximumXExclusive || MinimumY >= MaximumYExclusive)
                {
                    throw new InvalidOperationException(
                        $"pixel region x[{MinimumX}, {MaximumXExclusive}) " +
                        $"y[{MinimumY}, {MaximumYExclusive}) does not fit {width}x{height}");
                }
            }
        }
    }
}
