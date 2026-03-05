#!/usr/bin/env python3
"""
Validation script: After a load test, checks that metrics and rate limit data
in Table Storage match expectations.

Usage:
    python validate.py <storage_account_name> [--keys-file loadtest/api_keys.json]
"""

import argparse
import json

from azure.data.tables import TableServiceClient
from azure.identity import DefaultAzureCredential


def to_int(val):
    if isinstance(val, int):
        return val
    if hasattr(val, 'value'):
        return int(val.value)
    return int(val)


def validate(storage_account: str, keys_file: str):
    credential = DefaultAzureCredential()
    service_url = f"https://{storage_account}.table.core.windows.net"
    service = TableServiceClient(endpoint=service_url, credential=credential)

    with open(keys_file) as f:
        data = json.load(f)

    event_ids = data["event_ids"]
    api_keys = data["api_keys"]

    print("=" * 60)
    print("VALIDATION")
    print("=" * 60)

    # Check metrics
    metrics_table = service.get_table_client("metrics")
    total_metric_requests = 0
    total_metric_tokens = 0
    print("\nMetrics per event:")
    for event_id in event_ids:
        event_requests = 0
        event_tokens = 0
        for entity in metrics_table.query_entities(f"PartitionKey eq '{event_id}'"):
            event_requests += to_int(entity.get("RequestCount", 0))
            event_tokens += to_int(entity.get("TotalTokens", 0))
        if event_requests > 0:
            print(f"  {event_id}: {event_requests} requests, {event_tokens} tokens")
        total_metric_requests += event_requests
        total_metric_tokens += event_tokens

    print(f"\n  Total metric requests: {total_metric_requests}")
    print(f"  Total metric tokens:   {total_metric_tokens}")

    # Check attendee request counts
    requests_table = service.get_table_client("attendeerequests")
    total_rate_limit_requests = 0
    active_keys = 0
    for entity in requests_table.list_entities():
        total_rate_limit_requests += to_int(entity.get("RequestCount", 0))
        active_keys += 1

    print(f"\n  Rate limit entries: {active_keys} keys")
    print(f"  Total rate limit requests: {total_rate_limit_requests}")

    # Compare
    print(f"\n  Metric vs rate limit delta: {abs(total_metric_requests - total_rate_limit_requests)}")

    if total_metric_requests == total_rate_limit_requests:
        print("  ✓ Counts match perfectly")
    elif abs(total_metric_requests - total_rate_limit_requests) <= 10:
        print("  ✓ Counts match within tolerance (background flush timing)")
    else:
        print("  ✗ Significant mismatch — investigate background flush or rate limit logic")

    # Load test results if available
    try:
        with open("loadtest/results.json") as f:
            results = json.load(f)
        expected = results.get("success_count", 0)
        print(f"\n  Load test reported {expected} successful requests")
        print(f"  Metric table recorded {total_metric_requests} requests")
        delta = abs(expected - total_metric_requests)
        if delta == 0:
            print("  ✓ Perfect match with load test results")
        elif delta <= 20:
            print(f"  ✓ Within tolerance (delta: {delta}, likely flush timing)")
        else:
            print(f"  ✗ Mismatch of {delta} — possible metric loss under load")
    except FileNotFoundError:
        print("\n  (No loadtest/results.json found — skipping comparison)")

    # Check partition spread on attendeelookup
    lookup_table = service.get_table_client("attendeelookup")
    partitions = {}
    for entity in lookup_table.list_entities():
        pk = entity["PartitionKey"]
        partitions[pk] = partitions.get(pk, 0) + 1

    print(f"\n  Lookup partition spread: {len(partitions)} partitions")
    if partitions:
        max_pk = max(partitions.values())
        min_pk = min(partitions.values())
        avg_pk = sum(partitions.values()) / len(partitions)
        print(f"  Min/Avg/Max per partition: {min_pk}/{avg_pk:.1f}/{max_pk}")
        if max_pk > avg_pk * 3 and len(partitions) > 5:
            print("  ⚠ Uneven partition distribution")
        else:
            print("  ✓ Good partition distribution")

    print("=" * 60)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Validate load test results against Table Storage")
    parser.add_argument("storage_account", help="Azure Storage account name")
    parser.add_argument("--keys-file", default="loadtest/api_keys.json", help="Path to API keys JSON file")
    args = parser.parse_args()

    validate(args.storage_account, args.keys_file)
