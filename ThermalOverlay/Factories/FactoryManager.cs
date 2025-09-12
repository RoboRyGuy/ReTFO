
using ReTFO.ThermalOverlay.Config;
using ReTFO.ThermalOverlay.Configurers;
using ReTFO.ThermalOverlay.Interfaces;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Store, manages, and runs converters and generators for the ThermalOverlay plugin
/// </summary>
public class FactoryManager
{
    public FactoryManager()
    {
        AddThermalConverter(ThermalConverter_Auto.Name, new ThermalConverter_Auto(), true);

        AddSightConverter(SightConverter_Standard.Name, new SightConverter_Standard(), true);

        AddOverlayGenerator(OverlayGenerator_Standard.Name, new OverlayGenerator_Standard(), true);
        AddOverlayGenerator(OverlayGenerator_Load.Name, new OverlayGenerator_Load());
        
        AddMaterialGenerator(MaterialGenerator_PDW.Name, new MaterialGenerator_PDW());
        AddMaterialGenerator(MaterialGenerator_PR.Name, new MaterialGenerator_PR());
        AddMaterialGenerator(MaterialGenerator_Load.Name, new MaterialGenerator_Load());
        AddMaterialGenerator(MaterialGenerator_Auto.Name, new MaterialGenerator_Auto(), true);

        AddMeshGenerator(MeshGenerator_Plane.Name,   new MeshGenerator_Plane());
        AddMeshGenerator(MeshGenerator_Diamond.Name, new MeshGenerator_Diamond(), true);

        AddTextureGenerator("black",      new TextureGenerator_Simple(() => Texture2D.blackTexture));
        AddTextureGenerator("gray",       new TextureGenerator_Simple(() => Texture2D.grayTexture));
        AddTextureGenerator("linearGray", new TextureGenerator_Simple(() => Texture2D.linearGrayTexture));
        AddTextureGenerator("normal",     new TextureGenerator_Simple(() => Texture2D.normalTexture));
        AddTextureGenerator("red",        new TextureGenerator_Simple(() => Texture2D.redTexture));
        AddTextureGenerator("white",      new TextureGenerator_Simple(() => Texture2D.whiteTexture));

        AddTextureGenerator(TextureGenerator_Load.Name,  new TextureGenerator_Load());
        AddTextureGenerator(TextureGenerator_Bloom.Name, new TextureGenerator_Bloom(), true);
    }

    // A small helper which identifies generator/converter names
    public static string? BaseName(string? name)
    {
        int index = name?.IndexOf('(') ?? -1;
        if (index == -1) return name;
        else if (index == 0) return null;
        else return name!.Substring(0, index);
    }

    // A helper which identifies parameters in a name
    public static string[] GetParameters(string? name)
    {
        if (name == null) return Array.Empty<string>();

        int index = name.IndexOf('(');
        if (index < 0) return Array.Empty<string>();
        
        if (name.Last() != ')')
        {
            Plugin.Get().Log.LogError($"Expected \')\' at end of texture name: \'{name}\'");
            return name.Substring(index + 1).Split(',').Select(s => s.Trim()).ToArray();
        }
        else
            return name.Substring(index + 1, name.Length - index - 2).Split(',').Select(s => s.Trim()).ToArray();
    }

    // A helper which formats parameters together, typically for logging purposes
    public static string FormatParams(string[]? paras)
    {
        if (paras == null) return "\"<null>\"";
        else return $"\"{string.Join(", ", paras)}\"";
    }

    // Helper for material configs. Calculates and sets the ScopeCenter property, which is needed
    //  to correctly show the center of the screen when unscoped (if that settings is enabled)
    // Returns true on success, and false on fail (something was null)
    public static bool TryCalcScopeCenter(ConversionContext context)
    {
        Renderer? sight = context.Renderer;
        if (sight == null) return false;
        if (context.Material == null) return false;

        Vector3 align3 = sight.transform.InverseTransformPoint(context.Item.SightLookAlign.position);
        Vector4 align4 = new() { x = align3.x, y = align3.y, z = align3.z, w = 1f };
        context.Material.SetVector(MaterialConfig.ScopeCenter_Name, align4);
        return true;
    }

