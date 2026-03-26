import React, { useEffect, useState } from 'react'

interface ServiceStatus {
  service: string
  status: string
}

interface GameEvent {
  event_id: string
  event_type: string
  occurred_at: string
  actor_id: string | null
  region_id: string | null
  source_service: string
}

interface Region {
  id: string
  name: string
  bounds_min: { x: number; y: number; z: number }
  bounds_max: { x: number; y: number; z: number }
  active: boolean
  player_count: number
  tick_rate: number
}

interface Structure {
  id: string
  type: string
  position: { x: number; y: number; z: number }
  rotation: number
  owner_id: string
  region_id: string
  placed_at: string
  tags: string[]
}

interface PlayerProgress {
  player_id: string
  rank: number
  points: number
  updated_at: string
}

interface GuildProgress {
  guild_id: string
  points: number
  challenges_completed: number
  updated_at: string
}

const tableStyle = { width: '100%', borderCollapse: 'collapse' as const }
const thStyle = { textAlign: 'left' as const, padding: 8, borderBottom: '1px solid #30363d' }
const tdStyle = { padding: 8, borderBottom: '1px solid #21262d' }
const preStyle = { background: '#161b22', padding: 16, borderRadius: 6, overflow: 'auto' as const }
const subtitleStyle = { color: '#8b949e', marginBottom: 16 }
const badgeUp = { color: '#3fb950', fontWeight: 'bold' as const }
const badgeDown = { color: '#f85149', fontWeight: 'bold' as const }

