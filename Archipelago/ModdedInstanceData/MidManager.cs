
using BepInEx;
using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.Features.ObjectiveHandlers;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;

namespace ReTFO.Archipelago.ModdedInstanceData;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

// Wraps modded instance data; creates it and manages its lifetime
public class MidManager
{
    public bool IsLoaded { get; protected set; } = false;
    protected Game.Data? GameData { get; set; } = null;
    protected Dictionary<string, Expedition.Data> ExpeditionLookup { get; init; } = new();

    // Invalidate the current modded instance data, if there is any
    public void InvalidateModdedInstanceData()
    {
        IsLoaded = false;
        var data = GetUnprocessedGameData();
        data.LocationList.Clear();
        data.LocationLookup.Clear();
        data.ItemList.Clear();
        data.ItemLookup.Clear();
        ExpeditionLookup.Clear();
    }

    // Make / retrieve game data for use during processing
    // Use this to add processors to gameData prior to it being processed
    public Game.Data GetUnprocessedGameData()
    {
        if (GameData != null) return GameData;

        GameData = Game.MakeData();

        var gameProcessor = new Game.Processor();
        GameData.RegisterProcessor(gameProcessor);

        var expeditionProcessor = new Expedition.Processor().SubscribedTo(gameProcessor);
        GameData.RegisterProcessor(expeditionProcessor);

        var layerProcessor = new Layer.Processor().SubscribedTo(expeditionProcessor);
        GameData.RegisterProcessor(layerProcessor);

        var zoneProcessor = new Zone.Processor().SubscribedTo(layerProcessor);
        GameData.RegisterProcessor(zoneProcessor);

        var terminalProcessor = new Terminal.Processor().SubscribedTo(zoneProcessor);
        GameData.RegisterProcessor(terminalProcessor);

        var objectiveProcessor = new Objective.Processor().SubscribedTo(layerProcessor);
        GameData.RegisterProcessor(objectiveProcessor);

        var eventProcessor = new Event.Processor();
        GameData.RegisterProcessor(eventProcessor);

        return GameData;
    }

    // Make / retrieve game data for use post processing
    // Ensures processing was performed before returning
    public Game.Data GetProcessedGameData()
    {
        ProcessData();
        return GetUnprocessedGameData();
    }

    // Regenerate the expedition list
    public void ProcessData()
    {
        if (IsLoaded) return;
        IsLoaded = true;

        var gameData = GetUnprocessedGameData();
        gameData.GameProcessor.Process(gameData);
        DoGraphTraversal(gameData, false, true, null, -1, true);

        // We've most likely touched these blocks, so we're going to mark them dirty. Not sure if this really does anything
        RundownDataBlock.FileDirty = true;
        LevelLayoutDataBlock.FileDirty = true;
        WardenObjectiveDataBlock.FileDirty = true;
        DimensionDataBlock.FileDirty = true;
        TextDataBlock.FileDirty = true;
    }

