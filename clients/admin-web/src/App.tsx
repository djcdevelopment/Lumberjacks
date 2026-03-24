import React, { useEffect, useState } from 'react'

interface ServiceStatus {
  service: string
  status: string
}

function App() {
  const [activeTab, setActiveTab] = useState<string>('status')
  const [services, setServices] = useState<ServiceStatus[]>([])
  const [events, setEvents] = useState<unknown[]>([])
  const [regions, setRegions] = useState<unknown[]>([])

  useEffect(() => {
    fetch('/api/status')
      .then((r) => r.json())
      .then((d) => setServices(d.services || []))
      .catch(() => setServices([]))
  }, [])

  useEffect(() => {
    if (activeTab === 'events') {
      fetch('/api/events')
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
  }, [activeTab])

  const tabs = ['status', 'events', 'regions', 'players', 'guilds']

  return (
    <div style={{ display: 'flex', minHeight: '100vh' }}>
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
          ADMIN CONSOLE
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
      <main style={{ flex: 1, padding: 24 }}>
        <h1 style={{ marginBottom: 16, fontSize: 20 }}>
          {activeTab.charAt(0).toUpperCase() + activeTab.slice(1)}
        </h1>

        {activeTab === 'status' && (
          <div>
            <p style={{ color: '#8b949e', marginBottom: 16 }}>Service health overview</p>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ borderBottom: '1px solid #30363d' }}>
                  <th style={{ textAlign: 'left', padding: 8 }}>Service</th>
                  <th style={{ textAlign: 'left', padding: 8 }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {services.map((s) => (
                  <tr key={s.service} style={{ borderBottom: '1px solid #21262d' }}>
                    <td style={{ padding: 8 }}>{s.service}</td>
                    <td style={{ padding: 8, color: s.status === 'up' ? '#3fb950' : '#f85149' }}>
                      {s.status}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {activeTab === 'events' && (
          <div>
            <p style={{ color: '#8b949e', marginBottom: 16 }}>Recent events from the event log</p>
            {events.length === 0 ? (
              <p>No events recorded yet.</p>
            ) : (
              <pre style={{ background: '#161b22', padding: 16, borderRadius: 6, overflow: 'auto' }}>
                {JSON.stringify(events, null, 2)}
              </pre>
            )}
          </div>
        )}

        {activeTab === 'regions' && (
          <div>
            <p style={{ color: '#8b949e', marginBottom: 16 }}>Active world regions</p>
            {regions.length === 0 ? (
              <p>No regions loaded.</p>
            ) : (
              <pre style={{ background: '#161b22', padding: 16, borderRadius: 6, overflow: 'auto' }}>
                {JSON.stringify(regions, null, 2)}
              </pre>
            )}
          </div>
        )}

        {activeTab === 'players' && (
          <p style={{ color: '#8b949e' }}>Player inspection coming soon.</p>
        )}

        {activeTab === 'guilds' && (
          <p style={{ color: '#8b949e' }}>Guild progress coming soon.</p>
        )}
      </main>
    </div>
  )
}

export { App }
