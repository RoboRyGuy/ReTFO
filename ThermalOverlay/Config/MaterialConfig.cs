
using ReTFO.ThermalOverlay.Factories;
using System.Reflection;
using System.Text.Json.Serialization;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Config;

/// <summary>
/// Contains values and helpers for configuring a ThermalOverlay shader
/// </summary>
public struct MaterialConfig
{
    public MaterialConfig() { }

    // All shader properties stored by this config
    [JsonIgnore]
    public SortedList<string, object> Properties = new(1);

    // Keywords enabled for this shader
    [JsonIgnore]
    public List<string> Keywords = new(1);

    // Applies all property values, including default values, overriding all existing values
    public void ApplyAllWithDefaults(Material mat, ConversionContext context)
    {
        mat.SetFloat(Zoom_Name, Zoom.Value);
        mat.SetFloat(RatioAdjust_Name, RatioAdjust.Value);
        mat.SetFloat(ScreenIntensity_Name, ScreenIntensity.Value);
        mat.SetFloat(OffAngleFade_Name, OffAngleFade.Value);
        mat.SetTexture(HeatTex_Name, context.Factory.RunTextureGenerator(HeatTex.Value, context));
        mat.SetFloat(HeatFalloff_Name, HeatFalloff.Value);
        mat.SetFloat(FogFalloff_Name, FogFalloff.Value);
        mat.SetFloat(AlphaMult_Name, AlphaMult.Value);
        mat.SetFloat(BackgroundTemp_Name, BackgroundTemp.Value);
        mat.SetFloat(AmbientColorFactor_Name, AmbientColorFactor.Value);
        mat.SetFloat(AlbedoColorFactor_Name, AlbedoColorFactor.Value);
        mat.SetFloat(AmbientTemp_Name, AmbientTemp.Value);
        mat.SetFloat(OcclusionHeat_Name, OcclusionHeat.Value);
        mat.SetFloat(BodyOcclusionHeat_Name, BodyOcclusionHeat.Value);
        mat.SetTexture(DistortionTex_Name, context.Factory.RunTextureGenerator(DistortionTex.Value, context));
        mat.SetFloat(DistortionScale_Name, DistortionScale.Value);
        mat.SetFloat(DistortionSpeed_Name, DistortionSpeed.Value);
        mat.SetTexture(DistortionSignal_Name, context.Factory.RunTextureGenerator(DistortionSignal.Value, context));
        mat.SetFloat(DistortionSignalSpeed_Name, DistortionSignalSpeed.Value);
        mat.SetFloat(DistortionMin_Name, DistortionMin.Value);
        mat.SetFloat(DistortionMax_Name, DistortionMax.Value);
        mat.SetFloat(DistortionMinShadowEnemies_Name, DistortionMinShadowEnemies.Value);
        mat.SetFloat(DistortionMaxShadowEnemies_Name, DistortionMaxShadowEnemies.Value);
        mat.SetFloat(DistortionSignalSpeedShadowEnemies_Name, DistortionSignalSpeedShadowEnemies.Value);
        mat.SetFloat(ShadowEnemyFresnel_Name, ShadowEnemyFresnel.Value);
        mat.SetFloat(ShadowEnemyHeat_Name, ShadowEnemyHeat.Value);
        mat.SetVector(ScopeCenter_Name, ScopeCenter.Value);
        mat.SetFloat(CenterWhenUnscoped_Name, CenterWhenUnscoped.Value);
        mat.SetFloat(UncenterWhenScoped_Name, UncenterWhenScoped.Value);
        mat.SetTexture(MainTex_Name, context.Factory.RunTextureGenerator(MainTex.Value, context));
        mat.SetTexture(ReticuleA_Name, context.Factory.RunTextureGenerator(ReticuleA.Value, context));
        mat.SetTexture(ReticuleB_Name, context.Factory.RunTextureGenerator(ReticuleB.Value, context));
        mat.SetTexture(ReticuleC_Name, context.Factory.RunTextureGenerator(ReticuleC.Value, context));
        mat.SetColor(ReticuleColorA_Name, ReticuleColorA.Value);
        mat.SetColor(ReticuleColorB_Name, ReticuleColorB.Value);
        mat.SetColor(ReticuleColorC_Name, ReticuleColorC.Value);
        mat.SetFloat(SightDirt_Name, SightDirt.Value);
        mat.SetFloat(ProjSize1_Name, ProjSize1.Value);
        mat.SetFloat(ProjSize2_Name, ProjSize2.Value);
        mat.SetFloat(ProjSize3_Name, ProjSize3.Value);

        foreach (string key in Keywords)
            mat.SetKeywordEnabled(key, true);
    }

