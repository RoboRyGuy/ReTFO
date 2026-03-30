using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// TheArchive has an issue where it loads assemblies using Assembly.LoadFrom.
/// This creates a new, duplicate assembly which the debugger is not attached to.
/// This patch ovewrites that LoadFrom call with a custom resolver which attempts to
///  find an already-loaded assembly and uses that one instead
/// </summary>
[HarmonyPatch(typeof(TheArchive.Core.Bootstrap.ArchiveModuleChainloader), "LoadModules")]
public static class ArchiveLoadModulePatch
{
    public static Assembly FindOrLoadFrom(string path)
    {
        Plugin.Get().Log.LogWarning("Intercepted call by TheArchive to load assembly: " + path);
        var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .FirstOrDefault(a => a.Location == path);
        Plugin.Get().Log.LogWarning((loadedAssembly == null ? "No" : "An") + " already-loaded module was found to replace it");
        return loadedAssembly ?? Assembly.LoadFrom(path);
    }


    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo oldMethod = AccessTools.Method(typeof(Assembly), nameof(Assembly.LoadFrom), new Type[] { typeof(string) });
        MethodInfo newMethod = AccessTools.Method(typeof(ArchiveLoadModulePatch), nameof(FindOrLoadFrom));

        return instructions.Select(
            i => i.opcode == OpCodes.Call && i.operand is MethodInfo mi && mi == oldMethod
            ? new CodeInstruction(OpCodes.Call, newMethod)
            : i
        );

    }


}
