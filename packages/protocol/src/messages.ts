import { z } from 'zod'
import { Vec3Schema } from '@game/schemas'

// --- Client → Server messages ---

export const MessageType = {
  // Client messages
  JOIN_REGION: 'join_region',
  LEAVE_REGION: 'leave_region',
  PLAYER_MOVE: 'player_move',
  PLACE_STRUCTURE: 'place_structure',
  INTERACT: 'interact',

  // Server messages
  SESSION_STARTED: 'session_started',
  WORLD_SNAPSHOT: 'world_snapshot',
  ENTITY_UPDATE: 'entity_update',
  ENTITY_REMOVED: 'entity_removed',
  EVENT_EMITTED: 'event_emitted',
  ERROR: 'error',
} as const

export type MessageType = (typeof MessageType)[keyof typeof MessageType]

// Client messages
export const JoinRegionSchema = z.object({
  region_id: z.string(),
  token: z.string(),
})

export const LeaveRegionSchema = z.object({
  region_id: z.string(),
})

export const PlayerMoveSchema = z.object({
  position: Vec3Schema,
  velocity: Vec3Schema,
  timestamp: z.number(),
})

export const PlaceStructureSchema = z.object({
  structure_type: z.string(),
  position: Vec3Schema,
  rotation: z.number(),
})

// Server messages
export const SessionStartedSchema = z.object({
  session_id: z.string().uuid(),
  player_id: z.string().uuid(),
  world_id: z.string(),
})

export const WorldSnapshotSchema = z.object({
  region_id: z.string(),
  entities: z.array(z.record(z.unknown())),
  tick: z.number().int(),
})

export const EntityUpdateSchema = z.object({
  entity_id: z.string(),
  entity_type: z.string(),
  data: z.record(z.unknown()),
  tick: z.number().int(),
})

export const EntityRemovedSchema = z.object({
  entity_id: z.string(),
  tick: z.number().int(),
})

export const ErrorSchema = z.object({
  code: z.string(),
  message: z.string(),
})

export type JoinRegion = z.infer<typeof JoinRegionSchema>
export type LeaveRegion = z.infer<typeof LeaveRegionSchema>
export type PlayerMove = z.infer<typeof PlayerMoveSchema>
export type PlaceStructure = z.infer<typeof PlaceStructureSchema>
export type SessionStarted = z.infer<typeof SessionStartedSchema>
export type WorldSnapshot = z.infer<typeof WorldSnapshotSchema>
export type EntityUpdate = z.infer<typeof EntityUpdateSchema>
export type EntityRemoved = z.infer<typeof EntityRemovedSchema>
