# Godot Client — Vertical Slice Plan

**Goal:** Build the thinnest possible Godot 4.x client that connects to the existing backend and lets a player walk around, see other players, and place structures. This is a rendering shell, not a game engine — the server owns all truth (ADR 0001).

**Exit Criteria:**
- Player connects via WebSocket, receives `session_started`, joins a region
- World renders from `world_snapshot` (terrain, structures, other players)
- WASD movement sends `player_input`, server responds with `entity_update`, player moves
- Other players appear and move in real time (interpolated)
- Click-to-place a structure, see it appear after server confirms
- Disconnect and reconnect with resume token — world state restored

**Non-goals for this slice:**
- Inventory UI, item pickup, container storage
- Combat, health, damage
- Sound, particles, polish
- Binary protocol or UDP transport (JSON over WebSocket is fine)
- Client-side prediction / reconciliation (server authority is enough at 20Hz)
- Main menu, settings, account system

---

## Architecture

```
┌─────────────────────────────────────────────┐
│  Godot Client                                │
│                                              │
│  ┌──────────┐  ┌───────────┐  ┌───────────┐ │
│  │ Network  │→│  GameState │→│  Renderer  │ │
│  │ Manager  │  │  (mirror) │  │  (scenes)  │ │
│  └──────────┘  └───────────┘  └───────────┘ │
│       ↑                            ↑         │
│  ┌──────────┐              ┌───────────┐     │
│  │  Input   │              │    HUD     │     │
│  │ Capture  │              │            │     │
│  └──────────┘              └───────────┘     │
└─────────────────────────────────────────────┘
         │ WebSocket (JSON)
         ▼
   ┌───────────┐
   │  Gateway   │
   │  :4000     │
   └───────────┘
```

**Key Principle:** The client never decides where a player is. It sends input, the server computes position, the client renders what the server says. The local player is just another entity that happens to have a camera attached.

---

## Project Structure

```
clients/godot/
├── project.godot
├── scenes/
│   ├── main.tscn              # Entry point — connects, then loads world
│   ├── world.tscn             # 3D world scene (terrain, lighting, camera)
│   ├── hud.tscn               # Overlay UI (connection status, coordinates, build menu)
│   ├── entities/
│   │   ├── player.tscn        # Player model + nametag
│   │   ├── structure.tscn     # Generic structure (swaps mesh by type)
│   │   └── world_item.tscn    # Pickup item on ground
│   └── ui/
│       ├── connect_screen.tscn  # URL input, connect/resume buttons
│       └── build_menu.tscn      # Structure type selection
├── scripts/
│   ├── autoload/
│   │   ├── network_manager.gd   # WebSocket connection, send/receive, reconnect
│   │   └── game_state.gd        # Mirror of server world state (entities dict)
│   ├── world.gd                 # Spawns/removes entity scenes based on game_state
│   ├── player_controller.gd     # Input capture → player_input messages
│   ├── remote_entity.gd         # Interpolates position/heading from server updates
│   ├── structure_placer.gd      # Raycast + ghost preview + place_structure message
│   └── hud.gd                   # UI updates (player count, coords, connection state)
├── assets/
│   ├── models/                  # Placeholder meshes (.glb)
│   ├── materials/               # Basic materials
│   └── textures/                # Placeholder textures
└── export_presets.cfg           # Windows .exe export
```

---

## Phase 1: Connection & Session (Day 1)

**What:** WebSocket connects to Gateway, displays session info.

### 1.1 Create Godot Project

- New Godot 4.x project at `clients/godot/`
- Set up 3D rendering (Forward+), window size 1280x720
- Create `main.tscn` as the entry scene

### 1.2 NetworkManager (Autoload)

`scripts/autoload/network_manager.gd` — singleton, always running.

**Responsibilities:**
- Connect to `ws://localhost:4000` (configurable)
- Parse incoming JSON envelopes: `{ version, type, seq, timestamp, payload }`
- Emit signals: `session_started(payload)`, `world_snapshot(payload)`, `entity_updated(payload)`, `entity_removed(payload)`, `event_emitted(payload)`, `error_received(payload)`
- Send messages: `send_message(type: String, payload: Dictionary)`
- Handle reconnection with resume token

**Message envelope format (outgoing):**
```json
{
  "type": "join_region",
  "payload": { "region_id": "region-spawn" },
  "seq": 1
}
```

