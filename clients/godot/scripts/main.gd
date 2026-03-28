extends Node
## Entry point. Shows connect screen, then switches to world scene on join.

@onready var connect_screen: Control = $ConnectScreen
@onready var status_label: Label = $ConnectScreen/VBoxContainer/StatusLabel

var _world_scene: PackedScene = preload("res://scenes/world.tscn")
var _world_instance: Node = null


func _ready() -> void:
	NetworkManager.connected.connect(_on_connected)
	NetworkManager.disconnected.connect(_on_disconnected)
	NetworkManager.session_started.connect(_on_session_started)
	NetworkManager.world_snapshot.connect(_on_world_snapshot)
	NetworkManager.error_received.connect(_on_error)

	connect_screen.visible = true


func _on_connect_pressed(url: String) -> void:
	status_label.text = "Connecting..."
	NetworkManager.connect_to_server(url)


func _on_connected() -> void:
	status_label.text = "Connected, waiting for session..."


func _on_disconnected() -> void:
	status_label.text = "Disconnected. Reconnecting..."
	# If we're in the world, show an overlay instead of going back to connect screen
	# The NetworkManager handles auto-reconnect


func _on_session_started(payload: Dictionary) -> void:
	var player_id: String = payload.get("player_id", "?")
	var resumed: bool = payload.get("resumed", false)
	if resumed:
		status_label.text = "Resumed session as %s" % player_id.left(8)
	else:
		status_label.text = "Session started: %s\nJoining region..." % player_id.left(8)
	# Auto-join the default region
	NetworkManager.send_message("join_region", {"region_id": "region-spawn"})


func _on_world_snapshot(_payload: Dictionary) -> void:
	# Switch from connect screen to world
	if _world_instance == null:
		_world_instance = _world_scene.instantiate()
		add_child(_world_instance)
	connect_screen.visible = false


func _on_error(payload: Dictionary) -> void:
	status_label.text = "Error: %s" % payload.get("message", "unknown")
