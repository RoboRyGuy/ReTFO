
namespace ReTFO.DamageOverhaulCore;

// Custom extensions to make my life easier
internal static class Extensions
{
    internal static System.Numerics.Vector3 ToSystem(this UnityEngine.Vector3 source)
    {
        return new System.Numerics.Vector3()
        {
            X = source.x,
            Y = source.y,
            Z = source.z
        };
    }

    internal static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 source)
    {
        return new UnityEngine.Vector3()
        {
            x = source.X,
            y = source.Y,
            z = source.Z
        };
    }

    internal static System.Numerics.Vector3 ToFlatNormal(this System.Numerics.Vector3 source)
    {
        return System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(source.X, source.Y, 0f));
    }

}
