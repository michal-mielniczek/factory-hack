"""Smoke tests for Challenge-0 seed data validation."""

import json
import pathlib

import pytest

DATA_DIR = pathlib.Path(__file__).resolve().parents[2] / "challenge-0" / "data"


class TestSeedDataFiles:
    """Verify that all expected seed data files exist and are valid JSON."""

    EXPECTED_FILES = [
        "machines.json",
        "maintenance-history.json",
        "maintenance-windows.json",
        "parts-inventory.json",
        "technicians.json",
        "telemetry-samples.json",
        "thresholds.json",
        "work-orders.json",
        "knowledge-base.json",
    ]

    @pytest.mark.parametrize("filename", EXPECTED_FILES)
    def test_data_file_exists(self, filename):
        assert (DATA_DIR / filename).exists(), f"Missing seed data file: {filename}"

    @pytest.mark.parametrize("filename", EXPECTED_FILES)
    def test_data_file_is_valid_json(self, filename):
        data = json.loads((DATA_DIR / filename).read_text())
        assert isinstance(data, list), f"{filename} should contain a JSON array"
        assert len(data) > 0, f"{filename} should not be empty"

    def test_machines_count(self):
        machines = json.loads((DATA_DIR / "machines.json").read_text())
        assert len(machines) == 5

    def test_technicians_count(self):
        technicians = json.loads((DATA_DIR / "technicians.json").read_text())
        assert len(technicians) == 6

    def test_telemetry_has_anomalies(self):
        telemetry = json.loads((DATA_DIR / "telemetry-samples.json").read_text())
        warnings = [t for t in telemetry if t.get("status") == "warning"]
        assert len(warnings) >= 3, "Expected at least 3 telemetry samples with warning status"

    def test_kb_wiki_files_exist(self):
        wiki_dir = DATA_DIR / "kb-wiki"
        assert wiki_dir.exists()
        md_files = list(wiki_dir.glob("*.md"))
        assert len(md_files) == 5, f"Expected 5 wiki files, found {len(md_files)}"
