
# How-To: Using ThermalOverlay via Code

This piece talks about using ThermalOverlay programmatically. There are two main topics: integrating
the plugin with your items, and extending the plugin via its Factory system.
It's recommended to read the HowTo-CustomConfigs.md before this.
If you run into issues, reference the github at https://github.com/RoboRyGuy/ReTFO, and use
it as an example.

## Using the Plugin in your Rundown

The main purpose of using the plugin programmatically is to convert items (custom or otherwise) in your rundown.
There are a few main things to do to make this work correctly:
 - [Optional] Use the ConfigManager (obtainable via the Plugin instance) to set the ID mode, if needed.
   The default ID mode uses the item's checksum, which works in most (but not all) situations.
 - Use the ConfigManager to enable item IDs for conversion.
 - Load in configs. This can be done by creating instances of `ConfigEntry` and submitting them to the
   config manager or by creating a ConfigFile JSON file and requesting ConfigManager parse it by file name.

## Extensions using The Factory System

The Factory system is a system which was implemented to allow other developers to extend and 
improve ThermalOverlay without needing to go through or wait for me, the mod author. The 
process of using it is hopefully simple:

 - Create a new class implementing a factory interface
 - Register that class with the plugin's FactoryManager
 - [Optional] Change the configs so that your factory class gets used

There are six main interfaces you can implement, all of which live in the `ReTFO.ThermalOverlay.Interfaces` namespace:
 - IThermalConverter
 - ISightConverter
 - IOverlayGenerator
 - IMeshGenerator
 - ITextureGenerator
 - IMaterialGenerator

This document will outline how each of these work and how to implement them.

## Interface Inputs

When one of the above interfaces is called, it will be granted two inputs. The first is the name
used to call it `string? thisName`, and the second it the conversion context `ConversionContext context`.

### thisName

`thisName` is the name used to call the interface. Quick explanation, with more below:

 - It is only ever null when the interface is called as the "default"
 - It is a string value which starts with the name you used to register it
 - It is optionally followed by parentheses and parameters, ie "(5, black)"
   - A full name could be "MyName(5, black)" for the interface registered under "MyName"

You can choose to make your interface the default for its type when you register it; only one
interface can be the default at any given time; what it means to be the default varies by interface, and
will be explained further in the relevant section.

Parameters are typically considered optional and should only be required when the situation demands it.
That is, choose default values for parameters when and wherever you can.

### context

`context` is a `ReTFO.ThermalOverlay.Factories.ConversionContext` object. It contains context around
the conversion, including things like the item being configured, a reference to the plugin, factory, and
log objects, and references to the renderer and material selected, if they exist. Some interfaces are required
to set values in the context to be considered valid, and others are required to use values in the context
if they exist.

```
Plugin Plugin;                  // Always an instance of ReTFO.ThermalOverlay.Plugin
ItemEquippable Item;            // The Item being converted
Renderer? Renderer = null;      // The renderer displaying the thermals, if one has been identified
Material? Material = null;      // The material using the ThermalOverlay shader, which is being used by Renderer
ThermalConfig? Config = null;   // The config for the item, if one was found
```

## The Factory Manager

The `ReTFO.ThermalOverlay.Plugin` instance contains a single `ReTFO.ThermalOverlay.Factories.FactoryManager` object.
You can get the plugin using `Plugin.Get()`, and the `FactoryManager` is simply a public member of the plugin object.
The factory manager contains static helpers for working with parameters, and public methods for adding, getting, and 
running converters.

## IThermalConverter

`IThermalConverter` is the main interface responsible for converting an item. It will be the first and last item
to touch the item during the conversion process. It implements the `ConvertItem` method, wich has no return value.

`public abstract void ConvertItem(string? thisName, ConversionContext context);`

Its responsibilities are:
 - Select a conversion method (sight, overlay, both?) and run the relevant converters
   - If running a sight converter, it must select the renderer to convert and assign it to the context object
 - Apply shared configs before and after the conversion
   - Specifically, set the scope center and apply the config's MaterialConfig at the very end
 - Validate success and log failures (not successes)
   - Sight converters and overlay generators will log successes

The default ThermalConvert is used when an item has no config.

## ISightConverter

`ISightConverter` is responsible for taking an existing renderer and ensuring it has thermal vision. This generally
means adding a new Material using a thermal shader, which can be generated using a MaterialGenerator. `ISightConverter`
implements the `ConvertSight` method, which returns a bool (true if successful, false otherwise).

`public abstract bool ConvertSight(string? thisName, ConversionContext context);`

Its responsibilities are:
 - Ensure the sight is converted
 - Assign a Material to the context object
 - Log success/failure as needed

The default SightConverter is used when an item has no config and the ThermalConverter decides to use a SightConverter

## IOverlayGenerator