    // Similar to TryCalcScopeCenter, but throws an exception when it fails. Useful for debug and assertions
    public static void CalcScopeCenter(ConversionContext context)
    {
        if (!TryCalcScopeCenter(context))
        {
            if (context.Renderer == null && context.Material == null) 
                throw new NullReferenceException("CalcScopeCenter was passed null parameters: Renderer and Material");
            else if (context.Renderer == null)
                throw new NullReferenceException("CalcScopeCenter was passed a null parameter: Renderer");
            else if (context.Material == null)
                throw new NullReferenceException("CalcScopeCenter was passed a null parameter: Material");
            else
                throw new NullReferenceException("CalcScopeCenter encountered an unknown issue");
        }
    }

    // ============================================================================================
    // Thermal converters, which are the main handlers for converting items to having thermals
    private SortedList<string, IThermalConverter> thermalConverters = new();
    private int defaultThermalConverter = -1;

    // Add a converter
    public void AddThermalConverter(string name, IThermalConverter converter, bool makeDefault = false)
    {
        name = BaseName(name);
        if (thermalConverters.ContainsKey(name))
            Plugin.TryGet()?.Log.LogWarning($"Overwriting ThermalConvert \"{name}\" with \"{converter.GetType().Name}\"");
        thermalConverters[name] = converter;

        int index = thermalConverters.IndexOfKey(name);
        if (makeDefault) defaultThermalConverter = index;
        else if (index <= defaultThermalConverter) defaultThermalConverter += 1;
    }

    // Get the default converter
    public IThermalConverter GetDefaultThermalConverter() 
        => thermalConverters.ElementAt(defaultThermalConverter).Value;

    // Get a converter; if it fails (returns false), converter is the default converter
    public bool TryGetThermalConverter(string? name, out IThermalConverter converter)
    {
        int index = name != null ? thermalConverters.IndexOfKey(BaseName(name)) : -1;
        if (index >= 0) converter = thermalConverters.ElementAt(index).Value;
        else converter = GetDefaultThermalConverter();
        return index >= 0;
    }

    // Get a converter; if it fails, returns the default converter instead
    public IThermalConverter GetThermalConverter(string? name)
    {
        TryGetThermalConverter(name, out IThermalConverter converter);
        return converter;
    }

    // Shortcut to run a thermal converter
    public void RunThermalConverter(string? name, ConversionContext context)
    {
        if (!TryGetThermalConverter(name, out var converter) && name != null)
            context.Log.LogWarning($"Failed to find thermal converter \"{name}\", using default (\"{converter.GetType().Name}\")");
        converter.ConvertItem(name, context);
    }

    // ============================================================================================
    // Sight converters, which converts existing sights/scopes into their thermal variants
    private SortedList<string, ISightConverter> sightConverters = new();
    private int defaultSightConverter = -1;

    // Add a converter
    public void AddSightConverter(string name, ISightConverter converter, bool makeDefault = false)
    {
        name = BaseName(name);
        if (sightConverters.ContainsKey(name))
            Plugin.TryGet()?.Log.LogWarning($"Overwriting SightConverter \"{name}\" with \"{converter.GetType().Name}\"");
        sightConverters[name] = converter;

        int index = sightConverters.IndexOfKey(name);
        if (makeDefault) defaultSightConverter = index;
        else if (index <= defaultSightConverter) defaultSightConverter += 1;
    }

    // Get the default converter
    public ISightConverter GetDefaultSightConverter()
        => sightConverters.ElementAt(defaultSightConverter).Value;

    // Get a converter; if it fails (returns false), the out is the default converter
    public bool TryGetSightConverter(string? name, out ISightConverter converter)
    {
        int index = name != null ? sightConverters.IndexOfKey(BaseName(name)) : -1;
        if (index >= 0) converter = sightConverters.ElementAt(index).Value;
        else converter = GetDefaultSightConverter();
        return index >= 0;
    }

    // Get a converter; if it fails, returns the default converter instead
    public ISightConverter GetSightConverter(string? name)
    {
        TryGetSightConverter(name, out ISightConverter converter);
        return converter;
    }

