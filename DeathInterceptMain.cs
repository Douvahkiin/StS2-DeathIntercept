using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace DeathIntercept;

[ModInitializer(nameof(Initialize))]
public static class DeathInterceptMain
{
    public const string ModId = "DeathIntercept";

    private static Harmony? _harmony;

    public static void Initialize()
    {
        Log.Info("[DeathIntercept] Initializing...");

        _harmony = new Harmony(ModId);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        Log.Info("[DeathIntercept] Initialized.");
    }
}
