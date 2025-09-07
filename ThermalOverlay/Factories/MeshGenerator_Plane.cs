
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Generates a small plane on the XY axis. Accepts a scaler to make the plane larger/smaller
/// Plane([scale: float])
/// </summary>
public class MeshGenerator_Plane : IMeshGenerator
{
    public const string Name = "Plane";

    public virtual Mesh GenerateMesh(string? thisName, ConversionContext context)
    {
        // Set up parameters
        float size = .035f;
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
        {
            string item = parameters[0];
            if (item.Length == 0) { }
            else if (float.TryParse(item, out float scale))
                size *= scale;
            else
                context.Log.LogError($"MeshGenerator_Plane expected a float for its first parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 1)
            context.Log.LogWarning($"MeshGenerator_Plane ignoring extra parameters: {FactoryManager.FormatParams(parameters[1..])}");

        // Actually generate the mesh
        Mesh mesh = new()
        {
            name = "XY Plane (Generated)",
            vertices = new[] { new Vector3(-size, size, 0), new Vector3(size, size, 0), new Vector3(-size, -size, 0), new Vector3(size, -size, 0) },
            uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0) },
            triangles = new[] { 0, 1, 2,  1, 3, 2,  0, 2, 1,  1, 2, 3 },
            normals = new[] { new Vector3(0, 0, -1f), new Vector3(0, 0, -1f), new Vector3(0, 0, -1f), new Vector3(0, 0, -1f) }
        };
        mesh.RecalculateTangents();

        return mesh;
    }
}
