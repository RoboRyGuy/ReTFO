
using ReTFO.ThermalOverlay.Config;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Common data storage class used to provide information to converters and generators
/// </summary>
public class ConversionContext
{
    public ConversionContext(Plugin plugin, ItemEquippable item)
    { Plugin = plugin; Item = item; }

    public ConversionContext(Plugin plugin, ItemEquippable item, ThermalConfig? config)
    { Plugin = plugin; Item = item; Config = config; }

    public Plugin Plugin;
    public ItemEquippable Item;
    public Renderer? Renderer = null;
    public Material? Material = null;
    public ThermalConfig? Config = null;

    public BepInEx.Logging.ManualLogSource Log => Plugin.Log;
    public FactoryManager Factory => Plugin.FactoryManager;
}
