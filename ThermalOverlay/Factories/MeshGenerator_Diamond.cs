
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Generates a small diamond mesh, which is a plane with its center point moved toward the camerea. Accepts 
///  a scale for altering the size, and zscale which scales only the z-axis (applied ontop of regular scaling)
/// Diamond([scale: float], [zscale: float])
/// </summary>
public class MeshGenerator_Diamond : IMeshGenerator
{
    public const string Name = "Diamond";

    public virtual Mesh GenerateMesh(string? thisName, ConversionContext context)
    {
        // Set up parameters
        float size = .05f;
        float zscale = .5f;
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
        {
            string item = parameters[1];
            if (item.Length == 0) { }
            else if (float.TryParse(item, out float zscaling))
                zscale = zscaling;
            else
                context.Log.LogError($"MeshGenerator_Diamond expected a float for its second parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 2)
            context.Log.LogWarning($"MeshGenerator_Diamond ignoring extra parameters: {FactoryManager.FormatParams(parameters[2..])}");

        Mesh mesh = new()
        {
            name = "Diamond (Generated)",
            vertices = new[] { new Vector3(0, 0, -size * zscale), new Vector3(0, size, 0), new Vector3(size, 0, 0), new Vector3(0, -size, 0), new Vector3(-size, 0, 0) },
            uv       = new[] { new Vector2(.5f, .5f),             new Vector2(0, 1),       new Vector2(0, 0),        new Vector2(1, 0),        new Vector2(1, 1) },
            normals  = new[] { new Vector3(0, 0, -1),             new Vector3(0, 0, -1),   new Vector3(0, 0, -1),    new Vector3(0, 0, -1),    new Vector3(0, 0, -1) },
            triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1 }
        };
        mesh.RecalculateTangents();

        return mesh;
    }
}
