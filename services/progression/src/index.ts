import express from 'express'
import pg from 'pg'
import { EventType } from '@game/schemas'

const PORT = Number(process.env.PROGRESSION_PORT) || 4003
const pool = new pg.Pool({
  host: process.env.PGHOST || 'localhost',
  port: Number(process.env.PGPORT) || 5432,
  database: process.env.PGDATABASE || 'game',
  user: process.env.PGUSER || 'game',
  password: process.env.PGPASSWORD || 'game',
})

async function initDb() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS player_progress (
      player_id TEXT PRIMARY KEY,
      rank INT NOT NULL DEFAULT 0,
      points INT NOT NULL DEFAULT 0,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    )
  `)
  await pool.query(`
    CREATE TABLE IF NOT EXISTS guild_progress (
      guild_id TEXT PRIMARY KEY,
      points INT NOT NULL DEFAULT 0,
      challenges_completed INT NOT NULL DEFAULT 0,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    )
  `)
  console.log('[progression] database initialized')
}

const app = express()
app.use(express.json())

app.get('/health', (_req, res) => {
  res.json({ status: 'ok', service: 'progression', timestamp: new Date().toISOString() })
})

app.get('/players/:id/progress', async (req, res) => {
  const result = await pool.query('SELECT * FROM player_progress WHERE player_id = $1', [
    req.params.id,
  ])
  if (result.rows.length === 0) return res.status(404).json({ error: 'Player not found' })
  res.json(result.rows[0])
})

app.get('/guilds/:id/progress', async (req, res) => {
  const result = await pool.query('SELECT * FROM guild_progress WHERE guild_id = $1', [
    req.params.id,
  ])
  if (result.rows.length === 0) return res.status(404).json({ error: 'Guild not found' })
  res.json(result.rows[0])
})

// Process incoming events (called by event-log or internal bus)
app.post('/process-event', async (req, res) => {
  const { event_type, actor_id, guild_id, payload } = req.body

  try {
    switch (event_type) {
      case EventType.STRUCTURE_PLACED:
        if (actor_id) {
          await pool.query(
            `INSERT INTO player_progress (player_id, points, updated_at)
             VALUES ($1, 1, NOW())
             ON CONFLICT (player_id) DO UPDATE SET points = player_progress.points + 1, updated_at = NOW()`,
            [actor_id],
          )
        }
        if (guild_id) {
          await pool.query(
            `INSERT INTO guild_progress (guild_id, points, updated_at)
             VALUES ($1, 1, NOW())
             ON CONFLICT (guild_id) DO UPDATE SET points = guild_progress.points + 1, updated_at = NOW()`,
            [guild_id],
          )
        }
        break

      case EventType.CHALLENGE_COMPLETED:
        if (guild_id) {
          await pool.query(
            `INSERT INTO guild_progress (guild_id, challenges_completed, updated_at)
             VALUES ($1, 1, NOW())
             ON CONFLICT (guild_id) DO UPDATE SET challenges_completed = guild_progress.challenges_completed + 1, updated_at = NOW()`,
            [guild_id],
          )
        }
        break

      case EventType.PLAYER_RANK_CHANGED:
        if (actor_id && payload?.new_rank != null) {
          await pool.query(
            `INSERT INTO player_progress (player_id, rank, updated_at)
             VALUES ($1, $2, NOW())
             ON CONFLICT (player_id) DO UPDATE SET rank = $2, updated_at = NOW()`,
            [actor_id, payload.new_rank],
          )
        }
        break
    }

    res.json({ processed: true })
  } catch (err) {
    console.error('[progression] failed to process event:', err)
    res.status(500).json({ error: 'Processing failed' })
  }
})

async function start() {
  await initDb()
  app.listen(PORT, () => {
    console.log(`[progression] listening on http://localhost:${PORT}`)
  })
}

start().catch((err) => {
  console.error('[progression] failed to start:', err)
  process.exit(1)
})
