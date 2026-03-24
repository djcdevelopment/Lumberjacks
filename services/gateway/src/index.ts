import express from 'express'
import { WebSocketServer, WebSocket } from 'ws'
import { createServer } from 'http'
import { v4 as uuid } from 'uuid'
import { createEnvelope, serializeEnvelope, parseEnvelope, MessageType } from '@game/protocol'

const PORT = Number(process.env.GATEWAY_PORT) || 4000

const app = express()
app.use(express.json())

app.get('/health', (_req, res) => {
  res.json({ status: 'ok', service: 'gateway', timestamp: new Date().toISOString() })
})

const server = createServer(app)
const wss = new WebSocketServer({ server })

const sessions = new Map<string, { ws: WebSocket; playerId: string }>()

wss.on('connection', (ws) => {
  const sessionId = uuid()
  const playerId = uuid()

  sessions.set(sessionId, { ws, playerId })

  console.log(`[gateway] session ${sessionId} connected (player ${playerId})`)

  // Send session start
  const welcome = createEnvelope(MessageType.SESSION_STARTED, {
    session_id: sessionId,
    player_id: playerId,
    world_id: 'world-default',
  })
  ws.send(serializeEnvelope(welcome))

  ws.on('message', (raw) => {
    try {
      const envelope = parseEnvelope(raw.toString())
      console.log(`[gateway] received ${envelope.type} from session ${sessionId}`)

      // TODO: Route to simulation service based on message type
    } catch (err) {
      const errMsg = createEnvelope(MessageType.ERROR, {
        code: 'INVALID_MESSAGE',
        message: 'Failed to parse message',
      })
      ws.send(serializeEnvelope(errMsg))
    }
  })

  ws.on('close', () => {
    sessions.delete(sessionId)
    console.log(`[gateway] session ${sessionId} disconnected`)
  })
})

server.listen(PORT, () => {
  console.log(`[gateway] listening on http://localhost:${PORT}`)
  console.log(`[gateway] WebSocket on ws://localhost:${PORT}`)
})
