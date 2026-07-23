#nullable enable

namespace SOTFNeonLetters;

public interface IEmissionVisualSubtree
{
    string Name { get; }
    IReadOnlyList<IEmissionRenderer> Renderers { get; }
}

public interface IEmissionRenderer
{
    string Name { get; }
    IReadOnlyList<IEmissionMaterial> SharedMaterials { get; }

    IEmissionPropertyBlock ReadPropertyBlock(int materialIndex);
    void WritePropertyBlock(int materialIndex, IEmissionPropertyBlock propertyBlock);
}

public interface IEmissionMaterial
{
    float ReadEmissiveIntensity();
}

public interface IEmissionPropertyBlock
{
    void SetColor(string propertyName, NeonRgba color);
}

public static class NeonLetterEmissionPolicy
{
    public const string EmissiveColorPropertyName = "_EmissiveColor";

    public static void Apply(
        NeonLetterSmallDefinition definition,
        IReadOnlyList<IEmissionVisualSubtree> candidateSubtrees,
        NeonRgba selectedColor)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(candidateSubtrees);

        IEmissionVisualSubtree[] matches = candidateSubtrees
            .Where(subtree =>
                subtree != null &&
                string.Equals(
                    subtree.Name,
                    definition.ColliderVisualChildName,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one visual subtree named " +
                $"'{definition.ColliderVisualChildName}', but found {matches.Length}.");
        }

        IEmissionVisualSubtree selectedSubtree = matches[0];
        IReadOnlyList<IEmissionRenderer> renderers = selectedSubtree.Renderers;
        if (renderers == null || renderers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Visual subtree '{selectedSubtree.Name}' has no renderers.");
        }

        var preparedWrites = new List<(
            IEmissionRenderer Renderer,
            int MaterialIndex,
            IEmissionPropertyBlock PropertyBlock,
            NeonRgba Color)>();
        foreach (IEmissionRenderer renderer in renderers)
        {
            if (renderer == null)
            {
                throw new InvalidOperationException(
                    $"Visual subtree '{selectedSubtree.Name}' has a null renderer.");
            }

            IReadOnlyList<IEmissionMaterial> sharedMaterials = renderer.SharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.Name}' has no shared materials.");
            }

            for (int materialIndex = 0; materialIndex < sharedMaterials.Count; materialIndex++)
            {
                IEmissionMaterial material = sharedMaterials[materialIndex];
                if (material == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.Name}' has a null shared material at slot " +
                        $"{materialIndex}.");
                }

                float intensity = material.ReadEmissiveIntensity();
                if (!float.IsFinite(intensity))
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.Name}' material slot {materialIndex} requires a " +
                        $"finite positive emissive intensity, but got {intensity}.");
                }

                if (intensity <= 0f)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.Name}' material slot {materialIndex} has " +
                        $"non-positive emissive intensity {intensity}.");
                }

                NeonRgba emissiveColor = new(
                    SrgbToLinear(selectedColor.Red) * intensity,
                    SrgbToLinear(selectedColor.Green) * intensity,
                    SrgbToLinear(selectedColor.Blue) * intensity,
                    selectedColor.Alpha);
                IEmissionPropertyBlock propertyBlock =
                    renderer.ReadPropertyBlock(materialIndex);
                if (propertyBlock == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.Name}' has no property block for material slot " +
                        $"{materialIndex}.");
                }

                preparedWrites.Add((
                    renderer,
                    materialIndex,
                    propertyBlock,
                    emissiveColor));
            }
        }

        foreach (var preparedWrite in preparedWrites)
        {
            preparedWrite.PropertyBlock.SetColor(
                EmissiveColorPropertyName,
                preparedWrite.Color);
            preparedWrite.Renderer.WritePropertyBlock(
                preparedWrite.MaterialIndex,
                preparedWrite.PropertyBlock);
        }
    }

    private static float SrgbToLinear(float component)
    {
        return component <= 0.04045f
            ? component / 12.92f
            : MathF.Pow((component + 0.055f) / 1.055f, 2.4f);
    }
}
