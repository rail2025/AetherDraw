# CONTRIBUTING.md

## Contributing Quick Start
This project is open to community forks and contributions. 

### ⚠️ Critical Warning
The architecture of AetherDraw is highly coupled. Most changes require updates across multiple files. Failing to update all touchpoints (`DrawMode`, serialization, UI, controller) will result in silent failures or total network desync bugs. 

When in doubt, search the codebase for an existing `DrawMode` that is similar to what you want to build, and mirror its implementation exactly.

### Getting Started
1. Build the plugin using the standard Dalamud development environment.
2. Launch FFXIV with the plugin loaded via `/xlplugins`.
3. Open the AetherDraw window via the plugin UI or `/aetherdraw`.

### Example Walkthrough: Adding a New Object
To add a new feature (e.g., a new map, icon, or drawing tool), you must touch these four files in this exact order:

1. **`DrawingLogic/DrawMode.cs`**
   * Add your new object's name to the `DrawMode` enum.
2. **`UI/ToolbarDrawer.cs`**
   * Link the enum to its asset path in the `iconPaths` dictionary.
   * Add a display name to the `toolDisplayNames` dictionary.
   * Insert the enum into the appropriate menu group within the `mainToolbarButtons` list.
3. **`Core/CanvasController.cs`**
   * *For stamped images:* Add the enum to `IsImagePlacementMode()`. Then, define its default unscaled size and file path inside `HandleImagePlacementInput()`.
   * *For drawn shapes:* Add its instantiation logic to `CreateNewDrawingObject()`.
4. **`Serialization/DrawableSerializer.cs`**
   * Add the enum to the `switch` statements in **both** `SerializeSingleDrawable` and `DeserializeSingleDrawable`. 
   * *Note: If you skip this step, the object will draw locally but will crash the network deserializer for everyone else in the room.*

### Submitting Changes
* Keep changes minimal and scoped. Large architectural refactors are risky given the current tightly coupled design.
* Ensure binary serialization compatibility is strictly preserved.
* **Always test in multiplayer scenarios.** Single-player testing will not expose serialization or echo-cancellation bugs.
### ⚠️ Refactoring Warning
Large-scale refactors (especially around DrawMode, serialization, or networking) are high-risk and likely to introduce subtle desync bugs.

If you attempt one, expect to rewrite multiple subsystems and thoroughly test multiplayer behavior.