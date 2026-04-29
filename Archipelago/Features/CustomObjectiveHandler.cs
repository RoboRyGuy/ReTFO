using AIGraph;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features;

/// <summary>
/// Custom feature which manages and handles custom sub objectives.
/// This is often used to give unlocked information that would otherwise 
///  be much more obtuse to the player
/// </summary>
[EnableFeatureByDefault]
public class CustomObjectiveHandler : ArchipelagoFeature
{
    public override string Name => "Custom Objective Handler";
    public override string Description
        => "Handles the display of custom objective UI. This is used by several handlers, "
         + "such as the Reactor Startup handler, to display collected items";
    public override FeatureGroup Group => FeatureGroups.VanillaHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        AIG_CourseNode? node = null;
        if (PlayerManager.TryGetLocalPlayerAgent(out var agent))
            node = agent.CourseNode;
        foreach (var objective in s_objectives)
            objective.Value.CheckScope(node);
    }

    /// <summary>
    /// Possible scopes at which a progression objective is considered relevant.
    /// Objectives are only shown if the player is currently in their scope.
    /// </summary>
    public enum eObjectiveScope
    {
        /// <summary>
        /// The progression objective is always relevant.
        /// </summary>
        Expedition,

        /// <summary>
        /// The progression objective is only shown if the local player
        ///  is currently in the target dimension.
        /// </summary>
        Dimension,

        /// <summary>
        /// The progression objective is only shown if the local player
        ///  is currently in the target layer (of the target dimension)
        /// </summary>
        Layer,

        /// <summary>
        /// The progression objective is only shown if the local player
        ///  is currently in the target zone (of the target layer (of the target dimension))
        /// </summary>
        Zone,
    }

    /// <summary>
    /// Wraps a sub-objective UI instance. Request one via GetCustomSubObjective.
    /// </summary>
    public class ObjectiveItem
    {
        /// <summary>
        /// Set up this ObjectiveItem assuming it's a brand new objective item.
        /// If overriding this, ensure you call base.Setup AFTER doing your setup.
        /// </summary>
        public virtual void Setup()
        {
            CheckScope(TryGetCurrentCourseNode());
            Refresh();
        }

        /// <summary>
        /// Helper to get the current PUI_GameObjectives instance
        /// </summary>
        protected static PUI_GameObjectives GUI => GuiManager.PlayerLayer.WardenObjectives;

        /// <summary>
        /// Try to get the current player course node. Returns null if it fails (level hasn't loaded in, etc)
        /// </summary>
        protected static AIG_CourseNode? TryGetCurrentCourseNode()
        {
            if (PlayerManager.TryGetLocalPlayerAgent(out var agent))
                return agent.CourseNode;
            return null;
        }

        /// <summary>
        /// ID used to register this progression objective in the UI
        /// </summary>
        public int PUI_ID { get; private set; } = 0;

        /// <summary>
        /// If true, this UI element is currently visible.
        /// </summary>
        public bool IsVisible { get; private set; } = false;

        /// <summary>
        /// Display priority. Higher => Will display closer to top of screen.
        /// The main objective has a priority of int.MaxValue.
        /// </summary>
        public int Priority { get; set; } = -100;

        /// <summary>
        /// The target scope of the objective. See the eObjectiveScope enum for details.
        /// </summary>
        public eObjectiveScope Scope 
        { 
            get => m_scope;
            set { m_scope = value; CheckScope(TryGetCurrentCourseNode()); } 
        }
        private eObjectiveScope m_scope = eObjectiveScope.Layer;

        /// <summary>
        /// The target used by the Scope to determine relevance. Note that unrelated fields
        ///  are ignored (ie if Scope == Layer, then Zone will be ignored, but Dimension and Layer won't)
        /// </summary>
        public GlobalZoneIndex ScopeTarget 
        { 
            get => m_scopeTarget; 
            set { m_scopeTarget = value; CheckScope(TryGetCurrentCourseNode()); } 
        } 
        private GlobalZoneIndex m_scopeTarget
            = new(eDimensionIndex.Reality, LG_LayerType.MainLayer, GameData.eLocalZoneIndex.Zone_0);

        /// <summary>
        /// Tracks whether this Progression Objective is currently in scope.
        /// </summary>
        public bool IsInScope { get; private set; }

        /// <summary>
        /// User-owned bool which determines if this progression objective can be shown.
        /// If false, it will never be shown; if true, it will be shown if in scope.
        /// </summary>
        public bool IsActive 
        { 
            get => m_isActive; 
            set { m_isActive = value; Refresh(false); }
        }
        private bool m_isActive = true;

        /// <summary>
        /// When showing, should GTFO skip text styling? Not sure what this does exactly.
        /// </summary>
        public bool SkipStyling { get; set; } = false;

        /// <summary>
        /// When updating the text, should GTFO skip its blinking animation?
        /// </summary>
        public bool SkipBlinking { get; set; } = false;

        /// <summary>
        /// Custom tag displayed in brackets before the header.
        /// </summary>
        public string ObjectiveTag { get; set; } = "CUSTOM";

        /// <summary>
        /// String displayed after the objective tag
        /// </summary>
        public string HeaderText { get; set; } = "CUSTOM SUBOBJECTIVE";

        /// <summary>
        /// String displayed below the header text.
        /// Note that GTFO will prepend this with "> ".
        /// </summary>
        public string SubText { get; set; } = "This is a custom sub objective.\n  Hello World!";

        /// <summary>
        /// Get the text to be displayed as the header.
        /// This text is assumed to be already localized.
        /// </summary>
        public string GetHeaderText() => HeaderText;

        /// <summary>
        /// Get the text to be displayed below the header.
        /// This text is assuemd to be already localized.
        /// </summary>
        public string GetSubText() => SubText;

        /// <summary>
        /// Helper which immediately makes this progression objective visible.
        /// </summary>
        /// <param name="force">
        /// Force showing the objective, even if it's already shown. This will allow text to update,
        /// but may cause it to blink unecessarily if no text was changed.
        /// </param>
        public void ShowNow(bool force = true)
        {
            if (!ArchipelagoFeatureHelper.GetFeature<CustomObjectiveHandler>().Enabled)
            {
                HideNow();
                return;
            }

            if (!IsVisible)
            {
                PUI_ID = int.MaxValue << 2;
                while (GUI.m_progressionObjectiveMap.FindEntry(PUI_ID) != -1)
                    ++PUI_ID;
            }

            if (!IsVisible || force)
            {
                GUI.SetProgressionObjective(
                    PUI_ID, new Il2CppFunc_string(GetHeaderText), new Il2CppFunc_string(GetSubText), 
                    Priority, SkipStyling, SkipBlinking, ObjectiveTag
                );
            }

            IsVisible = true;
        }

        /// <summary>
        /// Helper which immediately hides this progression objective.
        /// </summary>
        public void HideNow()
        {
            if (IsVisible)
                GUI.RemoveProgressionObjective(PUI_ID);
            IsVisible = false;
        }

        /// <summary>
        /// Checks if currently in scope compared to the provided node.
        /// </summary>
        /// <param name="localNode">The local player agent's course node, or null if not available</param>
        public virtual void CheckScope(AIG_CourseNode? localNode)
        {
            if (localNode == null && Scope != eObjectiveScope.Expedition)
                IsInScope = false;
            else
            {
                IsInScope = true;
                if (Scope >= eObjectiveScope.Dimension)
                    IsInScope = IsInScope && localNode!.m_zone.DimensionIndex == ScopeTarget.Dimension;
                if (Scope >= eObjectiveScope.Layer)
                    IsInScope = IsInScope && localNode!.LayerType == ScopeTarget.Layer;
                if (Scope >= eObjectiveScope.Zone)
                    IsInScope = IsInScope && localNode!.m_zone.LocalIndex == ScopeTarget.Zone;
            }

            Refresh(false);
        }

        /// <summary>
        /// Call this after changing text. Pushes the new text.
        /// </summary>
        /// <param name="force">
        /// Force showing the objective, even if it's already shown. This will allow text to update,
        /// but may cause it to blink unecessarily if no text was changed.
        /// </param>
        public void Refresh(bool force = true)
        {
            if (IsInScope && IsActive) ShowNow(force);
            else HideNow();
        }
    }

    /// <summary>
    /// All custom sub-objectives registered
    /// </summary>
    private static Dictionary<string, ObjectiveItem> s_objectives = new();
    
    /// <summary>
    /// Either fetch or create a custom objective item handler instance.
    /// </summary>
    /// <typeparam name="T">The Type of the handler. This can be your own custom type which overrides virtual methods.</typeparam>
    /// <param name="key">The key for the objective.</param>
    /// <returns>The fetched or created objective item.</returns>
    public static T GetObjectiveItem<T>(string key)
        where T : ObjectiveItem, new()
    {
        if (s_objectives.TryGetValue(key, out var value))
            return (T)value;

        T item = new();
        s_objectives.Add(key, item);
        item.Setup();
        FeatureLogger.Debug("Registered new objective item: " + key);
        return item;
    }

    /// <summary>
    /// Random OnLevelCleanup patch which we use to delete all progression objectives
    /// </summary>
    [ArchivePatch(typeof(ProgressionObjectivesManager), nameof(ProgressionObjectivesManager.OnLevelCleanup))]
    public static class ProgressionObjectivesManager__OnLevelCleanup__Patch
    {
        public static void Postfix()
        {
            foreach (var value in s_objectives.Values)
                value.HideNow();
            s_objectives.Clear();
            FeatureLogger.Debug("Cleared custom progression objectives");
        }
    }

    /// <summary>
    /// React when the local player's course node changes
    /// </summary>
    [ArchivePatch(typeof(PlayerAgent), nameof(PlayerAgent.SetCourseNode))]
    public static class PlayerAgent__SetCourseNode__Patch
    {
        public static void Postfix(PlayerAgent __instance)
        {
            if (!__instance.IsLocallyOwned) return;

            foreach (var value in s_objectives.Values)
                value.CheckScope(__instance.CourseNode);
        }
    }
}
