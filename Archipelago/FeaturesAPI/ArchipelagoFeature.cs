using ReTFO.Archipelago.Features;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.FeaturesAPI;

/// <summary>
/// Extends the Archive's Feature class to better support Archipelago
/// </summary>
public class ArchipelagoFeature : Feature
{
    public override string Name => "Archipelago Feature";
    public override string Description 
        => "Abstract base class used by Archipelago features and handlers";
    public override FeatureGroup Group => FeatureGroups.Archipelago;

    /// <summary>
    /// Fallback FeatureLogger for Archipelago Features.
    /// Please create a new FeatureLogger in derived features!
    /// </summary>
    public new static IArchiveLogger FeatureLogger 
    { 
        get => m_featureLogger ??= Plugin.Get().Logger; 
        set => m_featureLogger = value; 
    }
    private static IArchiveLogger? m_featureLogger = null;

    public override bool ShouldInit()
    {
        if (!Plugin.TryGet(out var plugin))
        {
            FeatureLogger.Error("Failed to find plugin during init for feature: " + GetType().FullName);
            return false;
        }

        if (GetType() == typeof(ArchipelagoFeature))
            return false;
        else
            return base.ShouldInit();
    }

    /// <summary>
    /// When the feature is enabled, auto-enable callbacks
    /// </summary>
    public override void OnEnable()
    {
        base.OnEnable();
        ArchipelagoFeatureHelper.RegisterInstancedCallbacks(this);
    }

    /// <summary>
    /// When the feature is disabled, auto-disable callbacks
    /// </summary>
    public override void OnDisable()
    {
        base.OnDisable();
        ArchipelagoFeatureHelper.UnregisterInstancedCallbacks(this);
    }

    // Because each feature is a singleton and because I cannot modify TheArchive, I consider this valid
    public override bool Equals(object? obj)
    {
        if (obj is ArchipelagoFeatureHelper.FakeFeature fake)
            return GetType().Equals(fake.WrappedType);
        else
            return GetType().Equals(obj?.GetType());
    }
    public override int GetHashCode() => GetType().GetHashCode();
}