    // Shortcut to run a sight converter
    public bool RunSightConverter(string? name, ConversionContext context)
    {
        if (!TryGetSightConverter(name, out var converter) && name != null)
            context.Log.LogWarning($"Failed to find sight converter \"{name}\", using default (\"{converter.GetType().Name}\")");
        return converter.ConvertSight(name, context);
    }

    // ============================================================================================
    // Overlay generators, which add geometry to items which can then be used to display thermals
    private SortedList<string, IOverlayGenerator> overlayGenerators = new();
    private int defaultoverlayGenerator = -1;

    // Add a generator
    public void AddOverlayGenerator(string name, IOverlayGenerator generator, bool makeDefault = false)
    {
        name = BaseName(name);
        if (overlayGenerators.ContainsKey(name))
            Plugin.TryGet()?.Log.LogWarning($"Overwriting OverlayGenerator \"{name}\" with \"{generator.GetType().Name}\"");
        overlayGenerators[name] = generator;

        int index = overlayGenerators.IndexOfKey(name);
        if (makeDefault) defaultoverlayGenerator = index;
        else if (index <= defaultoverlayGenerator) defaultoverlayGenerator += 1;
    }

    // Get the default generator
    public IOverlayGenerator GetDefaultOverlayGenerator()
        => overlayGenerators.ElementAt(defaultoverlayGenerator).Value;

    // Get a generator; if it fails (returns false), the out is the default generator
    public bool TryGetOverlayGenerator(string? name, out IOverlayGenerator converter)
    {
        int index = name != null ? overlayGenerators.IndexOfKey(BaseName(name)) : -1;
        if (index >= 0) converter = overlayGenerators.ElementAt(index).Value;
        else converter = GetDefaultOverlayGenerator();
        return index >= 0;
    }

    // Get a generator; if it fails, returns the default generator instead
    public IOverlayGenerator GetOverlayGenerator(string? name)
    {
        TryGetOverlayGenerator(name, out IOverlayGenerator generator);
        return generator;
    }

    // Shortcut to run an overlay generator
    public bool RunOverlayGenerator(string? name, ConversionContext context)
    {
        if (!TryGetOverlayGenerator(name, out var generator) && name != null)
            context.Log.LogWarning($"Failed to find overlay generator \"{name}\", using default (\"{generator.GetType().Name}\")");
        return generator.GenerateOverlay(name, context);
    }

    // ============================================================================================
    // Mesh generators, which create meshes (for thermal overlays, typically)
    private SortedList<string, IMeshGenerator> meshGenerators = new();
    private int defaultMeshGenerator = -1;

    // Add a generator
    public void AddMeshGenerator(string name, IMeshGenerator generator, bool makeDefault = false)
    {
        name = BaseName(name);
        if (meshGenerators.ContainsKey(name))
            Plugin.TryGet()?.Log.LogWarning($"Overwriting MeshGenerator \"{name}\" with \"{generator.GetType().Name}\"");
        meshGenerators[name] = generator;

        int index = meshGenerators.IndexOfKey(name);
        if (makeDefault) defaultMeshGenerator = index;
        else if (index <= defaultMeshGenerator) defaultMeshGenerator += 1;
    }

    // Get the default generator
    public IMeshGenerator GetDefaultMeshGenerator()
        => meshGenerators.ElementAt(defaultMeshGenerator).Value;

    // Get a generator; if it fails (returns false), the out is the default generator
    public bool TryGetMeshGenerator(string? name, out IMeshGenerator generator)
    {
        int index = name != null ? meshGenerators.IndexOfKey(BaseName(name)) : -1;
        if (index >= 0) generator = meshGenerators.ElementAt(index).Value;
        else generator = GetDefaultMeshGenerator();
        return index >= 0;
    }

    // Get a generator; if it fails, returns the default generator instead
    public IMeshGenerator GetMeshGenerator(string? name)
    {
        TryGetMeshGenerator(name, out IMeshGenerator generator);
        return generator;
    }

