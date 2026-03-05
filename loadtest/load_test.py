#!/usr/bin/env python3
"""
Load test script: Blasts the proxy with concurrent requests using seeded API keys.
Requires the proxy to have UseMockProxy=true so requests don't hit real Azure OpenAI.

Usage:
    python load_test.py <proxy_url> [--concurrency N] [--requests-per-key M] [--duration-seconds S]

Example:
    python load_test.py https://myproxy.azurecontainerapps.io --concurrency 50 --duration-seconds 60
"""

import argparse
import asyncio
import json
import random
import time
from collections import Counter
from dataclasses import dataclass, field

import aiohttp


@dataclass
class LoadTestResults:
    total_requests: int = 0
    success_count: int = 0
    error_429: int = 0
    error_500: int = 0
    error_other: int = 0
    status_codes: Counter = field(default_factory=Counter)
    latencies: list = field(default_factory=list)
    start_time: float = 0
    end_time: float = 0

    @property
    def duration(self) -> float:
        return self.end_time - self.start_time

    @property
    def rps(self) -> float:
        return self.total_requests / self.duration if self.duration > 0 else 0

    @property
    def p50(self) -> float:
        if not self.latencies:
            return 0
        s = sorted(self.latencies)
        return s[len(s) // 2]

    @property
    def p95(self) -> float:
        if not self.latencies:
            return 0
        s = sorted(self.latencies)
        return s[int(len(s) * 0.95)]

    @property
    def p99(self) -> float:
        if not self.latencies:
            return 0
        s = sorted(self.latencies)
        return s[int(len(s) * 0.99)]


async def send_request(
    session: aiohttp.ClientSession,
    url: str,
    api_key: str,
    deployment: str,
    results: LoadTestResults,
):
    endpoint = f"{url}/api/v1/openai/deployments/{deployment}/chat/completions?api-version=2023-12-01-preview"
    payload = {
        "messages": [{"role": "user", "content": "Hello, this is a load test."}],
        "max_tokens": 100,
    }
    headers = {"api-key": api_key, "Content-Type": "application/json"}

    start = time.monotonic()
    try:
        async with session.post(endpoint, json=payload, headers=headers) as resp:
            elapsed = time.monotonic() - start
            results.total_requests += 1
            results.latencies.append(elapsed)
            results.status_codes[resp.status] += 1

            if resp.status == 200:
                results.success_count += 1
            elif resp.status == 429:
                results.error_429 += 1
            elif resp.status >= 500:
                results.error_500 += 1
            else:
                results.error_other += 1
    except Exception as e:
        elapsed = time.monotonic() - start
        results.total_requests += 1
        results.latencies.append(elapsed)
        results.error_other += 1


async def worker(
    session: aiohttp.ClientSession,
    url: str,
    api_keys: list,
    deployments: list,
    results: LoadTestResults,
    stop_event: asyncio.Event,
):
    while not stop_event.is_set():
        api_key = random.choice(api_keys)
        deployment = random.choice(deployments)
        await send_request(session, url, api_key, deployment, results)


async def run_load_test(
    proxy_url: str,
    concurrency: int,
    duration_seconds: int,
    api_keys: list,
    deployments: list,
):
    results = LoadTestResults()
    stop_event = asyncio.Event()

    connector = aiohttp.TCPConnector(limit=concurrency, limit_per_host=concurrency)
    timeout = aiohttp.ClientTimeout(total=30)

    print(f"Starting load test:")
    print(f"  Target: {proxy_url}")
    print(f"  Concurrency: {concurrency}")
    print(f"  Duration: {duration_seconds}s")
    print(f"  API keys: {len(api_keys)}")
    print(f"  Deployments: {deployments}")
    print()

    async with aiohttp.ClientSession(connector=connector, timeout=timeout) as session:
        results.start_time = time.monotonic()

        workers = [
            asyncio.create_task(worker(session, proxy_url, api_keys, deployments, results, stop_event))
            for _ in range(concurrency)
        ]

        # Progress reporting
        async def report_progress():
            last_count = 0
            while not stop_event.is_set():
                await asyncio.sleep(5)
                current = results.total_requests
                delta = current - last_count
                last_count = current
                elapsed = time.monotonic() - results.start_time
                print(
                    f"  [{elapsed:.0f}s] {current} requests "
                    f"({delta / 5:.0f} rps) | "
                    f"2xx: {results.success_count} | "
                    f"429: {results.error_429} | "
                    f"5xx: {results.error_500}"
                )

        reporter = asyncio.create_task(report_progress())

        await asyncio.sleep(duration_seconds)
        stop_event.set()

        await asyncio.gather(*workers, return_exceptions=True)
        reporter.cancel()

        results.end_time = time.monotonic()

    return results


def print_results(results: LoadTestResults):
    print("\n" + "=" * 60)
    print("LOAD TEST RESULTS")
    print("=" * 60)
    print(f"  Duration:          {results.duration:.1f}s")
    print(f"  Total requests:    {results.total_requests}")
    print(f"  Requests/sec:      {results.rps:.1f}")
    print(f"  Success (2xx):     {results.success_count}")
    print(f"  Rate limited (429):{results.error_429}")
    print(f"  Server error (5xx):{results.error_500}")
    print(f"  Other errors:      {results.error_other}")
    print()
    print(f"  Latency p50:       {results.p50 * 1000:.0f}ms")
    print(f"  Latency p95:       {results.p95 * 1000:.0f}ms")
    print(f"  Latency p99:       {results.p99 * 1000:.0f}ms")
    print()
    print("  Status codes:")
    for code, count in sorted(results.status_codes.items()):
        print(f"    {code}: {count}")
    print("=" * 60)

    # Write results to file
    output = {
        "duration_seconds": results.duration,
        "total_requests": results.total_requests,
        "requests_per_second": results.rps,
        "success_count": results.success_count,
        "error_429": results.error_429,
        "error_500": results.error_500,
        "latency_p50_ms": results.p50 * 1000,
        "latency_p95_ms": results.p95 * 1000,
        "latency_p99_ms": results.p99 * 1000,
        "status_codes": dict(results.status_codes),
    }
    with open("loadtest/results.json", "w") as f:
        json.dump(output, f, indent=2)
    print(f"\nResults written to loadtest/results.json")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Load test the AI proxy")
    parser.add_argument("proxy_url", help="Proxy base URL")
    parser.add_argument("--concurrency", type=int, default=50, help="Concurrent workers (default: 50)")
    parser.add_argument("--duration-seconds", type=int, default=60, help="Test duration in seconds (default: 60)")
    parser.add_argument("--keys-file", default="loadtest/api_keys.json", help="Path to API keys JSON file")
    args = parser.parse_args()

    with open(args.keys_file) as f:
        data = json.load(f)

    api_keys = data["api_keys"]
    deployments = data.get("deployment_names", ["gpt-4-0"])

    results = asyncio.run(
        run_load_test(args.proxy_url, args.concurrency, args.duration_seconds, api_keys, deployments)
    )

    print_results(results)
