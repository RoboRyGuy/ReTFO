
using BepInEx;
using BepInEx.Logging;
using GameData;
using LevelGeneration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ReTFO.Archipelago.ModdedInstanceData2;

public class Manager
{
    public ProcessExpedition ProcessExpedition { get; set; } = new();
    public ProcessEvent ProcessEvent { get; set; } = new();

    private List<Region>? regions = null;
    public List<Region> Regions
    {
        get { return regions ??= new(); }
        protected set { regions = value; }
    }

    private List<Location>? locations = null;
    public List<Location> Locations
    {
        get { return locations ??= new(); }
        protected set { locations = value; }
    }

    private List<Path>? paths = null;
    public List<Path> Paths
    {
        get { return paths ??= new(); }
        protected set { paths = value; }
    }

    protected Dictionary<string, int> RegionMap = new();   // Reverse index

    // Resets lists and reverse lookup maps
    public void Cleanup()
    {
        Regions = new();
        Locations = new();
        Paths = new();
        RegionMap = new();
    }

    // Creates and adds a region. Returns its index
    public int AddRegion(string name)
    {
        if (RegionMap.ContainsKey(name))
        {
            Plugin.Get().Log.LogWarning($"Region \"{name}\" already exists!");
            return RegionMap[name];
        }

        int index = Regions.Count;
        RegionMap[name] = index;
        Regions.Add(new Region(name));
        return index;
    }

    // Gets a region by name. Throws if it doesn't exist
    public int GetRegion(string name)
    {
        if (RegionMap.TryGetValue(name, out int index))
            return index;
        else
            throw new KeyNotFoundException($"Region \"{name}\" does not exist!");
    }

    // Gets a region by name. Creates it if it doesn't exist
    public int GetOrCreateRegion(string name)
    {
        if (RegionMap.TryGetValue(name, out int index))
            return index;
        else
            return AddRegion(name);
    }

    // Adds a location
    public void AddLocation(Location location)
    {
        Locations.Add(location);
    }

    // Add a path object directly
    public int AddPath(Path path)
    {
        Paths.Add(path);
        return Paths.Count - 1;
    }

    // Convenience method for adding a path by region names
    public Path AddPath(string startingRegion, string endingRegion)
    {
        return AddPath(GetOrCreateRegion(startingRegion), GetOrCreateRegion(endingRegion));
    }

    // Convenience method for adding a path by index and region name
    public Path AddPath(int startingRegion, string endingRegion)
    {
        return AddPath(startingRegion, GetOrCreateRegion(endingRegion));
    }

    // Convenience method for adding a path by region name and index
    public Path AddPath(string startingRegion, int endingRegion)
    {
        return AddPath(GetOrCreateRegion(startingRegion), endingRegion);
    }

    // Convenience method for adding a path by index only
    public Path AddPath(int startingRegion, int endingRegion)
    {
        Path path = new()
        {
            starting_region = startingRegion,
            ending_region = endingRegion
        };
        AddPath(path);
        return path;
    }

