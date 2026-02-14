
using GameData;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ReTFO.Archipelago;

/// <summary>
/// Truncates loaded Rundown GameData to ensure there's only one rundown.
/// This forces the "Connect to Rundown" screen.
/// </summary>
internal static class SingleRundownPatch
{
    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GameDataInit), nameof(GameDataInit.Initialize));
        yield return AccessTools.Method(typeof(GameDataInit), nameof(GameDataInit.ReInitialize));
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        GameSetupDataBlock setup = GameSetupDataBlock.GetAllBlocks()[0];
        uint id = setup.RundownIdsToLoad[0];
        //setup.RundownIdsToLoad = new(1);
        //setup.RundownIdsToLoad.Add(id);
        IEnumerable<ExpeditionInTierData> UnpackRundown(RundownDataBlock rundown)
        {
            int i = 0;
            for (i = 0; i < rundown.TierA.Count; i++) yield return rundown.TierA[i];
            for (i = 0; i < rundown.TierB.Count; i++) yield return rundown.TierB[i];
            for (i = 0; i < rundown.TierC.Count; i++) yield return rundown.TierC[i];
            for (i = 0; i < rundown.TierD.Count; i++) yield return rundown.TierD[i];
            for (i = 0; i < rundown.TierE.Count; i++) yield return rundown.TierE[i];
        }

        var expeditions = RundownDataBlock.GetAllBlocks()
            .Where(r => r.internalEnabled)
            .SelectMany(UnpackRundown)
            .Where(e => e.Enabled);

        foreach (var exp in expeditions)
        {
            IEnumerable<uint> layoutIDs = Enumerable.Empty<uint>()
                .Append(exp.LevelLayoutData)
                .Append(exp.SecondaryLayout)
                .Append(exp.ThirdLayout)
                .Concat(exp.DimensionDatas.Select(s => DimensionDataBlock.GetBlock(s.DimensionData)?.DimensionData.LevelLayoutData ?? 0))
            ;

            foreach (var layoutID in layoutIDs)
            {
                LevelLayoutDataBlock layout = LevelLayoutDataBlock.GetBlock(layoutID);
                if (layout == null) continue;
                foreach (var zone in layout.Zones)
                    zone.IsCheckpointDoor = false;
            }
        }

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
        writer.WriteRawValue($"{{ \"name\": \"{region.name}\" }}");
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