**Key signals:**
```gdscript
signal connected
signal disconnected
signal session_started(payload: Dictionary)
signal world_snapshot(payload: Dictionary)
signal entity_updated(payload: Dictionary)
signal entity_removed(payload: Dictionary)
signal event_emitted(payload: Dictionary)
signal error_received(payload: Dictionary)
```

### 1.3 Connect Screen

`scenes/ui/connect_screen.tscn` — shown on launch.

- Text input for server URL (default `ws://localhost:4000`)
- "Connect" button → calls `NetworkManager.connect_to_server(url)`
- On `session_started` → display player_id, then auto-join `region-spawn`
- On `world_snapshot` → switch to world scene

### 1.4 Acceptance Test

- Run local backend: `npm run dev`
- Launch Godot client
- Click Connect → see `session_started` in output log
- Auto-join region → see `world_snapshot` logged with entity list

---

## Phase 2: World Rendering (Day 2)

**What:** Render a ground plane, spawn entities from `world_snapshot`, update from `entity_update`.

### 2.1 GameState (Autoload)

`scripts/autoload/game_state.gd` — singleton, mirrors server state.

```gdscript
var my_player_id: String
var entities: Dictionary = {}  # entity_id → { entity_type, position, heading, data... }
var region_id: String

signal entity_added(entity_id: String, entity: Dictionary)
signal entity_changed(entity_id: String, entity: Dictionary)
signal entity_deleted(entity_id: String)
```

- On `world_snapshot`: clear entities, populate from snapshot, emit `entity_added` for each
- On `entity_update`: upsert entity, emit `entity_changed` or `entity_added`
- On `entity_removed`: delete entity, emit `entity_deleted`

### 2.2 World Scene

`scenes/world.tscn`:
- Large flat plane (ground) — `MeshInstance3D` with a `PlaneMesh` (1000x1000 units)
- `DirectionalLight3D` (sun)
- `WorldEnvironment` with sky
- Camera attached to local player (see Phase 3)

`scripts/world.gd`:
- Listens to `GameState.entity_added` → instantiate the right scene (`player.tscn`, `structure.tscn`)
- Listens to `GameState.entity_deleted` → `queue_free()` the node
- Maintains `entity_nodes: Dictionary` mapping entity_id → Node3D

### 2.3 Entity Scenes

**`scenes/entities/player.tscn`:**
- `CharacterBody3D` or plain `Node3D` with a capsule mesh (placeholder)
- Color: green for local player, blue for others
- `Label3D` above head showing player name
- Script: `remote_entity.gd`

**`scenes/entities/structure.tscn`:**
- `StaticBody3D` with a box mesh (placeholder)
- Color varies by type: orange for campfire, brown for wooden_wall
- Script: reads `structure_type` from entity data, swaps mesh/color

### 2.4 Remote Entity Interpolation

`scripts/remote_entity.gd` — attached to all entity scene instances.

```gdscript
var target_position: Vector3
var target_heading: float
var interpolation_speed: float = 10.0

func update_from_server(data: Dictionary):
    target_position = dict_to_vec3(data.position)
    target_heading = data.get("heading", 0.0)

func _process(delta):
    position = position.lerp(target_position, delta * interpolation_speed)
    rotation.y = lerp_angle(rotation.y, deg_to_rad(target_heading), delta * interpolation_speed)
```

Server sends updates at 20Hz. Client renders at 60Hz. Interpolation smooths the gap.

### 2.5 Acceptance Test

- Start backend, run `node scripts/test-multiplayer.js 3` in parallel
- Launch Godot client, connect → should see 3 moving capsules on the ground plane
- Structures placed by test script should appear as boxes

---

## Phase 3: Player Movement (Day 3)

**What:** WASD input sends `player_input`, server moves the player, camera follows.

### 3.1 Player Controller

`scripts/player_controller.gd` — only attached to the local player entity.

**Input mapping (project settings):**
- `move_forward` → W
- `move_back` → S
- `move_left` → A
- `move_right` → D

**Each `_process(delta)`:**

