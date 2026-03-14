using ReTFO.Archipelago.FeaturesAPI;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class EmptyHandler : ArchipelagoFeature
{
    public override string Name => "Empty Handler";
    public override string Description
        => "Handles the Empty objective type.\n"
        + "This is a special objective type which cannot be beaten, and requires a ForceCompelteObjective event (or Win event).\n"
        + "Example: R8E2 (Main) Objective #2\n"
        + " -> This level has two chained objectives; the first is survival, and winning it triggers the nightmare surge. "
        + "The second is empty, so you can't extract after \"beating\" the objective.";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // This objective type has nothing to do. So, skip it!

    /* TODO:
     */

    //private const eWardenObjectiveType ThisObjectiveType
    //    = eWardenObjectiveType.Empty;
    //
    //private static string ThisObjectiveSummary(Objective.Data data)
    //{
    //    CheckThisIsCorrectObjective(data);
    //    return "Empty";
    //}
    //
    //private static bool ThisIsCorrectObjective(Objective.Data data)
    //    => data.Objective.Type == ThisObjectiveType;
    //
    //private static void CheckThisIsCorrectObjective(Objective.Data data)
    //{
    //    if (!ThisIsCorrectObjective(data))
    //        FeatureLogger.Error($"Wrong objective type! Expected {Enum.GetName(ThisObjectiveType)}, got {data.Objective.Type}");
    //}
    //
    //private static string ThisObjectiveName(Objective.Data data)
    //{
    //    CheckThisIsCorrectObjective(data);
    //    return data.ObjectiveName(ThisObjectiveSummary(data));
    //}
    //
    //// Objective that cannot be completed; instead, a ForceCompleteObjective event (or win event) must be triggerred
    //[Objective.Callback]
    //public void HandleEmptyObjective(Objective.Data data)
    //{
    //    // There's no actual work to be done here
    //}
}
