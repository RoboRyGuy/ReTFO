using AIGraph;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Enemies;
using GameData;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature.Members;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Core.Localization;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features;

public class DeathLinkHandler : ArchipelagoFeature
{
    public override string Name => "DeahtLink";
    public override string Description
        => "Handles death link";
    public override FeatureGroup Group => FeatureGroups.Archipelago;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    [FeatureConfig]
    public static Settings Config { get; set; }

    public class Settings
    {
        [Localized]
        public enum TriggerType
        {
            OnHumanPlayerDowned,
            OnAnyDowned,
            OnExpeditionFail,
        }

        [Localized]
        public enum EffectType
        {
            SpawnTanks,
            SpawnSnatchers,
            SpawnNightmareScouts,
            DealDamage,
            DeleteResources,
            DownRandomPlayer,
            DownRandomHumanPlayer,
        }

        [FSDisplayName("DeathLink Trigger")]
        [FSDescription(
            "What in GTFO triggers a DeathLink event to be sent to the multiverse."
            + "\n - OnHumanPlayerDowned => When the number of human (non-bot) players downed meets or exceeds the threshold."
            + "\n - OnAnyDowned         => When the number of players (including bots) downed meets or exceeds the threshold."
            + "\n - OnExpeditionFail    => When an expedition ends in failure (not success, not abort)."
        )]
        public TriggerType Trigger { get; set; } = TriggerType.OnExpeditionFail;

        [FSDisplayName("DeathLink Trigger Threshold")]
        [FSDescription("The number of players who must be downed at the same time for the DeathLink to trigger")]
        public int TriggerThreshold { get; set; } = 1;

        [FSDisplayName("DeathLink Effect")]
        [FSDescription(
            "What in GTFO happens when a DeathLink event is received from the multiverse."
            + "\n - SpawnTanks            => Spawn the supplied number of tanks (immediately aggressive)."
            + "\n - SpawnSnatchers        => Spawn the supplied number of snatchers. They are spawned one at a time in 1-5 minute intervals."
            + "\n - SpawnNightmareScouts  => Spawn the supplied number of nightmare scouts (still asleep) in random unoccupied zones."
            + "\n - DealDamage            => The supplied percent is dealt as damage to each player, killing them if necessary."
            + "\n - DeleteResources       => The supplied percent is dealt as damage AND deleted from all gear for each player."
            + "\n                            If this would kill a player, they are instead left at 1% health"
            + "\n - DownRandomPlayer      => The supplied number of players are randomly picked and immediately downed."
            + "\n - DownRandomHumanPlayer => The supplied number of players are immediately downed, randomly picked with a priority for humans."
        )]
        public EffectType Effect { get; set; } = EffectType.DownRandomPlayer;

        [FSDisplayName("DeathLink Effect Number")]
        [FSDescription("The number used in numerous DeathLink effects. If a percent, scale from 0 to 100.")]
        public int EffectNumber { get; set; } = 1;

        [FSDisplayName("DeathLink Cooldown (Seconds)")]
        [FSDescription("Time between DeathLink events, since receing a DeathLink otherwise can very quickly result in another being sent out.")]
        public float Cooldown { get; set; } = 10f;
    }

    private static DeathLinkService? s_service = null;
    private static float LastTriggerTime = 0f;

    public override void OnEnable()
    {
        base.OnEnable();
        StateTracker stateTracker = StateTracker.Get();
        stateTracker.OnStateChange += OnStateTrackerStateChange;
        s_service ??= stateTracker.ApSession?.CreateDeathLinkService();
        if (s_service != null)
        {
            s_service.OnDeathLinkReceived += TryTriggerDeathLink;
            s_service?.EnableDeathLink();
        }
    }

    public override void OnDisable()
    {
        base.OnDisable();
        StateTracker stateTracker = StateTracker.Get();
        stateTracker.OnStateChange -= OnStateTrackerStateChange;
        s_service?.DisableDeathLink();
    }

    public static void OnStateTrackerStateChange(StateTracker stateTracker)
    {
        if ((UnityEngine.Time.realtimeSinceStartup - Config.Cooldown) <= LastTriggerTime)
            return;
        LastTriggerTime = UnityEngine.Time.realtimeSinceStartup;

        s_service ??= StateTracker.Get().ApSession?.CreateDeathLinkService();
        if (s_service != null)
        {
            s_service.OnDeathLinkReceived += TryTriggerDeathLink;
            s_service?.EnableDeathLink();
        }
    }

    public static string HostName => $"{StateTracker.Get().ApSession?.Players.ActivePlayer.Name ?? SNetwork.SNet.Master.NickName}";