```gdscript
var input_dir = Vector2.ZERO
if Input.is_action_pressed("move_forward"): input_dir.y -= 1
if Input.is_action_pressed("move_back"):    input_dir.y += 1
if Input.is_action_pressed("move_left"):    input_dir.x -= 1
if Input.is_action_pressed("move_right"):   input_dir.x += 1

if input_dir.length() > 0:
    input_dir = input_dir.normalized()
    # Convert to compass direction byte (0-255)
    var angle = atan2(input_dir.x, -input_dir.y)  # 0 = north
    var direction_byte = int((angle / TAU) * 256) % 256
    if direction_byte < 0: direction_byte += 256
    var speed_percent = 100
else:
    var direction_byte = 255
    var speed_percent = 0

input_seq += 1
NetworkManager.send_message("player_input", {
    "direction": direction_byte,
    "speed_percent": speed_percent,
    "action_flags": 0,
    "input_seq": input_seq
})
```

**Input rate:** Send every frame but throttle to max 20/sec (match server tick rate) to avoid flooding.

### 3.2 Camera

Third-person camera following the local player:

```gdscript
# Attached to local player node
@onready var camera_pivot = $CameraPivot  # Node3D
@onready var camera = $CameraPivot/Camera3D

# Camera3D offset: (0, 8, 12) looking down at ~30°
```

- Camera follows the interpolated position (smooth)
- Mouse scroll to zoom in/out (optional, stretch goal)

### 3.3 Local Player Identification

When `world.gd` spawns a player entity:
- Check if `entity.player_id == GameState.my_player_id`
- If yes: attach `player_controller.gd`, attach camera, color green
- If no: color blue, no input handling

### 3.4 Acceptance Test

- Launch client, connect, hold W → player capsule moves forward
- Open a second client instance (or run test script) → see both players moving
- Stop pressing keys → player decelerates (server applies friction)
- Walk toward region boundary → position is clamped, no rubber-banding

---

## Phase 4: Structure Placement (Day 4)

**What:** Player selects a structure type, clicks to place, server confirms.

### 4.1 Build Menu

`scenes/ui/build_menu.tscn`:
- Horizontal toolbar at bottom of screen
- Buttons: Campfire, Wooden Wall
- Pressing `B` toggles build mode
- In build mode, a ghost preview follows the mouse cursor on the ground

### 4.2 Structure Placer

`scripts/structure_placer.gd`:

```gdscript
var build_mode: bool = false
var selected_type: String = "campfire"
var ghost: Node3D = null  # semi-transparent preview

func _input(event):
    if event is InputEventKey and event.keycode == KEY_B and event.pressed:
        build_mode = !build_mode
        # show/hide ghost

func _process(delta):
    if not build_mode: return
    # Raycast from camera through mouse position to ground plane
    var ray_result = raycast_to_ground()
    if ray_result:
        ghost.position = ray_result.position

func _unhandled_input(event):
    if build_mode and event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
        var pos = ghost.position
        NetworkManager.send_message("place_structure", {
            "structure_type": selected_type,
            "position": {"x": pos.x, "y": pos.y, "z": pos.z}
        })
        build_mode = false
```

- On success: server broadcasts `entity_update` with structure → `world.gd` spawns it
- On failure: server sends `error` → HUD shows message
- Ghost preview disappears after placement

### 4.3 Acceptance Test

- Connect, press B, select campfire, click on ground → orange box appears
- Open admin dashboard → structure visible in Structures tab
- Second client connects → sees the structure in `world_snapshot`

---

## Phase 5: Reconnection & Polish (Day 5)

**What:** Resume after disconnect, HUD info, basic quality of life.

### 5.1 Resume Flow

In `network_manager.gd`:

```gdscript
var resume_token: String = ""

func _on_session_started(payload):
    resume_token = payload.get("resume_token", "")

func reconnect():
    var url = last_url + "?resume=" + resume_token
    connect_to_server(url)
```

- On WebSocket close: show reconnect overlay with countdown
- Auto-attempt reconnect after 2 seconds
- If resume succeeds (`resumed: true`): seamless — `world_snapshot` re-syncs everything
- If resume fails (token expired): show connect screen

### 5.2 HUD

`scenes/hud.tscn` — always-visible overlay:

- **Top-left:** Connection status (green dot = connected, red = disconnected)
- **Top-left:** Player count in region
- **Bottom-left:** Position coordinates (x, y, z from server)
- **Top-right:** Server tick number, ping estimate
- **Center:** Error messages (fade after 3 seconds)

