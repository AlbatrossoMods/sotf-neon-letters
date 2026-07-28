using RedLoader;
using SonsSdk;

namespace SOTFNeonLetters;

public class SOTFNeonLetters : SonsMod
{
    private readonly NeonLetterLifecycleCoordinator _lifecycle = new();

    public SOTFNeonLetters()
    {

        // Uncomment any of these if you need a method to run on a specific update loop.
        //OnUpdateCallback = MyUpdateMethod;
        //OnLateUpdateCallback = MyLateUpdateMethod;
        //OnFixedUpdateCallback = MyFixedUpdateMethod;
        //OnGUICallback = MyGUIMethod;

        // Uncomment this to automatically apply harmony patches in your assembly.
        HarmonyPatchAll = true;
}
    protected override void OnInitializeMod()
    {
        RLog.Msg("[SOTFNeonLetters] Mod initialization started.");
        try
        {
            NeonLetterMultiplayerRuntime.Initialize();
            _lifecycle.CompleteStage(NeonLetterMultiplayerRuntime.Deinitialize);
            Config.Init();
        }
        catch
        {
            CleanupReversibleStages();
            throw;
        }
    }

    protected override void OnSdkInitialized()
    {
        RLog.Msg("[SOTFNeonLetters] SDK initialization started.");
        try
        {
            SOTFNeonLettersUi.Create();
            _lifecycle.CompleteStage(SOTFNeonLettersUi.Destroy);
            RLog.Msg("[SOTFNeonLetters] UI initialization completed.");

            NeonLetterColorRuntime.Initialize();
            _lifecycle.CompleteStage(NeonLetterColorRuntime.Deinitialize);
            RLog.Msg("[SOTFNeonLetters] Color editing initialized.");

            NeonLetterSmallBlueprint.Register();
            _lifecycle.CompleteStage(NeonLetterSmallBlueprint.Deinitialize);
            RLog.Msg("[SOTFNeonLetters] A-Z blueprint registration prepared.");

            NeonLetterMultiplayerSaveRuntime.Initialize();
            _lifecycle.CompleteStage(
                NeonLetterMultiplayerSaveRuntime.Deinitialize);
            RLog.Msg("[SOTFNeonLetters] Multiplayer persistence initialized.");
        }
        catch (Exception exception)
        {
            try
            {
                RLog.Error(
                    $"[SOTFNeonLetters] SDK initialization failed: " +
                    exception);
            }
            catch
            {
                // Initialization rollback must not depend on error logging.
            }

            CleanupReversibleStages();
            throw;
        }

        // Add in-game settings ui for your mod.
        // SettingsRegistry.CreateSettings(this, null, typeof(Config));
    }

    protected override void OnGameStart()
    {
        // This is called once the player spawns in the world and gains control.
    }

    protected override void OnDeinitializeMod()
    {
        CleanupReversibleStages();
    }

    private void CleanupReversibleStages()
    {
        SOTFNeonLettersUi.Destroy();
        _lifecycle.Cleanup(
            exception => RLog.Error(
                $"[SOTFNeonLetters] Reversible lifecycle cleanup failed: " +
                exception));
    }

}