    // Shortcut to run a mesh generator
    public Mesh RunMeshGenerator(string? name, ConversionContext context)
    {
        if (!TryGetMeshGenerator(name, out var generator) && name != null)
            context.Log.LogWarning($"Failed to find mesh generator \"{name}\", using default (\"{generator.GetType().Name}\")");
        return generator.GenerateMesh(name, context);
    }

    // ============================================================================================
    // Material generators, which create materials 
    private SortedList<string, IMaterialGenerator> materialGenerators = new();
    private int defaultMaterialGenerator = -1;

    // Add a generator
    public void AddMaterialGenerator(string name, IMaterialGenerator generator, bool makeDefault = false)
    {
        name = BaseName(name);
        if (materialGenerators.ContainsKey(name))
            Plugin.TryGet()?.Log.LogWarning($"Overwriting MaterialGenerator \"{name}\" with \"{generator.GetType().Name}\"");
        materialGenerators[name] = generator;

        int index = materialGenerators.IndexOfKey(name);
        if (makeDefault) defaultMaterialGenerator = index;
        else if (index <= defaultMaterialGenerator) defaultMaterialGenerator += 1;
    }

    // Get the default generator
    public IMaterialGenerator GetDefaultMaterialGenerator()
        => materialGenerators.ElementAt(defaultMaterialGenerator).Value;

    // Get a generator; if it fails (returns false), the out is the default generator
    public bool TryGetMaterialGenerator(string? name, out IMaterialGenerator generator)
    {
        int index = name != null ? materialGenerators.IndexOfKey(BaseName(name)) : -1;
        if (index >= 0) generator = materialGenerators.ElementAt(index).Value;
        else generator = GetDefaultMaterialGenerator();
        return index >= 0;
    }

    // Get a generator; if it fails, returns the default generator instead
    public IMaterialGenerator GetMaterialGenerator(string? name)
    {
        TryGetMaterialGenerator(name, out IMaterialGenerator generator);
        return generator;
    }

    // Shortcut to run a material generator
    public Material RunMaterialGenerator(string? name, ConversionContext context)
    {
        if (!TryGetMaterialGenerator(name, out var generator) && name != null)
            context.Log.LogWarning($"Failed to find material generator \"{name}\", using default (\"{generator.GetType().Name}\")");
        return generator.GenerateMaterial(name, context);
    }

    // ============================================================================================
    // Texture generators, which create textures 
    private SortedList<string, ITextureGenerator> textureGenerators = new();
    private int defaultTextureGenerator = -1;

    // Add a generator
    public void AddTextureGenerator(string name, ITextureGenerator generator, bool makeDefault = false)
    {
        name = BaseName(name);
        if (textureGenerators.ContainsKey(name))
            Plugin.TryGet()?.Log.LogWarning($"Overwriting TextureGenerator \"{name}\" with \"{generator.GetType().Name}\"");
        textureGenerators[name] = generator;

        int index = textureGenerators.IndexOfKey(name);
        if (makeDefault) defaultTextureGenerator = index;
        else if (index <= defaultTextureGenerator) defaultTextureGenerator += 1;
    }

    // Get the default generator
    public ITextureGenerator GetDefaultTextureGenerator()
        => textureGenerators.ElementAt(defaultTextureGenerator).Value;

    // Get a generator; if it fails (returns false), the out is the default generator
    public bool TryGetTextureGenerator(string? name, out ITextureGenerator generator)
    {
        int index = name != null ? textureGenerators.IndexOfKey(BaseName(name)) : -1;
        if (index >= 0) generator = textureGenerators.ElementAt(index).Value;
        else generator = GetDefaultTextureGenerator();
        return index >= 0;
    }

    // Get a generator; if it fails, returns the default generator instead
    public ITextureGenerator GetTextureGenerator(string? name)
    {
        TryGetTextureGenerator(name, out ITextureGenerator generator);
        return generator;
    }

    // Shortcut to run a texture generator
    public Texture RunTextureGenerator(string? name, ConversionContext context)
    {
        if (!TryGetTextureGenerator(name, out var generator) && name != null)
            context.Log.LogWarning($"Failed to find texture generator \"{name}\", using default (\"{generator.GetType().Name}\")");
        return generator.GenerateTexture(name, context);
    }

}
