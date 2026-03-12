import { useEffect, useState } from 'react'

interface KPIs {
  totalMachines: number
  operationalMachines: number
  totalParts: number
  lowStockParts: number
  openWorkOrders: number
  totalFaults: number
  totalDowntimeMinutes: number
}

interface MachineStatus {
  id: string
  name: string
  type: string
  status: string
  operatingHours: number
  cyclesCompleted: number
  faultCount: number
  totalDowntimeMinutes: number
}

interface InventoryItem {
  id: string
  name: string
  partNumber: string
  quantityInStock: number
  reorderLevel: number
  stockRatio: number
  status: string
  unitCost: number
  leadTimeDays: number
}

interface DashboardData {
  timestamp: string
  kpis: KPIs
  machines: MachineStatus[]
  inventory: InventoryItem[]
  workOrdersByStatus: Record<string, number>
  recentWorkOrders: Array<{
    id: string
    title: string
    status: string
    priority: string
    machineId: string
    createdDate: string
  }>
  upcomingMaintenanceWindows: Array<{
    id: string
    startTime: string
    endTime: string
    productionImpact: string
    shift: string
    description: string
  }>
}

interface RiskScore {
  machineId: string
  machineName: string
  riskScore: number
  riskLevel: string
  totalDowntimeMinutes: number
  avgRepairCost: number
  recommendation: string
  factors: {
    faultFrequency: { score: number; max: number; faultCount: number }
    operatingWear: { score: number; max: number; hours: number }
    recentFaults: { score: number; max: number }
    partsAvailability: { score: number; max: number }
  }
}

interface InventoryForecast {
  partId: string
  name: string
  partNumber: string
  currentStock: number
  reorderLevel: number
  dailyUsageRate: number
  daysUntilDepletion: number
  daysUntilReorder: number
  leadTimeDays: number
  reorderUrgency: string
  estimatedReorderCost: number
}

