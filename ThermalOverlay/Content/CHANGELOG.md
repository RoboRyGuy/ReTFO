# Changelog

### Version 0.1.0

Beta release of ThermalOverlay. 
 - Only automatic conversions
 - Very simple backend logic
 - Configurations don't work

## Version 1.0.0

Initial full release of ThermalOverlay
 - FactoryManager is fully implemented
 - JSON configs now work
 - How-to articles added for usability
 - Psuedo-random material generation added

## Version 1.1.0

 - Added in some default JSON configs to help make some vanilla weapons look better
 - Added the ThermalOverlay shader's source to Github
 - Fixed an issue where Vector2, 3, and 4's weren't being serialized/deserialized
 - Corrected the placement logic in OverlayGenerator_Standard so that manual adjustments are more consistent

## Version 1.2.0

 - Added a default thermal config for spears
 - Quaternions now serialize correctly
 - Fixed version number on the Plugin class

 ### Version 1.3.0

 - Changed configs to use item names instead of using checksums. This is to improve consistency, but will 
   **likely cause issues** during updates. 
 - SightConverter_Standard now sets default shader properties when converting vanilla thermal scopes.
 - Fixed the Combat Shotgun's config

**If you have issues:**
 - Delete BepInEx/GameData/ThermalOverlay/ThermalOverlayConfig.json to reset the config file, so it uses the new mode
 - If you have custom configs, change it to use *Items* instead of *IDs*, and replace the ID numbers with the item names.
   Item names can be found in the config file; they're the gun's actual name, ie "Omenco exp1".

