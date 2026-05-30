using Candide.MainMenu;
using Candide.Scene;
using Candide.Scenes.Intro;
using HarmonyLib;
using Romestead.ModLoader;

namespace Romestead.SkipIntroMod;

[ModManifest("romestead.skip-intro", "Skip Intro", "0.1.0", SyncMode = MultiplayerSyncMode.ClientOnly)]
public sealed class SkipIntroMod : IRomesteadMod
{
    public void Initialize(IModContext context)
    {
        var harmony = new Harmony("romestead.skip-intro");
        harmony.PatchAll(typeof(SkipIntroMod).Assembly);
        context.Logger.Info("Skip Intro mod ready. Startup intro will be bypassed.");
    }
}

[HarmonyPatch(typeof(IntroScene), nameof(IntroScene.LoadContent))]
internal static class IntroSceneLoadContentPatch
{
    private static bool Prefix() => false;
}

[HarmonyPatch(typeof(IntroScene), nameof(IntroScene.OnSceneActivated))]
internal static class IntroSceneOnSceneActivatedPatch
{
    private static bool Prefix()
    {
        GameSceneManager.SetCurrentScene<MainMenu>();
        return false;
    }
}

[HarmonyPatch(typeof(IntroScene), nameof(IntroScene.Update))]
internal static class IntroSceneUpdatePatch
{
    private static bool Prefix() => false;
}

[HarmonyPatch(typeof(IntroScene), nameof(IntroScene.Draw))]
internal static class IntroSceneDrawPatch
{
    private static bool Prefix() => false;
}
