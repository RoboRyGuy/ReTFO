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
using TheArchive.Core.FeaturesAPI.Components;
using TheArchive.Core.Localization;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features;

public class DeathLinkHandler : ArchipelagoFeature
{
    public override string Name => "Death Link";
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
    public static Settings Config { get; set; } = null!;

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
            + "\n\n<u>On Human Player Downed</u>" + "\nWhen the number of human (non-bot) players downed meets or multiples the threshold, or on expedition fail."
            + "\n\n<u>On Any Downed</u>" + "\nWhen the number of players (including bots) downed meets or multiples the threshold, or on expedition fail."
            + "\n\n<u>On Expedition Fail</u>" + "\nWhen an expedition ends in failure (not success, not abort)."
        )]
        public TriggerType Trigger { get; set; } = TriggerType.OnExpeditionFail;

        [FSDisplayName("DeathLink Trigger Threshold")]
        [FSDescription("The number of players who must be downed at the same time for the DeathLink to trigger. 1 will trigger anytime someone is downed; 2 on 2, 4, 6, ... downed; 3 on 3, 6, ...; etc ")]
        public int TriggerThreshold { get; set; } = 2;

        [FSDisplayName("DeathLink Effect")]
        [FSDescription(
            "What in GTFO happens when a DeathLink event is received from the multiverse."
            + "\n\n<u>Spawn Tanks</u>" + "\nSpawn the supplied number of tanks (immediately aggressive)."
            + "\n\n<u>Spawn Snatchers</u>" + "\nSpawn the supplied number of snatchers. One is spawned immediately, the rest are chain-spawned in 1-5 minute intervals."
            + "\n\n<u>Spawn Nightmare Scouts</u>" + "\nSpawn the supplied number of nightmare scouts (still asleep) in random unoccupied zones."
            + "\n\n<u>Deal Damage</u>" + "\nThe supplied percent is dealt as damage to each player, killing them if necessary."
            + "\n\n<u>Delete Resources</u>" + "\nThe supplied percent is dealt as damage AND deleted from all gear for each player. If this would kill a player, they are instead left at 1% health."
            + "\n\n<u>Down Random Player</u>" + "\nThe supplied number of players are randomly picked and immediately downed."
            + "\n\n<u>Down Random Human Player</u>" + "\nThe supplied number of players are immediately downed, randomly picked with a priority for humans."
        )]
        public EffectType Effect { get; set; } = EffectType.DownRandomPlayer;

        [FSDisplayName("DeathLink Effect Number")]
        [FSDescription("The number used in numerous DeathLink effects. If a percent, scale from 0 to 100.")]
        public int EffectNumber { get; set; } = 1;

        [FSDisplayName("DeathLink Cooldown (Seconds)")]
        [FSDescription(
            "Cooldown after sending or receiving a DeathLink during which another cannot be sent or received."
            + "\n\nThis exists because receiving a DeathLink can quickly result in sending a new DeathLink, and because certain DeathLink trigger configs are capable of triggering multiple DeathLinks in quick succession."
        )]
        public float Cooldown { get; set; } = 10f;

        [FSDisplayName("Trigger Now")]
        [FSDescription("Immediately try to trigger the DeathLink effect. Affects this GTFO lobby only; is restricted by the cooldown")]
        public FButton TriggerEffectButton { get; set; } = new FButton("Test DeathLink", callback: () => {
            if (!SNetwork.SNet.IsMaster)
                StateTracker.LogForLobby("You cannot trigger a death link; you are not host!", true);
            else
                s_deaths.Enqueue(new DeathLink(HostName, $"{PlayerManager.GetLocalPlayerAgent()?.Owner.NickName ?? "Someone"} hit the \"Test DeathLink\" button."));
        });

        [FSDisplayName("Show Messages")]
        [FSDescription("If true, show death messages when sending or receiving a DeathLink event.")]
        public bool DoShowMessages { get; set; } = true;
    }

    private static DeathLinkService? s_service = null;
    private static Queue<DeathLink> s_deaths = new();
    private static float LastTriggerTime = 0f;
    private static float LastSnatcherTime = -1000f;

    /// <summary>
    /// Create the death service
    /// </summary>
    public override void OnEnable()
    {
        base.OnEnable();
        StateTracker stateTracker = StateTracker.Get();
        stateTracker.OnStateChange += OnStateTrackerStateChange;
        s_service?.DisableDeathLink(); // Just in case
        s_service = stateTracker.ApSession?.CreateDeathLinkService();
        if (s_service != null)
        {
            s_service.OnDeathLinkReceived += s_deaths.Enqueue;
            s_service?.EnableDeathLink();
        }
    }

    /// <summary>
    /// Destroy the death service
    /// </summary>
    public override void OnDisable()
    {
        base.OnDisable();
        StateTracker stateTracker = StateTracker.Get();
        stateTracker.OnStateChange -= OnStateTrackerStateChange;
        s_service?.DisableDeathLink();
        s_service = null;
    }

    /// <summary>
    /// React to the state tracker's state changing - create or destroy the death service
    /// </summary>
    public static void OnStateTrackerStateChange(StateTracker stateTracker)
    {
        if ((UnityEngine.Time.realtimeSinceStartup - Config.Cooldown) <= LastTriggerTime)
            return;
        LastTriggerTime = UnityEngine.Time.realtimeSinceStartup;

        s_service?.DisableDeathLink(); // In case we still have a valid one somehow
        s_service = stateTracker.ApSession?.CreateDeathLinkService();
        if (s_service != null)
        {
            s_service.OnDeathLinkReceived += s_deaths.Enqueue;
            s_service?.EnableDeathLink();
        }
    }

    /// <summary>
    /// Helper which gets the host's name
    /// </summary>
    public static string HostName => $"{StateTracker.Get().ApSession?.Players.ActivePlayer.Name ?? SNetwork.SNet.Master?.NickName ?? "Admin"}";

    /// <summary>
    /// Receive a DeathLink event and trigger the effect
    /// </summary>
    /// <param name="data">Event data received from the multiverse</param>
    public static void TryTriggerDeathLink(DeathLink data)
    {
        if (!SNetwork.SNet.IsMaster) return;
        FeatureLogger.Debug("Received DeathLink event");
        if (!GameStateManager.IsInExpedition) return; // Spared, for now...

        if ((UnityEngine.Time.realtimeSinceStartup + Config.Cooldown) <= LastTriggerTime)
            return;
        LastTriggerTime = UnityEngine.Time.realtimeSinceStartup;

        if (Config.DoShowMessages)
        {
            if (data.Cause != null)
                StateTracker.LogForLobby($"<#F0F>[Death]</color> {data.Cause}", false);
            else 
                StateTracker.LogForLobby($"<#F0F>[Death]</color> (Unknown cause)", false);

        }
    
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
                    Delay = MathF.Max(0f, (LastSnatcherTime - UnityEngine.Time.fixedTime) + 60f + Random.Shared.NextSingle() * 240f),
                };
                for (int i = 0; i < Config.EffectNumber; i++)
                {
                    WorldEventManager.ExecuteEvent(snatcherData);
                    snatcherData.Delay += 60f + Random.Shared.NextSingle() * 240f;
                }
                LastSnatcherTime = UnityEngine.Time.fixedTime + snatcherData.Delay;
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
        if (!SNetwork.SNet.IsMaster) return;

        if ((UnityEngine.Time.realtimeSinceStartup - Config.Cooldown) <= LastTriggerTime)
            return;
        LastTriggerTime = UnityEngine.Time.realtimeSinceStartup;

        FeatureLogger.Debug("Sending DeathLink event: " + cause);
        s_service?.SendDeathLink(new DeathLink(HostName, cause));
        if (Config.DoShowMessages)
            StateTracker.LogForLobby($"<#F0F>[Death]</color> {cause}", false);
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

    /// <summary>
    /// Check for death link events and trigger them
    /// </summary>
    public override void Update()
    {
        if (s_deaths.TryDequeue(out DeathLink? death))
            TryTriggerDeathLink(death);
    }

    /// <summary>
    /// Detect when players die!
    /// </summary>
    [ArchivePatch(typeof(Dam_PlayerDamageBase), nameof(Dam_PlayerDamageBase.ReceiveSetDead))]
    public static class Dam_PlayerDamageBase__ReceiveSetDead__Patch
    {
        public static void Postfix(Dam_PlayerDamageBase __instance, pSetDeadData data)
            => CheckDeath(__instance, data);
    }

    /// <summary>
    /// Detect if specifically the local player dies!
    /// </summary>
    [ArchivePatch(typeof(Dam_PlayerDamageLocal), nameof(Dam_PlayerDamageLocal.ReceiveSetDead))]
    public static class Dam_PlayerDamageLocal__ReceiveSetDead__Patch
    {
        public static void Postfix(Dam_PlayerDamageBase __instance, pSetDeadData data)
            => CheckDeath(__instance, data);
    }

    /// <summary>
    /// Detect when the game ends
    /// </summary>
    [ArchivePatch(typeof(GameStateManager), nameof(GameStateManager.DoChangeState))]
    public static class GameStateManager__DoChangeState__Patch
    {
        public static void Postfix(eGameStateName nextState)
        {
            if (nextState != eGameStateName.ExpeditionFail)
                return;

            if (((int)Config.Trigger) < 3)
                NotifyDied($"Team {HostName} marked MIA. Objective failed.");
        }
    }
}
