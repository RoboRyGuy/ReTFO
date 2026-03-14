
using Il2CppInterop.Runtime.Injection;
using System;
using System.Collections;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

[InjectToIl2Cpp(typeof(Il2CppSystem.Collections.IEnumerable))]
public class Il2CppEnumerable : Il2CppSystem.Object
{
    public Il2CppEnumerable(IntPtr ptr) : base(ptr) { }
    public Il2CppEnumerable(IEnumerable enumerable)
        : base(ClassInjector.DerivedConstructorPointer<Il2CppEnumerable>())
    {
        ClassInjector.DerivedConstructorBody(this);
        wrappedEnumerable = enumerable;
    }

    public Il2CppSystem.Collections.IEnumerator GetEnumerator()
    {
        var enumerator = wrappedEnumerable?.GetEnumerator() ?? Enumerable.Empty<object?>().GetEnumerator();
        if (enumerator is Il2CppSystem.Collections.IEnumerator e)
            return e;
        else
            return new Il2CppEnumerator(enumerator);
    }

    IEnumerable? wrappedEnumerable;

    public static implicit operator Il2CppSystem.Collections.IEnumerable(Il2CppEnumerable enumerable)
        => new Il2CppSystem.Collections.IEnumerable(enumerable.Pointer);
}
