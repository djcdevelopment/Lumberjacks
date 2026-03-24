import { describe, it, expect } from 'vitest'
import {
  JoinRegionSchema,
  PlayerMoveSchema,
  PlaceStructureSchema,
  WorldSnapshotSchema,
  EntityUpdateSchema,
  MessageType,
} from './messages.js'

describe('MessageType', () => {
  it('has client and server message types', () => {
    expect(MessageType.JOIN_REGION).toBe('join_region')
    expect(MessageType.PLAYER_MOVE).toBe('player_move')
    expect(MessageType.WORLD_SNAPSHOT).toBe('world_snapshot')
    expect(MessageType.ERROR).toBe('error')
  })
})

describe('Client message schemas', () => {
  it('validates JoinRegion', () => {
    const msg = { region_id: 'region-spawn', token: 'abc123' }
    expect(JoinRegionSchema.safeParse(msg).success).toBe(true)
  })

  it('validates PlayerMove', () => {
    const msg = {
      position: { x: 10, y: 0, z: 20 },
      velocity: { x: 1, y: 0, z: 0 },
      timestamp: Date.now(),
    }
    expect(PlayerMoveSchema.safeParse(msg).success).toBe(true)
  })

  it('validates PlaceStructure', () => {
    const msg = {
      structure_type: 'wooden_wall',
      position: { x: 50, y: 0, z: 50 },
      rotation: 90,
    }
    expect(PlaceStructureSchema.safeParse(msg).success).toBe(true)
  })
})

describe('Server message schemas', () => {
  it('validates WorldSnapshot', () => {
    const msg = {
      region_id: 'region-spawn',
      entities: [{ id: 'e1', type: 'player', x: 0 }],
      tick: 100,
    }
    expect(WorldSnapshotSchema.safeParse(msg).success).toBe(true)
  })

  it('validates EntityUpdate', () => {
    const msg = {
      entity_id: 'player-001',
      entity_type: 'player',
      data: { position: { x: 10, y: 0, z: 20 } },
      tick: 101,
    }
    expect(EntityUpdateSchema.safeParse(msg).success).toBe(true)
  })
})
