
using Il2CppInterop.Runtime.Injection;
using System;
using System.Collections;

namespace ReTFO.Archipelago.Utilities;

[InjectToIl2Cpp(typeof(Il2CppSystem.Collections.IEnumerator))]
public class Il2CppEnumerator : Il2CppSystem.Object
{

    public Il2CppEnumerator(IntPtr ptr) : base(ptr) { }
    public Il2CppEnumerator(IEnumerator enumerator)
        : base(ClassInjector.DerivedConstructorPointer<Il2CppEnumerator>())
    {
        ClassInjector.DerivedConstructorBody(this);
        wrappedEnumerator = enumerator;
    }

    public Il2CppSystem.Object Current => (wrappedEnumerator?.Current as Il2CppSystem.Object)!;
    public bool MoveNext() => wrappedEnumerator?.MoveNext() ?? false;
    public void Reset() => wrappedEnumerator?.Reset();

    IEnumerator? wrappedEnumerator;

    public static implicit operator Il2CppSystem.Collections.IEnumerator(Il2CppEnumerator enumerable)
        => new Il2CppSystem.Collections.IEnumerator(enumerable.Pointer);
}
