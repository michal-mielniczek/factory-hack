"""Factory data aggregation module.

Loads machine, inventory, work order, threshold, and maintenance data
from JSON seed files and provides aggregation functions for the dashboard.
"""

import json
from datetime import datetime, timezone
from pathlib import Path

DATA_DIR = Path(__file__).resolve().parent.parent.parent.parent / "challenge-0" / "data"


def _load_json(filename: str) -> list[dict]:
    path = DATA_DIR / filename
    if not path.exists():
        return []
    with open(path) as f:
        return json.load(f)


def get_machines() -> list[dict]:
    return _load_json("machines.json")


def get_inventory() -> list[dict]:
    return _load_json("parts-inventory.json")


def get_work_orders() -> list[dict]:
    return _load_json("work-orders.json")


def get_thresholds() -> list[dict]:
    return _load_json("thresholds.json")


def get_maintenance_history() -> list[dict]:
    return _load_json("maintenance-history.json")


def get_maintenance_windows() -> list[dict]:
    return _load_json("maintenance-windows.json")


def get_technicians() -> list[dict]:
    return _load_json("technicians.json")


def get_telemetry_samples() -> list[dict]:
    return _load_json("telemetry-samples.json")


# ── Aggregation helpers ──────────────────────────────────────────────


def build_dashboard_summary() -> dict:
    """Build a complete factory dashboard summary."""
    machines = get_machines()
    inventory = get_inventory()
    work_orders = get_work_orders()
    history = get_maintenance_history()
    windows = get_maintenance_windows()

    # Machine health summary
    machine_statuses = []
    for m in machines:
        fault_count = sum(1 for h in history if h.get("machineId") == m["id"])
        total_downtime = sum(h.get("downtime", 0) for h in history if h.get("machineId") == m["id"])
        machine_statuses.append(
            {
                "id": m["id"],
                "name": m["name"],
                "type": m["type"],
                "status": m.get("status", "unknown"),
                "operatingHours": m.get("operatingHours", 0),
                "cyclesCompleted": m.get("cyclesCompleted", 0),
                "faultCount": fault_count,
                "totalDowntimeMinutes": total_downtime,
            }
        )

    # Inventory health
    inventory_items = []
    low_stock_count = 0
    for part in inventory:
        qty = part.get("quantityInStock", 0)
        reorder = part.get("reorderLevel", 0)
        ratio = qty / reorder if reorder > 0 else 999
        status = "critical" if ratio <= 1.0 else "warning" if ratio <= 2.0 else "healthy"
        if status in ("critical", "warning"):
            low_stock_count += 1
        inventory_items.append(
            {
                "id": part["id"],
                "name": part["name"],
                "partNumber": part.get("partNumber", ""),
                "quantityInStock": qty,
                "reorderLevel": reorder,
                "stockRatio": round(ratio, 2),
                "status": status,
                "unitCost": part.get("unitCost", 0),
                "leadTimeDays": part.get("leadTimeDays", 0),
            }
        )

    # Work order summary
    wo_by_status: dict[str, int] = {}
    for wo in work_orders:
        s = wo.get("status", "unknown")
        wo_by_status[s] = wo_by_status.get(s, 0) + 1

    # Upcoming maintenance windows
    now = datetime.now(timezone.utc)
    upcoming_windows = [
        w for w in windows if w.get("isAvailable") and _parse_dt(w.get("startTime", "")) > now
    ]
    upcoming_windows.sort(key=lambda w: w.get("startTime", ""))

    return {
        "timestamp": now.isoformat(),
        "kpis": {
            "totalMachines": len(machines),
            "operationalMachines": sum(1 for m in machines if m.get("status") == "operational"),
            "totalParts": len(inventory),
            "lowStockParts": low_stock_count,
            "openWorkOrders": sum(
                1 for wo in work_orders if wo.get("status") not in ("completed", "closed")
            ),
            "totalFaults": len(history),
            "totalDowntimeMinutes": sum(h.get("downtime", 0) for h in history),
        },
        "machines": machine_statuses,
        "inventory": sorted(inventory_items, key=lambda x: x["stockRatio"]),
        "workOrdersByStatus": wo_by_status,
        "recentWorkOrders": sorted(
            work_orders,
            key=lambda w: w.get("createdDate", ""),
            reverse=True,
        )[:10],
        "upcomingMaintenanceWindows": upcoming_windows[:5],
    }


