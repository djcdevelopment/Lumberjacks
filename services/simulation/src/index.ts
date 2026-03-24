import express from 'express'
import type { Region, Player, Vec3 } from '@game/schemas'

const PORT = Number(process.env.SIMULATION_PORT) || 4001

const app = express()
app.use(express.json())

// In-memory world state
const regions = new Map<string, Region>()
const players = new Map<string, Player>()

// Seed a default region
regions.set('region-spawn', {
  id: 'region-spawn',
  name: 'Spawn Island',
  bounds_min: { x: -500, y: -10, z: -500 },
  bounds_max: { x: 500, y: 200, z: 500 },
  active: true,
  player_count: 0,
  tick_rate: 20,
})

app.get('/health', (_req, res) => {
  res.json({ status: 'ok', service: 'simulation', timestamp: new Date().toISOString() })
})

app.get('/regions', (_req, res) => {
  res.json(Array.from(regions.values()))
})

app.get('/regions/:id', (req, res) => {
  const region = regions.get(req.params.id)
  if (!region) return res.status(404).json({ error: 'Region not found' })
  res.json(region)
})

app.get('/players', (_req, res) => {
  res.json(Array.from(players.values()))
})

// Simulation tick loop
const TICK_RATE = 20
const TICK_MS = 1000 / TICK_RATE
let tickCount = 0

function tick() {
  tickCount++
  // TODO: Process movement, resolve placements, emit events
  // This is where interest management, activation tiers, and
  // authoritative state resolution will live
}

const tickInterval = setInterval(tick, TICK_MS)

process.on('SIGTERM', () => {
  clearInterval(tickInterval)
  process.exit(0)
})

app.listen(PORT, () => {
  console.log(`[simulation] listening on http://localhost:${PORT}`)
  console.log(`[simulation] tick rate: ${TICK_RATE}Hz`)
  console.log(`[simulation] regions loaded: ${regions.size}`)
})
