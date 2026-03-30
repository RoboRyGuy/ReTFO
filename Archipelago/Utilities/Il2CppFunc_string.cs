using Il2CppInterop.Runtime.Injection;
using System;

namespace ReTFO.Archipelago.Utilities;

[InjectToIl2Cpp]
public class Il2CppFunc_string : Il2CppSystem.Object
{
    public Func<string>? WrappedFunc = null;

    public Il2CppFunc_string(Func<string> action) : base(ClassInjector.DerivedConstructorPointer<Il2CppFunc_string>())
    {
        ClassInjector.DerivedConstructorBody(this);
        WrappedFunc = action;
    }

    public Il2CppFunc_string(IntPtr pointer) : base(pointer) { }

    public string Func()
    {
        if (WrappedFunc != null)
            return WrappedFunc();
        else return default(string)!;
    }

    public static implicit operator Il2CppSystem.Func<string>(Il2CppFunc_string self)
    {
        IntPtr methodPtr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
            self.ObjectClass,
            false,
            nameof(Func),
            typeof(string).FullName!,
            Array.Empty<string>()
        );
        return new Il2CppSystem.Func<string>(self, methodPtr);
    }
}
