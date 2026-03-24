# Event Catalog v0

These are the first canonical events for the platform.

## Player Events

- `player_connected`
- `player_disconnected`
- `player_entered_region`
- `player_followed_road_segment`
- `player_discovered_landmark`
- `player_joined_guild`
- `player_left_guild`
- `player_rank_changed`

## Build And World Events

- `structure_placement_requested`
- `structure_placed`
- `structure_removed`
- `container_opened`
- `item_picked_up`
- `item_stored`
- `road_segment_maintained`
- `settlement_signature_updated`

## Progression And Community Events

- `challenge_started`
- `challenge_completed`
- `guild_objective_progressed`
- `guild_objective_completed`
- `reward_granted`
- `discord_identity_linked`
- `discord_role_sync_requested`
- `discord_role_synced`

## Operational Events

- `region_activated`
- `region_deactivated`
- `interest_subscription_changed`
- `edge_node_registered`
- `edge_node_unhealthy`
- `edge_node_detached`

## Event Requirements

Every canonical event should carry:
- `event_id`
- `event_type`
- `occurred_at`
- `world_id`
- `region_id` when applicable
- `actor_id` when applicable
- `guild_id` when applicable
- `source_service`
- `schema_version`
- event-specific payload

## First Event Flows To Prove

1. Player joins a guild challenge:
- `player_connected`
- `player_entered_region`
- `challenge_started`

2. Player contributes by building:
- `structure_placed`
- `guild_objective_progressed`

3. Reward and sync:
- `guild_objective_completed`
- `reward_granted`
- `discord_role_sync_requested`
- `discord_role_synced`
