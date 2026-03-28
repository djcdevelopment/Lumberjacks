extends Node
## Mirrors the server's authoritative world state. Autoload singleton.
## Updated by NetworkManager signals; consumed by world.gd for rendering.

signal entity_added(entity_id: String, entity: Dictionary)
signal entity_changed(entity_id: String, entity: Dictionary)
signal entity_deleted(entity_id: String)

## Our player's server-assigned ID
var my_player_id: String = ""
## Current region
var region_id: String = ""
## Current server tick
var current_tick: int = 0
## All entities keyed by entity_id
var entities: Dictionary = {}


func _ready() -> void:
	NetworkManager.session_started.connect(_on_session_started)
	NetworkManager.world_snapshot.connect(_on_world_snapshot)
	NetworkManager.entity_updated.connect(_on_entity_updated)
	NetworkManager.entity_removed.connect(_on_entity_removed)


func _on_session_started(payload: Dictionary) -> void:
	my_player_id = payload.get("player_id", "")
	print("[GameState] My player ID: ", my_player_id)


func _on_world_snapshot(payload: Dictionary) -> void:
	region_id = payload.get("region_id", "")
	current_tick = payload.get("tick", 0)

	# Clear existing entities and rebuild from snapshot
	var old_ids = entities.keys().duplicate()
	for eid in old_ids:
		entities.erase(eid)
		entity_deleted.emit(eid)

	var snapshot_entities: Array = payload.get("entities", [])
	for ent in snapshot_entities:
		var eid: String = ent.get("entity_id", "")
		if eid == "":
			continue
		entities[eid] = ent
		entity_added.emit(eid, ent)

	print("[GameState] World loaded — region: %s, %d entities" % [region_id, entities.size()])


func _on_entity_updated(payload: Dictionary) -> void:
	var eid: String = payload.get("entity_id", "")
	if eid == "":
		return

	current_tick = maxi(current_tick, payload.get("tick", 0))

	if eid in entities:
		# Merge update data into existing entity
		var existing: Dictionary = entities[eid]
		existing["entity_type"] = payload.get("entity_type", existing.get("entity_type", ""))
		var data: Dictionary = payload.get("data", {})
		for key in data:
			existing[key] = data[key]
		# Also store data sub-dict for convenience
		existing["_data"] = data
		entities[eid] = existing
		entity_changed.emit(eid, existing)
	else:
		# New entity we haven't seen before
		var ent: Dictionary = {
			"entity_id": eid,
			"entity_type": payload.get("entity_type", "unknown"),
		}
		var data: Dictionary = payload.get("data", {})
		for key in data:
			ent[key] = data[key]
		ent["_data"] = data
		entities[eid] = ent
		entity_added.emit(eid, ent)


func _on_entity_removed(payload: Dictionary) -> void:
	var eid: String = payload.get("entity_id", "")
	if eid == "":
		return
	if eid in entities:
		entities.erase(eid)
		entity_deleted.emit(eid)


## Helper: Get position Vector3 from an entity dict
static func get_entity_position(ent: Dictionary) -> Vector3:
	var pos = ent.get("position", null)
	if pos == null:
		# Try nested in _data
		var data = ent.get("_data", {})
		pos = data.get("position", null)
	if pos is Dictionary:
		return Vector3(
			float(pos.get("x", 0)),
			float(pos.get("y", 0)),
			float(pos.get("z", 0))
		)
	return Vector3.ZERO


## Helper: Get heading from an entity dict (degrees → radians)
static func get_entity_heading_rad(ent: Dictionary) -> float:
	var heading = ent.get("heading", null)
	if heading == null:
		var data = ent.get("_data", {})
		heading = data.get("heading", 0.0)
	return deg_to_rad(float(heading))
