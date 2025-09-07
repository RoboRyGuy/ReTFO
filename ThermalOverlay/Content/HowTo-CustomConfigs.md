
# How-To: Configuring ThermalOverlay to Customize Weapons

So, it's come to the point that the default configurations aren't enough. 
Well, the good news is that I've made it as easy as possible to change as
much as possible. Written below are the basic behind how the mod works,
and then how to use configs to leverage that for your advantage. Enjoy!

Note: When reading JSON in this file, `#` marks a comment. Putting `#` in actual
JSON files will cause an error, so don't copy it!

## File Paths Reference

Quick reference for file paths.

| File           | Path                                                      |
| -------------- | --------------------------------------------------------- |
| UserConfig     | BepInEx/GameData/ThermalOverlay/ThermalOverlayConfig.json |
| MaterialConfig | BepInEx/GameData/*                                        |
| Textures       | BepInEx/Assets/*                                          |

## JSON Reference

The default config file can be found at `BepInEx/GameData/ThermalOverlay/ThermalOverlayConfig.json`.
The config file is made up of a single object with a single property, "Configs", which is a list of config entries

```
{
  "Configs": [
    ## Config entries go here   
  ]
}
```

Each config entry looks like the below. Most lines can be omitted to use the default value:

```
{
  "ConfigName": "My test config",
  "IDs": [ 504338160, 3524198550, 1767509775 ],
  "Config": {
    "Handler": "Auto",
    "SightConverter": "Standard",
    "OverlayGenerator": "Standard",
    "OverlayConfig": {
      "MeshGenerator": "Diamond",
      "MeshOffset":   { "x": 0, "y": 0, "z": 0 },
      "MeshRotation": { "x": 0, "y": 0, "z": 0, "w": 1 },
      "MeshScale":    { "x": 1, "y": 1, "z": 1 },
      "TextureGenerator": "Bloom(Circle)"
    },
    "MaterialGenerator": "PR",
    "MaterialConfig": {
      "Zoom": 0,
      "RatioAdjust": 1,
      "ScreenIntensity": 0.2,
      "OffAngleFade": 0.95,
      "HeatTex": "Load(ThermalOverlay, whitehot_Transparent.png)",
      "HeatFalloff": 0.01,
      "FogFalloff": 0.1,
      "AlphaMult": 1,
      "BackgroundTemp": 0.05,
      "AmbientColorFactor": 5,
      "AlbedoColorFactor": 0.5,
      "AmbientTemp": 0.15,
      "OcclusionHeat": 0.5,
      "BodyOcclusionHeat": 2.5,
      "DistortionTex": "Load(ThermalOverlay, Scanline.png)",
      "DistortionScale": 1,
      "DistortionSpeed": 1,
      "DistortionSignal": "Load(ThermalOverlay, ThermalDistortionSignal.png)",
      "DistortionSignalSpeed": 1,
      "DistortionMin": 0.1,
      "DistortionMax": 0.4,
      "DistortionMinShadowEnemies": 0.2,
      "DistortionMaxShadowEnemies": 1,
      "DistortionSignalSpeedShadowEnemies": 0.025,
      "ShadowEnemyFresnel": 10,
      "ShadowEnemyHeat": 0.1,
      "ScopeCenter": { "x": 0, "y": 0, "z": 0, "w": 1 },
      "CenterWhenUnscoped": 1,
      "UncenterWhenScoped": 0,
      "MainTex": "red",
      "ReticuleA": "black",
      "ReticuleB": "black",
      "ReticuleC": "black",
      "ReticuleColorA": { "r": 1, "g": 1, "b": 1, "a": 1 },
      "ReticuleColorB": { "r": 1, "g": 1, "b": 1, "a": 1 },
      "ReticuleColorC": { "r": 1, "g": 1, "b": 1, "a": 1 },
      "SightDirt": 1,
      "ProjSize1": 1,
      "ProjSize2": 1,
      "ProjSize3": 1
    }
  }
}
```

| Property Name                          | Type                          | Optional? | Explanation                                                                                   |
| ----------------------------------     | ----------------------------- | ----------| --------------------------------------------------------------------------------------------- |
| ConfigName                             | Any string                    | Yes       | Used when printing debug messages                                                             |
| IDs                                    | A list of uints               | No        | A list of gear items the config applies to                                                    |
| Config                                 | A ThermalConfig object        | No        | Contains the config to apply to the gear items listed in IDs                                  |
| Handler                                | A ThermalConverter name       | Yes       | The main handler used to give an item thermal vision.                                         |
| SightConverter                         | A SightConverter name         | Yes       | The conerter used to give an existing scope thermal vision.                                   |
| OverlayGenerator                       | An OverlayGenerator name      | Yes       | The generator used to create an overlay, generally if there is no scope.                      |
| OverlayConfig                          | An OverlayConfig object       | Yes       | Additional information for generating the overlay                                             |
|  - MeshGenerator                       | A MeshGenerator name          | Yes       | Used to generate the mesh the overlay will use.                                               |
|  - MeshOffset                          | A Vector3                     | Yes       | An offset added to the generated mesh (in local space, after it's been aligned)               |
|  - MeshRotation                        | A Quaternion                  | Yes       | The rotation of the generated mesh (in local space, after it's been aligned)                  |
|  - MeshScale                           | A Vector3                     | Yes       | The scale of the generated mesh (in lcaol space, after it's been aligned)                     |
|  - TextureGenerator                    | A TextureGenerator name       | Yes       | This will be the MainTex used by the overlay shader as an alpha and dirt mask                 |
| MaterialGenerator                      | A MaterialGenerator name      | Yes       | The material generator used to create the thermal material                                    |
| MaterialConfig                         | A MaterialConfig object       | Yes       | Various properties that will be applied to the material, overriding existing values           |
|  - Zoom                                | A float (0 to 1)              | Yes       | The zoom applied to the scope; 0 is none, .5 is double. 2/3 is triple, etc...                 |
|  - RatioAdjust                         | A float (0 to 2)              | Yes       | Stretches / squeezes the scope, making it taller / wider                                      |
|  - ScreenIntensity                     | A float (0 to 1)              | Yes       | How bright the thermal image will be. 0 is pure black; .1 or .2 works well                    |
|  - OffAngleFade                        | A float (0 to 1)              | Yes       | Mutes the screen when viewing it at glancing angles. Higher is stricter; defaults to .95      |
|  - HeatTex                             | A TextureGenerator name       | Yes       | The heat texture to use, typically uses Load()                                                |
|  - HeatFalloff                         | A float (0 to 1)              | Yes       | Heat falloff by distance, with zero being none                                                |
|  - FogFalloff                          | A float (0 to 1)              | Yes       | The amount of heat falloff in fog, with zero being none                                       |
|  - AlphaMult                           | A float                       | Yes       | Multiplies the calculated alpha, making the material more transparent or opaque               |
|  - BackgroundTemp                      | A float (0 to 1)              | Yes       | The temperature to show when there is no object in viewing distance; the sky's temp           |
|  - AmbientColorFactor                  | A float (0 to 10)             | Yes       | I don't know. It's also called "Screen Color Factor"                                          |
|  - AlbedoColorFactor                   | A float (0 to 10)             | Yes       | I don't know. It's also called "Screen Albedo Factor"                                         |
|  - AmbientTemp                         | A float (0 to 1)              | Yes       | Temp of misc objects which have no heat                                                       |
|  - OcclusionHeat                       | A float                       | Yes       | Heat modifier for occluded spaces, ie ambient occlusion                                       |
|  - BodyOcclusionHeat                   | A float                       | Yes       | Heat modifier for hot occluded spaces, namely enemy skin                                      |
|  - DistortionTex                       | A TextureGenerator name       | Yes       | The distortion texture to use. This is typically scanline.png                                 |
|  - DistortionScale                     | A float (0 to 1)              | Yes       | Scale of the distortion effect                                                                |
|  - DistortionSpeed                     | A float                       | Yes       | How fast the distortion effect is                                                             |
|  - DistortionSignal                    | A TextureGenerator name       | Yes       | A second, more severe and less consistent distortion texture for severe distortion            |
|  - DistortionSignalSpeed               | A float                       | Yes       | How fast the distortion signal is                                                             |
|  - DistortionMin                       | A float (0 to 1)              | Yes       | Min amount of distortion                                                                      |
|  - DistortionMax                       | A float (0 to 1)              | Yes       | Max amount of distortion                                                                      |
|  - DistortionMinShadowEnemies          | A float (0 to 1)              | Yes       | Min amount of distortion for shadow enemies                                                   |
|  - DistortionMaxShadowEnemies          | A float (0 to 1)              | Yes       | Max amount of distortion for shadow enemies                                                   |
|  - DistortionSignalSpeedShadowEnemies  | A float                       | Yes       | Distortion signal speed when looking at shadow enemies                                        |
|  - ShadowEnemyFresnel                  | A float                       | Yes       | Amount of fresnel when viewing shadow enemies. Typically around 50                            |
|  - ShadowEnemyHeat                     | A float (0 to 1)              | Yes       | How hot shadow enemies are                                                                    |
|  - ScopeCenter                         | A Vector4                     | Yes       | Indicates where the center of the scope is, in object space. Used for CenterWhenUnscoped      |
|  - CenterWhenUnscoped                  | A float (0 or 1)              | Yes       | 1 = Show the center of the screen on the scope when not ADS, 0 = show what's behind the scope |
|  - UncenterWhenScoped                  | A float (0 or 1)              | Yes       | Typically 0. Requires CenterWhenUnscoped. Keep using center view when scoped in?              |
|  - MainTex                             | A TextureGenerator name       | Yes       | Red is an alpha mask (higher is more opaque), green is dirt (mute color to black)             |
|  - ReticuleA                           | A TextureGenerator name       | Yes       | A reticule to display on the screen. Red is sharp, green is blurry                            |
|  - ReticuleB                           | A TextureGenerator name       | Yes       | A reticule to display on the screen. Red is sharp, green is blurry                            |
|  - ReticuleC                           | A TextureGenerator name       | Yes       | A reticule to display on the screen. Red is sharp, green is blurry                            |
|  - ReticuleColorA                      | A Color                       | Yes       | Color of reticule A                                                                           |
|  - ReticuleColorB                      | A Color                       | Yes       | Color of reticule B                                                                           |
|  - ReticuleColorC                      | A Color                       | Yes       | Color of reticule C                                                                           |
|  - SightDirt                           | A float                       | Yes       | Multiplier for amount of dirt. Typically 0 or 1 to enable/disable it                          |
|  - ProjSize1                           | A float                       | Yes       | Scale applied to reticule A                                                                   |
|  - ProjSize2                           | A float                       | Yes       | Scale applied to reticule B                                                                   |
|  - ProjSize3                           | A float                       | Yes       | Scale applied to reticule C                                                                   |


## Converters and Generators

To support extension and advanced customization by plugin developers, this mod uses a factory 
system, which maps string names to certain Converters and Generators calls. It works like this:
 
 - First, an item gets a request to be given thermal vision. Internally, this is called "converting" it.
 - The plugin tries to find a config entry for the item.
 - If the config exists, and if it names a handler, and if that handler exists, it is run. Otherwise, the default handler is used instead.
   - The default Handlers, converters, and generators can all be changed by other mods.
   - Handlers, converters, and generators can also be added by other mods.
 - The handler chooses how to convert the item. Does it modify the existing scope? Add an overlay? Things like that.
 - The handler then passes control to the sight converter and/or overlay generator as needed. Again,
   it uses the named ones in the config if possible, otherwise it falls back to the default ones.
 - These sub handlers then invoke more generators, namely the Material, Mesh, and Texture generators, to get the assets
   they need to convert the item. When applicable, the ones named in the config will be used; otherwise, the defaults will be.

When naming a converter, generator, or handler, you can pass in parameters similar to how you'd call a function.
For example, "Auto(OverlayOnly)" would denote using the "Auto" handler, and passing in the "OverlayOnly" keyword.
Similarly, you can pass in floats, ints, file paths, and more.
Here is a quick reference of the converters and generators available by default.

| Type              | Name       | Description                                                                                                       | Signature                                                                                   |
| ----------------- | ---------- | ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| Handler           | Auto       | The default handler, tries to convert the sight, then tries to add an overlay                                     | Auto([AllowAll/SightOnly/OverlayOnly/AllowNone])                                            |
| SightConverter    | Standard   | The default converter, either changes the current thermal to the new shader or adds the new shader as a new layer | Standard([Thermal/NonThermal])                                                              |
| OverlayGenerator  | Standard   | Uses the mesh and texture named by the config, or the default ones if none are named                              | Standard([AttachRear/AttachFront])                                                          |
| OverlayGenerator  | Load       | Loads a sight by PersistentID, which is then converted by the SightConverter. Not recommended, causes glitches    | Load(sightPartPersistentID)                                                                 |
| MaterialGenerator | PDW        | Creates a material copying the PDW's thermal stats                                                                | PDW([Transparent/Opaque])                                                                   |
| MaterialGenerator | PR         | Creates a material copying the PR's thermal stats                                                                 | PR([Transparent/Opaque])                                                                    |
| MaterialGenerator | Load       | Loads a material config from a json file and uses that. You can use the refence below to define one               | Load([SubDirectory1], [SubDirectory2], ..., FileName)                                       |
| MeshGenerator     | Plane      | Creates a small plane in the XY axis                                                                              | Plane([scale: float])                                                                       |
| MeshGenerator     | Diamond    | Creates a cube rotated so its corners lie in the axises                                                           | Diamond([scale: float])                                                                     |
| TextureGenerator  | black      | Uses Unity's Texture2D.black texture                                                                              | black                                                                                       |
| TextureGenerator  | gray       | Uses Unity's Texture2D.gray texture                                                                               | gray                                                                                        |
| TextureGenerator  | linearGray | Uses Unity's Texture2D.linearGray texture                                                                         | linearGray                                                                                  |
| TextureGenerator  | normal     | Uses Unity's Texture2D.normal texture                                                                             | normal                                                                                      |
| TextureGenerator  | red        | Uses Unity's Texture2D.red texture                                                                                | red                                                                                         |
| TextureGenerator  | white      | Uses Unity's Texture2D.white texture                                                                              | white                                                                                       |
| TextureGenerator  | Load       | Loads a texture from file using the provided file path                                                            | Load([SubDirectory1], [SubDirectory2], ..., Filename)                                       |
| TextureGenerator  | Bloom      | Creates a red texture brightest at the center, which fades out to black at the edges                              | Bloom([Square/Circle], [minRed: float 0 to 1], [maxRed: float 0 to 1], [textureSize: uint]) |

For the signatures: If there are slashes ('/'), those are options, and you choose exactly one.
If the variable type is not specifified, use a string (Don't double-quote the string).
The Load variants, which accept subdirectories, will combine them using Path.Combine. It's recommended to use this over using '/' or '\'.
 - The MaterialLoad checks in the BepInEx/GameData folder
 - The TextureLoad check in the BepInEx/Assets folder
You can omit all parameters on many converters, either by not putting any or even by not putting parentheses
Any variable can be omitted by simply not typing in a value, ie immediately putting another comma. This will use the default value.

Some example usages:
```
"TextureGenerator": "Bloom(Circle)"                                     # Equivalent to "Bloom(Circle, 0, 1, 256)"
"TextureGenerator": "Bloom(, .5,, 1024)"                                # Equivalent to "Bloom(Circle, .5, 1, 1024)"
"DistortionSignal": "Load(ThermalOverlay, ThermalDistortionSignal.png)" # Will look for "BepInEx/Assets/ThermalOverlay/ThermalDistortionSignal.png"
"Handler": "Auto(AllowAll)"                                             # Equivalent to "Auto" or "Auto()"
"MaterialGenerator": "PDW"                                              # Equivalent to "PDW(Transparent)"
```

## Transparency Masks

The shader used by this mod for thermals is a modified version of GTFO's thermal shader. The main difference is that it can be
transparent. There are two factors that control transparency; first, the heat texture used can be transparent, which means
that transparency is controlled by how hot the things you're looking at are. This is most useful for viewing enemies; you can
make the hot colors opaque and the cool ones transparent, meaning only enemies are visible on the overlay.

The second fator controlling transparency is the MainTex used by the shader. The red channel is used as an alpha multiplier;
more red results in more opaque colors. This choice was made so that you can use the same MainTex as the other guns in the game
and get a pretty good alpha mask; namely, for guns like the Veruta and Arbalist, the sight is actually just a square, but
the texture is used to make most of it transparent. By using a similar mechanism in the ThermalOverlay shader, automatic
masking can be applied. The standard sight converter will set the material's MainTex to the sight's MainTex if it is not converting
a thermal weapon, unless you override this in the material config.

Finally, you can use the AlphaMult variable to increase or decrease alpha by a multiplier. This is best when the MainTex
inherited from the standard overlay is only slightly red, in which case you can increase it.

## Overlays

Generated overlays are the catchall which allows guns without scopes to act as thermal weapons. The default
for this mod is to generate a small cube, orient it as a diamond (corners on the main axises) and put a "Bloom" 
transparency mask on it (In this case, Bloom is a circle which is pure bright in the center and fades to black at the edges).
The ThermalOverlay shader is then put on this mesh, and the mesh is centered on the sights, resulting in a 
small circle of thermal vision around the sights when aiming. 
It works pretty well for vanilla weapons with iron sights, but isn't that great when your gun has an actual scope, as the
simple attachment method often results in the overlay clipping through the scope.

## Temperature and Color

The biggest and most fun thing to change about the thermal overlay shader is the HeatTex file it uses.
Included in the assets folder is a good handful of heat textures. Most are based on real heat textures,
which you can learn more about at [This website](https://www.flir.com/discover/industrial/picking-a-thermal-color-palette/?srsltid=AfmBOopPE1otNMnGgH9n54JbNYhRDowrZR8v6k88sBsj3eYvjnDtwanx).
The vanilla heat textures are 256x64 pixels, but the size doesn't really matter as long as you stick to 
powers of 2 (1, 2, 4, 8, 16, etc). The x-axis of the image corresponds to the heat being displayed, with 
low heat on the left and high heat on the right. The y-axis does not have any noticeable effect.

Included in the Assets folder are many heat textures. The two used by the vanilla game are FLIR for the PDW
and EVIL for the PR. I've added transparent versions of these for use with overlays, which cut out ambient
temperatures fairly well. There are also a few other color variants (whitehot, blackhot, rainbow, lava) which
are based on other common thermal gradients. These also have their own transparent varients, which work
about the same. Finally, there is a blackspot texture which uses a unique blend to try and keep ambient 
temps transparent, shadows blackhot, and regular enemies whitehot. It kinda works; far away normal enemies
will quickly fade to black, but otherwise it looks like desired.

It's worth noting that one easy way to temper a ThermalOverlay's effectiveness is to use blackhot colors. 
Since it blends with the background, blackhot enemies end up being much harder to see in the dark. This is 
the main school of though behind the blackspot gradient; shadows remain hard to see in the dark (and in general),
while regular enemies become much easier to see.

On top of altering the HeatTex, you can also modify the base heat of various things, notably including
shadows and the environment, but not regular enemies. I'm not sure about things like door controls and other
warm environment items. Changing these can help alter how the thermal plays, ie by making shadows invisible
or, conversely, by making shadows the only enemy highlighted by the scope. Heat falloff and fog falloff can
also play into this system, as you see fit.

