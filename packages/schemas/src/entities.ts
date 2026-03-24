import { z } from 'zod'

export const Vec3Schema = z.object({
  x: z.number(),
  y: z.number(),
  z: z.number(),
})

export type Vec3 = z.infer<typeof Vec3Schema>

export const PlayerSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  guild_id: z.string().uuid().optional(),
  rank: z.number().int().default(0),
  position: Vec3Schema,
  region_id: z.string(),
  connected: z.boolean(),
  connected_at: z.string().datetime().optional(),
})

export type Player = z.infer<typeof PlayerSchema>

export const RegionSchema = z.object({
  id: z.string(),
  name: z.string(),
  bounds_min: Vec3Schema,
  bounds_max: Vec3Schema,
  active: z.boolean(),
  player_count: z.number().int().default(0),
  tick_rate: z.number().default(20),
})

export type Region = z.infer<typeof RegionSchema>

export const GuildSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  leader_id: z.string().uuid(),
  member_ids: z.array(z.string().uuid()),
  points: z.number().int().default(0),
  created_at: z.string().datetime(),
})

export type Guild = z.infer<typeof GuildSchema>

export const SessionSchema = z.object({
  session_id: z.string().uuid(),
  player_id: z.string().uuid(),
  world_id: z.string(),
  region_id: z.string(),
  connected_at: z.string().datetime(),
  token: z.string(),
})

export type Session = z.infer<typeof SessionSchema>

export const StructureSchema = z.object({
  id: z.string().uuid(),
  type: z.string(),
  position: Vec3Schema,
  rotation: z.number().default(0),
  owner_id: z.string().uuid(),
  region_id: z.string(),
  placed_at: z.string().datetime(),
  tags: z.array(z.string()).default([]),
})

export type Structure = z.infer<typeof StructureSchema>
