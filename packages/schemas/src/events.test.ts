import { describe, it, expect } from 'vitest'
import { GameEventSchema, EventType } from './events.js'

describe('GameEventSchema', () => {
  it('validates a well-formed event', () => {
    const event = {
      event_id: '550e8400-e29b-41d4-a716-446655440000',
      event_type: EventType.PLAYER_CONNECTED,
      occurred_at: '2026-03-24T12:00:00.000Z',
      world_id: 'world-default',
      region_id: 'region-spawn',
      actor_id: 'player-001',
      source_service: 'gateway',
      schema_version: 1,
      payload: { ip: '127.0.0.1' },
    }

    const result = GameEventSchema.safeParse(event)
    expect(result.success).toBe(true)
  })

  it('rejects an event with missing required fields', () => {
    const event = {
      event_id: '550e8400-e29b-41d4-a716-446655440000',
      // missing event_type, occurred_at, etc.
    }

    const result = GameEventSchema.safeParse(event)
    expect(result.success).toBe(false)
  })

  it('rejects an invalid UUID for event_id', () => {
    const event = {
      event_id: 'not-a-uuid',
      event_type: EventType.PLAYER_CONNECTED,
      occurred_at: '2026-03-24T12:00:00.000Z',
      world_id: 'world-default',
      source_service: 'gateway',
      schema_version: 1,
      payload: {},
    }

    const result = GameEventSchema.safeParse(event)
    expect(result.success).toBe(false)
  })

  it('allows optional fields to be omitted', () => {
    const event = {
      event_id: '550e8400-e29b-41d4-a716-446655440000',
      event_type: EventType.REGION_ACTIVATED,
      occurred_at: '2026-03-24T12:00:00.000Z',
      world_id: 'world-default',
      source_service: 'simulation',
      schema_version: 1,
      payload: {},
    }

    const result = GameEventSchema.safeParse(event)
    expect(result.success).toBe(true)
  })
})

describe('EventType', () => {
  it('contains all canonical event types', () => {
    expect(EventType.PLAYER_CONNECTED).toBe('player_connected')
    expect(EventType.STRUCTURE_PLACED).toBe('structure_placed')
    expect(EventType.CHALLENGE_COMPLETED).toBe('challenge_completed')
    expect(EventType.REGION_ACTIVATED).toBe('region_activated')
    expect(EventType.DISCORD_ROLE_SYNCED).toBe('discord_role_synced')
  })

  it('has 30 event types', () => {
    const count = Object.keys(EventType).length
    expect(count).toBe(30)
  })
})
