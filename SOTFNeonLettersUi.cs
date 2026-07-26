using RedLoader;
using SUI;
using SonsSdk.Networking;
using UnityEngine;
using static SUI.SUI;

namespace SOTFNeonLetters;

public static class SOTFNeonLettersUi
{
    private const string PanelId = "SOTFNeonLetters.ColorEditor";
    private static SPanelOptions _panel;
    private static SColorWheelOptions _colorWheel;
    private static SContainerOptions _previewSwatch;
    private static SLabelOptions _hexLabel;
    private static readonly NeonLetterColorEditorSession<NeonLetterColorTarget>
        EditorSession = new();
    private static readonly NeonLetterUiDestroyCoordinator<NeonLetterColorTarget>
        DestroyCoordinator = new();
    private static bool _created;
    private static bool _updatingWheel;

    internal static bool IsOpen => EditorSession.Target != null;

    public static void Create()
    {
        if (_created ||
            NetUtils.IsDedicatedServer ||
            Application.isBatchMode)
        {
            return;
        }

        DestroyCoordinator.Begin();
        try
        {
            _panel = RegisterNewPanel(PanelId, enableInput: true);
            _panel
                .Dock(EDockType.Fill)
                .Background(new Color(0f, 0f, 0f, 0.72f), EBackground.None);

            SContainerOptions card = SVertical
                .Anchor(AnchorType.MiddleCenter)
                .Pivot(0.5f, 0.5f)
                .Size(520f, 650f)
                .Padding(36f)
                .Background(
                    new Color(0.07f, 0.08f, 0.10f, 0.98f),
                    EBackground.Round28);

            _colorWheel = SColorWheel
                .Size(360f, 360f)
                .PWidth(360f)
                .PHeight(360f)
                .MWidth(360f)
                .MHeight(360f)
                .BgActive(false)
                .Notify(OnColorChanged);
            _previewSwatch = SContainer
                .Height(48f)
                .Background(
                    ToUnityColor(NeonRgba.ProjectCyan),
                    EBackground.Round10);
            _hexLabel = SLabel
                .Text(NeonLetterColorFormatting.ToHex(NeonRgba.ProjectCyan))
                .FontSize(24);

            SContainerOptions buttons = SHorizontal
                .Height(56f)
                .Add(SButton.Text("APPLY").Notify(Apply))
                .Add(SButton.Text("CANCEL").Notify(Cancel))
                .Add(SButton.Text("RESET").Notify(Reset));

            card
                .Add(SLabel.Text("NEON LETTER COLOR").FontSize(30))
                .Add(_colorWheel)
                .Add(_previewSwatch)
                .Add(_hexLabel)
                .Add(buttons);
            _panel.Add(card);
            _panel.Active(false);
            _created = true;
        }
        catch
        {
            Destroy();
            throw;
        }
    }

    internal static void Destroy()
    {
        DestroyCoordinator.Destroy(
            EditorSession,
            (target, color) => target.PreviewColor(color),
            ClosePanel,
            () => RemovePanel(PanelId),
            ResetUiState,
            LogDestroyError);
    }

    public static void Open(NeonLetterColorTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_created)
        {
            throw new InvalidOperationException("The neon letter color UI is not initialized.");
        }