def compute_risk_scores() -> list[dict]:
    """Compute predictive maintenance risk scores per machine.

    Uses a weighted multi-factor model (0-100 scale) combining:
      1. Fault frequency & severity (0-25) — count weighted by avg repair cost
      2. Operating wear (0-20) — hours relative to machine-type expected lifetime
      3. Recent fault recency (0-15) — decays with days since last fault
      4. Telemetry anomaly (0-20) — current sensor readings vs thresholds
      5. Parts supply chain risk (0-10) — stock vs reorder vs lead time
      6. Maintenance backlog (0-10) — open/in-progress work orders
    """
    machines = get_machines()
    history = get_maintenance_history()
    inventory = get_inventory()
    thresholds = get_thresholds()
    telemetry = get_telemetry_samples()
    work_orders = get_work_orders()

    # Expected lifetime hours per machine type (industry heuristics)
    lifetime_hours = {
        "tire_curing_press": 20000,
        "tire_building_machine": 25000,
        "tire_extruder": 20000,
        "tire_uniformity_machine": 30000,
        "banbury_mixer": 35000,
    }

    # Build threshold lookup: machineType -> {metric -> threshold_record}
    threshold_map: dict[str, dict[str, dict]] = {}
    for t in thresholds:
        mt = t.get("machineType", "")
        metric = t.get("metric", "")
        threshold_map.setdefault(mt, {})[metric] = t

    risk_scores = []
    for m in machines:
        mid = m["id"]
        mtype = m["type"]
        machine_history = [h for h in history if h.get("machineId") == mid]

        # Factor 1: Fault frequency & cost severity (0-25 pts)
        fault_count = len(machine_history)
        total_cost = sum(h.get("cost", 0) for h in machine_history)
        # Scale: 0 faults = 0, cost-weighted with diminishing returns
        cost_weight = min(total_cost / 5000, 1.0)  # $5K normalizer
        fault_score = min(int((fault_count * 5) + (cost_weight * 10)), 25)

        # Factor 2: Operating wear (0-20 pts)
        hours = m.get("operatingHours", 0)
        expected = lifetime_hours.get(mtype, 25000)
        wear_ratio = hours / expected
        hours_score = min(int(wear_ratio * 20), 20)

        # Factor 3: Recent fault recency (0-15 pts)
        recency_score = 0
        if machine_history:
            dates = []
            for h in machine_history:
                try:
                    dates.append(datetime.fromisoformat(h["occurrenceDate"].replace("Z", "+00:00")))
                except (KeyError, ValueError):
                    pass
            if dates:
                most_recent = max(dates)
                days_since = (datetime.now(timezone.utc) - most_recent).days
                recency_score = max(0, 15 - days_since // 7)  # loses 1pt per week

        # Factor 4: Telemetry anomaly score (0-20 pts)
        telemetry_score = 0
        machine_telemetry = [t for t in telemetry if t.get("machineId") == mid]
        machine_thresholds = threshold_map.get(mtype, {})
        anomaly_details = []
        if machine_telemetry and machine_thresholds:
            # Use the most recent telemetry reading
            latest = sorted(machine_telemetry, key=lambda t: t.get("timestamp", ""))[-1]
            metrics = latest.get("metrics", {})
            for metric_name, value in metrics.items():
                thr = machine_thresholds.get(metric_name)
                if not thr:
                    continue
                warning = thr.get("warningThreshold", 0)
                critical = thr.get("criticalThreshold", 0)
                # Handle inverted thresholds (throughput: low is bad)
                if warning > critical:  # inverted
                    if value <= critical:
                        telemetry_score += 10
                        anomaly_details.append(f"{metric_name}={value} CRITICAL (≤{critical})")
                    elif value <= warning:
                        telemetry_score += 5
                        anomaly_details.append(f"{metric_name}={value} WARNING (≤{warning})")
                else:  # normal: high is bad
                    if value >= critical:
                        telemetry_score += 10
                        anomaly_details.append(f"{metric_name}={value} CRITICAL (≥{critical})")
                    elif value >= warning:
                        telemetry_score += 5
                        anomaly_details.append(f"{metric_name}={value} WARNING (≥{warning})")
            telemetry_score = min(telemetry_score, 20)

        # Factor 5: Parts supply chain risk (0-10 pts)
        compatible_parts = [p for p in inventory if mtype in p.get("compatibleMachines", [])]
        parts_score = 0
        critical_parts = []
        if compatible_parts:
            for p in compatible_parts:
                qty = p.get("quantityInStock", 0)
                reorder = p.get("reorderLevel", 0)
                lead = p.get("leadTimeDays", 14)
                if qty <= reorder:
                    parts_score += 4
                    critical_parts.append(p.get("name", ""))
                elif lead >= 30 and qty <= reorder * 2:
                    parts_score += 2  # long lead time + low buffer
            parts_score = min(parts_score, 10)

        # Factor 6: Maintenance backlog (0-10 pts)
        backlog_score = 0
        machine_open_wos = [
            wo
            for wo in work_orders
            if wo.get("machineId") == mid and wo.get("status") not in ("completed", "closed")
        ]
        backlog_score = min(len(machine_open_wos) * 5, 10)

        # Bonus: machine already in degraded status
        status_bonus = 5 if m.get("status") == "maintenance_required" else 0

        total_risk = min(
            fault_score
            + hours_score
            + recency_score
            + telemetry_score
            + parts_score
            + backlog_score
            + status_bonus,
            100,
        )

        level = "low" if total_risk < 30 else "medium" if total_risk < 60 else "high"

        total_downtime = sum(h.get("downtime", 0) for h in machine_history)
        avg_repair_cost = total_cost / fault_count if fault_count > 0 else 0

        risk_scores.append(
            {
                "machineId": mid,
                "machineName": m["name"],
                "machineType": mtype,
                "riskScore": total_risk,
                "riskLevel": level,
                "factors": {
                    "faultFrequency": {
                        "score": fault_score,
                        "max": 25,
                        "faultCount": fault_count,
                        "totalCost": total_cost,
                    },
                    "operatingWear": {
                        "score": hours_score,
                        "max": 20,
                        "hours": hours,
                        "expectedLifetime": expected,
                        "wearRatio": round(wear_ratio, 2),
                    },
                    "recentFaults": {"score": recency_score, "max": 15},
                    "telemetryAnomaly": {
                        "score": telemetry_score,
                        "max": 20,
                        "anomalies": anomaly_details,
                    },
                    "partsSupplyRisk": {
                        "score": parts_score,
                        "max": 10,
                        "criticalParts": critical_parts,
                    },
                    "maintenanceBacklog": {
                        "score": backlog_score,
                        "max": 10,
                        "openWorkOrders": len(machine_open_wos),
                    },
                },
                "statusBonus": status_bonus,
                "totalDowntimeMinutes": total_downtime,
                "avgRepairCost": round(avg_repair_cost, 2),
                "recommendation": (
                    "URGENT: Schedule preventive maintenance immediately — high failure probability"
                    if level == "high"
                    else "Monitor closely — plan maintenance within next available window"
                    if level == "medium"
                    else "Normal operation — continue routine checks"
                ),
            }
        )

    return sorted(risk_scores, key=lambda x: x["riskScore"], reverse=True)


def compute_inventory_forecast() -> list[dict]:
    """Forecast inventory depletion based on historical usage."""
    inventory = get_inventory()
    work_orders = get_work_orders()

    # Calculate usage rate: parts consumed per work order
    part_usage: dict[str, int] = {}
    for wo in work_orders:
        for pu in wo.get("partsUsed", []):
            pid = pu.get("partId", "")
            qty = pu.get("quantity", 1)
            part_usage[pid] = part_usage.get(pid, 0) + qty

    forecasts = []
    for part in inventory:
        pid = part["id"]
        stock = part.get("quantityInStock", 0)
        reorder = part.get("reorderLevel", 0)
        lead_time = part.get("leadTimeDays", 14)
        total_used = part_usage.get(pid, 0)

        # Assume work orders span ~90 days worth of history
        daily_usage = total_used / 90 if total_used > 0 else 0
        days_until_depletion = stock / daily_usage if daily_usage > 0 else 999
        days_until_reorder = (stock - reorder) / daily_usage if daily_usage > 0 else 999

        urgency = (
            "critical"
            if days_until_reorder <= lead_time
            else ("warning" if days_until_reorder <= lead_time * 2 else "ok")
        )

        forecasts.append(
            {
                "partId": pid,
                "name": part["name"],
                "partNumber": part.get("partNumber", ""),
                "currentStock": stock,
                "reorderLevel": reorder,
                "dailyUsageRate": round(daily_usage, 3),
                "daysUntilDepletion": round(min(days_until_depletion, 999)),
                "daysUntilReorder": round(min(days_until_reorder, 999)),
                "leadTimeDays": lead_time,
                "reorderUrgency": urgency,
                "estimatedReorderCost": round(
                    max(0, reorder * 2 - stock) * part.get("unitCost", 0), 2
                ),
            }
        )

    return sorted(forecasts, key=lambda x: x["daysUntilReorder"])


def _parse_dt(s: str) -> datetime:
    try:
        return datetime.fromisoformat(s.replace("Z", "+00:00"))
    except (ValueError, AttributeError):
        return datetime.min.replace(tzinfo=timezone.utc)
