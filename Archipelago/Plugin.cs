
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Newtonsoft.Json;
using ReTFO.Archipelago.ModdedInstanceData2;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ReTFO.Archipelago;

[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
public class Plugin : BasePlugin
{
    public const string Name = "Archipelago";       // Plugin name
    public const string Author = "RoboRyGuy";       // Plugin author
    public const string GUID = $"{Author}.{Name}";  // Plugin GUID, unique identifier used by BepInEx
    public const string Version = "1.0.0";          // Plugin version, can be used by System.Version

    // Reference to plugin instance that is loaded by BepInEx
    private static Plugin? _plugin = null;

    // Instance of harmony used for patching
    protected Harmony harmony = new(GUID);

    // Get the plugin instance, throws an exception if it fails
    public static Plugin Get() => TryGet() ?? throw new NullReferenceException($"Tried to retrieve {Name}, but it was not loaded!");

    // Tries to get the plugin instance, returns null if it fails
    public static Plugin? TryGet() => _plugin ??= IL2CPPChainloader.Instance.Plugins.FirstOrDefault(p => p.Key == GUID).Value.Instance as Plugin;

    // Tries to get the plugin instance, returns true if it succeeds
    public static bool TryGet([NotNullWhen(true)] out Plugin? plugin)
    {
        plugin = TryGet();
        return plugin != null;
    }

    public override void Load()
    {
        _plugin = this;
        harmony.PatchAll(GetType());
        harmony.PatchAll(typeof(SingleRundownPatch));
        harmony.PatchAll(typeof(PostShowIntel));
        Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<Il2CppAction>();
        GTFO.API.AssetAPI.OnStartupAssetsLoaded += PostInit;
        Log.LogInfo($"{GUID} is loaded!");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    // --------------------------------------------------------------------------------------------

    // Manager for Modded instance data, manages generating it and such
    // Subclasses ProcessExpedition, in case you want to react to that event
    public Manager Manager 
    {
        get { return manager ??= new(); }
        protected set { manager = value; } 
    }
    private Manager? manager = null;

    // Event handler for the ProcessLayer event (for generating modded instance data)
    public ProcessLayer ProcessLayer
    {
        get { return processLayer ??= new ProcessLayer().RegisteredTo(Manager.ProcessExpedition); }
        protected set { processLayer = value; }
    }
    private ProcessLayer? processLayer = null;

    // Event handler for the ProcessZone event (for generating modded instance data)
    public ProcessZone ProcessZone
    {
        get { return processZone ??= new ProcessZone().RegisteredTo(ProcessLayer); }
        protected set { processZone = value; }
    }
    private ProcessZone? processZone = null;

    // Event handler for the ProcessTerminal event (for generating modded instance data)
    public ProcessTerminal ProcessTerminal
    {
        get { return processTerminal ??= new ProcessTerminal().RegisteredTo(ProcessZone); }
        protected set { processTerminal = value; }
    }
    private ProcessTerminal? processTerminal = null;

    // Hanlder for the ProcessObjective callbacks (for generating modded instance data)
    public ProcessObjective ProcessObjective
    {
        get { return processObjective ??= new ProcessObjective().RegisteredTo(ProcessLayer); }
        protected set { processObjective = value; }
    }
    private ProcessObjective? processObjective = null;

    public ModdedInstanceData2.ModdedInstanceData GetModdedInstanceData()
    {
        // Force init various events, in case they aren't initted
        object obj;
        obj = Manager;
        obj = ProcessLayer;
        obj = ProcessZone;
        obj = ProcessTerminal;
        obj = ProcessObjective;

        // Create and return data
        return Manager.CreateData();
    }

    public static void PostInit()
    {
        //var data = ModdedInstanceData.Manager.GenerateModdedInstanceData();
        var data = Plugin.Get().GetModdedInstanceData();

        JsonSerializerSettings settings = new();
        settings.Converters.Insert(0, new RegionConverter());
        settings.Converters.Insert(0, new IntListConverer());
        settings.Converters.Insert(0, new Newtonsoft.Json.Converters.StringEnumConverter());
        JsonSerializer serializer = new();

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented, settings);

        string outputPath = "C:\\Users\\rydan\\Downloads\\moddedInstanceData.json";
        System.IO.File.WriteAllText(outputPath, json);

        /*
        foreach (var exp in expeditions)
        {
            var objectives = Enumerable.Empty<Tuple<uint, IEnumerable<uint>, string>>()
                .Append(Tuple.Create(exp.MainLayerData.ObjectiveData.DataBlockId,      exp.MainLayerData.ChainedObjectiveData.Select(d => d.DataBlockId) ,      "Main"))
                .Append(Tuple.Create(exp.SecondaryLayerData.ObjectiveData.DataBlockId, exp.SecondaryLayerData.ChainedObjectiveData.Select(d => d.DataBlockId) , "Secondary"))
                .Append(Tuple.Create(exp.ThirdLayerData.ObjectiveData.DataBlockId,     exp.ThirdLayerData.ChainedObjectiveData.Select(d => d.DataBlockId),      "Overload"))
            ;

            foreach (var pair in objectives)
            {
                WardenObjectiveDataBlock objective = WardenObjectiveDataBlock.GetBlock(pair.Item1);
                if (objective == null) continue;

                int count = 1;
                for (int i = 0; i < objective.EventsOnActivate.Count; i++)
                {
                    if (objective.EventsOnActivate[i].Type == eWardenObjectiveEventType.EventBreak)
                        objective.EventsOnActivate.Insert(++i, new()
                        {
                            Type = eWardenObjectiveEventType.None,
                            WardenIntel = new() { UntranslatedText = $"On_Activate{(objective.OnActivateOnSolveItem ? "_Or_Solve" : "")} {count++} - {pair.Item3}"}
                        });
                }
                objective.EventsOnActivate.Insert(0, new()
                {
                    Type = eWardenObjectiveEventType.None,
                    WardenIntel = new() { UntranslatedText = $"On_Activate{(objective.OnActivateOnSolveItem ? "_Or_Solve" : "")} 0 - {pair.Item3}" }
                });

                objective.EventsOnGotoWin.Insert(0, new()
                {
                    Type = eWardenObjectiveEventType.None,
                    WardenIntel = new() { UntranslatedText = $"On_Goto_Win - {pair.Item3}" }
                });

                int chainCount = 1;
                foreach (var chainID in pair.Item2)
                {
                    var chainedObjective = WardenObjectiveDataBlock.GetBlock(chainID);
                    if (chainedObjective == null) continue;

                    chainedObjective.EventsOnElevatorLand.Insert(0, new()
                    {
                        Type = eWardenObjectiveEventType.None,
                        WardenIntel = new() { UntranslatedText = $"On_Elevator_Land - {pair.Item3}_{chainCount}" }
                    });

                    count = 1;
                    for (int i = 0; i < chainedObjective.EventsOnActivate.Count; i++)
                    {
                        if (chainedObjective.EventsOnActivate[i].Type == eWardenObjectiveEventType.EventBreak)
                            chainedObjective.EventsOnActivate.Insert(++i, new()
                            {
                                Type = eWardenObjectiveEventType.None,
                                WardenIntel = new() { UntranslatedText = $"On_Activate{(chainedObjective.OnActivateOnSolveItem ? "_Or_Solve" : "")} {count++} - {pair.Item3}_{chainCount}" }
                            });
                    }
                    chainedObjective.EventsOnActivate.Insert(0, new()
                    {
                        Type = eWardenObjectiveEventType.None,
                        WardenIntel = new() { UntranslatedText = $"On_Activate{(chainedObjective.OnActivateOnSolveItem ? "_Or_Solve" : "")} 0 - {pair.Item3}_{chainCount}" }
                    });

                    chainedObjective.EventsOnGotoWin.Insert(0, new()
                    {
                        Type = eWardenObjectiveEventType.None,
                        WardenIntel = new() { UntranslatedText = $"On_Goto_Win - {pair.Item3}_{chainCount}" }
                    });
                }
            }
        }
        /**/
    }
}

// Patch which copies warden intel messages into the chat, specifically so that if I miss them I can look back at the history
public static class PostShowIntel
{
    [HarmonyPatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.DisplayWardenIntel))]
    [HarmonyPostfix]
    public static void Postfix(Localization.LocalizedText text)
    {
        if ((text.UntranslatedText?.Length ?? 0) == 0) return;
        Plugin.Get().Log.LogWarning(text.UntranslatedText);
        PlayerChatManager.WantToSentTextMessage(Player.PlayerManager.GetLocalPlayerAgent(), text.UntranslatedText, Player.PlayerManager.GetLocalPlayerAgent());
    }
}

// Custom formatting for regions to make them inline
public sealed class RegionConverter : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(ModdedInstanceData2.Region);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not ModdedInstanceData2.Region region) throw new ArgumentException("Expected value to be of type Region.", nameof(value));
        writer.WriteRawValue($"{{ \"name\": \"{region.name.Replace("\"", "\\\"")}\" }}");
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("This converter cannot read.");
    }
}

// Custom formatting for lists of ints to make them inline
public sealed class IntListConverer : JsonConverter
{
    public override bool CanRead => false;
    public override bool CanWrite => true;

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(List<int>);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not List<int> ints) throw new ArgumentException("Expected value to be of type List<int>.", nameof(value));
        writer.WriteRawValue($"[ {string.Join(", ", ints)} ]");
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("This converter cannot read.");
    }
}
