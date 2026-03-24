import express from 'express'
import cors from 'cors'
import pg from 'pg'

const PORT = Number(process.env.OPERATOR_API_PORT) || 4004
const SIMULATION_URL = process.env.SIMULATION_URL || 'http://localhost:4001'
const EVENT_LOG_URL = process.env.EVENT_LOG_URL || 'http://localhost:4002'
const PROGRESSION_URL = process.env.PROGRESSION_URL || 'http://localhost:4003'

const pool = new pg.Pool({
  host: process.env.PGHOST || 'localhost',
  port: Number(process.env.PGPORT) || 5432,
  database: process.env.PGDATABASE || 'game',
  user: process.env.PGUSER || 'game',
  password: process.env.PGPASSWORD || 'game',
})

const app = express()
app.use(cors())
app.use(express.json())

app.get('/health', (_req, res) => {
  res.json({ status: 'ok', service: 'operator-api', timestamp: new Date().toISOString() })
})

// Proxy to simulation for region/player data
app.get('/api/regions', async (_req, res) => {
  try {
    const response = await fetch(`${SIMULATION_URL}/regions`)
    const data = await response.json()
    res.json(data)
  } catch {
    res.status(502).json({ error: 'Simulation service unavailable' })
  }
})

app.get('/api/players', async (_req, res) => {
  try {
    const response = await fetch(`${SIMULATION_URL}/players`)
    const data = await response.json()
    res.json(data)
  } catch {
    res.status(502).json({ error: 'Simulation service unavailable' })
  }
})

// Proxy to event-log
app.get('/api/events', async (req, res) => {
  try {
    const params = new URLSearchParams(req.query as Record<string, string>)
    const response = await fetch(`${EVENT_LOG_URL}/events?${params}`)
    const data = await response.json()
    res.json(data)
  } catch {
    res.status(502).json({ error: 'Event log service unavailable' })
  }
})

// Guild progress from progression service
app.get('/api/guilds', async (_req, res) => {
  try {
    const result = await pool.query('SELECT * FROM guild_progress ORDER BY points DESC')
    res.json(result.rows)
  } catch {
    res.status(500).json({ error: 'Failed to query guild progress' })
  }
})

// Service status overview
app.get('/api/status', async (_req, res) => {
  const services = ['gateway', 'simulation', 'event-log', 'progression']
  const urls = [
    'http://localhost:4000/health',
    `${SIMULATION_URL}/health`,
    `${EVENT_LOG_URL}/health`,
    `${PROGRESSION_URL}/health`,
  ]

  const checks = await Promise.all(
    urls.map(async (url, i) => {
      try {
        const r = await fetch(url, { signal: AbortSignal.timeout(2000) })
        const data = await r.json()
        return { service: services[i], status: 'up', ...data }
      } catch {
        return { service: services[i], status: 'down' }
      }
    }),
  )

  res.json({ services: checks, timestamp: new Date().toISOString() })
})

app.listen(PORT, () => {
  console.log(`[operator-api] listening on http://localhost:${PORT}`)
})