`IOverlayGenerator` is responsible for generating geometry which shows thermal vision and attaching it to the item. This 
generally means using a MeshGenerator to create geometry and using MaterialGenerator to attach a thermal material to that 
geometry. `IOverlayGenerator` implements the `GenerateOverlay` method, which returns a bool (true if successful, false otherwise)

`public abstract bool GenerateOverlay(string? thisName, ConversionContext context);`

Its responsibilities are:
 - Generate geometry
 - Attach it to the item (in a location it chooses)
 - Applying the config's transforms after attachment, if they exist
 - Assign both a Renderer and a Material to the context object
 - Log success/failure as needed

The default OverlayGenerator is used when the item has no config and the ThermalConverter decides to use an OverlayGenerator

## IMeshGenerator

`IMeshGenerator`, as the name suggests, is responsible for generating a `Mesh` object. It has a fair bit of freedom in this
respect, but it should generally try to generate a mesh that is reasonably sized and rotated before any transforms, and which
has UVs map to a square in its local XY plane (with the expectation of a texture being used as an alpha mask on it). This is
a rather loose definition, but do your best. `IMeshGenerator` implements the `GenerateMesh` method, which must return a `Mesh`.

`public abstract Mesh GenerateMesh(string? thisName, ConversionContext context);`

Its responsibilities are:
 - Provide a mesh, including UVs, Normals, and Tangents
   - It may reuse an existing mesh, including loading one from file or taking one from a loaded asset
 - Log failures

The default MeshGenerator is used by the OverlayGenerator to generate geometry for an overlay.

## ITextureGenerator

`ITextureGenerator` is similar to mesh generator, in which it must provide a Texture object, typically a Texture2D. It is used
mostly by the `MaterialConfig.Apply` method, though it can be used anywhere. It implements the `GenerateTexture` method, which
must return a `Texture` object.

`public abstract Texture GenerateTexture(string? thisName, ConversionContext context);`

Its responsibilities are:
 - Provide a Texture (any)
   - It may reuse an existing texture, including loading one from file or taking one from a loaded asset
 - Log failures

The default TextureGenerator is typically used as the MainTex on a generated overlay; it is typically an AlphaMask for the default Mesh.

## IMaterialGenerator

`IMaterialGenerator` typically generates a thermal material. Its main purpose is to set up a `Material` with a shader and
default properties. Practically speaking, this means having or creating a `MaterialConfig` and calling `ApplyAllWithDefaults` 
to a newly created `Material`, which it then returns. `IMaterialGenerator` implements the `GenerateMaterial` method, which 
must return a `Material`. If there is an error in the process, it may return a `Material` using Unity's builtin error shader, 
"Hidden/InternalErrorShader".

`public abstract Material GenerateMaterial(string? thisName, ConversionContext context);`

Its responsibilities are:
  - Provide a Material
  - Ensure all properties of that material are set to at least default values
  - Do *not* use the config's MaterialConfig
  - Log failures

The default MaterialGenerator is used when the item has no config

## Processing Parameters

For consistency, I recommend this pattern for processing parameters from a `thisName`.

```
// Set up parameters; give them default values, if possible
bool circle = true;
float min = 0f;
float max = 1f;
int size = 256;

// Use FactoryManager to process parameters
string[] parameters = FactoryManager.GetParameters(thisName);

// Check if each parameter exists and, if so, process it
if (parameters.Length > 0)
{
    // Extract the parameter and allow it to be empty
    string item = parameters[0];
    if (item.Length == 0) { }

    // It it isn't empty, try to use it. In this case, check against possible keywords
    else if (item == "Circle") circle = true;
    else if (item == "Square") circle = false;

    // Log errors
    else context.Log.LogError($"TextureGenerator_Bloom expected either \"Circle\" or \"Square\" for its first parameter, but instead got \"{item}\"");
}
if (parameters.Length > 1)
{
    // Extract the parameter and allow it to be empty
    string item = parameters[1];
    if (item.Length == 0) { }

    // It it isn't empty, try to use it. In this case, try and use it as a float
    else if (float.TryParse(item, out float value)) min = value;

    // Log errors
    else context.Log.LogError($"TextureGenerator_Bloom expected a float for its second parameter, but instead got \"{item}\"");
}
if (parameters.Length > 2)
{
    string item = parameters[2];
    if (item.Length == 0) { }
    else if (float.TryParse(item, out float value)) max = value;
    else context.Log.LogError($"TextureGenerator_Bloom expected a float for its third parameter, but instead got \"{item}\"");
}
if (parameters.Length > 3)
{
    string item = parameters[3];
    if (item.Length == 0) { }
    else if (uint.TryParse(item, out uint value)) size = (int)value;
    else context.Log.LogError($"TextureGenerator_Bloom expected a uint for its fourth parameter, but instead got \"{item}\"");
}

// If extra parameters made it through, log errors on those, too
if (parameters.Length > 4)
    context.Log.LogWarning($"TextureGenerator_Bloom ignoring extra parameters: {FactoryManager.FormatParams(parameters[4..])}");

```