    // Applies all properties and keywords held in this config to the provided material
    public void ApplyAll(Material mat, ConversionContext context)
    {
        foreach (var pair in Properties)
        {
            int index = mat.shader.FindPropertyIndex(pair.Key);
            Type expectedType;
            UnityEngine.Rendering.ShaderPropertyType type = mat.shader.GetPropertyType(index);
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    expectedType = typeof(Color);
                    Color? color = pair.Value as Color?;
                    if (color == null)
                        break;
                    mat.SetColor(index, color.Value);
                    continue;

                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    expectedType = typeof(Vector4);
                    Vector4? vector = pair.Value as Vector4?;
                    if (vector == null)
                        break;
                    mat.SetVector(index, vector.Value);
                    continue;

                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    expectedType = typeof(float);
                    float? num = pair.Value as float?;
                    if (num == null)
                        break;
                    mat.SetFloat(index, num.Value);
                    continue;

                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    expectedType = typeof(string);
                    string? texture = pair.Value as string;
                    if (texture == null)
                        break;
                    Texture tex = context.Factory.RunTextureGenerator(texture, context);
                    mat.SetTexture(pair.Key, tex);
                    continue;

                default:
                    context.Log.LogWarning($"Encountered unknown shader property type {type} while applying to material; ignoring");
                    continue;
            }
            context.Log.LogError($"MaterialConfig expected a {expectedType.Name} for key \"{pair.Key}\", but got a {pair.Value?.GetType().Name ?? "null"} instead");
        }

