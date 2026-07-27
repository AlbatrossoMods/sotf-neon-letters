#nullable enable

namespace SOTFNeonLetters;

public readonly record struct WallMountedVisualDepthLayout(
    float OutwardTranslation,
    float MinimumDepth,
    float MaximumDepth)
{
    public float TranslateDepth(float originalDepth)
    {
        if (!float.IsFinite(originalDepth))
        {
            throw new ArgumentOutOfRangeException(
                nameof(originalDepth),
                "A wall-mounted visual depth must be finite before it can be translated.");
        }

        float translatedDepth = originalDepth + OutwardTranslation;
        if (!float.IsFinite(translatedDepth))
        {
            throw new InvalidOperationException(
                "The translated wall-mounted visual depth must be finite.");
        }

        return translatedDepth;
    }
}

public static class WallMountedVisualDepthPolicy
{
    // StructureRecipe.AnchorType.Back aligns the prefab's positive local Z
    // with the acquired outward wall normal, so wall-mounted geometry needs
    // a positive minimum Z to remain visibly clear of the supporting surface.
    public const float SurfaceClearance = 0.01f;

    public static WallMountedVisualDepthLayout Resolve(
        float minimumDepth,
        float maximumDepth)
    {
        if (!float.IsFinite(minimumDepth) ||
            !float.IsFinite(maximumDepth) ||
            maximumDepth <= minimumDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                "Wall-mounted visual depth bounds must be finite and have positive size.");
        }

        float outwardTranslation = SurfaceClearance - minimumDepth;
        return new WallMountedVisualDepthLayout(
            outwardTranslation,
            minimumDepth + outwardTranslation,
            maximumDepth + outwardTranslation);
    }

    public static float ResolveColliderCenterDepth(
        float visualCenterDepth,
        float colliderDepth)
    {
        if (!float.IsFinite(visualCenterDepth))
        {
            throw new ArgumentOutOfRangeException(
                nameof(visualCenterDepth),
                "A wall-mounted collider must start from a finite visual center depth.");
        }

        if (!float.IsFinite(colliderDepth) || colliderDepth <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(colliderDepth),
                "A wall-mounted collider depth must be finite and positive.");
        }

        float minimumCenterDepth =
            SurfaceClearance + colliderDepth / 2f;
        return Math.Max(visualCenterDepth, minimumCenterDepth);
    }
}

internal sealed class WallMountedVisualDepthMutation<TTarget>
    where TTarget : class
{
    private const float DepthValidationTolerance = 0.0001f;

    private readonly TTarget[] _targets;
    private readonly float[] _originalDepths;
    private readonly WallMountedVisualDepthLayout _layout;
    private readonly Func<TTarget, float> _readDepth;
    private readonly Action<TTarget, float> _writeDepth;

    public WallMountedVisualDepthMutation(
        IReadOnlyList<TTarget> targets,
        WallMountedVisualDepthLayout layout,
        Func<TTarget, float> readDepth,
        Action<TTarget, float> writeDepth)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(readDepth);
        ArgumentNullException.ThrowIfNull(writeDepth);
        if (targets.Count == 0)
        {
            throw new ArgumentException(
                "At least one wall-mounted prefab visual is required.",
                nameof(targets));
        }

        _targets = new TTarget[targets.Count];
        _originalDepths = new float[targets.Count];
        _layout = layout;
        _readDepth = readDepth;
        _writeDepth = writeDepth;

        for (int index = 0; index < targets.Count; index++)
        {
            TTarget target = targets[index] ??
                throw new ArgumentException(
                    $"Wall-mounted prefab visual at index {index} is null.",
                    nameof(targets));
            float originalDepth = readDepth(target);
            if (!float.IsFinite(originalDepth))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targets),
                    $"Wall-mounted prefab visual at index {index} has a non-finite local depth.");
            }

            _targets[index] = target;
            _originalDepths[index] = originalDepth;
        }
    }

    public void Apply()
    {
        for (int index = 0; index < _targets.Length; index++)
        {
            float expectedDepth =
                _layout.TranslateDepth(_originalDepths[index]);
            _writeDepth(_targets[index], expectedDepth);
            float actualDepth = _readDepth(_targets[index]);
            if (!float.IsFinite(actualDepth) ||
                Math.Abs(actualDepth - expectedDepth) >
                DepthValidationTolerance)
            {
                throw new InvalidOperationException(
                    $"Wall-mounted prefab visual at index {index} did not retain its outward " +
                    $"wall-depth translation; expected {expectedDepth:F4}, but found " +
                    $"{actualDepth:F4}.");
            }
        }
    }

    public void Restore()
    {
        for (int index = 0; index < _targets.Length; index++)
        {
            _writeDepth(_targets[index], _originalDepths[index]);
        }
    }
}