    // This little bit is stolen straight from https://stackoverflow.com/a/21953690
    [DllImport("shell32", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, nint hToken = 0);
    private static Guid DownloadsGUID => new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>
    /// Export game data as a JSON file to the designated path.
    /// </summary>
    /// <param name="filename">
    /// The full path of the file to export to.
    /// If null, defaults to a file in the downloads folder.
    /// </param>
    public void ExportGameData(string? filename = null)
    {
        if (filename == null)
            filename = System.IO.Path.Combine(SHGetKnownFolderPath(DownloadsGUID, 0), "moddedInstanceData.json");

        Game.Data gameData = GetProcessedGameData();
        JsonSerializerSettings settings = new()
        {
            Formatting = Formatting.None,
        };
        settings.Converters.Add(new Clonesoft.Json.Converters.StringEnumConverter());
        settings.Converters.Add(new Utilities.IntListConverer());
        string json = JsonConvert.SerializeObject(gameData, settings);
        File.WriteAllText(filename, json);
    }

    /// <summary>
    ///   Performs a graph traversal on the provided Game.Data, optionally performing processing / modifications along the way
    /// </summary>
    /// <param name="gameData">The Game.Data data to traverse</param>
    /// <param name="hasDirectReqs">If true, the provided Game.Data has had its direct requirements evaluated already</param>
    /// <param name="doProcessing">If true, overwrite the region reachability. If not hasDirectReqs, also calculate and add direct path requirements</param>
    /// <param name="startingItemCategories">
    ///   Randomization categories to pull from floating items. Items matching these categories are added to the starting inventory.
    ///   If null, defaults to just <see cref="UnlockExpeditionHandler.ExpeditionUnlocksCat"/>, which enables traversal of all expeditions.
    /// </param>
    /// <param name="expectedSectorCount">
    ///   How many sectors to expect. When exploration is complete, it will check against this count to test for success.
    ///   Defaults to -1, which will have it calculate how many sector clear items there are total.
    /// </param>
    /// <param name="logDebugInfo">If true and the Game.Data is not beatable, log info describing the stuck state to help debug why it's considered unbeatable</param>
    /// <returns>True if the Game.Data can be fully traversed (all sectors cleared), false otherwise</returns>
    /// <remarks>
    ///   Direct requirements refers to the sum of consumable items required to reach a region. A brief explanation:
    ///   <list type="bullet">
    ///    <item>
    ///      When Game.Data is originally generated, each path is calculated using the exact count of items needed to traverse 
    ///      only that path
    ///    </item>
    ///    <item>
    ///      However, Archipelago cannot use that information correctly because it cannot account for items which are "consumed"; 
    ///      It simply assumes once you have an item you can use it infinite times, once on each path
    ///    </item>
    ///    <item>
    ///      For GTFO, we solve this by increasing the number of items needed to traverse each path to account for the amount 
    ///      needed to reach that path. This is called the "Direct Requirements"
    ///    </item>
    ///    <item>
    ///      This is not a perfect solution because sometimes you need more of that item to get some other requisite items
    ///      in different region(s). This would be considered an "Indirect Requirement", and we do not calculate those.
    ///    </item>
    ///    <item>
    ///      Indirect requirements like these are a standing issue in Archipelago, and while I'd love to fix it I'm simply too lazy :)
    ///      <br />
    ///      Hopefully the direct requirements system will be sufficient for GTFO
    ///    </item>
    ///   </list>
    ///   It's also worth noting that while I made logDebugInfo as good as I can, it can help to place a break on the final 
    ///    print statement for the debug logging (so you can examine the full state yourself using the debugger)
    /// </remarks>
    public static bool DoGraphTraversal(Game.Data gameData, bool hasDirectReqs = true, bool doProcessing = false, HashSet<string>? startingItemCategories = null, int expectedSectorCount = -1, bool logDebugInfo = true)
    {
        startingItemCategories ??= new() { UnlockExpeditionHandler.ExpeditionUnlocksCat };

        // Progress tracking
        Dictionary<string, int> items;
        List<Item> itemsActuals;
        List<Dictionary<string, int>?> usedItems
            = hasDirectReqs ? new(0)
            : Enumerable.Repeat<Dictionary<string, int>?>(null, gameData.RegionList.Count).ToList();
        List<bool> isReachable
            = doProcessing ? new List<bool>(0)
            : Enumerable.Repeat(false, gameData.RegionList.Count).ToList();
        List<Path> paths;
        if (doProcessing)
            foreach (var region in gameData.RegionList) region.Reachable = false;

        // Reachability for a reagion
        bool getReachable(int region) => doProcessing ? gameData.LookupRegion(region).Reachable : isReachable[region];
        bool setReachable(int region) => doProcessing ? (gameData.LookupRegion(region).Reachable = true) : (isReachable[region] = true);

        // Item helpers
        int usedItemCount(int region, string item) => hasDirectReqs ? 0 : usedItems[region]!.GetValueOrDefault(item, 0);
        int availableItemCount(int region, string item)
        {
            int totalCount = items.GetValueOrDefault(item, 0);
            int usedCount = usedItemCount(region, item);
            return totalCount - usedCount;
        }

        // Starting state
        int startingRegion = gameData.GetOrCreateRegion(gameData.MenuRegionName);
        setReachable(startingRegion);
        if (!hasDirectReqs) usedItems[startingRegion] = new(0);

        itemsActuals = Enumerable.Empty<Item>()
            .Concat( // Items in locations in the starting region
                gameData.RegionList[startingRegion].ConnectedLocationIds
                  .Select(gameData.LookupLocation)
                  .Where(l => (l.OwningRegionIds.Count == 1) && (l.OwningRegionIds[0] == startingRegion))
                  .Where(l => l.ItemID != 0)
                  .Select(l => gameData.LookupItem(l.ItemID))
            ).Concat( // Floating items matching startingItemCategories 
                gameData.FloatingItemIds
                  .Select(gameData.LookupItem)
                  .Where(i => i.RandomizationCategories.Any(startingItemCategories.Contains))
            ).ToList();

        items = itemsActuals
            .SelectMany(i => i.Categories.AsEnumerable().Prepend(i.Name))
            .GroupBy(i => i)
            .ToDictionary(g => g.Key!, g => g.Count());

        paths = new(gameData.LookupRegion(startingRegion).ConnectedPaths);

        // Traversal iterations
        int oldCount = 0;
        int newCount = 1 + items.Values.Sum();
        while (newCount > oldCount)
        {
            oldCount = newCount;
            for (int i = 0; i < paths.Count; i++)
            {
                // Whether it's worth checking this path
                Path path = paths[i];
                if (getReachable(path.EndingRegion))
                {
                    paths.RemoveAt(i--);
                    continue;
                }

                // Checking if path is traversable
                bool hasMainReqs
                    = path.RequiredItem == null ? true
                    : availableItemCount(path.StartingRegion, path.RequiredItem) >= path.RequiredItemCount;

                bool hasCategoryReqs
                    = path.CategoryItem == null ? true
                    : availableItemCount(path.StartingRegion, path.CategoryItem) >= path.CategoryItemCount;

                bool hasAlternateReqs
                    = path.AlternateItem == null ? false
                    : availableItemCount(path.StartingRegion, path.AlternateItem) > 0;

                if ((hasMainReqs && hasCategoryReqs) || hasAlternateReqs)
                {
                    setReachable(path.EndingRegion);
                    ++newCount;
                    paths.RemoveAt(i--);
                    paths.AddRange(gameData.LookupRegion(path.EndingRegion).ConnectedPaths);

                    // Since this is the first time we're here, mark the direct requirements
                    if (!hasDirectReqs)
                    {
                        usedItems[path.EndingRegion] = new(usedItems[path.StartingRegion]!);
                        if (path.RequiredItem != null)
                        {
                            int usedCount = usedItemCount(path.StartingRegion, path.RequiredItem);
                            if (!hasAlternateReqs)
                                usedItems[path.EndingRegion]![path.RequiredItem] = checked(usedCount + (int)path.RequiredItemCount);
                            if (doProcessing)
                                path.RequiredItemCount = checked(path.RequiredItemCount + (uint)usedCount);
                        }
                        if (path.CategoryItem != null)
                        {
                            int usedCount = usedItemCount(path.StartingRegion, path.CategoryItem);
                            if (!hasAlternateReqs)
                                usedItems[path.EndingRegion]![path.CategoryItem] = checked(usedCount + (int)path.CategoryItemCount);
                            if (doProcessing)
                                path.CategoryItemCount = checked(path.CategoryItemCount + (uint)usedCount);
                        }
                    }

                    // Collect all locations newly available because of this region
                    foreach (var loc in gameData.RegionList[path.EndingRegion].ConnectedLocationIds.Select(gameData.LookupLocation))
                    {
                        if (loc.ItemID == 0) continue;
                        if (!loc.OwningRegionIds.Contains(path.EndingRegion)) continue;
                        if (loc.OwningRegionIds.Any(r => !getReachable(r))) continue;
                        Item item = gameData.LookupItem(loc.ItemID);
                        itemsActuals.Add(item);
                        foreach (var name in item.Categories.Prepend(item.Name))
                            items[name] = items.GetValueOrDefault(name, 0) + 1;
                        ++newCount;
                    }
                }
            }
        }

        // If we stopped making progress and have not won...
        var sectorCounts = expectedSectorCount != -1 
            ? expectedSectorCount 
            : gameData.LocationList.Select(l => l.ItemID).Where(i => i != 0).Count(i => gameData.LookupItem(i).RandomizationCategories.Contains(SharedObjectiveHandler.SectorClearsCat));
        var currentSectorCounts = itemsActuals.Count(item => item.RandomizationCategories.Contains(SharedObjectiveHandler.SectorClearsCat));
        if (sectorCounts > currentSectorCounts)
        {
            if (!logDebugInfo) return false;

            // Print prettily formatted error message to help debug
            FeatureLogger.Error($"Graph traversal failed for game!");

            ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
            ConsoleManager.ConsoleStream.WriteLine("\n    Regions:");

            for (int i = 0; i < gameData.RegionList.Count; i++)
            {
                bool reachable = getReachable(i);
                if (reachable) ConsoleManager.SetConsoleColor(ConsoleColor.Green);
                else ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                ConsoleManager.ConsoleStream.WriteLine($"  {(reachable ? "[ Reachable ]" : "[Unreachable]")} [{i.ToString("000")}] {gameData.RegionList[i].Name}");
            }
            if (gameData.RegionList.Count == 0)
                ConsoleManager.ConsoleStream.WriteLine($"\n  NO REGIONS FOUND");

            ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
            ConsoleManager.ConsoleStream.WriteLine("\n    Blocked paths:");

            foreach (var path in paths)
            {
                ConsoleManager.ConsoleStream.WriteLine();
                ConsoleManager.ConsoleStream.WriteLine($"  Name:  {path.Name ?? "None"}");
                ConsoleManager.SetConsoleColor(ConsoleColor.Green);
                ConsoleManager.ConsoleStream.WriteLine($"  Start: [{path.StartingRegion.ToString("000")}] {gameData.RegionList[path.StartingRegion].Name}");
                ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                ConsoleManager.ConsoleStream.WriteLine($"  End:   [{path.EndingRegion.ToString("000")}] {gameData.RegionList[path.EndingRegion].Name}");
                ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
                ConsoleManager.ConsoleStream.WriteLine($"  Main:  {(usedItemCount(path.StartingRegion, path.RequiredItem!) + path.RequiredItemCount).ToString("000")}x {path.RequiredItem!}");
                ConsoleManager.ConsoleStream.WriteLine($"  Cat:   {(usedItemCount(path.StartingRegion, path.CategoryItem!) + path.CategoryItemCount).ToString("000")}x {path.CategoryItem!}");
                ConsoleManager.ConsoleStream.WriteLine($"  Alt:   001x {path.AlternateItem}");
            }
            if (paths.Count == 0)
                ConsoleManager.ConsoleStream.WriteLine($"\n  NO BLOCKED PATHS FOUND");

            ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
            ConsoleManager.ConsoleStream.WriteLine("\n    Notable unfound locations:");

            HashSet<string> neededItems = Enumerable.Empty<string>()
                .Concat(paths.Select(p => p.RequiredItem!))
                .Concat(paths.Select(p => p.CategoryItem!))
                .Concat(paths.Select(p => p.AlternateItem!)
                .OfType<string>()).Distinct().ToHashSet();
            bool printedLocation = false;

            var locs = Enumerable.Range(0, gameData.RegionList.Count)
                .SelectMany(i => gameData.LookupRegion(i).ConnectedLocationIds)
                .Distinct()
                .Select(gameData.LookupLocation);

            foreach (var loc in locs)
            {
                if (loc.ItemID == 0) continue;
                Item item = gameData.LookupItem(loc.ItemID);
                if (item.Categories.Prepend(item.Name).All(n => !neededItems.Contains(n))) continue;
                if (loc.OwningRegionIds.All(getReachable)) continue;
                printedLocation = true;

                ConsoleManager.ConsoleStream.WriteLine();
                ConsoleManager.ConsoleStream.WriteLine($"  Name: {loc.Name}");
                ConsoleManager.ConsoleStream.WriteLine($"  Item: {item.Name}");
                if (item.Categories.Count > 0)
                {
                    ConsoleManager.ConsoleStream.WriteLine($"  Categories:");
                    foreach (var cat in item.Categories)
                        ConsoleManager.ConsoleStream.WriteLine($"   \"{cat}\"");

                }
                ConsoleManager.ConsoleStream.WriteLine($"  Regions:");
                if (loc.OwningRegionIds.Count == 0)
                {
                    ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                    ConsoleManager.ConsoleStream.WriteLine("  LOCATION HAS NO REGIONS AND CANNOT BE DISCOVERED");
                }
                else foreach (var i in loc.OwningRegionIds)
                {
                    bool reachable = getReachable(i);
                    if (reachable) ConsoleManager.SetConsoleColor(ConsoleColor.Green);
                    else ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                    ConsoleManager.ConsoleStream.WriteLine($"   {(reachable ? "[ Reachable ]" : "[Unreachable]")} [{i.ToString("000")}] {gameData.RegionList[i].Name}");
                }
                ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
            }
            if (neededItems.Count == 0)
                ConsoleManager.ConsoleStream.WriteLine($"\n  NO NEEDED ITEMS FOUND");
            else if (!printedLocation)
                ConsoleManager.ConsoleStream.WriteLine($"\n  NO NOTABLE LOCATIONS FOUND");

            ConsoleManager.ConsoleStream.WriteLine();
            return false;
        }

        return true;
    }

    // Register an expedition by name with this ModdedInstanceData. Returns false if a duplicate exists
    public bool TryRegisterExpedition(string name, Expedition.Data expedition)
        => ExpeditionLookup.TryAdd(name, expedition);

    // Look up an expedition pair
    public Expedition.Data? LookupExpedition(string name)
    {
        ProcessData();
        return ExpeditionLookup.GetValueOrDefault(name);
    }
}
