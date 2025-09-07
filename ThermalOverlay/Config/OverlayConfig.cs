
using UnityEngine;

namespace ReTFO.ThermalOverlay.Config;

/// <summary>
/// Contains properties for creating thermal overlays
/// </summary>
public struct OverlayConfig
{
    public OverlayConfig() { }

    // The mesh to use, if not the default
    public string? MeshGenerator { get; set; } = null;

    // The texture to use - independent of configurer
    public string? TextureGenerator { get; set; } = null;

    // The offset of the mesh
    public Vector3 MeshOffset { get; set; } = Vector3.zero;

    // The rotation of the mesh - a quaternion
    public Quaternion MeshRotation { get; set; } = Quaternion.identity;

    // The scale applied to the mesh
    public Vector3 MeshScale { get; set; } = Vector3.one;
}
