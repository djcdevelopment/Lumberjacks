import express from 'express'
import pg from 'pg'
import { GameEventSchema } from '@game/schemas'

const PORT = Number(process.env.EVENT_LOG_PORT) || 4002

const pool = new pg.Pool({
  host: process.env.PGHOST || 'localhost',
  port: Number(process.env.PGPORT) || 5432,
  database: process.env.PGDATABASE || 'game',
  user: process.env.PGUSER || 'game',
  password: process.env.PGPASSWORD || 'game',
})

async function initDb() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS events (
      id SERIAL PRIMARY KEY,
      event_id UUID UNIQUE NOT NULL,
      event_type TEXT NOT NULL,
      occurred_at TIMESTAMPTZ NOT NULL,
      world_id TEXT NOT NULL,
      region_id TEXT,
      actor_id TEXT,
      guild_id TEXT,
      source_service TEXT NOT NULL,
      schema_version INT NOT NULL,
      payload JSONB NOT NULL DEFAULT '{}',
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    )
  `)
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_events_type ON events (event_type)
  `)
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_events_actor ON events (actor_id)
  `)
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_events_occurred ON events (occurred_at)
  `)
  console.log('[event-log] database initialized')
}

const app = express()
app.use(express.json())

app.get('/health', (_req, res) => {
  res.json({ status: 'ok', service: 'event-log', timestamp: new Date().toISOString() })
})

// Append an event
app.post('/events', async (req, res) => {
  try {
    const event = GameEventSchema.parse(req.body)
    await pool.query(
      `INSERT INTO events (event_id, event_type, occurred_at, world_id, region_id, actor_id, guild_id, source_service, schema_version, payload)
       VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)`,
      [
        event.event_id,
        event.event_type,
        event.occurred_at,
        event.world_id,
        event.region_id,
        event.actor_id,
        event.guild_id,
        event.source_service,
        event.schema_version,
        JSON.stringify(event.payload),
      ],
    )
    res.status(201).json({ event_id: event.event_id })
  } catch (err) {
    console.error('[event-log] failed to append event:', err)
    res.status(400).json({ error: 'Invalid event', details: String(err) })
  }
})

// Query events
app.get('/events', async (req, res) => {
  const { type, actor_id, region_id, limit = '50', offset = '0' } = req.query
  const conditions: string[] = []
  const params: unknown[] = []
  let paramIdx = 1

  if (type) {
    conditions.push(`event_type = $${paramIdx++}`)
    params.push(type)
  }
  if (actor_id) {
    conditions.push(`actor_id = $${paramIdx++}`)
    params.push(actor_id)
  }
  if (region_id) {
    conditions.push(`region_id = $${paramIdx++}`)
    params.push(region_id)
  }

  const where = conditions.length > 0 ? `WHERE ${conditions.join(' AND ')}` : ''
  const query = `SELECT * FROM events ${where} ORDER BY occurred_at DESC LIMIT $${paramIdx++} OFFSET $${paramIdx++}`
  params.push(Number(limit), Number(offset))

  const result = await pool.query(query, params)
  res.json({ events: result.rows, count: result.rows.length })
})

async function start() {
  await initDb()
  app.listen(PORT, () => {
    console.log(`[event-log] listening on http://localhost:${PORT}`)
  })
}

start().catch((err) => {
  console.error('[event-log] failed to start:', err)
  process.exit(1)
})
