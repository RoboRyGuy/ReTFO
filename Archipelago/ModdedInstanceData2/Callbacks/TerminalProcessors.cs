using GameData;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData2.Callbacks;

public static class TerminalProcessors
{
    // Add terminals (and paths to them) to zones
    [ProcessTerminal.Callback]
    public static void AddTerminalRegions(Manager manager, ProcessTerminal.Data data)
    {
        Path path = manager.AddPath(data.ZoneName, data.TerminalName);
        
        // Note the count > 0 check; this is to account for R8A2, which has the locked secret terminal
        if (data.StartingStateData.PasswordProtected && data.StartingStateData.TerminalZoneSelectionDatas.Count > 0)
        {
            string passName = $"{data.TerminalName} Password Part";
            path.required_item = passName;
            path.required_item_count = (uint)data.StartingStateData.PasswordPartCount;

            List<List<int>> regionSets = data.StartingStateData.TerminalZoneSelectionDatas.Select(
                ps => ps.Select(p => manager.GetOrCreateRegion(new ProcessTerminal.Data(data.FindZoneByIndex(p.LocalIndex)!, p.TerminalIndex).TerminalName)).ToList()
            ).ToList();

            for (int i = 1; i <= path.required_item_count; i++)
            {
                manager.AddLocation(new(
                    $"{passName} (Location #{i})",
                    passName,
                    regionSets[(i - 1) % regionSets.Count],
                    true
                ));
            }
        }
    }

    // Helper to process unique commands on terminals. Returns path to unique command region
    public static Path ProcessUniqueCommand(Manager manager, ProcessTerminal.Data data, CustomTerminalCommand command)
    {
        string name = $"{data.TerminalName} Unique Command (\"{command.Command}\")";
        int commandRegion = manager.GetOrCreateRegion(name);

        manager.ProcessEvent.Invoke(manager, new(
            data, command.CommandEvents.Iter().ToList(),
            manager.GetOrCreateRegion(data.TerminalName), name
        ));

        return manager.AddPath(data.TerminalName, commandRegion);
    }

    // Triggers event processing for when unique commands are triggered
    [ProcessTerminal.Callback]
    public static void AddUniqueCommandEvents(Manager manager, ProcessTerminal.Data data)
    {
        foreach (var command in data.UniqueCommands)
            ProcessUniqueCommand(manager, data, command);
    }

    // Processes events which add commands to terminals
    //[ProcessEvent.Callback]
    //public static void AddSetTerminalCommandEvents(Manager manager, ProcessEvent.Data data)
    //{
    //    int count = 0;
    //    foreach (var e in data.Events)
    //    {
    //        if (e.Type != eWardenObjectiveEventType.SetTerminalCommand) 
    //            continue;
    //        count += 1;
    //
    //        // Find the terminal
    //        ProcessZone.Data? targetZone = data.FindZoneByEvent(e);
    //        int terminalIndex = 0;
    //        for (int i = 0; i < (targetZone?.Zone?.SpecificTerminalSpawnDatas?.Count ?? 0); i++)
    //        {
    //            if (targetZone!.Zone!.SpecificTerminalSpawnDatas[i].WorldEventObjectFilter == e.WorldEventObjectFilter)
    //            {
    //                terminalIndex = 1 - i;
    //                break;
    //            }
    //        }
    //        if (terminalIndex == 0)
    //        {
    //            Plugin.Get().Log.LogWarning($"Failed to find target terminal for event: {data.SourceName} - Set Terminal Command Event #{count}");
    //            continue;
    //        }
    //        ProcessTerminal.Data targetTerminal = new(targetZone!, terminalIndex);
    //
    //        string itemName = $"{targetTerminal.TerminalName} Set Unique Command {}";
    //        manager.AddLocation(new(
    //            $"{data.SourceName} - Set Terminal Command Event #{count}",
    //            itemName,
    //            new(1) { data.SourceRegion },
    //            true
    //        ));
    //
    //        Path path = ProcessUniqueCommand(manager, targetTerminal, e.command)
    //
    //        manager.ProcessEvent.Invoke(manager, new(
    //            data, new List<WardenObjectiveEventData>() { commandEvent },
    //            manager.GetOrCreateRegion(new ProcessTerminal.Data(data.FindZoneByIndex(commandEvent.TerminalZoneIndex)!, commandEvent.TerminalIndex).TerminalName),
    //            $"{data.ZoneName} OnAddCommandToTerminal (Command: {commandEvent.CommandToAdd})"
    //        ));
    //
    //    }
    //}
}

