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
        
        if (data.TerminalData.StartingStateData.PasswordProtected)
        {
            string passName = $"{data.TerminalName} Password Part";
            path.required_item = passName;
            path.required_item_count = (uint)data.TerminalData.StartingStateData.PasswordPartCount;

            List<List<int>> regionSets = data.TerminalData.StartingStateData.TerminalZoneSelectionDatas.Select(
                ps => ps.Select(p => manager.GetOrCreateRegion(new ProcessTerminal.Data(data.FindZoneByIndex(p.LocalIndex), p.TerminalIndex).TerminalName)).ToList()
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

}