    /* Helper for registering methods with an attribute to an event
     *  TAttribute       - The attribute used to identify callbacks
     *  TDelegate        - The delegate to cast to for registration
     *  registerCallback - A lambda which registers the callback to the event (since we can't pass events as parameters)
     */
    public static void RegisterStaticCallbacks<TAttribute, TDelegate>(Action<TDelegate> registerCallback)
        where TAttribute : Attribute
        where TDelegate : System.Delegate
    {
        BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;

        var methods = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); } })
            .SelectMany(t => t.GetMethods(bf))
            .Where(m => m.CustomAttributes.Any(a => a.AttributeType == typeof(TAttribute)));

        foreach (var method in methods)
        {
            TDelegate? del = Delegate.CreateDelegate(typeof(TDelegate), method) as TDelegate;
            if (del == null)
            {
                Plugin.Get().Log.LogWarning($"Failed to register callback {method.DeclaringType?.FullName}.{method.Name} to event; failed to convert to delegate type");
                continue;
            }
            registerCallback(del);
        }
    }

    // Create modded instance data
    public ModdedInstanceData CreateData()
    {
        IEnumerable<ProcessExpedition.Data> UnpackExpeditions(RundownDataBlock rundown)
        {
            int i;
            for (i = 0; i < rundown.TierA.Count; i++) yield return new ProcessExpedition.Data(rundown, eRundownTier.TierA, i);
            for (i = 0; i < rundown.TierB.Count; i++) yield return new ProcessExpedition.Data(rundown, eRundownTier.TierB, i);
            for (i = 0; i < rundown.TierC.Count; i++) yield return new ProcessExpedition.Data(rundown, eRundownTier.TierC, i);
            for (i = 0; i < rundown.TierD.Count; i++) yield return new ProcessExpedition.Data(rundown, eRundownTier.TierD, i);
            for (i = 0; i < rundown.TierE.Count; i++) yield return new ProcessExpedition.Data(rundown, eRundownTier.TierE, i);
        }

        ModdedInstanceData result = new();
        Cleanup();
        
        var datas = RundownDataBlock.GetAllBlocks()
            .Where(r => r.internalEnabled)
            .SelectMany(UnpackExpeditions)
            .Where(d => d.Expedition.Enabled)
        ;

        foreach (var data in datas)
        {
            // Some rundowns (test rundowns, mainly) will cause issues if we try to process them. So we'll skip those
            if ((LevelLayoutDataBlock.GetBlock(data.Expedition.LevelLayoutData)?.Zones?.Count ?? 0) == 0) continue;

            ProcessExpedition.Invoke(this, data);

            // After processing, move into isolated expedition object and add to list
            Expedition expedition = new()
            {
                name = data.GetExpeditionName(),
                regions = Regions,
                locations = Locations,
                paths = Paths,
                start_region = GetOrCreateRegion(new ProcessLayer.Data(data, LG_LayerType.MainLayer).GetFirstZone().GetZoneName()),
                num_sectors = 1 + (data.Expedition.SecondaryLayerEnabled ? 1 : 0) + (data.Expedition.ThirdLayerEnabled ? 1 : 0),
            };
            result.expeditions.Add(expedition);
            Cleanup();

            // Validating expeditions using simple graph traversal // ==================
            List<string> items = new();

            // Starting state
            expedition.regions[expedition.start_region].reachable = true;
            foreach (var loc in expedition.locations)
            {
                if (loc.regions.Count == 1 && loc.regions[0] == expedition.start_region && loc.item != null)
                    items.Add(loc.item);
            }

            int oldCount = 1 + items.Count; // This count tracks how many items + regions we had at the start of each iteration
            while (items.Count(i => i == UnlockExpeditionName) < expedition.num_sectors)
            {
                foreach (var path in expedition.paths)
                {
                    // Whether it's worth checking this path
                    if (!expedition.regions[path.starting_region].reachable) continue;
                    if (expedition.regions[path.ending_region].reachable) continue;

                    // Checking if path is traversable
                    bool isTraversable = path.required_item == null;
                    if (!isTraversable)
                        isTraversable = items.Count(i => i == path.required_item!) >= path.required_item_count;
                    if (!isTraversable && path.alternate_item != null)
                        isTraversable = items.Any(i => i == path.alternate_item!);

                    // If a new region is accessible, collect all items inside it immediately
                    if (isTraversable)
                    {
                        expedition.regions[path.ending_region].reachable = true;
                        foreach (var loc in expedition.locations)
                        {
                            if (loc.item == null) continue;
                            if (!loc.regions.Contains(path.ending_region)) continue;
                            if (loc.regions.Select(r => expedition.regions[r].reachable).Contains(false)) continue;
                            items.Add(loc.item);
                        }
                    }
                }
                int newCount = items.Count + expedition.regions.Count(r => r.reachable);
                if (newCount == oldCount)
                {   // Print prettily formatted error message to help debug
                    Plugin.Get().Log.LogError($"Failed to explore expedition: {expedition.name}");
                    ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
                    ConsoleManager.ConsoleStream.WriteLine("  Blocked paths:");
                    foreach (var path in expedition.paths)
                    {
                        if (!expedition.regions[path.starting_region].reachable) continue;
                        if (expedition.regions[path.ending_region].reachable) continue;

                        ConsoleManager.ConsoleStream.WriteLine($"\n    From: {expedition.regions[path.starting_region].name}");
                        ConsoleManager.ConsoleStream.WriteLine($"      To: {expedition.regions[path.ending_region].name}");
                        ConsoleManager.ConsoleStream.WriteLine($"   Needs: {path.required_item_count}x{path.required_item}");
                        if (path.alternate_item != null) ConsoleManager.ConsoleStream.WriteLine($"      Or: {path.alternate_item}");
                    }
                    ConsoleManager.ConsoleStream.WriteLine();
                    break;
                }
                oldCount = newCount;
            }
        }

        // TODO:
        //  optional_items
        //  filler_items
        //  trap_items

        // Validate the newly generated expedition for pathing issues
        foreach (var expedition in result.expeditions)
        {

        }

        return result;
    }

    // Progresses unlocking the next expedition. You'd need as many as are currently available to trigger the unlock
    public string UnlockExpeditionName => "Unlock Next Expedition";

    // Unlocks the next expedition. Awared only for clearing the main sector. For main-only randomization option
    public string UnlockExpeditionMainOnlyName => "Unlock Next Expedition (Main Only)";

}
