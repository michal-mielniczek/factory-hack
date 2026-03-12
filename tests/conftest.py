"""Root conftest – safety fixtures to prevent hitting real Azure resources."""

import pytest


@pytest.fixture(autouse=True)
def _block_azure_env(monkeypatch):
    """Prevent tests from accidentally using real Azure credentials."""
    monkeypatch.setenv("COSMOS_ENDPOINT", "https://localhost:8081")
    monkeypatch.setenv("COSMOS_KEY", "test-key-not-real")
    monkeypatch.setenv("AZURE_AI_PROJECT_ENDPOINT", "https://localhost/fake")
