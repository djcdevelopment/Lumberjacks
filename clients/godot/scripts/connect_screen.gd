extends Control
## Connect screen UI. URL input + connect button.

signal connect_requested(url: String)

@onready var url_input: LineEdit = $VBoxContainer/URLInput
@onready var connect_button: Button = $VBoxContainer/ConnectButton


func _ready() -> void:
	connect_button.pressed.connect(_on_connect_pressed)
	url_input.text_submitted.connect(func(_text): _on_connect_pressed())


func _on_connect_pressed() -> void:
	var url = url_input.text.strip_edges()
	if url == "":
		url = "ws://localhost:4000"
		url_input.text = url
	connect_requested.emit(url)