function App() {
  const [activeTab, setActiveTab] = useState<string>('status')
  const [services, setServices] = useState<ServiceStatus[]>([])
  const [events, setEvents] = useState<GameEvent[]>([])
  const [regions, setRegions] = useState<Region[]>([])
  const [structures, setStructures] = useState<Structure[]>([])
  const [players, setPlayers] = useState<PlayerProgress[]>([])
  const [guilds, setGuilds] = useState<GuildProgress[]>([])

  useEffect(() => {
    fetch('/api/status')
      .then((r) => r.json())
      .then((d) => setServices(d.services || []))
      .catch(() => setServices([]))
  }, [])

  useEffect(() => {
    if (activeTab === 'events') {
      fetch('/api/events?limit=50')
        .then((r) => r.json())
        .then((d) => setEvents(d.events || []))
        .catch(() => setEvents([]))
    }
    if (activeTab === 'regions') {
      fetch('/api/regions')
        .then((r) => r.json())
        .then((d) => setRegions(Array.isArray(d) ? d : []))
        .catch(() => setRegions([]))
    }
    if (activeTab === 'structures') {
      fetch('/api/structures')
        .then((r) => r.json())
        .then((d) => setStructures(Array.isArray(d) ? d : []))
        .catch(() => setStructures([]))
    }
    if (activeTab === 'players') {
      fetch('/api/events?type=structure_placed&limit=100')
        .then((r) => r.json())
        .then((d) => {
          const actorIds = [...new Set((d.events || []).map((e: GameEvent) => e.actor_id).filter(Boolean))]
          return Promise.all(
            actorIds.map((id) =>
              fetch(`/api/players/${id}/progress`)
                .then((r) => (r.ok ? r.json() : null))
                .catch(() => null)
            )
          )
        })
        .then((results) => setPlayers(results.filter(Boolean)))
        .catch(() => setPlayers([]))
    }
    if (activeTab === 'guilds') {
      fetch('/api/guilds')
        .then((r) => r.json())
        .then((d) => setGuilds(Array.isArray(d) ? d : []))
        .catch(() => setGuilds([]))
    }
  }, [activeTab])

  const tabs = ['status', 'events', 'regions', 'structures', 'players', 'guilds']

  return (
    <div style={{ display: 'flex', minHeight: '100vh', fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif' }}>
      {/* Sidebar */}
      <nav
        style={{
          width: 200,
          background: '#161b22',
          padding: '20px 0',
          borderRight: '1px solid #30363d',
        }}
      >
        <h2 style={{ padding: '0 16px', marginBottom: 20, fontSize: 14, color: '#8b949e' }}>
          OPERATOR CONSOLE
        </h2>
        {tabs.map((tab) => (
          <button
            key={tab}
            onClick={() => setActiveTab(tab)}
            style={{
              display: 'block',
              width: '100%',
              padding: '8px 16px',
              background: activeTab === tab ? '#21262d' : 'transparent',
              border: 'none',
              color: activeTab === tab ? '#58a6ff' : '#c9d1d9',
              textAlign: 'left',
              cursor: 'pointer',
              fontSize: 14,
              textTransform: 'capitalize',
            }}
          >
            {tab}
          </button>
        ))}
      </nav>

      {/* Main content */}
      <main style={{ flex: 1, padding: 24, background: '#0d1117', color: '#c9d1d9' }}>
        <h1 style={{ marginBottom: 16, fontSize: 20 }}>
          {activeTab.charAt(0).toUpperCase() + activeTab.slice(1)}
        </h1>

        {activeTab === 'status' && (
          <div>
            <p style={subtitleStyle}>Service health overview</p>
            <table style={tableStyle}>
              <thead>
                <tr>
                  <th style={thStyle}>Service</th>
                  <th style={thStyle}>Status</th>
                </tr>
              </thead>
              <tbody>
                {services.map((s) => (
                  <tr key={s.service}>
                    <td style={tdStyle}>{s.service}</td>
                    <td style={{ ...tdStyle, ...(s.status === 'up' ? badgeUp : badgeDown) }}>
                      {s.status === 'up' ? '● up' : '○ down'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {activeTab === 'events' && (
          <div>
            <p style={subtitleStyle}>Recent events from the event log ({events.length} shown)</p>
            {events.length === 0 ? (
              <p>No events recorded yet.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Actor</th>
                    <th style={thStyle}>Region</th>
                    <th style={thStyle}>Source</th>
                    <th style={thStyle}>Time</th>
                  </tr>
                </thead>
                <tbody>
                  {events.map((e) => (
                    <tr key={e.event_id}>
                      <td style={{ ...tdStyle, color: '#d2a8ff' }}>{e.event_type}</td>
                      <td style={tdStyle}>{e.actor_id ? e.actor_id.slice(0, 8) + '...' : '—'}</td>
                      <td style={tdStyle}>{e.region_id || '—'}</td>
                      <td style={tdStyle}>{e.source_service}</td>
                      <td style={{ ...tdStyle, color: '#8b949e' }}>
                        {new Date(e.occurred_at).toLocaleString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}

        {activeTab === 'regions' && (
          <div>
            <p style={subtitleStyle}>Active world regions</p>
            {regions.length === 0 ? (
              <p>No regions loaded.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Name</th>
                    <th style={thStyle}>ID</th>
                    <th style={thStyle}>Active</th>
                    <th style={thStyle}>Players</th>
                    <th style={thStyle}>Tick Rate</th>
                    <th style={thStyle}>Bounds</th>
                  </tr>
                </thead>
                <tbody>
                  {regions.map((r) => (
                    <tr key={r.id}>
                      <td style={{ ...tdStyle, fontWeight: 'bold' }}>{r.name}</td>
                      <td style={{ ...tdStyle, color: '#8b949e' }}>{r.id}</td>
                      <td style={tdStyle}>
                        <span style={r.active ? badgeUp : badgeDown}>
                          {r.active ? '● active' : '○ inactive'}
                        </span>
                      </td>
                      <td style={tdStyle}>{r.player_count}</td>
                      <td style={tdStyle}>{r.tick_rate} Hz</td>
                      <td style={{ ...tdStyle, fontSize: 12, color: '#8b949e' }}>
                        ({r.bounds_min.x}, {r.bounds_min.y}, {r.bounds_min.z}) →
                        ({r.bounds_max.x}, {r.bounds_max.y}, {r.bounds_max.z})
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}

        {activeTab === 'structures' && (
          <div>
            <p style={subtitleStyle}>Placed structures in the world ({structures.length} total)</p>
            {structures.length === 0 ? (
              <p>No structures placed yet. Connect via WebSocket and send a place_structure message.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Position</th>
                    <th style={thStyle}>Owner</th>
                    <th style={thStyle}>Region</th>
                    <th style={thStyle}>Placed At</th>
                    <th style={thStyle}>Tags</th>
                  </tr>
                </thead>
                <tbody>
                  {structures.map((s) => (
                    <tr key={s.id}>
                      <td style={{ ...tdStyle, color: '#7ee787', fontWeight: 'bold' }}>{s.type}</td>
                      <td style={{ ...tdStyle, fontFamily: 'monospace', fontSize: 12 }}>
                        ({s.position.x.toFixed(1)}, {s.position.y.toFixed(1)}, {s.position.z.toFixed(1)})
                      </td>
                      <td style={tdStyle}>{s.owner_id.slice(0, 8)}...</td>
                      <td style={tdStyle}>{s.region_id}</td>
                      <td style={{ ...tdStyle, color: '#8b949e' }}>
                        {new Date(s.placed_at).toLocaleString()}
                      </td>
                      <td style={tdStyle}>{s.tags.length > 0 ? s.tags.join(', ') : '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}

        {activeTab === 'players' && (
          <div>
            <p style={subtitleStyle}>Player progression</p>
            {players.length === 0 ? (
              <p>No player progress recorded yet.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Player ID</th>
                    <th style={thStyle}>Rank</th>
                    <th style={thStyle}>Points</th>
                    <th style={thStyle}>Last Updated</th>
                  </tr>
                </thead>
                <tbody>
                  {players.map((p) => (
                    <tr key={p.player_id}>
                      <td style={{ ...tdStyle, fontFamily: 'monospace' }}>{p.player_id.slice(0, 12)}...</td>
                      <td style={tdStyle}>{p.rank}</td>
                      <td style={{ ...tdStyle, color: '#d2a8ff', fontWeight: 'bold' }}>{p.points}</td>
                      <td style={{ ...tdStyle, color: '#8b949e' }}>
                        {new Date(p.updated_at).toLocaleString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}

        {activeTab === 'guilds' && (
          <div>
            <p style={subtitleStyle}>Guild progress</p>
            {guilds.length === 0 ? (
              <p>No guild progress recorded yet.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Guild ID</th>
                    <th style={thStyle}>Points</th>
                    <th style={thStyle}>Challenges</th>
                    <th style={thStyle}>Last Updated</th>
                  </tr>
                </thead>
                <tbody>
                  {guilds.map((g) => (
                    <tr key={g.guild_id}>
                      <td style={{ ...tdStyle, fontFamily: 'monospace' }}>{g.guild_id}</td>
                      <td style={{ ...tdStyle, color: '#d2a8ff', fontWeight: 'bold' }}>{g.points}</td>
                      <td style={tdStyle}>{g.challenges_completed}</td>
                      <td style={{ ...tdStyle, color: '#8b949e' }}>
                        {new Date(g.updated_at).toLocaleString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
      </main>
    </div>
  )
}

export { App }