    /// <summary>
    /// Receive a DeathLink event and trigger the effect
    /// </summary>
    /// <param name="data">Event data received from the multiverse</param>
    public static void TryTriggerDeathLink(DeathLink data)
    {
        FeatureLogger.Debug("Received DeathLink event");
        if (!GameStateManager.IsInExpedition) return; // Spared, for now...

        if ((UnityEngine.Time.realtimeSinceStartup + Config.Cooldown) <= LastTriggerTime)
            return;
        LastTriggerTime = UnityEngine.Time.realtimeSinceStartup;

        if (data.Cause != null)
            GuiManager.PlayerLayer.m_gameEventLog.AddLogItem($"<#F0F>[Death]</color> {data.Cause}");
            //PlayerChatManager.WantToSentTextMessage(PlayerManager.GetLocalPlayerAgent(), data.Cause);
    
        const uint SingleEnemyWave = 30; // Vanilla survival wave settings which spawns a single enemy (filtered to weakling)
        switch (Config.Effect)
        {
            case Settings.EffectType.SpawnTanks:
                const uint TankPop = 16; // Vanilla survival wave population which spawns only tanks
                WardenObjectiveEventData tankData = new()
                {
                    Type = eWardenObjectiveEventType.SpawnEnemyWave,
                    EnemyWaveData = new()
                    {
                        AreaDistance = 2,
                        IntelMessage = 0,
                        SpawnDelay = 1f,
                        TriggerAlarm = false,
                        WavePopulation = TankPop,
                        WaveSettings = SingleEnemyWave,
                        WorldEventObjectFilterSpawnPoint = null
                    },
                    Delay = 0f,
                };
                for (int i = 0; i < Config.EffectNumber; i++)
                    WorldEventManager.ExecuteEvent(tankData);
                break;

            case Settings.EffectType.SpawnSnatchers:
                const uint SnatcherPop = 56; // Vanilla survival wave population which spawns only snatchers
                WardenObjectiveEventData snatcherData = new()
                {
                    Type = eWardenObjectiveEventType.SpawnEnemyWave,
                    EnemyWaveData = new()
                    {
                        AreaDistance = 2,
                        IntelMessage = 0,
                        SpawnDelay = 1f,
                        TriggerAlarm = false,
                        WavePopulation = SnatcherPop,
                        WaveSettings = SingleEnemyWave,
                        WorldEventObjectFilterSpawnPoint = null
                    },
                    Delay = 0f,
                };
                for (int i = 0; i < Config.EffectNumber; i++)
                {
                    WorldEventManager.ExecuteEvent(snatcherData);
                    snatcherData.Delay = 60f + Random.Shared.NextSingle() * 240f;
                }
                break;

            case Settings.EffectType.SpawnNightmareScouts:
                const uint NightmareScoutID = 56; // Vanilla ID for nightmare scouts

                // Collect all nodes and filter out nodes fewer than two rooms
                HashSet<IntPtr> nodes = AIG_CourseNode.s_allNodes.Select(n => n.Pointer).ToHashSet();
                foreach (var player in SNetwork.SNet.LobbyPlayers)
                {
                    PlayerAgent? agent = player?.PlayerAgent?.TryCast<PlayerAgent>();
                    if (agent?.CourseNode == null) continue;

                    nodes.Remove(agent.CourseNode.Pointer);
                    foreach (var connectedNode in agent.CourseNode.m_portals.Select(p => p.GetOppositeNode(agent.CourseNode)))
                        nodes.Remove(connectedNode.Pointer);
                }

                // Spawn up to one scout per node
                int count = Config.EffectNumber;
                while (count-- > 0 && nodes.Count > 0)
                {
                    IntPtr nodeChoicePtr = nodes.ElementAt(Random.Shared.Next(0, nodes.Count));
                    nodes.Remove(nodeChoicePtr);
                    AIG_CourseNode nodeChoice = new(nodeChoicePtr);
                    EnemyAgent.SpawnEnemy(
                        NightmareScoutID,
                        nodeChoice.GetRandomPositionInside(),
                        nodeChoice,
                        Agents.AgentMode.Scout
                    );
                }
                break;

            case Settings.EffectType.DealDamage:
                foreach (var player in SNetwork.SNet.LobbyPlayers)
                {
                    // Max health is 25
                    PlayerAgent? agent = player?.PlayerAgent?.TryCast<PlayerAgent>();
                    agent?.Damage.FallDamage(Config.EffectNumber * .25f);
                }
                break;

            case Settings.EffectType.DeleteResources:
                foreach (var player in SNetwork.SNet.LobbyPlayers)
                {
                    // Max health is 25
                    PlayerAgent? agent = player?.PlayerAgent?.TryCast<PlayerAgent>();
                    float damage = MathF.Min(((agent?.Damage.Health ?? 0f) - .25f) * 25f, Config.EffectNumber * .25f);
                    agent?.Damage.FallDamage(damage);

                    // Max standard is 460, max special is 260, max tool is 150
                    PlayerBackpack backpack = PlayerBackpackManager.GetBackpack(player);
                    backpack.AmmoStorage.StandardAmmo.AmmoInPack -= MathF.Min(Config.EffectNumber * 4.6f, backpack.AmmoStorage.StandardAmmo.AmmoInPack);
                    backpack.AmmoStorage.SpecialAmmo.AmmoInPack -= MathF.Min(Config.EffectNumber * 2.3f, backpack.AmmoStorage.SpecialAmmo.AmmoInPack);
                    backpack.AmmoStorage.ClassAmmo.AmmoInPack -= MathF.Min(Config.EffectNumber * 1.5f, backpack.AmmoStorage.ClassAmmo.AmmoInPack);
                }
                break;

            case Settings.EffectType.DownRandomPlayer:
                List<PlayerAgent> livingPlayers = SNetwork.SNet.LobbyPlayers
                    .Select(p => p.PlayerAgent.Cast<PlayerAgent>())
                    .Where(p => p.Alive)
                    .ToList();
                int remainingKills = Config.EffectNumber;
                while (remainingKills-- > 0 && livingPlayers.Count > 0)
                {
                    PlayerAgent choice = livingPlayers[Random.Shared.Next(0, livingPlayers.Count)];
                    livingPlayers.Remove(choice);
                    choice.Damage.FallDamage(100_000_000f); // Overkill >:)
                }
                break;

            case Settings.EffectType.DownRandomHumanPlayer:
                List<PlayerAgent> livingHumans = SNetwork.SNet.LobbyPlayers
                    .Where(p => !p.IsBot)
                    .Select(p => p.PlayerAgent.Cast<PlayerAgent>())
                    .Where(p => p.Alive)
                    .ToList();
                int remainingDeaths = Config.EffectNumber;
                while (remainingDeaths-- > 0 && livingHumans.Count > 0)
                {
                    PlayerAgent choice = livingHumans[Random.Shared.Next(0, livingHumans.Count)];
                    livingHumans.Remove(choice);
                    choice.Damage.FallDamage(100_000_000f); // Overkill >:)

                    // This can trigger twice; once when all humans are dead, again when all bots are dead. It'll still work fine though
                    if (remainingDeaths > 0 && livingHumans.Count == 0)
                    {
                        livingHumans = SNetwork.SNet.LobbyPlayers
                            .Select(p => p.PlayerAgent.Cast<PlayerAgent>())
                            .Where(p => p.Alive)
                            .ToList();

                    }
                }
                break;

            default:
                FeatureLogger.Error("Unrecognized DeathLink effect: " + (int)Config.Effect);
                break;
        }
    }

