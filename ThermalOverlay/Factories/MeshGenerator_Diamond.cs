
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Generates a small diamond mesh (effectively, a rotated cube). Accepts a scale for altering the size
/// Diamond([scale: float])
/// </summary>
public class MeshGenerator_Diamond : IMeshGenerator
{
    public const string Name = "Diamond";

    public virtual Mesh GenerateMesh(string? thisName, ConversionContext context)
    {
        // Set up parameters
        float size = .05f;
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
        {
            string item = parameters[0];
            if (item.Length == 0) { }
            else if (float.TryParse(item, out float scale))
                size *= scale;
            else
                context.Log.LogError($"MeshGenerator_Diamond expected a float for its first parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 1)
            context.Log.LogWarning($"MeshGenerator_Diamond ignoring extra parameters: {FactoryManager.FormatParams(parameters[1..])}");

        Mesh mesh = new()
        {
            name = "Diamond (Generated)",
            vertices = new[] { new Vector3(0, size, 0), new Vector3(-size, 0, 0), new Vector3(0, 0, size), new Vector3(size, 0, 0), new Vector3(0, 0, -size), new Vector3(0, -size, 0) },
            uv = new[]       { new Vector2(0, 1),       new Vector2(0, 0),        new Vector2(.5f, .5f),   new Vector2(1, 1),       new Vector2(.5f, .5f),    new Vector2(1, 0) },
            triangles = new[] { 0, 1, 2,  0, 2, 3,  0, 3, 4,  0, 4, 1,  5, 2, 1,  5, 3, 2,  5, 4, 3,  5, 1, 4 },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }
}
