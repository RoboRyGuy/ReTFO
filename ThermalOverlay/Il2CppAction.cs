
using Il2CppInterop.Runtime.Injection;

namespace ReTFO.ThermalOverlay;

public class Il2CppAction : Il2CppSystem.Object
{
    private Action? _action = null;

    public Il2CppAction(Action action) : base(ClassInjector.DerivedConstructorPointer<Il2CppAction>())
    {
        ClassInjector.DerivedConstructorBody(this);
        _action = action;
    }

    public Il2CppAction(IntPtr pointer) : base(pointer) { }

    public void Action()
    {
        if (_action != null) _action();
    }

    public static implicit operator Il2CppSystem.Action (Il2CppAction self)
    {
        IntPtr methodPtr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
            self.ObjectClass, 
            false, 
            nameof(Action), 
            "System.Void", 
            Array.Empty<string>()
        );
        return new Il2CppSystem.Action(self, methodPtr);
    }
}