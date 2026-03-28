extends Node3D
## Interpolates position and heading from server updates.
## Attached to all entity scene roots (players, structures).

var target_position: Vector3 = Vector3.ZERO
var target_heading_rad: float = 0.0
var interpolation_speed: float = 12.0

## Set to false for structures (no interpolation needed)
var should_interpolate: bool = true


func update_from_server(entity: Dictionary) -> void:
	target_position = GameState.get_entity_position(entity)
	target_heading_rad = GameState.get_entity_heading_rad(entity)

	if not should_interpolate:
		position = target_position
		rotation.y = target_heading_rad


func _process(delta: float) -> void:
	if not should_interpolate:
		return

	# Smooth interpolation between current and target
	if position.distance_to(target_position) > 0.01:
		position = position.lerp(target_position, delta * interpolation_speed)
	else:
		position = target_position

	# Interpolate heading
	rotation.y = lerp_angle(rotation.y, target_heading_rad, delta * interpolation_speed)
