using Il2CppInterop.Runtime.Injection;
using System;

namespace ReTFO.Archipelago.Utilities;

[InjectToIl2Cpp]
public class Il2CppAction_int : Il2CppSystem.Object
{
    public Action<int>? WrappedAction = null;

    public Il2CppAction_int(Action<int> action) : base(ClassInjector.DerivedConstructorPointer<Il2CppAction_int>())
    {
        ClassInjector.DerivedConstructorBody(this);
        WrappedAction = action;
    }

    public Il2CppAction_int(IntPtr pointer) : base(pointer) { }

    public void Action(int value)
    {
        if (WrappedAction != null) WrappedAction(value);
    }

    public static implicit operator Il2CppSystem.Action<int>(Il2CppAction_int self)
    {
        IntPtr methodPtr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
            self.ObjectClass,
            false,
            nameof(Action),
            typeof(void).FullName!,
            new string[] { typeof(int).FullName! }
        );
        return new Il2CppSystem.Action<int>(self, methodPtr);
    }
}
