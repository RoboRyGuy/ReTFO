using CellMenu;
using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
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

namespace ReTFO.Archipelago.Features.FloatingItems;

[EnableFeatureByDefault]
public class ArtifactsAndBoostersHandler : ArchipelagoFeature
{
    public override string Name => "Artifacts and Boosters Handler";
    public override string Description
        => "Converts all collected artifacts to shared energy, and allows players to used energy to equip boosters.";
    public override FeatureGroup Group => FeatureGroups.FloatingHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        if (PersistentInventoryManager.Current != null && m_overwriteCallback == null)
        {
            m_overwriteCallback = new(OverwriteInventory);
            OverwriteInventory();
            PersistentInventoryManager.Current.OnBoosterImplantInventoryChanged.Invoke();
            PersistentInventoryManager.Current.OnBoosterImplantInventoryChanged += m_overwriteCallback;
        }
    }

    public override void OnDisable()
    {
        base.OnDisable();
        if (PersistentInventoryManager.Current != null)
        {
            // Kinda a lazy way to go about this? It's probably fine, most people hopefully won't turn this on/off often
            if (m_overwriteCallback != null)
            {
                PersistentInventoryManager.Current.OnBoosterImplantInventoryChanged -= m_overwriteCallback;
                m_overwriteCallback = null;
            }

            PersistentInventoryManager.Current.m_boosterImplantInventory = new();
            var cancel = new Il2CppSystem.Threading.CancellationTokenSource();
            var task = DropServerManager.Current.GetBoosterImplantPlayerDataAsync(cancel.Token);
            task.Wait();
            if (task.IsCanceled || task.IsFaulted || !task.Result.BoosterData.HasValue)
            {
                FeatureLogger.Warning("Failed to retrieve actual booster data. Clearing out booster inventory!");
                return;
            }

            var result = task.Result.BoosterData.GetValueOrDefault();
            var pam = PersistentInventoryManager.Current.m_boosterImplantInventory;

            pam.New = new(result.New.Count);
            foreach (var item in result.New) pam.New.Add(item);

            pam.Categories[0].Currency = result.Basic.Currency;
            pam.Categories[0].Missed = result.Basic.Missed;
            pam.Categories[0].MissedAcknowledged = result.Basic.MissedAck;
            pam.Categories[0].Inventory = new(result.Basic.Inventory.Count);
            foreach (var item in result.Basic.Inventory) pam.Categories[0].Inventory.Add(new(item));

            pam.Categories[1].Currency = result.Advanced.Currency;
            pam.Categories[1].Missed = result.Advanced.Missed;
            pam.Categories[1].MissedAcknowledged = result.Advanced.MissedAck;
            pam.Categories[1].Inventory = new(result.Advanced.Inventory.Count);
            foreach (var item in result.Advanced.Inventory) pam.Categories[1].Inventory.Add(new(item));

            pam.Categories[2].Currency = result.Specialized.Currency;
            pam.Categories[2].Missed = result.Specialized.Missed;
            pam.Categories[2].MissedAcknowledged = result.Specialized.MissedAck;
            pam.Categories[2].Inventory = new(result.Specialized.Inventory.Count);
            foreach (var item in result.Specialized.Inventory) pam.Categories[2].Inventory.Add(new(item));
        }
    }

    /// <summary>
    /// All implants created / cached by this handler
    /// </summary>
    public IReadOnlyDictionary<BoosterImplantCategory, Il2CppReferenceArray<DropServer.BoosterImplants.BoosterImplantInventoryItem>> CachedImplants
    {
        get
        {
            RegenerateLists();
            return m_cachedImplants!;
        }
    }
    private SortedList<BoosterImplantCategory, Il2CppReferenceArray<DropServer.BoosterImplants.BoosterImplantInventoryItem>>? m_cachedImplants = null;
    public uint NextID { get; protected set; } = 0;
    private Il2CppAction? m_overwriteCallback = null;

    /// <summary>
    /// Ensures that all booster lists are fully generated
    /// </summary>
    public void RegenerateLists(bool force = false)
    {
        if (m_cachedImplants != null && !force) return;
        m_cachedImplants?.Clear();
        m_cachedImplants ??= new();
        NextID = 1;

        SortedList<BoosterImplantCategory, List<DropServer.BoosterImplants.BoosterImplantInventoryItem>> newItems = new(4);
        foreach (var template in BoosterImplantTemplateDataBlock.GetAllBlocks())
        {
            if (!newItems.TryGetValue(template.ImplantCategory, out var list))
            {
                list = new();
                newItems[template.ImplantCategory] = list;
            }
            foreach (var booster in GenerateIdealBoosters(template))
                list.Add(booster);
        }

        // Copy the results to our cache
        foreach (var pair in newItems)
            m_cachedImplants[pair.Key] = pair.Value.ToArray();

    }

    /// <summary>
    /// Creates all possible variants of the provided template with ideal values
    /// </summary>
    public IEnumerable<DropServer.BoosterImplants.BoosterImplantInventoryItem> GenerateIdealBoosters(BoosterImplantTemplateDataBlock template)
    {
        DropServer.BoosterImplants.BoosterImplantInventoryItem baseItem = new()
        {
            Flags = (uint)DropServer.BoosterImplants.BoosterImplantInventoryItemFlags.Touched,
            Effects = new(template.Effects.Count + template.RandomEffects.Count),
            Conditions = new(template.Conditions.Count + (template.RandomConditions.Count > 0 ? 1 : 0)),
            TemplateId = template.persistentID,
            UsesRemaining = (int)template.DurationRange.y,
            Id = 1
        };

        // Setting non-random conditions
        for (int i = 0; i < template.Conditions.Count; i++)
            baseItem.Conditions[i] = template.Conditions[i];

        // Setting non-random effects
        for (int i = 0; i < template.Effects.Count; i++)
        {
            baseItem.Effects[i] = new()
            {
                Id = template.Effects[i].BoosterImplantEffect,
                Param = template.Effects[i].MaxValue,
            };
        }

        // Helper function for generating an unknown number of combinations of boosters
        IEnumerable<DropServer.BoosterImplants.BoosterImplantInventoryItem> GenerateRecursive(int depth)
        {
            if (depth >= template.RandomEffects.Count) // All lists have selected an entry
            {
                // Copy the current base booster and return the copy
                yield return new DropServer.BoosterImplants.BoosterImplantInventoryItem()
                {
                    Flags = baseItem.Flags,
                    Conditions = baseItem.Conditions.ToArray(),
                    Effects = baseItem.Effects.ToArray(),
                    TemplateId = baseItem.TemplateId,
                    UsesRemaining = baseItem.UsesRemaining,
                    Id = NextID++,
                };
            }
            else for (int i = 0; i < template.RandomEffects[depth].Count; i++)
            {
                // Skip the glowstick power effect, to decrease the number of generated boosters (since it's generally ineffective)
                if (template.RandomEffects[depth][i].BoosterImplantEffect == 40 && template.RandomEffects[depth].Count > 1)
                    continue;

                // Choose and set an effect, then iterate one depth deeper
                baseItem.Effects[template.Effects.Count + depth] = new()
                {
                    Id = template.RandomEffects[depth][i].BoosterImplantEffect,
                    Param = template.RandomEffects[depth][i].MaxValue,
                };
                foreach (var booster in GenerateRecursive(depth + 1))
                    yield return booster;
            }
        }

        // If there are no random conditions, we still need to return boosters without a random condition
        if (template.RandomConditions.Count == 0)
        {
            foreach (var booster in GenerateRecursive(0))
                yield return booster;
        }
        else foreach (var condition in template.RandomConditions)
        {
            // Skip the "far from enemy" condition, to decrease the number of generated boosters (because the condition is very hard to satisfy)
            if (condition == 11 && template.RandomConditions.Count > 1) continue;

            // Set the last condition to the random condition and then generate as normal
            baseItem.Conditions[template.Conditions.Count] = condition;
            foreach (var booster in GenerateRecursive(0))
                yield return booster;
        }
    }

    /// <summary>
    /// Get an artifact's energy value
    /// </summary>
    public static long GetArtifactValue(ArtifactCategory cat)
        // I'm just kinda eye-balling these values
        => cat switch
        {
            ArtifactCategory.Common   =>  40_000_000_000L, // About one day in factorio at 40 MW
            ArtifactCategory.Uncommon => 126_000_000_000L, // Approximate geometric mean of the two
            ArtifactCategory.Rare     => 400_000_000_000L, // About one day in factory at 1 GW
            _ => throw new ArgumentException($"Unexpected artifact category: {(int)cat}")
        };

    /// <summary>
    /// When an artifact is picked up, send energy to the multiworld
    /// </summary>
    [ArchivePatch(typeof(ArtifactPickup_Core), nameof(ArtifactPickup_Core.OnInteractionPickUp))]
    public static class ArtifactPickup_Core__OnInteractionPickUp__Patch
    {
        public static void Postfix(ArtifactPickup_Core __instance, PlayerAgent player)
        {
            // Only the host processes this callback, since normally everyone processes it
            if (SNetwork.SNet.Master.Pointer != player.Owner.Pointer) return;

            int currency = DropServer.BoosterImplants.BoosterUtils.BoosterCurrencyFromHeatAndArtifactCount((DropServer.BoosterImplants.BoosterImplantCategory)(int)__instance.m_artifactCategory, 1f, 1);
            float parts = DropServer.BoosterImplants.BoosterUtils.BoosterPartsFromCurrency(currency);

            long energy = (long)(GetArtifactValue(__instance.m_artifactCategory) * parts);

            EnergyLinkHandler.AddEnergy(energy).ContinueWith(t =>
                {
                    if (!t.IsCompletedSuccessfully) FeatureLogger.Error("Failed to add energy when grabbing artifact!");
                });
        }
    }

    public void OverwriteInventory()
    {
        var pMan = PersistentInventoryManager.Current;
        if (pMan.m_boosterImplantInventory.Categories.Sum(c => c.Inventory.Count) == CachedImplants.Sum(pair => pair.Value.Count))
            return;

        FeatureLogger.Debug("Overwriting booster inventory");
        var source = CachedImplants[BoosterImplantCategory.Muted];
        var category = pMan.m_boosterImplantInventory.Categories[0];
        category.Inventory.Clear();
        category.Inventory = new(source.Length);
        foreach (var item in source) category.Inventory.Add(new(item));

        source = CachedImplants[BoosterImplantCategory.Bold];
        category = pMan.m_boosterImplantInventory.Categories[1];
        category.Inventory.Clear();
        category.Inventory = new(source.Length);
        foreach (var item in source) category.Inventory.Add(new(item));

        source = CachedImplants[BoosterImplantCategory.Aggressive];
        category = pMan.m_boosterImplantInventory.Categories[2];
        category.Inventory.Clear();
        category.Inventory = new(source.Length);
        foreach (var item in source) category.Inventory.Add(new(item));

        // Retrigger the event so everything uses our new inventory
        pMan.OnBoosterImplantInventoryChanged.Invoke();
    }

    /// <summary>
    /// When setting up persistent inventory, add a callback which overwrites its contents
    /// </summary>
    [ArchivePatch(typeof(PersistentInventoryManager), nameof(PersistentInventoryManager.Setup))]
    public static class PersistentInventoryManager__Setup__Patch
    {
        public static void Postfix(PersistentInventoryManager __instance)
        {
            var self = ArchipelagoFeatureHelper.GetFeature<ArtifactsAndBoostersHandler>();
            if (self.m_overwriteCallback == null)
            {
                self.m_overwriteCallback = new(self.OverwriteInventory);
                __instance.OnBoosterImplantInventoryChanged += self.m_overwriteCallback;
            }

        }
    }

    /// <summary>
    /// When showing the booster select, extend the list to show all available boosters (and not just the default 20)
    /// </summary>
    [ArchivePatch(typeof(CM_ScrollWindow), nameof(CM_ScrollWindow.SetContentItems))]
    public static class CM_ScrollWindow__SetContentItems__Patch
    {
        public static void Prefix(CM_ScrollWindow __instance, Il2CppSystem.Collections.Generic.List<iScrollWindowContent> contentItems)
        {
            CM_BoosterImplantSlotItem? test = contentItems.FirstOrDefault()?.TryCast<CM_BoosterImplantSlotItem>();
            if (test != null)
            {
                // This is unecessary; if switching categories and we're still looking at the previous category's booster,
                //  null that out for consistency's sake
                if (test.m_parentBar.selectedBoosterImplantItem?.BoosterImplant.Category != test.BoosterImplant.Category)
                    test.m_parentBar.selectedBoosterImplantItem = null;

                var items = PersistentInventoryManager.GetBoosterImplantInventory(test.BoosterImplant.Category);
                while (contentItems.Count < items.Count)
                {
                    GameObject go = GameObject.Instantiate(test.gameObject);
                    CM_BoosterImplantSlotItem item = go.GetComponent<CM_BoosterImplantSlotItem>();

                    // Not sure how many of these setup functions are actually needed
                    item.Setup();
                    item.SetupFromLobby(test.m_guiAlign, test.m_parentBar);
                    item.LoadData(items[contentItems.Count]);
                    item.ID = (int)item.BoosterInstanceID;
                    item.PlayIntro(contentItems.Count);
                    item.SetBackgroundEnabled(true);
                    item.SetIconFromCategory();
                    item.SetVisible(true);

                    // If this item happens to be the prepared item, we can have it be selected by default
                    if (item.IsPreparedSlot)
                        item.m_parentBar.selectedBoosterImplantItem = item;

                    contentItems.Add(item.Cast<iScrollWindowContent>());
                }
            }
        }
    }

    /// <summary>
    /// Prevent alterations to our booster model
    /// </summary>
    [ArchivePatch(typeof(PersistentInventoryManager), nameof(PersistentInventoryManager.ApplyPendingBoosterImplantTransactionsToModel))]
    public static class PersistentInventoryManager__ApplyPendingBoosterImplantTransactionsToModel__Patch
    {
        public static bool Prefix()
        {
            PersistentInventoryManager.Current.ClearPendingBoosterImplantTransactions();
            return false;
        }
    }

    /// <summary>
    /// Prevent the drop server from ever knowing we equipped boosters.
    /// This prevents most (if not all) booster consumption.
    /// We need to prevent booster consumption because, while our model is regenerable, the original
    ///  boosters still exist under the hood and can still be modified by the drop server.
    /// </summary>
    [ArchivePatch(typeof(DropServerManager), nameof(DropServerManager.NewGameSession))]
    public static class DropServerManager__NewGameSession__Patch
    {
        public static void Prefix(ref uint[] boosterIds) => boosterIds = null!;
    }

    /// <summary>
    /// Prevent the drop server from consuming our boosters.
    /// We need to prevent booster consumption because, while our model is regenerable, the original
    ///  boosters still exist under the hood and can still be modified by the drop server.
    /// </summary>
    [ArchivePatch(typeof(DropServerGameSession), nameof(DropServerGameSession.ConsumeBoosters))]
    public static class DropServerGameSession__ConsumeBoosters__Patch
    {
        public static bool Prefix() => false;
    }

    /// <summary>
    /// When preparing a booster, make it depend on energy. If not enough, fail!
    /// </summary>
    [ArchivePatch(typeof(CM_BoosterImplantSlotItem), nameof(CM_BoosterImplantSlotItem.PrepareBoosterImplant))]
    public static class CM_BoosterImplantSlotItem__PrepareBoosterImplant__Patch
    {
        public static bool Prefix(CM_BoosterImplantSlotItem __instance)
        {
            var task = EnergyLinkHandler.RequestEnergy(GetArtifactValue(__instance.BoosterImplant.Category switch
            {
                BoosterImplantCategory.Muted => ArtifactCategory.Common,
                BoosterImplantCategory.Bold => ArtifactCategory.Uncommon,
                BoosterImplantCategory.Aggressive => ArtifactCategory.Rare,
                _ => throw new ArgumentException("Expected booster type to be one of Muted, Bold, or Aggressive"),
            }), true);
            task.Wait();
            return task.IsCompletedSuccessfully;
        }
    }

    /// <summary>
    /// When unpreparing a booster, try to refund it
    /// </summary>
    [ArchivePatch(typeof(CM_BoosterImplantSlotItem), nameof(CM_BoosterImplantSlotItem.UnprepareBoosterImplant))]
    public static class CM_BoosterImplantSlotItem__UnprepareBoosterImplant__Patch
    {
        public static void Postfix(CM_BoosterImplantSlotItem __instance)
        {
            var task = EnergyLinkHandler.AddEnergy(GetArtifactValue(__instance.BoosterImplant.Category switch
            {
                BoosterImplantCategory.Muted => ArtifactCategory.Common,
                BoosterImplantCategory.Bold => ArtifactCategory.Uncommon,
                BoosterImplantCategory.Aggressive => ArtifactCategory.Rare,
                _ => throw new ArgumentException("Expected booster type to be one of Muted, Bold, or Aggressive"),
            }));
        }
    }

}
