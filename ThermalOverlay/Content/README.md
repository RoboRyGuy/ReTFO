# Thermal Overlay

Adds the ability give any weapon thermal vision. Each weapon can be invidually configured, 
and the plugin exposes APIs so that other developers can add even more customizations and
integrate with their own custom weapons.

## How does it work?

- Adds a new shader, "Unlit/HolographicSight_ThermalOverlay.shader", or simply the "Thermal Overlay" shader.
- This works very similarly to the existing thermal shader, except it can be transparent.
- By adding it on top of existing scopes, you get thermal overlays that can highlight enemies without affecting the background.
- For guns without scopes, the overlay can be applied to a small, auto-generated mesh which sits on the sights.

## Getting Started

**By default, this mod does nothing!** Load up the game with it running once, then use the BepInEx config file to choose which
gear you want converted. Configs have been included to help make vanilla weapons feel unique, but this mod will automatically 
detect new weapons and apply a default configuration to them, too, if you want to convert them.

If you wish to customize weapons further, check the guides included in the plugin folder, at BepInEx/plugins/ThermalOverlay
