using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace TranslateUs.Patches;

[HarmonyPatch(typeof(IL2CPPChainloader), nameof(IL2CPPChainloader.LoadPlugin))]
public static class DisableOtherPlugins
{
    public static bool Prefix([HarmonyArgument(0)] PluginInfo pluginInfo, [HarmonyArgument(1)] Assembly pluginAssembly)
    {
        if (pluginInfo.Metadata.GUID == "dev.xtracube.authfix") Main.IsAuthFix = true;
        return true;
    }
}