        foreach (string keyword in Keywords)
            mat.SetKeywordEnabled(keyword, true);
    }

    // Deep clone of this config
    public MaterialConfig Clone()
    {
        MaterialConfig clone = new MaterialConfig();
        clone.Properties = new(Properties);
        clone.Keywords = new(Keywords);
        return clone;
    }

    // Creates a config from an existing material. Useful for saving tweaked settings. See also: LogProperties
    public static MaterialConfig FromMaterial(Material mat, bool removeDefault = true)
    {
        MaterialConfig result = new();
        //string[] propNames = new[] {
        //    Zoom_Name, RatioAdjust_Name, ScreenIntensity_Name, OffAngleFade_Name, HeatTex_Name, HeatFalloff_Name, FogFalloff_Name,
        //    AlphaMult_Name, BackgroundTemp_Name, AmbientColorFactor_Name, AlbedoColorFactor_Name, AmbientTemp_Name, OcclusionHeat_Name,
        //    BodyOcclusionHeat_Name, DistortionTex_Name, DistortionScale_Name, DistortionSpeed_Name, DistortionSignal_Name,
        //    DistortionSignalSpeed_Name, DistortionMin_Name, DistortionMax_Name, DistortionMinShadowEnemies_Name, DistortionMaxShadowEnemies_Name,
        //    DistortionSignalSpeedShadowEnemies_Name, ShadowEnemyFresnel_Name, ShadowEnemyHeat_Name, ScopeCenter_Name, CenterWhenUnscoped_Name,
        //    UncenterWhenScoped_Name, MainTex_Name, ReticuleA_Name, ReticuleB_Name, ReticuleC_Name, ReticuleColorA_Name, ReticuleColorB_Name,
        //    ReticuleColorC_Name, SightDirt_Name, ProjSize1_Name, ProjSize2_Name, ProjSize3_Name,
        //};
        PropertyInfo[] props = typeof(MaterialConfig).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        foreach (PropertyInfo prop in props)
        {
            string name = '_' + prop.Name;
            int index = mat.shader.FindPropertyIndex(name);
            if (index < 0) continue; // Not sure what this is about
            UnityEngine.Rendering.ShaderPropertyType type = mat.shader.GetPropertyType(index);
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    Color color = mat.GetColor(name);
                    if (removeDefault && color == ((ShaderValue<Color>)prop.GetValue(result)!).Value)
                        continue;
                    result.Set(name, new ShaderValue<Color>(color));
                    continue;

                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    Vector4 vector = mat.GetVector(name);
                    if (removeDefault && vector == ((ShaderValue<Vector4>)prop.GetValue(result)!).Value)
                        continue;
                    result.Set(name, new ShaderValue<Vector4>(vector));
                    continue;

                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    float val = mat.GetFloat(name);
                    if (removeDefault && val == ((ShaderValue<float>)prop.GetValue(result)!).Value)
                        continue;
                    result.Set(name, new ShaderValue<float>(val));
                    continue;

                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    string tex = mat.GetTexture(name)?.name ?? "null";
                    if (removeDefault && tex == ((ShaderValue<string>)prop.GetValue(result)!).Value)
                        continue;
                    result.Set(name, new ShaderValue<string>(tex));
                    continue;
            }
        }
        result.Keywords = mat.GetShaderKeywords().ToList();
        return result;
    }

    public static void LogProperties(Material mat, BepInEx.Logging.ManualLogSource log, bool skipDefaults = true)
    {
        MaterialConfig config = FromMaterial(mat, skipDefaults);
        log.LogDebug(string.Join("\n - ", Enumerable.Concat(Enumerable.Concat(
            Enumerable.Repeat($"\nMaterial config for \"{mat.name}\":", 1),
            config.Properties.Select(pair => $"{pair.Key} -> {pair.Value}")),
            config.Keywords.Select(keyword => $"{keyword} enabled")
        )));
    }

    #region Definitions

    // Private helper for getting shader values
    ShaderValue<T> Get<T>(string key, T defaultValue)
    {
        if (Properties.ContainsKey(key))
        {
            object value = Properties[key];
            if (value is T)
                return new ShaderValue<T> { Value = (T)value, IsSet = true };
        }
        return new ShaderValue<T> { Value = defaultValue, IsSet = false };
    }

    // Private helper for setting shader values
    void Set<T>(string key, ShaderValue<T> value)
    {
        if (value.IsSet)
            Properties[key] = value.Value!;
        else Properties.Remove(key);
    }
    

    // PICTURE CONFIG // ==========================================================================

    // "Pixel Zoom", from 0 (none) to 1 (infinite). The formula is `ActualZoom = 1 / (1 - Zoom)`.
    // For example, .5 is 2x zoom, .666 is about 3x zoom, etc
    public ShaderValue<float> Zoom
    {
        get { return Get(Zoom_Name, 0f); }
        set { Set(Zoom_Name, value); }
    }
    public const string Zoom_Name = "_Zoom";

    // "Aspect Ratio Adjust", from 0 to 2. Stretches the image vertically
    //  without changing the width. More = taller and thinner
    public ShaderValue<float> RatioAdjust
    {
        get { return Get(RatioAdjust_Name, 1f); }
        set { Set(RatioAdjust_Name, value); }
    }
    public const string RatioAdjust_Name = "_RatioAdjust";

    // "Screen Brightness", from 0 to 1. At 0, the screen is pure black
    public ShaderValue<float> ScreenIntensity
    {
        get { return Get(ScreenIntensity_Name, 0.2f); }
        set { Set(ScreenIntensity_Name, value); }
    }
    public const string ScreenIntensity_Name = "_ScreenIntensity";

    // "Off-Angle Fade", from 0 to 1. When viewing the screen at glancing
    //  angles, when does it fade to black/white?
    public ShaderValue<float> OffAngleFade
    {
        get { return Get(OffAngleFade_Name, 0.95f); }
        set { Set(OffAngleFade_Name, value); }
    }
    public const string OffAngleFade_Name = "_OffAngleFade";

    // THERMAL CONFIG // ==========================================================================

    // "Heat Gradient", 2D texture. Maps heat values to colors; the x-axis is
    //   the heat. See the example textures for, well, examples
    public ShaderValue<string> HeatTex
    {
        get { return Get(HeatTex_Name, "white"); }
        set { Set(HeatTex_Name, value); }
    }
    public const string HeatTex_Name = "_HeatTex";

    // "Distance Falloff", from 0 to 1. How much heat dissapates over distances
    public ShaderValue<float> HeatFalloff
    {
        get { return Get(HeatFalloff_Name, 0.01f); }
        set { Set(HeatFalloff_Name, value); }
    }
    public const string HeatFalloff_Name = "_HeatFalloff";

    // "Fog Falloff", from 0 to 1. How much heat dissapates when due to fog.
    //  Thicker fog makes a bigger difference.
    public ShaderValue<float> FogFalloff
    {
        get { return Get(FogFalloff_Name, 0.1f); }
        set { Set(FogFalloff_Name, value); }
    }
    public const string FogFalloff_Name = "_FogFalloff";

    // "Alpha Mult", float, is multiplied against the resulting alpha value
    //  before blending. Best used when reusing the scope's _MainTex for
    //  masking, to compensate for excessive transparency.
    public ShaderValue<float> AlphaMult
    {
        get { return Get(AlphaMult_Name, 1.0f); }
        set { Set(AlphaMult_Name, value); }
    }
    public const string AlphaMult_Name = "_AlphaMult";

    // "Sky Temperature", from 0 to 1. The default temperature when no object is
    //  within viewing distance, namely the sky during the rare outdoor sections.
    public ShaderValue<float> BackgroundTemp
    {
        get { return Get(BackgroundTemp_Name, 0.05f); }
        set { Set(BackgroundTemp_Name, value); }
    }
    public const string BackgroundTemp_Name = "_BackgroundTemp";

    // "Screen Color Factor", from 0 to 10. Not sure what this does
    public ShaderValue<float> AmbientColorFactor
    {
        get { return Get(AmbientColorFactor_Name, 5f); }
        set { Set(AmbientColorFactor_Name, value); }
    }
    public const string AmbientColorFactor_Name = "_AmbientColorFactor";

    // "Screen Albedo Factor", from 0 to 10. How much color from the physical
    //  screen is blended into the result, I think?
    public ShaderValue<float> AlbedoColorFactor
    {
        get { return Get(AlbedoColorFactor_Name, 0.5f); }
        set { Set(AlbedoColorFactor_Name, value); }
    }
    public const string AlbedoColorFactor_Name = "_AlbedoColorFactor";

    // "Temperature Scale", from 0 to 1. Not sure what this does.
    public ShaderValue<float> AmbientTemp
    {
        get { return Get(AmbientTemp_Name, 0.15f); }
        set { Set(AmbientTemp_Name, value); }
    }
    public const string AmbientTemp_Name = "_AmbientTemp";


    // "Ambient Occlusion Factor", float. Heat modifier for ambient occluded spaces
    public ShaderValue<float> OcclusionHeat
    {
        get { return Get(OcclusionHeat_Name, 0.5f); }
        set { Set(OcclusionHeat_Name, value); }
    }
    public const string OcclusionHeat_Name = "_OcclusionHeat";

    // "Ambient Occlusion Factor (Skin)", float. Heat modifier for hot occluded
    //  spaces, namely enemies.
    public ShaderValue<float> BodyOcclusionHeat
    {
        get { return Get(BodyOcclusionHeat_Name, 2.5f); }
        set { Set(BodyOcclusionHeat_Name, value); }
    }
    public const string BodyOcclusionHeat_Name = "_BodyOcclusionHeat";


    // DISTORTION // ==============================================================================

    // "Distortion Texture", 2D texture. Texture used to constantly distort the image
    public ShaderValue<string> DistortionTex
    {
        get { return Get(DistortionTex_Name, "gray"); }
        set { Set(DistortionTex_Name, value); } 
    }
    public const string DistortionTex_Name = "_DistortionTex";

    // <unused> "Distortion Center", Range(0, 1)) = 0.5
    // <unused> public const string DistortionCenter_Name = "_DistortionCenter"; 

    // "Distortion Scale", float. Scales distortion effect magnitudes
    public ShaderValue<float> DistortionScale
    {
        get { return Get(DistortionScale_Name, 1f); }
        set { Set(DistortionScale_Name, value); }
    }
    public const string DistortionScale_Name = "_DistortionScale";

    // "Distortion Speed", float. How quickly the distortion texture is traversed
    public ShaderValue<float> DistortionSpeed
    {
        get { return Get(DistortionSpeed_Name, 1f); }
        set { Set(DistortionSpeed_Name, value); }
    }
    public const string DistortionSpeed_Name = "_DistortionSpeed";


    // "Distortion Signal", 2D texture. Texture used to occassionally super distort the image
    public ShaderValue<string> DistortionSignal
    {
        get { return Get(DistortionSignal_Name, "black"); }
        set { Set(DistortionSignal_Name, value); }
    }
    public const string DistortionSignal_Name = "_DistortionSignal";

    // "Distortion Signal Speed", float. How quickly the signal texture is traversed.
    public ShaderValue<float> DistortionSignalSpeed
    {
    get { return Get(DistortionSignalSpeed_Name, 1f); }
    set { Set(DistortionSignalSpeed_Name, value); }
    }
    public const string DistortionSignalSpeed_Name = "_DistortionSignalSpeed";

    // "Distortion Min", from 0 to 1. Scales distortion effects
    public ShaderValue<float> DistortionMin
    {
    get { return Get(DistortionMin_Name, 0.01f); }
    set { Set(DistortionMin_Name, value); }
    }
    public const string DistortionMin_Name = "_DistortionMin";

    // "Distortion Max", from 0 to 1. Scales distortion effects
    public ShaderValue<float> DistortionMax
    {
        get { return Get(DistortionMax_Name, 0.4f); }
        set { Set(DistortionMax_Name, value); }
    }
    public const string DistortionMax_Name = "_DistortionMax";

    // SHADOW ENEMIES // ==========================================================================

    // "Min Shadow Enemy Distortion", from 0 to 1. Scales distortion effects for shadows
    public ShaderValue<float> DistortionMinShadowEnemies
    {
        get { return Get(DistortionMinShadowEnemies_Name, 0.2f); }
        set { Set(DistortionMinShadowEnemies_Name, value); }
    }
    public const string DistortionMinShadowEnemies_Name = "_DistortionMinShadowEnemies";

    // "Max Shadow Enemy Distortion", from 0 to 1. Scales distortion effects for shadows
    public ShaderValue<float> DistortionMaxShadowEnemies
    {
        get { return Get(DistortionMaxShadowEnemies_Name, 1f); }
        set { Set(DistortionMaxShadowEnemies_Name, value); }
    }
    public const string DistortionMaxShadowEnemies_Name = "_DistortionMaxShadowEnemies";

    // "Distortion Signal Speed Shadow enemies", float. Speed modifier for distortion effects for shadows
    public ShaderValue<float> DistortionSignalSpeedShadowEnemies
    {
        get { return Get(DistortionSignalSpeedShadowEnemies_Name, 0.025f); }
        set { Set(DistortionSignalSpeedShadowEnemies_Name, value); }
    }
    public const string DistortionSignalSpeedShadowEnemies_Name = "_DistortionSignalSpeedShadowEnemies";

    // "Shadow Enemy Fresnel", float. Scales fresnel effects for shadows
    public ShaderValue<float> ShadowEnemyFresnel
    {
        get { return Get(ShadowEnemyFresnel_Name, 10f); }
        set { Set(ShadowEnemyFresnel_Name, value); }
    }
    public const string ShadowEnemyFresnel_Name = "_ShadowEnemyFresnel";

    // "Shadow Enemy Heat", from 0 to 1. Heat of shadow enemies
    public ShaderValue<float> ShadowEnemyHeat
    {
        get { return Get(ShadowEnemyHeat_Name, 0.1f); }
        set { Set(ShadowEnemyHeat_Name, value); }
    }
    public const string ShadowEnemyHeat_Name = "_ShadowEnemyHeat";

    // SIGHT CONFIG // ============================================================================

    // Where the center of the scope is in object space; the spot, in local coordinates, that will
    //  be in the center of the screen when scoped in. 
    // This is usually computed automatically when a ThermalOverlay is created, but it can be
    //  overriden in this config if the need arises.
    public ShaderValue<Vector4> ScopeCenter
    {
        get { return Get(ScopeCenter_Name, new Vector4(0, 0, 0, 1)); }
        set { Set(ScopeCenter_Name, value); }
    }
    public const string ScopeCenter_Name = "_ScopeCenter";

    // "Center when Unscoped", toggle. Whether to show the center of the screen when unscoped, as
    //  opposed to showing what is behind the thermal object (passthrough). Typically 1 (true),
    //  which matches how it works in vanilla.
    public ShaderValue<float> CenterWhenUnscoped
    {
        get { return Get(CenterWhenUnscoped_Name, 1f); }
        set { Set(CenterWhenUnscoped_Name, value); }
    }
    public const string CenterWhenUnscoped_Name = "_CenterWhenUnscoped";

    // "Uncenter when Scoped", toggle. Requires CenterWhenScoped. When scoping in, continue using
    //  center-of-screen thermals? Leave off to let weapons aim accurately, but turn on for tools
    //  since they never put the screen in the center of the screen.
    // Context: With transparent thermals, you can see the disconnect while aiming due to the gun
    //  moving left/right; to counter this, the overlay switches modes when scoped in to just
    //  show exactly what is behind the scope, instead of what's in the center of the screen.
    //  This lets you disable that for tools which don't need/use good aim, ie the Biotracker,
    //  C-Foam Launcher, etc. Use as you see fit.
    public ShaderValue<float> UncenterWhenScoped
    {
        get { return Get(UncenterWhenScoped_Name, 1f); }
        set { Set(UncenterWhenScoped_Name, value); }
    }
    public const string UncenterWhenScoped_Name = "_UncenterWhenScoped";

    // "Main Texture", 2D texture. Red: Alpha Mask (multiplied), Green: Dirt (OneMinus multiplied)
    // The intent is that you can use the same MainTex as the Holographic sight as an alpha mask
    public ShaderValue<string> MainTex
    {
        get { return Get(MainTex_Name, "red"); }
        set { Set(MainTex_Name, value); }
    }
    public const string MainTex_Name = "_MainTex";

    // "ReticuleA R:Sharp G:Blurry", 2D texture.
    // Shape of the reticule; color is ignored, see ReticuleColorA
    public ShaderValue<string> ReticuleA
    {
        get { return Get(ReticuleA_Name, "black"); }
        set { Set(ReticuleA_Name, value); }
    }
    public const string ReticuleA_Name = "_ReticuleA";

    // "ReticuleB R:Sharp G:Blurry", 2D texture.
    // Shape of the reticule; color is ignored, see ReticuleColorB
    public ShaderValue<string> ReticuleB
    {
        get { return Get(ReticuleB_Name, "black"); }
        set { Set(ReticuleB_Name, value); }
    }
    public const string ReticuleB_Name = "_ReticuleB";

    // "ReticuleC R:Sharp G:Blurry", 2D texture.
    // Shape of the reticule; color is ignored, see ReticuleColorC
    public ShaderValue<string> ReticuleC
    {
        get { return Get(ReticuleC_Name, "black"); }
        set { Set(ReticuleC_Name, value); }
    }
    public const string ReticuleC_Name = "_ReticuleC";

    // "ReticuleA color", Color. Color of the reticule
    public ShaderValue<Color> ReticuleColorA
    {
        get { return Get(ReticuleColorA_Name, Color.white); }
        set { Set(ReticuleColorA_Name, value); }
    }
    public const string ReticuleColorA_Name = "_ReticuleColorA";

    // "ReticuleB color", Color. Color of the reticule
    public ShaderValue<Color> ReticuleColorB
    {
        get { return Get(ReticuleColorB_Name, Color.white); }
        set { Set(ReticuleColorB_Name, value); }
    }
    public const string ReticuleColorB_Name = "_ReticuleColorB";

    // "ReticuleC color", Color. Color of the reticule
    public ShaderValue<Color> ReticuleColorC
    {
        get { return Get(ReticuleColorC_Name, Color.white); }
        set { Set(ReticuleColorC_Name, value); }
    }
    public const string ReticuleColorC_Name = "_ReticuleColorC";

    // "Sight Dirt", from 0 to 20. The amount of dirt on the sight. Multiplied by the green
    //  of _MainTex, then used to lower RGB: `outRGB = inRGB * (1 - green * _SightDirt)`
    public ShaderValue<float> SightDirt
    {
        get { return Get(SightDirt_Name, 1f); }
        set { Set(SightDirt_Name, value); }
    }
    public const string SightDirt_Name = "_SightDirt";

    // <unused> "Supports FPS Rendering", Float) = 1
    // <unused> public const string SupportsFPSRendering_Name = "_SupportsFPSRendering"; 
    // <unused> "FPS Rendering?", Float) = 0
    // <unused> public const string EnableFPSRendering_Name = "_EnableFPSRendering"; 

    // <unused> "Lit Glass", Float) = 1
    // <unused> public const string LitGlass_Name = "_LitGlass"; 

    // <unused> "Clip Borders", Float) = 1
    // <unused> public const string ClipBorders_Name = "_ClipBorders"; 
    // <unused> "X Axis", Vector) = (1,0,0,0)
    // <unused> public const string AxisX_Name = "_AxisX"; 
    // <unused> "Y Axis", Vector) = (0,1,0,0)
    // <unused> public const string AxisY_Name = "_AxisY"; 
    // <unused> "Z Axis", Vector) = (0,0,1,0)
    // <unused> public const string AxisZ_Name = "_AxisZ"; 
    // <unused> "Flip", Float) = 1
    // <unused> public const string Flip_Name = "_Flip"; 

    // <unused> "Distance 1", Range(1, 100)) = 100
    // <unused> public const string ProjDist1_Name = "_ProjDist1"; 
    // <unused> "Distance 2", Range(1, 100)) = 66
    // <unused> public const string ProjDist2_Name = "_ProjDist2"; 
    // <unused> "Distance 3", Range(1, 100)) = 33
    // <unused> public const string ProjDist3_Name = "_ProjDist3"; 

    // "Size 1", from 0 to 3. Size scale applied to reticule A
    public ShaderValue<float> ProjSize1
        {
        get { return Get(ProjSize1_Name, 1f); }
        set { Set(ProjSize1_Name, value); }
        }
    public const string ProjSize1_Name = "_ProjSize1";

    // "Size 2", from 0 to 3. Size scale applied to reticule B
    public ShaderValue<float> ProjSize2
        {
        get { return Get(ProjSize2_Name, 1f); }
        set { Set(ProjSize2_Name, value); }
        }
    public const string ProjSize2_Name = "_ProjSize2";

    // "Size 3", from 0 to 3. Size scale applied to reticule C
    public ShaderValue<float> ProjSize3
    {
        get { return Get(ProjSize3_Name, 1f); }
        set { Set(ProjSize3_Name, value); }
    }
    public const string ProjSize3_Name = "_ProjSize3";

    // <unused> "Zeroing", Range(-1, 1)) = 0
    // <unused> public const string ZeroOffset_Name = "_ZeroOffset"; 

    // KEYWORDS // ================================================================================

    // When enabled, renders using the item layer camera instead of the main camera.
    // Typically automatically enabled when gear is equipped (I'm not sure when exactly).
    public bool FPSRenderingEnabled
    {
        get { return Keywords.Contains(FPSRenderingKeyword); }
        set
        {
            if (value && !Keywords.Contains(FPSRenderingKeyword))
                Keywords.Add(FPSRenderingKeyword);
            else if (!value)
                Keywords.RemoveAll(s => s == FPSRenderingKeyword);
        }
    }
    public const string FPSRenderingKeyword = "ENABLE_FPS_RENDERING";

    // When enabled, render in a method compatible with the Unity editor. Not used in vanilla gameplay.
    public bool EditorRenderingEnabled
    {
        get { return Keywords.Contains(EditorRenderingKeyword); }
        set
        {
            if (value && !Keywords.Contains(EditorRenderingKeyword))
                Keywords.Add(EditorRenderingKeyword);
            else if (!value)
                Keywords.RemoveAll(s => s == EditorRenderingKeyword);
        }
    }
    public const string EditorRenderingKeyword = "EDITOR_RENDERING_ENABLED";

    #endregion
}
