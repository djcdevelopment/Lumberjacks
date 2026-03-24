import { describe, it, expect } from 'vitest'
import { PlayerSchema, RegionSchema, GuildSchema, StructureSchema, Vec3Schema } from './entities.js'

describe('Vec3Schema', () => {
  it('validates a valid vector', () => {
    expect(Vec3Schema.safeParse({ x: 10.5, y: -3, z: 200 }).success).toBe(true)
  })

  it('rejects missing components', () => {
    expect(Vec3Schema.safeParse({ x: 1, y: 2 }).success).toBe(false)
  })
})

describe('PlayerSchema', () => {
  it('validates a full player', () => {
    const player = {
      id: '550e8400-e29b-41d4-a716-446655440000',
      name: 'TestPlayer',
      guild_id: '660e8400-e29b-41d4-a716-446655440000',
      rank: 3,
      position: { x: 0, y: 0, z: 0 },
      region_id: 'region-spawn',
      connected: true,
      connected_at: '2026-03-24T12:00:00.000Z',
    }

    expect(PlayerSchema.safeParse(player).success).toBe(true)
  })

  it('applies defaults for rank', () => {
    const player = {
      id: '550e8400-e29b-41d4-a716-446655440000',
      name: 'NewPlayer',
      position: { x: 0, y: 0, z: 0 },
      region_id: 'region-spawn',
      connected: false,
    }

    const result = PlayerSchema.parse(player)
    expect(result.rank).toBe(0)
  })
})

describe('RegionSchema', () => {
  it('validates spawn island', () => {
    const region = {
      id: 'region-spawn',
      name: 'Spawn Island',
      bounds_min: { x: -500, y: -10, z: -500 },
      bounds_max: { x: 500, y: 200, z: 500 },
      active: true,
      player_count: 0,
      tick_rate: 20,
    }

    expect(RegionSchema.safeParse(region).success).toBe(true)
  })
})

describe('GuildSchema', () => {
  it('validates a guild with members', () => {
    const guild = {
      id: '550e8400-e29b-41d4-a716-446655440000',
      name: 'Road Builders',
      leader_id: '660e8400-e29b-41d4-a716-446655440000',
      member_ids: [
        '660e8400-e29b-41d4-a716-446655440000',
        '770e8400-e29b-41d4-a716-446655440000',
      ],
      points: 150,
      created_at: '2026-01-15T10:00:00.000Z',
    }

    expect(GuildSchema.safeParse(guild).success).toBe(true)
  })
})

describe('StructureSchema', () => {
  it('validates a placed structure with tags', () => {
    const structure = {
      id: '550e8400-e29b-41d4-a716-446655440000',
      type: 'wooden_wall',
      position: { x: 100, y: 0, z: 50 },
      rotation: 90,
      owner_id: '660e8400-e29b-41d4-a716-446655440000',
      region_id: 'region-spawn',
      placed_at: '2026-03-24T12:00:00.000Z',
      tags: ['road', 'foundation'],
    }

    expect(StructureSchema.safeParse(structure).success).toBe(true)
  })

  it('defaults tags to empty array', () => {
    const structure = {
      id: '550e8400-e29b-41d4-a716-446655440000',
      type: 'campfire',
      position: { x: 0, y: 0, z: 0 },
      owner_id: '660e8400-e29b-41d4-a716-446655440000',
      region_id: 'region-spawn',
      placed_at: '2026-03-24T12:00:00.000Z',
    }

    const result = StructureSchema.parse(structure)
    expect(result.tags).toEqual([])
  })
})
