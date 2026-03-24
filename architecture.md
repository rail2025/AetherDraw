# ARCHITECTURE.md

## 1. System Overview
AetherDraw is a real-time collaborative whiteboard plugin for Final Fantasy XIV, running on the Dalamud framework. It utilizes an Immediate Mode GUI (ImGui) for rendering and a custom binary serialization protocol over WebSockets for low-latency state synchronization.

## 2. Core Architectural Patterns
The application follows a tightly-coupled Manager pattern, driven by the ImGui render loop (`MainWindow.Draw()`). 

* **State Management:** The source of truth is the `PageManager`, which maintains lists of polymorphic `BaseDrawable` objects.
* **Rendering:** Objects are rendered via ImGui's `ImDrawListPtr`. Logical coordinates are decoupled from screen coordinates via a `GlobalScale` multiplier and canvas origin offsets.
* **Networking Protocol:** Operates on a **State-Sync / Delta-Sync** hybrid model. Incremental actions (moving, drawing) send object-specific byte payloads. Destructive actions (clear, replace background) send full-page byte arrays.

## 3. Subsystem Breakdown

### A. Data Models & Logic (`DrawingLogic` & `Core`)
* **`BaseDrawable`:** The abstract base class for all canvas objects. Contains common properties (`UniqueId`, `Color`, `Thickness`, `IsFilled`, `IsLocked`). 
* **`DrawMode` Enum:** Functions as the primary type discriminator across the entire application. It dictates UI rendering, serialization mapping, and canvas interaction logic.
* **`CanvasController`:** Handles new object instantiation. It splits logic into two streams:
    * *Persistent Objects:* Buffered in `currentDrawingObjectInternal` during the drag phase, committed to `PageManager` on mouse release.
    * *Ephemeral Objects (Lasers):* Bypasses the main state. Stored in `_ephemeralDrawables`, throttles network updates to ~40ms, and self-destructs via an asynchronous task after 600ms.
* **`ShapeInteractionHandler`:** Manages hit detection and transformations (resize, rotate, move) for existing objects. Maps mouse deltas to object-specific anchor points.

### B. Serialization (`Serialization/DrawableSerializer.cs`)
Uses custom binary serialization (`BinaryReader`/`BinaryWriter`) rather than JSON. 
* **Purpose:** Minimizes WebSocket payload size for real-time syncing.
* **Structure:** Reads/writes a `SERIALIZATION_VERSION` header. Serializes objects sequentially by writing the `DrawMode` byte discriminator, followed by base properties, followed by a switch statement handling shape-specific data (e.g., floats for radii, vectors for points).
* **Constraint:** Requires strict sequential reading. A corrupted byte offset will fail the entire page load.

### C. Network Synchronization & Conflict Resolution
The system relies on client-authoritative updates with server echoing.
* **Echo Cancellation:** To prevent visual rubber-banding, `MainWindow.cs` maintains a `pendingEchoGuids` dictionary. When a client moves an object, it sends an `UpdateObjects` payload and logs the object's `UniqueId`. When the server broadcasts that movement back, the originating client drops the packet if the ID is in the pending list.
* **Payload Types:** `AddObjects`, `UpdateObjects`, `DeleteObjects`, `ReplacePage`, `ClearPage`.

### D. User Interface (`UI` & `Windows`)
* **`MainWindow`:** The primary god-class. Glues network events to local state changes, manages ImGui window constraints, and handles global keyboard nudging.
* **`ToolbarDrawer`:** Constructs the UI matrix. Heavily reliant on hardcoded dictionaries mapping `DrawMode` enums to local/remote texture assets.

## 4. Execution Flow: Drawing a Shape (Networked)
1.  **Input:** User clicks the canvas. `CanvasController` instantiates a new object (e.g. `DrawableRectangle`).
2.  **Preview:** `MainWindow.Draw()` calls `currentDrawingObjectInternal.Draw()`, rendering the shape to the ImGui overlay in real-time.
3.  **Commit:** User releases LMB. `CanvasController` moves the object to `PageManager.GetCurrentPageDrawables()`.
4.  **Serialize:** `DrawableSerializer.SerializePageToBytes()` converts the specific object list to a byte array.
5.  **Transmit:** `NetworkManager.SendStateUpdateAsync` pushes a `PayloadActionType.AddObjects` packet to the WebSocket.
6.  **Echo:** Server broadcasts. Originating client ignores via `pendingEchoGuids`. Remote clients deserialize the byte array and append the object to their local `PageManager`.

## 5. Technical Debt & Scaling Bottlenecks
* **Tight Coupling of DrawModes:** The architecture is not data-driven. Adding a single new object requires explicit, hardcoded logic modifications in at least four different files.
* **God Classes:** `MainWindow.cs` currently handles UI rendering, file I/O dialogs, network event routing, and tool coordination. 
* **Memory Management:** The Undo stack records full lists of `BaseDrawable` clones per action. High-frequency actions with large object counts will rapidly consume heap memory.