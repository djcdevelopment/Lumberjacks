extends Node3D
## World scene root. Spawns/removes entity nodes based on GameState signals.

var entity_nodes: Dictionary = {}  # entity_id → Node3D

var _player_scene: PackedScene = preload("res://scenes/entities/player.tscn")
var _structure_scene: PackedScene = preload("res://scenes/entities/structure.tscn")


func _ready() -> void:
	GameState.entity_added.connect(_on_entity_added)
	GameState.entity_changed.connect(_on_entity_changed)
	GameState.entity_deleted.connect(_on_entity_deleted)

	# Spawn any entities that already exist in GameState (from snapshot)
	for eid in GameState.entities:
		_on_entity_added(eid, GameState.entities[eid])


func _on_entity_added(entity_id: String, entity: Dictionary) -> void:
	if entity_id in entity_nodes:
		return  # Already spawned

	var entity_type: String = entity.get("entity_type", "unknown")
	var node: Node3D = null

	match entity_type:
		"player":
			node = _spawn_player(entity_id, entity)
		"structure":
			node = _spawn_structure(entity_id, entity)
		_:
			return  # Unknown entity type, skip

	if node != null:
		entity_nodes[entity_id] = node
		add_child(node)


func _on_entity_changed(entity_id: String, entity: Dictionary) -> void:
	if entity_id not in entity_nodes:
		# Might be a new entity arriving via entity_update
		_on_entity_added(entity_id, entity)
		return

	var node = entity_nodes[entity_id]
	if node.has_method("update_from_server"):
		node.update_from_server(entity)


func _on_entity_deleted(entity_id: String) -> void:
	if entity_id in entity_nodes:
		var node = entity_nodes[entity_id]
		entity_nodes.erase(entity_id)
		node.queue_free()


func _spawn_player(entity_id: String, entity: Dictionary) -> Node3D:
	var node = _player_scene.instantiate()
	var is_local = (entity.get("player_id", entity_id) == GameState.my_player_id) or (entity_id == GameState.my_player_id)

	node.name = "Player_" + entity_id.left(8)
	node.set_meta("entity_id", entity_id)

	# Set initial position
	var pos = GameState.get_entity_position(entity)
	node.position = pos

	# Configure as local or remote player
	if is_local:
		node.set_meta("is_local", true)
	else:
		node.set_meta("is_local", false)

	# Let the node initialize itself
	if node.has_method("initialize"):
		node.initialize(entity, is_local)

	return node


func _spawn_structure(entity_id: String, entity: Dictionary) -> Node3D:
	var node = _structure_scene.instantiate()
	node.name = "Structure_" + entity_id.left(8)
	node.set_meta("entity_id", entity_id)

	var pos = GameState.get_entity_position(entity)
	node.position = pos

	if node.has_method("initialize"):
		node.initialize(entity)

	return node
