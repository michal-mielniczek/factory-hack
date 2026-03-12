"""Shared test fixtures for factory-hack."""

import pytest


@pytest.fixture(autouse=True)
def _set_test_env(monkeypatch):
    """Ensure tests never accidentally hit real Azure resources."""
    monkeypatch.setenv("COSMOS_ENDPOINT", "https://test.documents.azure.com:443/")
    monkeypatch.setenv("COSMOS_KEY", "dGVzdC1rZXk=")
    monkeypatch.setenv("COSMOS_DATABASE_NAME", "TestDB")
    monkeypatch.setenv("AZURE_AI_PROJECT_ENDPOINT", "https://test.services.ai.azure.com/")
    monkeypatch.setenv("MODEL_DEPLOYMENT_NAME", "gpt-4o-test")