### 5.3 Ping Estimation

```gdscript
# Send a player_input, record local time
# When entity_update arrives with matching input_seq, compute round-trip
var pending_pings: Dictionary = {}  # input_seq → timestamp_ms

func estimate_ping(input_seq: int):
    pending_pings[input_seq] = Time.get_ticks_msec()

func on_entity_update(data):
    var seq = data.get("last_input_seq", 0)
    if seq in pending_pings:
        var rtt = Time.get_ticks_msec() - pending_pings[seq]
        current_ping = rtt
        pending_pings.erase(seq)
```

### 5.4 Acceptance Test

- Connect, walk around, unplug network cable → reconnect overlay appears
- Plug back in within 2 min → auto-reconnects, player is where they left off
- HUD shows accurate position, player count, ping

---

## Phase 6: Azure Testing (Day 6)

**What:** Point the client at the live Azure deployment and validate everything works over the internet.

### 6.1 Configuration

- Connect screen URL field accepts `wss://gateway.wittyplant-6c0ca715.eastus2.azurecontainerapps.io`
- WebSocket connection uses TLS (Godot supports `wss://` natively)

### 6.2 Validation Checklist

| Check | Expected |
|-------|----------|
| Connect to Azure Gateway | `session_started` received |
| Join region | `world_snapshot` with entities |
| Move with WASD | Smooth movement (latency < 100ms typical) |
| Two clients same region | See each other's movement in real time |
| Place structure | Appears for both clients |
| Disconnect/reconnect | Resume works, world intact |
| Admin dashboard | Shows both players, structures, tick running |

### 6.3 Export .exe

- Configure export preset for Windows (x86_64)
- Export to `builds/game-client.exe`
- Share with friends: they only need the .exe and the server URL

---

## Tech Notes

### Server Protocol Reference (JSON only for this slice)

**Client sends:**
| Type | Payload | When |
|------|---------|------|
| `join_region` | `{ region_id, guild_id? }` | After session_started |
| `leave_region` | `{}` | Before joining a different region |
| `player_input` | `{ direction: 0-255, speed_percent: 0-100, action_flags: 0, input_seq }` | Every frame (throttled to 20/sec) |
| `place_structure` | `{ structure_type, position: {x,y,z}, rotation? }` | On click in build mode |

**Server sends:**
| Type | Payload | When |
|------|---------|------|
| `session_started` | `{ session_id, player_id, world_id, resume_token, resumed, udp_token, udp_port }` | On connect |
| `world_snapshot` | `{ region_id, entities: [...], tick }` | After join_region or resume |
| `entity_update` | `{ entity_id, entity_type, data: {...}, tick, state_hash }` | 20Hz per entity in AoI |
| `entity_removed` | `{ entity_id, tick }` | Player left or structure destroyed |
| `event_emitted` | `{ event_type, data }` | Inventory/progression feedback |
| `error` | `{ code, message }` | Validation failures |

### Direction Byte Mapping
```
  0 = North (+Z)
 64 = East  (+X)
128 = South (-Z)
192 = West  (-X)
```

In Godot, `Vector3.FORWARD` is `-Z`, so north (server) = `Vector3(0, 0, 1)` in world space. Adjust camera and input accordingly.

### Structure Types (current)
| Type | Description | Placeholder Mesh |
|------|-------------|-----------------|
| `campfire` | Gathering point | Orange cylinder |
| `wooden_wall` | Basic barrier | Brown box |
| `test_beacon` | Debug marker | Yellow sphere |

### Region Bounds
Default `region-spawn`: `(-500, -10, -500)` to `(500, 200, 500)` — 1km² playable area.

---

## Future Slices (not this plan)

These are parked. Don't build them until this slice is proven.

- **Inventory & items:** Pickup world items, inventory UI, store in containers
- **Binary protocol:** Switch from JSON to compact binary for lower bandwidth
- **UDP transport:** Bind UDP socket for datagram-lane messages (entity_update, player_input)
- **Client-side prediction:** Predict local movement immediately, reconcile with server via input_seq
- **Terrain:** Replace flat plane with heightmap terrain, biomes
- **Art pass:** Replace placeholder meshes with actual 3D models
- **Audio:** Footsteps, placement sounds, ambient
- **Combat:** Health, damage, respawn
- **Guild UI:** Guild membership, challenge progress display
