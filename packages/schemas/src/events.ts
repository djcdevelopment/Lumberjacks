import { z } from 'zod'

// Base envelope every canonical event must carry (from docs/events.md)
export const GameEventSchema = z.object({
  event_id: z.string().uuid(),
  event_type: z.string(),
  occurred_at: z.string().datetime(),
  world_id: z.string(),
  region_id: z.string().optional(),
  actor_id: z.string().optional(),
  guild_id: z.string().optional(),
  source_service: z.string(),
  schema_version: z.number().int().positive(),
  payload: z.record(z.unknown()),
})

export type GameEvent = z.infer<typeof GameEventSchema>

// Canonical event types from docs/events.md
export const EventType = {
  // Player events
  PLAYER_CONNECTED: 'player_connected',
  PLAYER_DISCONNECTED: 'player_disconnected',
  PLAYER_ENTERED_REGION: 'player_entered_region',
  PLAYER_FOLLOWED_ROAD_SEGMENT: 'player_followed_road_segment',
  PLAYER_DISCOVERED_LANDMARK: 'player_discovered_landmark',
  PLAYER_JOINED_GUILD: 'player_joined_guild',
  PLAYER_LEFT_GUILD: 'player_left_guild',
  PLAYER_RANK_CHANGED: 'player_rank_changed',

  // Build and world events
  STRUCTURE_PLACEMENT_REQUESTED: 'structure_placement_requested',
  STRUCTURE_PLACED: 'structure_placed',
  STRUCTURE_REMOVED: 'structure_removed',
  CONTAINER_OPENED: 'container_opened',
  ITEM_PICKED_UP: 'item_picked_up',
  ITEM_STORED: 'item_stored',
  ROAD_SEGMENT_MAINTAINED: 'road_segment_maintained',
  SETTLEMENT_SIGNATURE_UPDATED: 'settlement_signature_updated',

  // Progression and community events
  CHALLENGE_STARTED: 'challenge_started',
  CHALLENGE_COMPLETED: 'challenge_completed',
  GUILD_OBJECTIVE_PROGRESSED: 'guild_objective_progressed',
  GUILD_OBJECTIVE_COMPLETED: 'guild_objective_completed',
  REWARD_GRANTED: 'reward_granted',
  DISCORD_IDENTITY_LINKED: 'discord_identity_linked',
  DISCORD_ROLE_SYNC_REQUESTED: 'discord_role_sync_requested',
  DISCORD_ROLE_SYNCED: 'discord_role_synced',

  // Operational events
  REGION_ACTIVATED: 'region_activated',
  REGION_DEACTIVATED: 'region_deactivated',
  INTEREST_SUBSCRIPTION_CHANGED: 'interest_subscription_changed',
  EDGE_NODE_REGISTERED: 'edge_node_registered',
  EDGE_NODE_UNHEALTHY: 'edge_node_unhealthy',
  EDGE_NODE_DETACHED: 'edge_node_detached',
} as const

export type EventType = (typeof EventType)[keyof typeof EventType]
