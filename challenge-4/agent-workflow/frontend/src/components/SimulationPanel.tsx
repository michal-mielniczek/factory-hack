import { useState } from 'react'

interface SimulationEvent {
  machineId: string
  machineName: string
  healthFactor: number
  telemetry: Record<string, number>
  alerts: Array<{ metric: string; value: number; severity: string; threshold: number }>
  isAnomaly: boolean
  repaired: boolean
}

interface SimulationStep {
  step: number
  events: SimulationEvent[]
}

interface InventoryStatus {
  partId: string
  name: string
  originalStock: number
  currentStock: number
  consumed: number
}

interface SimulationResult {
  steps: number
  degradationRate: number
  summary: {
    totalAnomalies: number
    totalRepairs: number
    machinesAtRisk: number
  }
  machineHealth: Record<string, number>
  inventoryStatus: InventoryStatus[]
  timeline: SimulationStep[]
}

export function SimulationPanel({ apiBaseUrl }: { apiBaseUrl: string | undefined }) {
  const [steps, setSteps] = useState(12)
  const [degradationRate, setDegradationRate] = useState(0.03)
  const [result, setResult] = useState<SimulationResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selectedStep, setSelectedStep] = useState<number | null>(null)

  const makeUrl = (path: string) =>
    apiBaseUrl ? new URL(path, apiBaseUrl).toString() : path

  const runSimulation = async () => {
    setLoading(true)
    setError(null)
    setResult(null)
    setSelectedStep(null)

    try {
      const res = await fetch(makeUrl('/api/factory/simulate'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ steps, degradation_rate: degradationRate }),
      })
      if (!res.ok) throw new Error(`Simulation failed: ${res.status}`)
      const data = await res.json()
      setResult(data)
      setSelectedStep(0)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unknown error')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="simulation-panel">
      <div className="sim-controls">
        <div className="sim-control-group">
          <label>Time Steps</label>
          <input
            type="range"
            min={5}
            max={50}
            value={steps}
            onChange={e => setSteps(Number(e.target.value))}
            disabled={loading}
          />
          <span className="sim-value">{steps}</span>
        </div>
        <div className="sim-control-group">
          <label>Degradation Rate</label>
          <input
            type="range"
            min={1}
            max={10}
            value={Math.round(degradationRate * 100)}
            onChange={e => setDegradationRate(Number(e.target.value) / 100)}
            disabled={loading}
          />
          <span className="sim-value">{(degradationRate * 100).toFixed(0)}%</span>
        </div>
        <button className="primary-button" onClick={runSimulation} disabled={loading}>
          {loading ? 'Simulating...' : 'Run Simulation'}
        </button>
      </div>

      {error && (
        <div className="error-message" role="alert"><span>{error}</span></div>
      )}

      {result && (
        <div className="sim-results">
          {/* Summary */}
          <div className="sim-summary">
            <div className="sim-stat">
              <span className="sim-stat__value">{result.summary.totalAnomalies}</span>
              <span className="sim-stat__label">Anomalies Detected</span>
            </div>
            <div className="sim-stat">
              <span className="sim-stat__value">{result.summary.totalRepairs}</span>
              <span className="sim-stat__label">Repairs Made</span>
            </div>
            <div className="sim-stat">
              <span className="sim-stat__value">{result.summary.machinesAtRisk}</span>
              <span className="sim-stat__label">Machines at Risk</span>
            </div>
          </div>

          {/* Machine Health Bars */}
          <h4 className="dash-section-title">Final Machine Health</h4>
          <div className="sim-health-bars">
            {Object.entries(result.machineHealth).map(([mid, health]) => {
              const pct = Math.min(100, ((health - 1) / 1) * 100)
              const level = health < 1.3 ? 'good' : health < 1.6 ? 'warn' : 'danger'
              return (
                <div key={mid} className="sim-health-bar">
                  <span className="sim-health-bar__label">{mid}</span>
                  <div className="sim-health-bar__track">
                    <div className={`sim-health-bar__fill sim-health-bar__fill--${level}`} style={{ width: `${pct}%` }} />
                  </div>
                  <span className="sim-health-bar__value">{health.toFixed(2)}</span>
                </div>
              )
            })}
          </div>

          {/* Inventory Consumption */}
          <h4 className="dash-section-title">Parts Consumed</h4>
          <div className="sim-inventory">
            {result.inventoryStatus.filter(i => i.consumed > 0).map(inv => (
              <div key={inv.partId} className="sim-inv-item">
                <span className="sim-inv-name">{inv.name}</span>
                <span className="sim-inv-consumed">-{inv.consumed}</span>
                <span className="sim-inv-remaining">{inv.currentStock} left</span>
              </div>
            ))}
            {result.inventoryStatus.every(i => i.consumed === 0) && (
              <div className="muted">No parts consumed during simulation.</div>
            )}
          </div>

          {/* Timeline Scrubber */}
          <h4 className="dash-section-title">Timeline</h4>
          <div className="sim-timeline-scrubber">
            <input
              type="range"
              min={0}
              max={result.timeline.length - 1}
              value={selectedStep ?? 0}
              onChange={e => setSelectedStep(Number(e.target.value))}
              className="sim-timeline-slider"
            />
            <span className="sim-timeline-label">Step {(selectedStep ?? 0) + 1} / {result.timeline.length}</span>
          </div>

          {selectedStep !== null && result.timeline[selectedStep] && (
            <div className="sim-step-detail">
              {result.timeline[selectedStep].events.map(evt => (
                <div key={evt.machineId} className={`sim-event ${evt.isAnomaly ? 'sim-event--anomaly' : ''} ${evt.repaired ? 'sim-event--repaired' : ''}`}>
                  <div className="sim-event__header">
                    <span className="sim-event__machine">{evt.machineName}</span>
                    <span className={`badge badge--${evt.isAnomaly ? 'error' : 'done'}`}>
                      {evt.repaired ? '🔧 Repaired' : evt.isAnomaly ? '⚠️ Anomaly' : '✅ Normal'}
                    </span>
                    <span className="sim-event__health">Health: {evt.healthFactor}</span>
                  </div>
                  {evt.alerts.length > 0 && (
                    <div className="sim-event__alerts">
                      {evt.alerts.map((a, i) => (
                        <span key={i} className={`sim-alert sim-alert--${a.severity}`}>
                          {a.metric}: {a.value} (threshold: {a.threshold})
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
