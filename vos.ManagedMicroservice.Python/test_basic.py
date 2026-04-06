"""
Basic tests for the VillageOS ManagedMicroservice (Python)

Run with: pytest test_basic.py
"""

import pytest
from fastapi.testclient import TestClient
from unittest.mock import patch, AsyncMock
import uuid

# Import the app (but we'll mock the lifespan to avoid broker calls during testing)
from app import app, handler_id, broker_url_stored


@pytest.fixture
def client():
    """Create a test client with mocked lifespan."""
    # Mock the lifespan to avoid actual broker registration during tests
    with patch('app.register_with_broker', new=AsyncMock()):
        with patch('app.deregister_from_broker', new=AsyncMock()):
            with TestClient(app) as c:
                yield c


def test_health_endpoint(client):
    """Test the health endpoint returns 200 OK with correct status."""
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "Healthy"}


def test_main_endpoint_with_json(client):
    """Test the main endpoint echoes JSON payload."""
    test_data = {
        "operation": "test",
        "data": "value",
        "number": 42
    }

    response = client.post("/", json=test_data)

    assert response.status_code == 200
    json_response = response.json()

    assert "handledBy" in json_response
    assert json_response["handledBy"] == "VillageOS.ManagedMicroservice.Python"
    assert "payload" in json_response
    assert json_response["payload"] == test_data
    assert "timestamp" in json_response


def test_main_endpoint_without_json(client):
    """Test the main endpoint handles requests without JSON body."""
    response = client.post("/")

    assert response.status_code == 200
    json_response = response.json()

    assert "handledBy" in json_response
    assert "payload" in json_response
    # Payload should be None when no JSON is sent
    assert json_response["payload"] is None


def test_shutdown_endpoint(client):
    """Test the shutdown endpoint returns correct response."""
    response = client.post("/shutdown")

    assert response.status_code == 200
    assert response.json() == {"message": "Shutting down"}


def test_api_documentation_available(client):
    """Test that OpenAPI documentation endpoints are available."""
    # Swagger UI
    response = client.get("/docs")
    assert response.status_code == 200

    # ReDoc
    response = client.get("/redoc")
    assert response.status_code == 200

    # OpenAPI JSON schema
    response = client.get("/openapi.json")
    assert response.status_code == 200
    assert "openapi" in response.json()


def test_demo_endpoint_format(client):
    """Test that the demo endpoint has correct path parameter format."""
    # Verify the route exists by checking the OpenAPI spec
    response = client.get("/openapi.json")
    assert response.status_code == 200
    openapi = response.json()

    # Check that the demo endpoint is registered with correct path
    paths = openapi.get("paths", {})
    demo_path = "/demo/vos/{thing_id}"
    assert demo_path in paths, f"Expected {demo_path} in OpenAPI paths"
    assert "post" in paths[demo_path], "Expected POST method on demo endpoint"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