        try
        {
            EditorSession.Open(target, target.CurrentColor);
            SetPreview(EditorSession.Editor.Preview, updateWheel: true);
            TogglePanel(PanelId, show: true);
        }
        catch (Exception exception)
        {
            RLog.Error($"[SOTFNeonLetters] Failed to open color editor: {exception}");
            AbortEditorBestEffort("opening the color editor");
        }
    }

    public static void OnWorldExited()
    {
        NeonLetterColorTarget target = EditorSession.Target;
        NeonLetterColorTargetLoss targetLoss = EditorSession.ExitWorld();
        try
        {
            if (targetLoss.ShouldRollback && target != null)
            {
                target.PreviewColor(targetLoss.RollbackColor);
            }
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to roll back color on world exit: " +
                $"{exception}");
        }
        finally
        {
            ClosePanel();
        }
    }

    internal static void OnDismantled(int structureInstanceId)
    {
        OnStructureUnavailable(structureInstanceId);
    }

    internal static void OnStructureUnavailable(int structureInstanceId)
    {
        NeonLetterColorTarget target = EditorSession.Target;
        if (target == null ||
            target.StructureInstanceId != structureInstanceId)
        {
            return;
        }

        EditorSession.Close();
        ClosePanel();
    }

    private static void OnColorChanged(Color color)
    {
        if (_updatingWheel ||
            EditorSession.Editor == null ||
            EditorSession.Target == null)
        {
            return;
        }

        RunCallback(
            "previewing a color",
            () =>
            {
                var selectedColor = new NeonRgba(color.r, color.g, color.b, color.a);
                EditorSession.Editor.SetPreview(selectedColor);
                SetPreview(selectedColor, updateWheel: false);
                EditorSession.Target.PreviewColor(selectedColor);
            },
            closeOnSuccess: false);
    }

    private static void Apply()
    {
        if (EditorSession.Editor == null || EditorSession.Target == null)
        {
            return;
        }

        RunCallback(
            "applying a color",
            () =>
            {
                NeonLetterColorDecision decision = EditorSession.Editor.Apply();
                EditorSession.Target.CommitColor(decision.Color);
            },
            closeOnSuccess: true);
    }

    private static void Cancel()
    {
        if (EditorSession.Editor == null || EditorSession.Target == null)
        {
            return;
        }

        RunCallback(
            "cancelling color editing",
            () =>
            {
                NeonLetterColorDecision decision = EditorSession.Editor.Cancel();
                EditorSession.Target.PreviewColor(decision.Color);
            },
            closeOnSuccess: true);
    }

    private static void Reset()
    {
        if (EditorSession.Editor == null || EditorSession.Target == null)
        {
            return;
        }

        RunCallback(
            "resetting the color",
            () =>
            {
                EditorSession.Editor.Reset();
                SetPreview(EditorSession.Editor.Preview, updateWheel: true);
                EditorSession.Target.PreviewColor(EditorSession.Editor.Preview);
            },
            closeOnSuccess: false);
    }

    private static void SetPreview(NeonRgba color, bool updateWheel)
    {
        Color unityColor = ToUnityColor(color);
        if (updateWheel)
        {
            _updatingWheel = true;
            try
            {
                _colorWheel.Value(unityColor);
            }
            finally
            {
                _updatingWheel = false;
            }
        }

        _previewSwatch.Background(unityColor, EBackground.Round10);
        _hexLabel.Text(NeonLetterColorFormatting.ToHex(color));
    }

    private static void RunCallback(
        string action,
        Action callback,
        bool closeOnSuccess)
    {
        try
        {
            callback();
            if (closeOnSuccess)
            {
                CompleteEditor();
            }
        }
        catch (Exception exception)
        {
            RLog.Error($"[SOTFNeonLetters] Failed while {action}: {exception}");
            AbortEditorBestEffort(action);
        }
    }

    private static void CompleteEditor()
    {
        EditorSession.Close();
        ClosePanel();
    }

    private static void AbortEditorBestEffort(string action)
    {
        NeonLetterColorTarget target = EditorSession.Target;
        NeonLetterColorTargetLoss targetLoss = EditorSession.ExitWorld();
        try
        {
            if (targetLoss.ShouldRollback && target != null)
            {
                target.PreviewColor(targetLoss.RollbackColor);
            }
        }
        catch (Exception rollbackException)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to roll back color after {action}: " +
                $"{rollbackException}");
        }
        finally
        {
            ClosePanel();
        }
    }

    private static void ClosePanel()
    {
        try
        {
            TogglePanel(PanelId, show: false);
        }
        catch (Exception exception)
        {
            RLog.Error($"[SOTFNeonLetters] Failed to hide color panel: {exception}");
        }

        try
        {
            _panel?.Active(false);
        }
        catch (Exception exception)
        {
            RLog.Error($"[SOTFNeonLetters] Failed to deactivate color panel: {exception}");
        }
    }

    private static void ResetUiState()
    {
        _panel = null;
        _colorWheel = null;
        _previewSwatch = null;
        _hexLabel = null;
        _created = false;
        _updatingWheel = false;
    }

    private static void LogDestroyError(Exception exception)
    {
        RLog.Error(
            $"[SOTFNeonLetters] Failed to tear down color editor UI: " +
            exception);
    }

    private static Color ToUnityColor(NeonRgba color)
    {
        return new Color(color.Red, color.Green, color.Blue, color.Alpha);
    }
}
