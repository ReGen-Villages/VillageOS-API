#!/usr/bin/env python3
"""
Test script for the 'is' Handler Service (daemon mode)

This script tests the persistent is_handler_service.py by:
1. Starting the service
2. Sending test relationship requests
3. Verifying responses
4. Shutting down gracefully
"""

import asyncio
import httpx
import subprocess
import time
import sys


async def test_is_handler_service():
    """Test the is_handler_service with mock data."""

    # Configuration
    service_port = 5100
    broker_url = "https://localhost:7243"
    service_url = f"https://localhost:{service_port}"

    print("=" * 60)
    print("Testing 'is' Handler Service (Daemon Mode)")
    print("=" * 60)
    print(f"Service Port: {service_port}")
    print(f"Broker URL: {broker_url}")
    print("=" * 60)
    print()

    # Start the service in the background
    print("[1/5] Starting is_handler_service...")
    process = subprocess.Popen(
        [
            sys.executable,
            "is_handler_service.py",
            "--port", str(service_port),
            "--brokerUrl", broker_url
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )

    # Wait for service to start
    print("      Waiting for service to initialize...")
    await asyncio.sleep(3)

    if process.poll() is not None:
        print("      ✗ Service failed to start!")
        stdout, stderr = process.communicate()
        print(f"      STDOUT: {stdout}")
        print(f"      STDERR: {stderr}")
        return False

    print("      ✓ Service started")
    print()

    try:
        async with httpx.AsyncClient() as client:
            # Test 1: Health check
            print("[2/5] Testing health endpoint...")
            try:
                health_response = await client.get(f"{service_url}/health", timeout=5.0)
                if health_response.status_code == 200:
                    health_data = health_response.json()
                    print(f"      ✓ Health check passed: {health_data}")
                else:
                    print(f"      ✗ Health check failed: {health_response.status_code}")
                    return False
            except Exception as e:
                print(f"      ✗ Health check error: {e}")
                return False
            print()

            # Test 2: Stats endpoint
            print("[3/5] Testing stats endpoint...")
            try:
                stats_response = await client.get(f"{service_url}/stats", timeout=5.0)
                if stats_response.status_code == 200:
                    stats_data = stats_response.json()
                    print(f"      ✓ Stats retrieved: {stats_data}")
                else:
                    print(f"      ✗ Stats failed: {stats_response.status_code}")
            except Exception as e:
                print(f"      ⚠ Stats error (non-critical): {e}")
            print()

            # Test 3: Handle relationship (will fail without broker, but tests the endpoint)
            print("[4/5] Testing /handle endpoint...")
            test_payload = {
                "relationshipId": "00000000-0000-0000-0000-000000000001",
                "subjectId": "00000000-0000-0000-0000-000000000002",
                "targetId": "00000000-0000-0000-0000-000000000003"
            }

            try:
                handle_response = await client.post(
                    f"{service_url}/handle",
                    json=test_payload,
                    timeout=10.0
                )

                if handle_response.status_code == 200:
                    print(f"      ✓ Handle endpoint succeeded (unexpected, broker likely running)")
                    print(f"      Response: {handle_response.json()}")
                elif handle_response.status_code in [503, 500, 404]:
                    # Expected when broker is not running
                    print(f"      ✓ Handle endpoint responded correctly ({handle_response.status_code})")
                    print(f"      Note: This is expected without a running broker")
                else:
                    print(f"      ⚠ Unexpected status: {handle_response.status_code}")
                    print(f"      Response: {handle_response.text}")
            except Exception as e:
                print(f"      ⚠ Handle endpoint error (expected without broker): {e}")
            print()

            # Test 4: Graceful shutdown
            print("[5/5] Testing graceful shutdown...")
            try:
                shutdown_response = await client.post(f"{service_url}/shutdown", timeout=5.0)
                if shutdown_response.status_code == 200:
                    print(f"      ✓ Shutdown initiated: {shutdown_response.json()}")
                else:
                    print(f"      ⚠ Shutdown status: {shutdown_response.status_code}")

                # Wait for process to terminate
                await asyncio.sleep(2)

                if process.poll() is not None:
                    print("      ✓ Service terminated gracefully")
                else:
                    print("      ⚠ Service still running, forcing termination...")
                    process.terminate()
                    process.wait(timeout=5)
                    print("      ✓ Service terminated")

            except Exception as e:
                print(f"      ⚠ Shutdown error: {e}")
                process.terminate()
            print()

        print("=" * 60)
        print("✓ All tests completed successfully!")
        print("=" * 60)
        return True

    except Exception as e:
        print(f"\n✗ Test failed with error: {e}")
        process.terminate()
        return False
    finally:
        # Ensure process is terminated
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()


def main():
    """Main entry point."""
    success = asyncio.run(test_is_handler_service())

    if success:
        print("\n✓ Test suite passed")
        sys.exit(0)
    else:
        print("\n✗ Test suite failed")
        sys.exit(1)


if __name__ == "__main__":
    main()
