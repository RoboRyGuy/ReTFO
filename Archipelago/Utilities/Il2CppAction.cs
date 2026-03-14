
using Il2CppInterop.Runtime.Injection;
using System;

namespace ReTFO.Archipelago.Utilities;

[InjectToIl2Cpp]
public class Il2CppAction : Il2CppSystem.Object
{
    public Action? WrappedAction = null;

    public Il2CppAction(Action action) : base(ClassInjector.DerivedConstructorPointer<Il2CppAction>())
    {
        ClassInjector.DerivedConstructorBody(this);
        WrappedAction = action;
    }

    public Il2CppAction(IntPtr pointer) : base(pointer) { }

    public void Action()
    {
        if (WrappedAction != null) WrappedAction();
    }

    public static implicit operator Il2CppSystem.Action(Il2CppAction self)
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
            "System.Void",
            Array.Empty<string>()
        );
        return new Il2CppSystem.Action<int>(self, methodPtr);
    }
}