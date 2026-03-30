using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using ReTFO.Archipelago.FeaturesAPI;
using SNetwork;
using System;
using System.Runtime.InteropServices;

namespace ReTFO.Archipelago.Features;

// Tracks Archipelago state.
// This file is dedicated to the SNetwork integration for StateTracker
public partial class StateTracker : ArchipelagoFeature
{

    /// <summary>
    /// Struct used to sync state data
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct pArchipelagoState
    {
        [FieldOffset(0)]
        public uint value1; // Temp for development
    }

    /// <summary>
    /// Struct used to sync interaction data
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct pArchipelagoInteraction
    {
        [FieldOffset(0)]
        public uint value1; // Temp for development
    }

    /// <summary>
    /// Implements iSNet_StateReplicatorProvider<pArchipelagoState, pArchipelagoInteraction>
    /// Forwards related calls to Archipelago
    /// </summary>
    private class StateReplicatorProvider : Il2CppSystem.Object
    {
        private StateTracker? m_owner = null;
        
        public StateReplicatorProvider(IntPtr ptr) : base(ptr) { }
        public StateReplicatorProvider(StateTracker owner) : base(ClassInjector.DerivedConstructorPointer<StateReplicatorProvider>())
        {
            ClassInjector.DerivedConstructorBody(this);
            m_owner = owner;
        }

        public void OnStateChange(pArchipelagoState oldState, pArchipelagoState newState, bool isRecall)
        {
            if (m_owner == null)
                FeatureLogger.Error("Archipelago's StateTracker.StateReplicatorProvider does not have a valid owner!");
            else
                m_owner?.OnStateChange(oldState, newState, isRecall);
        }

        public void AttemptInteract(pArchipelagoInteraction interaction)
        {
            if (m_owner == null)
                FeatureLogger.Error("Archipelago's StateTracker.StateReplicatorProvider does not have a valid owner!");
            else
                m_owner?.AttemptInteract(interaction);
        }
    }

    /// <summary>
    /// Callback for when state is changed
    /// </summary>
    public void OnStateChange(pArchipelagoState oldState, pArchipelagoState newState, bool isRecall)
    {
        FeatureLogger.Debug("Archipelago.StateTracker.OnStateChange");
    }

    /// <summary>
    /// Callback used to initiate an interaction
    /// </summary>
    public void AttemptInteract(pArchipelagoInteraction interaction)
    {
        FeatureLogger.Debug("Archipelago.StateTracker.AttemptInteract");
    }

    /// <summary>
    /// Inject the types needed for replciaton to Il2Cpp.
    /// We cannot use the normal InjectToIl2Cpp attribute on these types because the static consutructor for iSNet_StateReplicatorProvider
    ///  and SNet_StateReplicator require pArchipelagoState and pArchipelagoInteraction to be injected, which we cannot guarntee.
    /// With this method, we inject the dependent types first, overwrite the generic type pointers, and then ineject our derived types
    /// </summary>
    internal static void InjectReplicatorTypes()
    {
        // Register these types using ValueType as their base
        ClassInjector.RegisterTypeInIl2Cpp(typeof(pArchipelagoState));
        ClassInjector.RegisterTypeInIl2Cpp(typeof(pArchipelagoInteraction));





    }

    public StateTracker()
    {
        m_stateReplicatorProvider = new(this);
        StateReplicator = SNet_StateReplicator<pArchipelagoState, pArchipelagoInteraction>.Create(
            new iSNet_StateReplicatorProvider<pArchipelagoState, pArchipelagoInteraction>(m_stateReplicatorProvider.Pointer), 
            eSNetReplicatorLifeTime.NeverDestroyed
        );
    }

    private StateReplicatorProvider m_stateReplicatorProvider;
    public SNet_StateReplicator<pArchipelagoState, pArchipelagoInteraction> StateReplicator { get; private set; }


}