export function Dashboard({ apiBaseUrl }: { apiBaseUrl: string | undefined }) {
  const [dashboard, setDashboard] = useState<DashboardData | null>(null)
  const [riskScores, setRiskScores] = useState<RiskScore[]>([])
  const [inventoryForecast, setInventoryForecast] = useState<InventoryForecast[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [activeSection, setActiveSection] = useState<'overview' | 'risk' | 'inventory'>('overview')

  const makeUrl = (path: string) =>
    apiBaseUrl ? new URL(path, apiBaseUrl).toString() : path

  const fetchAll = async () => {
    setLoading(true)
    setError(null)
    try {
      const [dashRes, riskRes, invRes] = await Promise.all([
        fetch(makeUrl('/api/factory/dashboard')),
        fetch(makeUrl('/api/factory/risk-scores')),
        fetch(makeUrl('/api/factory/inventory-forecast')),
      ])
      if (!dashRes.ok || !riskRes.ok || !invRes.ok) {
        throw new Error('Failed to fetch dashboard data')
      }
      setDashboard(await dashRes.json())
      setRiskScores(await riskRes.json())
      setInventoryForecast(await invRes.json())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unknown error')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchAll()
  }, [])

  if (loading) {
    return (
      <div className="dashboard-loading">
        <div className="spinner" />
        <p>Loading factory dashboard...</p>
      </div>
    )
  }

  if (error) {
    return (
      <div className="error-message" role="alert">
        <span>Dashboard error: {error}</span>
      </div>
    )
  }

  if (!dashboard) return null

  const { kpis } = dashboard

  return (
    <div className="dashboard">
      {/* KPI Cards */}
      <div className="dashboard-kpis">
        <KpiCard label="Machines Online" value={`${kpis.operationalMachines}/${kpis.totalMachines}`} icon="🏭" color="green" />
        <KpiCard label="Open Work Orders" value={kpis.openWorkOrders} icon="📋" color={kpis.openWorkOrders > 3 ? 'red' : 'blue'} />
        <KpiCard label="Low Stock Parts" value={kpis.lowStockParts} icon="📦" color={kpis.lowStockParts > 0 ? 'orange' : 'green'} />
        <KpiCard label="Total Faults" value={kpis.totalFaults} icon="⚠️" color="red" />
        <KpiCard label="Total Downtime" value={`${Math.round(kpis.totalDowntimeMinutes / 60)}h`} icon="⏱️" color="purple" />
      </div>

      {/* Section Tabs */}
      <div className="dashboard-tabs">
        <button className={`dash-tab ${activeSection === 'overview' ? 'dash-tab--active' : ''}`} onClick={() => setActiveSection('overview')}>
          Factory Overview
        </button>
        <button className={`dash-tab ${activeSection === 'risk' ? 'dash-tab--active' : ''}`} onClick={() => setActiveSection('risk')}>
          Risk Scores
        </button>
        <button className={`dash-tab ${activeSection === 'inventory' ? 'dash-tab--active' : ''}`} onClick={() => setActiveSection('inventory')}>
          Inventory Forecast
        </button>
        <button className="dash-tab dash-tab--refresh" onClick={fetchAll} aria-label="Refresh dashboard">
          ↻ Refresh
        </button>
      </div>

      {/* Overview Section */}
      {activeSection === 'overview' && (
        <div className="dashboard-section">
          <h3 className="dash-section-title">Machine Status</h3>
          <div className="machine-grid">
            {dashboard.machines.map(m => (
              <div key={m.id} className={`machine-card machine-card--${m.status}`}>
                <div className="machine-card__header">
                  <span className="machine-card__name">{m.name}</span>
                  <span className={`machine-card__status badge badge--${m.status === 'operational' ? 'done' : 'error'}`}>
                    {m.status}
                  </span>
                </div>
                <div className="machine-card__stats">
                  <div><span className="stat-label">Hours</span><span className="stat-value">{m.operatingHours.toLocaleString()}</span></div>
                  <div><span className="stat-label">Cycles</span><span className="stat-value">{m.cyclesCompleted.toLocaleString()}</span></div>
                  <div><span className="stat-label">Faults</span><span className="stat-value">{m.faultCount}</span></div>
                  <div><span className="stat-label">Downtime</span><span className="stat-value">{Math.round(m.totalDowntimeMinutes / 60)}h</span></div>
                </div>
              </div>
            ))}
          </div>

          <h3 className="dash-section-title">Work Orders</h3>
          <div className="wo-summary">
            {Object.entries(dashboard.workOrdersByStatus).map(([status, count]) => (
              <div key={status} className="wo-chip">
                <span className={`wo-dot wo-dot--${status}`} />
                {status}: {count}
              </div>
            ))}
          </div>

          {dashboard.upcomingMaintenanceWindows.length > 0 && (
            <>
              <h3 className="dash-section-title">Upcoming Maintenance Windows</h3>
              <div className="maint-windows">
                {dashboard.upcomingMaintenanceWindows.map(w => (
                  <div key={w.id} className="maint-window-card">
                    <div className="maint-window__time">
                      {new Date(w.startTime).toLocaleDateString()} — {w.shift}
                    </div>
                    <div className="maint-window__desc">{w.description}</div>
                    <span className={`badge badge--${w.productionImpact === 'None' ? 'done' : 'pending'}`}>
                      Impact: {w.productionImpact}
                    </span>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      )}

      {/* Risk Scores Section */}
      {activeSection === 'risk' && (
        <div className="dashboard-section">
          <h3 className="dash-section-title">Predictive Maintenance Risk Scores</h3>
          <div className="risk-grid">
            {riskScores.map(r => (
              <div key={r.machineId} className={`risk-card risk-card--${r.riskLevel}`}>
                <div className="risk-card__header">
                  <span className="risk-card__name">{r.machineName}</span>
                  <div className="risk-gauge">
                    <div className="risk-gauge__fill" style={{ width: `${r.riskScore}%` }} />
                    <span className="risk-gauge__label">{r.riskScore}/100</span>
                  </div>
                </div>
                <div className="risk-card__level">
                  <span className={`badge badge--${r.riskLevel === 'high' ? 'error' : r.riskLevel === 'medium' ? 'running' : 'done'}`}>
                    {r.riskLevel.toUpperCase()}
                  </span>
                </div>
                <div className="risk-factors">
                  <FactorBar label="Fault Severity" score={r.factors.faultFrequency?.score ?? 0} max={r.factors.faultFrequency?.max ?? 25} />
                  <FactorBar label="Operating Wear" score={r.factors.operatingWear?.score ?? 0} max={r.factors.operatingWear?.max ?? 20} />
                  <FactorBar label="Recent Faults" score={r.factors.recentFaults?.score ?? 0} max={r.factors.recentFaults?.max ?? 15} />
                  <FactorBar label="Telemetry" score={r.factors.telemetryAnomaly?.score ?? 0} max={r.factors.telemetryAnomaly?.max ?? 20} />
                  <FactorBar label="Parts Risk" score={r.factors.partsSupplyRisk?.score ?? 0} max={r.factors.partsSupplyRisk?.max ?? 10} />
                  <FactorBar label="Backlog" score={r.factors.maintenanceBacklog?.score ?? 0} max={r.factors.maintenanceBacklog?.max ?? 10} />
                </div>
                <div className="risk-card__stats">
                  <span>Avg repair: ${r.avgRepairCost?.toLocaleString()}</span>
                  <span>Downtime: {Math.round((r.totalDowntimeMinutes ?? 0) / 60)}h</span>
                  {r.factors.operatingWear?.wearRatio != null && (
                    <span>Wear: {Math.round(r.factors.operatingWear.wearRatio * 100)}%</span>
                  )}
                </div>
                <div className="risk-card__recommendation">{r.recommendation}</div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Inventory Forecast Section */}
      {activeSection === 'inventory' && (
        <div className="dashboard-section">
          <h3 className="dash-section-title">Inventory Forecast & Reorder Planning</h3>
          <div className="inventory-table-wrapper">
            <table className="inventory-table">
              <thead>
                <tr>
                  <th>Part</th>
                  <th>Stock</th>
                  <th>Reorder Lvl</th>
                  <th>Daily Usage</th>
                  <th>Days to Depletion</th>
                  <th>Lead Time</th>
                  <th>Urgency</th>
                  <th>Reorder Cost</th>
                </tr>
              </thead>
              <tbody>
                {inventoryForecast.map(f => (
                  <tr key={f.partId} className={`inv-row inv-row--${f.reorderUrgency}`}>
                    <td>
                      <div className="inv-part-name">{f.name}</div>
                      <div className="inv-part-number">{f.partNumber}</div>
                    </td>
                    <td>{f.currentStock}</td>
                    <td>{f.reorderLevel}</td>
                    <td>{f.dailyUsageRate.toFixed(2)}/day</td>
                    <td>
                      <span className={f.daysUntilDepletion < 30 ? 'text-danger' : ''}>
                        {f.daysUntilDepletion >= 999 ? '∞' : `${f.daysUntilDepletion}d`}
                      </span>
                    </td>
                    <td>{f.leadTimeDays}d</td>
                    <td>
                      <span className={`badge badge--${f.reorderUrgency === 'critical' ? 'error' : f.reorderUrgency === 'warning' ? 'running' : 'done'}`}>
                        {f.reorderUrgency}
                      </span>
                    </td>
                    <td>${f.estimatedReorderCost.toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}

function KpiCard({ label, value, icon, color }: { label: string; value: string | number; icon: string; color: string }) {
  return (
    <div className={`kpi-card kpi-card--${color}`}>
      <span className="kpi-icon">{icon}</span>
      <div className="kpi-value">{value}</div>
      <div className="kpi-label">{label}</div>
    </div>
  )
}

function FactorBar({ label, score, max }: { label: string; score: number; max: number }) {
  const pct = max > 0 ? (score / max) * 100 : 0
  return (
    <div className="factor-bar">
      <span className="factor-bar__label">{label}</span>
      <div className="factor-bar__track">
        <div className="factor-bar__fill" style={{ width: `${pct}%` }} />
      </div>
      <span className="factor-bar__score">{score}/{max}</span>
    </div>
  )
}
