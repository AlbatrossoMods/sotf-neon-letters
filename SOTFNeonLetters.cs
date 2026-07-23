using RedLoader;
using SonsSdk;

namespace SOTFNeonLetters;

public class SOTFNeonLetters : SonsMod
{
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
        NeonLetterMultiplayerRuntime.Initialize();
        Config.Init();
    }

    protected override void OnSdkInitialized()
    {
        RLog.Msg("[SOTFNeonLetters] SDK initialization started.");
        try
        {
            SOTFNeonLettersUi.Create();
            RLog.Msg("[SOTFNeonLetters] UI initialization completed.");

            NeonLetterColorRuntime.Initialize();
            RLog.Msg("[SOTFNeonLetters] Color editing initialized.");

            NeonLetterSmallBlueprint.Register();
            RLog.Msg("[SOTFNeonLetters] A-Z blueprint registration prepared.");

            NeonLetterMultiplayerSaveRuntime.Initialize();
            RLog.Msg("[SOTFNeonLetters] Multiplayer persistence initialized.");
        }
        catch (Exception exception)
        {
            RLog.Error($"[SOTFNeonLetters] SDK initialization failed: {exception}");
            throw;
        }

        // Add in-game settings ui for your mod.
        // SettingsRegistry.CreateSettings(this, null, typeof(Config));
    }

    protected override void OnGameStart()
    {
        // This is called once the player spawns in the world and gains control.
    }

}
