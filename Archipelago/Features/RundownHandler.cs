using CellMenu;
using GameData;
using ReTFO.Archipelago.Features.ObjectiveHandlers;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// A special feature used to handle rundowns and their presentation / integration into AP.
/// </summary>
[EnableFeatureByDefault]
public class RundownHandler : ArchipelagoFeature
{
    public override string Name => "Rundown Handler";
    public override string Description => "Controls the appearance of rundowns, expeditions, and other UI helpers.";
    public override FeatureGroup Group => FeatureGroups.Archipelago;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        StateTracker? st = StateTracker.Get();
        if (st == null)
            Plugin.Get().LateSetup += _ => StateTracker.Get().OnStateChange += TryClearCache;
        else
            st.OnStateChange += TryClearCache;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        StateTracker.Get()?.OnStateChange -= TryClearCache;
    }

    private static void TryClearCache(StateTracker st)
    {
        if (st.CurrentState == StateTracker.eState.CleanState)
            s_entities.Clear();
    }

    /// <summary>
    /// Overwrite the current loaded rundowns with customized rundowns for the current randomization
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown for several misc edge cases which should never occur</exception>
    public static void OverwriteRundowns(StateTracker stateTracker)
    {
        // Find all expeditions and create copies to be loaded in
        Game.Data gameData = stateTracker.GameData;

        // Set of parent tags so we can identify which expeditions are required to reach/see all enabled regions
        HashSet<RegionID> parents = gameData.Regions.GetAllParents(stateTracker.RegionWhitelist.Where(id => !gameData.Regions.IsChild(id, stateTracker.RegionBlacklist)));

        Queue<ExpeditionInTierData> newExpeditions = new();
        foreach (Expedition.Data expData in gameData.GetAllExpeditions())
        {
            if (!(gameData.Regions.IsChild(expData.Region_Expedition, stateTracker.RegionWhitelist) && !gameData.Regions.IsChild(expData.Region_Expedition, stateTracker.RegionBlacklist)) && !parents.Contains(expData.Region_Expedition))
                continue;

            ExpeditionInTierData newExpedition = expData.Expedition.MemberwiseClone().Cast<ExpeditionInTierData>();
            newExpedition.Descriptive.Prefix = expData.ExpeditionName;
            newExpedition.Descriptive.SkipExpNumberInName = true;
            newExpedition.ExcludeFromMatchmaking = true;
            newExpedition.ExcludeFromProgression = false;
            newExpedition.Descriptive.ProgressionVisualStyle = eProgressionVisualStyle.Normal;
            newExpedition.Accessibility = eExpeditionAccessibility.AlwayBlock; // Will be overwritten by expedition unlock item
            newExpedition.HideOnLocked = false; // R8 right-side expeditions
            newExpedition.UnlockedByExpedition = new() { Tier = eRundownTier.TierA, Exp = 0 };
            newExpeditions.Enqueue(newExpedition);
        }

        // This section mostly deals with distributing expeditions into separate rundowns such that it looks pretty and unique
        int numRundowns;
        if (newExpeditions.Count < 12)
            numRundowns = 1;
        else
            numRundowns = (int)Math.Ceiling(newExpeditions.Count * .1);
        numRundowns = Math.Min(numRundowns, MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections.Count);

        System.Random random = new(unchecked((int)stateTracker.RootSeed));
        List<RundownDataBlock> newRundowns = Enumerable.Range(1, numRundowns).Select(i => RundownDataBlock.GetBlock($"Archipelago {i}")).ToList();

        for (int i = 0; i < numRundowns; i++)
        {
            var rundown = newRundowns[i];
            if (newRundowns[i] == null)
            {
                newRundowns[i] = new() { name = $"Archipelago {i + 1}" };
                RundownDataBlock.AddBlock(newRundowns[i]);
                rundown = newRundowns[i];

                rundown.NeverShowRundownTree = false;
                rundown.UseTierUnlockRequirements = false;
                rundown.VanityItemLayerDropDataBlock = 0;

                rundown.ReqToReachTierA = new()
                {
                    AllClearedSectors = 0,
                    MainSectors = 0,
                    SecondarySectors = 0,
                    ThirdSectors = 0
                };

                rundown.ReqToReachTierB = rundown.ReqToReachTierA;
                rundown.ReqToReachTierC = rundown.ReqToReachTierA;
                rundown.ReqToReachTierD = rundown.ReqToReachTierA;
                rundown.ReqToReachTierE = rundown.ReqToReachTierA;

                static Localization.LocalizedText MakeText(string name, string text)
                {
                    LanguageData data = new()
                    {
                        ShouldTranslate = false,
                        Translation = text
                    };
                    TextDataBlock block = new()
                    {
                        SkipLocalization = true,
                        MachineTranslation = true,
                        English = text,
                        Description = "Text auto-generated by Archipelago",
                        CharacterMetaData = 1,
                        French = data,
                        Italian = data,
                        German = data,
                        Spanish = data,
                        Russian = data,
                        Portuguese_Brazil = data,
                        Polish = data,
                        Japanese = data,
                        Korean = data,
                        Chinese_Traditional = data,
                        Chinese_Simplified = data,
                        ExportVersion = 6, // I'm not really sure what these are for
                        ImportVersion = 6, // I'm not really sure what these are for
                        name = name,
                        internalEnabled = true,
                    };
                    TextDataBlock.AddBlock(block);
                    return new()
                    {
                        Id = block.persistentID,
                        OldId = 0,
                        UntranslatedText = text,
                    };
                }

                rundown.StorytellingData = new()
                {
                    Title = MakeText($"{rundown.name} - Title", $"{rundown.name}\nMULTIVERSE"),
                    ExternalExpTitle = MakeText($"{rundown.name} - ExpTitle", rundown.name),
                    SurfaceDescription = MakeText($"{rundown.name} - Surface Description", ""
                      + "\nWORK WITH FELLOW PRISONERS TO RECOVER SECURE ASSETS AND COMPLETE THE RUNDOWN"
                      + "\n\n-----------------------------------"
                      + "\n\nCOORDINATE ACROSS THE MULTIWORLD TO MINIMIZE CASUALTIES AND ACCELERATE PRIORITIES"
                      + "\n\n-----------------------------------"
                      + "\n\nCOLLECT WARDEN ARTIFACTS TO SUPPLEMENT SUCCESS MARGINS AT THE COST OF EMOTIONAL DURESS"
                    ),
                    SurfaceIconPosition = new(0, 0),
                    Visuals = new() { ColorBackground = Color.magenta },
                    TextLog = MakeText($"{rundown.name} - Log", "THIS IS ARCHIPELAGO"),
                    TextLogPos = new(0, 0),
                };
            }

            (rundown.TierA ??= new()).Clear();
            (rundown.TierB ??= new()).Clear();
            (rundown.TierC ??= new()).Clear();
            (rundown.TierD ??= new()).Clear();
            (rundown.TierE ??= new()).Clear();
        }

        // We're looking at all expedition slots and weighting them. Higher weight = more likely to be disqualified later
        const float rw = 1.3f; // Weighting of the rundown index
        const float tw = 2f; // Weighting of the tier
        const float iw = 3f; // Weighting of the expedition index

        const int numTiers = 5;
        const int numIndicies = 5;

        // The order matters here. It's set up to go through all A1 slots, then all A2 slots, etc..
        float middle = numRundowns * .5f + .5f;
        List<float> weights = Enumerable.Repeat(1f, 1)
            .SelectMany(w => Enumerable.Range(1, numTiers).Select(wt => w * MathF.Pow(tw, MathF.Abs(wt - 2.5f) * .4f)))
            .SelectMany(w => Enumerable.Range(1, numRundowns).Select(wr => w * MathF.Pow(rw, MathF.Abs(wr - middle) / middle)))
            .SelectMany(w => Enumerable.Range(0, numIndicies).Select(wi => w * MathF.Pow(iw, MathF.Abs(wi - 2f) * .5f)))
            .Select(w => w / (rw * tw * iw)) // Normalize 0 to 1
            .ToList();
        float totalWeight = weights.Sum();

        // We now randomly eliminate as many expedition slots as needed
        const float throwoutChance = .3f; // Chance to make slot empty instead of just disabled
        for (int j = newExpeditions.Count; j < weights.Count; j++)
        {
            float sample = totalWeight * random.NextSingle();
            float sum = 0f;
            for (int k = 0; k < weights.Count; k++)
            {
                sum += MathF.Max(weights[k], 0f);
                if (sum > sample)
                {
                    totalWeight -= weights[k];
                    if (weights[k] > throwoutChance)
                        weights[k] = -2f;
                    else
                        weights[k] = -1f;
                    break;
                }
            }
        }

        // Populate the slots with expeditions!
        for (int tier = 0; tier < numTiers; tier++)
        {
            for (int rundownIndex = 0; rundownIndex < numRundowns; rundownIndex++)
            {
                for (int index = 0; index < numIndicies; index++)
                {
                    int i = tier * numRundowns * numIndicies
                        + rundownIndex * numIndicies + index;

                    ExpeditionInTierData expedition;
                    if (weights[i] > 0f)
                        expedition = newExpeditions.Dequeue();
                    else
                        continue; // Throwout

                    switch (tier)
                    {
                        case 0:
                            newRundowns[rundownIndex].TierA.Add(expedition);
                            break;
                        case 1:
                            newRundowns[rundownIndex].TierB.Add(expedition);
                            break;
                        case 2:
                            newRundowns[rundownIndex].TierC.Add(expedition);
                            break;
                        case 3:
                            newRundowns[rundownIndex].TierD.Add(expedition);
                            break;
                        case 4:
                            newRundowns[rundownIndex].TierE.Add(expedition);
                            break;
                        default:
                            throw new NotSupportedException("Attempted to place rundown in a tier outside A-E");
                    }
                }
            }
        }

        if (newExpeditions.Count > 0)
            throw new NotSupportedException("Failed to populate the correct number of expeditions");

        // Clean up the visuals
        static IEnumerable<(int, TierVisualData)> GetVisualData(RundownDataBlock rundown)
        {
            yield return (rundown.TierA.Count, rundown.StorytellingData.Visuals.TierAVisuals);
            yield return (rundown.TierB.Count, rundown.StorytellingData.Visuals.TierBVisuals);
            yield return (rundown.TierC.Count, rundown.StorytellingData.Visuals.TierCVisuals);
            yield return (rundown.TierD.Count, rundown.StorytellingData.Visuals.TierDVisuals);
            yield return (rundown.TierE.Count, rundown.StorytellingData.Visuals.TierEVisuals);
        }
        foreach (var pair in newRundowns.SelectMany(GetVisualData))
        {
            pair.Item2.Scale = pair.Item1 switch
            {
                0 => .4f + .4f * random.NextSingle(),
                1 => .1f,
                2 => .35f + .2f * random.NextSingle(),
                3 => .5f + .22f * random.NextSingle(),
                4 => .675f + .1f * random.NextSingle(),
                5 => .72f + .18f * random.NextSingle(),
                _ => throw new NotSupportedException("More than 5 expeditions in a single tier!")
            };
            // Note: ScaleYModifier appears to have no effect
            pair.Item2.Color = Color.HSVToRGB(MathF.Pow(random.NextSingle(), .125f), 1f, 1f);
        }

        foreach (var icon in MainMenuGuiLayer.Current.PageRundownNew.m_expIconsAll)
        {
            icon.SetStatusTextVisible(true);
        }

        // Set up the menu so everything is correctly displayed
        Il2CppSystem.Collections.Generic.List<uint> ids = new(newRundowns.Count);
        foreach (var rundown in newRundowns) ids.Add(rundown.persistentID);
        Globals.Global.RundownIdToLoad = ids[0];
        Globals.Global.ActiveRundownIds = ids.ToArray().Cast<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<uint>>();
        GameSetupDataBlock.GetAllBlocks()[0].RundownIdsToLoad = ids;

        // Sorting rundowns placements by name (so they load left to right, which they don't in vanilla)
        if (MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections.Count == 8)
        {
            string[] sortKeys = MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections.Select(s => s.Name).ToArray();
            var sortValues = Enumerable.Range(0, 8).Select(i => (MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections[i], MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelectionPositions[i])).ToArray();
            Array.Sort(sortKeys, sortValues);

            for (int i = 0; i < sortKeys.Length; i++)
            {
                MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections[i] = sortValues[i].Item1;
                MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelectionPositions[i] = sortValues[i].Item2;
            }
        }

        // Placing new rundowns into the selections
        for (int i = 0; i < numRundowns; i++)
        {
            MainMenuGuiLayer.Current.PageRundownNew.UpdateRundownSelectionButton(
                MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections[i],
                newRundowns[i].persistentID
            );
        }

        // Because we modify this in ModifyRundownMenuPatch, we need to revert it now.
        // If we don't, we get a null reference exception as GTFO tries to clean up a non-showing rundown screen
        MainMenuGuiLayer.Current.PageRundownNew.m_selectionIsRevealed = false;

        // Shows the rundowns menu.
        if (numRundowns == 1)
        {
            // Update the spawned rundown based on the new data in Globals.Global
            MainMenuGuiLayer.Current.PageRundownNew.ResetRundownItems();
            MainMenuGuiLayer.Current.PageRundownNew.m_currentRundownData = newRundowns[0];
            MainMenuGuiLayer.Current.PageRundownNew.PlaceRundown(newRundowns[0]);
            MainMenuGuiLayer.Current.PageRundownNew.UpdateHeaderText();

            // The function normally assigned to the Connect button for single-rundown menus
            MainMenuGuiLayer.Current.PageRundownNew._Setup_b__102_0(0);
        }
        else
        {
            // I believe this is the lambda assigned to the "Select Rundown" button. Either way, it works here :)
            MainMenuGuiLayer.Current.PageRundownNew._Setup_b__102_3(0);
        }
    }

    /// <summary>
    /// Cache of expedition locations and items
    /// </summary>
    private static SortedList<RegionID, (LocationID[], ItemID[])> s_entities = new();

    /// <summary>
    /// Get the locations and floating items associated with a particular expedition
    /// </summary>
    /// <param name="expedition"></param>
    /// <returns></returns>
    public static (LocationID[], ItemID[]) GetExpeditionEntities(Expedition.Data expedition)
    {
        if (s_entities.TryGetValue(expedition.Region_Expedition, out var value)) return value;

        HashSet<RegionID> regions = new() { };
        void searchRecursive(RegionID region)
        {
            if (regions.Add(region))
            {
                foreach (var pathid in expedition.Regions.LookUpValue(region).ConnectedPaths)
                    searchRecursive(expedition.LookUpPath(pathid).EndingRegion);
            }
        }
        searchRecursive(expedition.Region_Expedition);
        regions.RemoveWhere(r => !expedition.Regions.LookUpValue(r).Reachable);

        LocationID[] locations = regions
            .SelectMany(r => expedition.Regions.LookUpValue(r).ConnectedLocations)
            .Distinct()
            .ToArray();
        ItemID[] items = expedition.GetAllFloatingItems()
            .Where(pair => regions.Contains(pair.Item1))
            .Select(pair => (pair.Item2, expedition.Items.LookUpValueChecked(pair.Item2)))
            .Where(pair => pair.Item2.RandData.IsWhitelisted && !pair.Item2.RandData.IsBlacklisted && pair.Item2.RandData.IsProgression)
            .Select(pair => pair.Item1)
            .ToArray();

        s_entities[expedition.Region_Expedition] = (locations, items);
        return (locations, items);
    }

    /// <summary>
    /// Little helper which safely updates location counts on all expedition icons
    /// </summary>
    public static void UpdateAllCounts()
    {
        IEnumerable<CM_MenuBar?> items = [
            //MainMenuGuiLayer.Current.PageBelowSpec?.m_menuBar,
            //MainMenuGuiLayer.Current.PageBugHunters?.m_menuBar,
            //MainMenuGuiLayer.Current.PageCredits?.m_menuBar,
            //MainMenuGuiLayer.Current.PageCustomExpeditionSuccess?.m_menuBar,
            //MainMenuGuiLayer.Current.PageEmpty?.m_menuBar,
            //MainMenuGuiLayer.Current.PageEULA?.m_menuBar,
            //MainMenuGuiLayer.Current.PageExpeditionFail?.m_menuBar,
            //MainMenuGuiLayer.Current.PageExpeditionSuccess?.m_menuBar,
            MainMenuGuiLayer.Current.PageGearDetails?.m_menuBar,
            //MainMenuGuiLayer.Current.PageIntro?.m_menuBar,
            //MainMenuGuiLayer.Current.PageIntro?.m_menuBar,
            MainMenuGuiLayer.Current.PageLoadout?.m_menuBar,
            //MainMenuGuiLayer.Current.PageLogos?.m_menuBar,
            //MainMenuGuiLayer.Current.PageMap?.m_menuBar,
            //MainMenuGuiLayer.Current.PageMatchmaking?.m_menuBar,
            //MainMenuGuiLayer.Current.PageObjectives?.m_menuBar,
            MainMenuGuiLayer.Current.PagePlayerDetails?.m_menuBar,
            MainMenuGuiLayer.Current.PageRundown?.m_menuBar,
            MainMenuGuiLayer.Current.PageRundownNew?.m_menuBar,
            MainMenuGuiLayer.Current.PageSettings?.m_menuBar,
            //MainMenuGuiLayer.Current.PageStart?.m_menuBar,
        ];

        foreach (var item in items)
        {
            if (item?.m_expIcon?.DataBlock != null)
                item.m_expIcon.SetArtifactHeat(1f);
        }

        // Updating the menu icons only if in the menu
        if (!GameStateManager.IsInExpedition && MainMenuGuiLayer.Current.PageRundownNew != null)
        {
            foreach (var icon in MainMenuGuiLayer.Current.PageRundownNew.m_expIconsAll)
                icon.SetStatus(icon.Status);
        }
    }

    /// <summary>
    /// When updating the expedition status, use the StateTracker's status instead of the drop server's
    /// </summary>
    [ArchivePatch(typeof(CM_ExpeditionIcon_New), nameof(CM_ExpeditionIcon_New.SetStatus))]
    public static class CM_ExpeditionIcon_New__SetStatus__Patch
    {
        public static void Prefix(CM_ExpeditionIcon_New __instance, ref eExpeditionIconStatus status, ref string mainFinishCount, ref string secondFinishCount, ref string thirdFinishCount, ref string allFinishedCount)
        {
            StateTracker stateTracker = StateTracker.Get();
            if (!Expedition.Data.TryGetFromExpedition(__instance.DataBlock, out Expedition.Data? data))
            {
                FeatureLogger.Warning("Failed to overwrite expedition icon status: failed to find expedition");
                return;
            }
    
            int mainCount = stateTracker.CollectedItemCounts.GetValueOrDefault(data.MainLayer.Item_SectorClear_Instance, 0);
            mainFinishCount = mainCount.ToString();

            if (__instance.DataBlock.Accessibility == eExpeditionAccessibility.AlwaysAllow)
            {
                if (mainCount > 0) status = eExpeditionIconStatus.PlayedAndFinished;
                else status = eExpeditionIconStatus.NotPlayed;
            }
            else status = eExpeditionIconStatus.TierLocked;

            if (data.HasSecondary)
                secondFinishCount = stateTracker.CollectedItemCounts.GetValueOrDefault(data.GetLayer(LayerType.Secondary).Item_SectorClear_Instance, 0).ToString();
            if (data.HasOverload)
                thirdFinishCount = stateTracker.CollectedItemCounts.GetValueOrDefault(data.GetLayer(LayerType.Overload).Item_SectorClear_Instance, 0).ToString();
            if (data.HasSecondary && data.HasOverload)
                allFinishedCount = stateTracker.CollectedItemCounts.GetValueOrDefault(data.Item_PEClear_Instance, 0).ToString();
        }
    }

    /// <summary>
    /// Lower the completion count on the expedition icon to make space for our item count
    /// </summary>
    [ArchivePatch(typeof(CM_ExpeditionIcon_New), nameof(CM_ExpeditionIcon_New.Setup), [ typeof(ExpeditionInTierData), typeof(string), typeof(eRundownTier), typeof(int), typeof(Color), typeof(Transform) ])]
    public static class CM_ExpeditionIcon_New__Setup__Patch
    {
        public static void Prefix(CM_ExpeditionIcon_New __instance)
        {
            __instance.m_statusText.rectTransform.anchoredPosition3D += Vector3.down * 20f;
            __instance.m_hostingFriendsCountText.rectTransform.anchoredPosition3D += Vector3.down * 20f;
        }
    }

    /// <summary>
    /// Replace artifact heat with location and item counts
    /// </summary>
    [ArchivePatch(typeof(CM_ExpeditionIcon_New), nameof(CM_ExpeditionIcon_New.SetArtifactHeat))]
    public static class CM_ExpeditionIcon_New__SetArtifactHeat__Patch
    {
        public static bool Prefix(CM_ExpeditionIcon_New __instance)
        {
            StateTracker stateTracker = StateTracker.Get();
            Game.Data gameData = stateTracker.GameData;
            if (!gameData.TryGetExpeditionData(__instance.DataBlock, out var data))
            {
                FeatureLogger.Error($"Failed to look up expedition for location count: {__instance.DataBlock.Descriptive.Prefix}");
                return true;
            }

            var counts = GetExpeditionEntities(data);

            int locationTotalCount = 0, locationFoundCount = 0;
            int itemTotalCount = 0;
            Dictionary<ItemID, int> itemCounts = new();
            foreach (LocationID id in counts.Item1)
            {
                Location? loc = data.Locations.LookUpValue(id);
                if (loc == null) continue;
                if (!loc.RandData.IsRandomized) continue;

                Item item = data.Items.LookUpValueChecked(loc.ItemID);
                if (item.RandData.IsProgression)
                {
                    ++itemTotalCount;
                    itemCounts[loc.ItemID] = itemCounts.GetValueOrDefault(loc.ItemID, 0) + 1;
                }
                
                if (loc.RandData.IsExcluded || loc.RandData.IsTrap) continue;

                ++locationTotalCount;
                if (stateTracker.HasLocation(id)) ++locationFoundCount;
            }

            itemTotalCount += counts.Item2.Length;
            foreach (ItemID id in counts.Item2)
                itemCounts[id] = itemCounts.GetValueOrDefault(id, 0) + 1;
            int itemFoundCount = itemCounts.Sum(pair => Math.Min(pair.Value, stateTracker.CollectedItemCounts.GetValueOrDefault(pair.Key, 0)));

            var test = itemCounts.ToDictionary(id => data.Items.LookUpName(id.Key), id => stateTracker.CollectedItemCounts.GetValueOrDefault(id.Key, 0) - id.Value);

            Color lc;
            if (locationTotalCount == 0)
                lc = Color.grey;
            else if (locationFoundCount == locationTotalCount)
                lc = new Color(0f, 1f, 0f);
            else
                lc = Color.white;

            Color ic;
            if (itemTotalCount == 0)
                ic = Color.grey;
            else if (itemFoundCount == itemTotalCount)
                ic = new Color(0f, 1f, 0f);
            else
                ic = Color.white;

            __instance.m_artifactHeatText.SetText(
                  $"<#{(int)(lc.r * 255):X2}{(int)(lc.g * 255):X2}{(int)(lc.b * 255):X2}>Found Locations: {locationFoundCount} / {locationTotalCount}</color>"
                + $"\n<#{(int)(ic.r * 255):X2}{(int)(ic.g * 255):X2}{(int)(ic.b * 255):X2}>Progression Items: {itemFoundCount} / {itemTotalCount}</color>"
            );

            return false;
        }
    }

    /// <summary>
    /// Ensure that the artifact heat is updated after an expedition ends, even if no one grabbed any artifacts
    /// </summary>
    [ArchivePatch(typeof(RundownManager), nameof(RundownManager.OnExpeditionEnded))]
    public static class RundownManager__OnExpeditionEnded__Patch
    {
        public static void Postfix()
            => UpdateAllCounts();
    }

}