    /// <summary>
    /// Notify that the conditions for a death link have been met
    /// </summary>
    /// <param name="cause">A full death message ("RoboRyGuy was pulverised by Big Charger")</param>
    public static void NotifyDied(string cause)
    {
        if ((UnityEngine.Time.realtimeSinceStartup - Config.Cooldown) <= LastTriggerTime)
            return;
        LastTriggerTime = UnityEngine.Time.realtimeSinceStartup;

        FeatureLogger.Debug("Sending DeathLink event: " + cause);
        s_service?.SendDeathLink(new DeathLink(HostName, cause));
        GuiManager.PlayerLayer.m_gameEventLog.AddLogItem($"<#F0F>[Death]</color> {cause}");
    }

    /// <summary>
    /// Common code for checking deaths when any team member is downed.
    /// </summary>
    /// <param name="__instance">The damage instance for the player downed</param>
    /// <param name="data">The received downed packet data</param>
    public static void CheckDeath(Dam_PlayerDamageBase __instance, pSetDeadData data)
    {
        if (!__instance.Owner.Alive) return;

        int downedCount;
        if (Config.Trigger == Settings.TriggerType.OnAnyDowned)
            downedCount = SNetwork.SNet.LobbyPlayers.Where(p => p.PlayerAgent.Cast<PlayerAgent>().Alive).Count();
        else if (Config.Trigger == Settings.TriggerType.OnHumanPlayerDowned && !__instance.Owner.Owner.IsBot)
            downedCount = SNetwork.SNet.LobbyPlayers.Where(p => !p.IsBot && p.PlayerAgent.Cast<PlayerAgent>().Alive).Count();
        else return;

        if ((Config.TriggerThreshold != 0) && (downedCount % Config.TriggerThreshold != 0))
            return;

        NotifyDied($"Team {HostName} suffering casualties. Objective success compromised.");
    }

    [ArchivePatch(typeof(Dam_PlayerDamageBase), nameof(Dam_PlayerDamageBase.ReceiveSetDead))]
    public static class Dam_PlayerDamageBase__ReceiveSetDead__Patch
    {
        public static void Postfix(Dam_PlayerDamageBase __instance, pSetDeadData data)
            => CheckDeath(__instance, data);
    }

    [ArchivePatch(typeof(Dam_PlayerDamageLocal), nameof(Dam_PlayerDamageLocal.ReceiveSetDead))]
    public static class Dam_PlayerDamageLocal__ReceiveSetDead__Patch
    {
        public static void Postfix(Dam_PlayerDamageBase __instance, pSetDeadData data)
            => CheckDeath(__instance, data);
    }

    [ArchivePatch(typeof(RundownManager), nameof(RundownManager.OnExpeditionEnded))]
    public static class RundownManager__OnExpeditionEnded__Patch
    {
        public static void Postfix(ExpeditionEndState endState)
        {
            if (endState == ExpeditionEndState.Fail && Config.Trigger == Settings.TriggerType.OnExpeditionFail)
                NotifyDied($"Team {HostName} marked MIA.");
        }
    }

}
