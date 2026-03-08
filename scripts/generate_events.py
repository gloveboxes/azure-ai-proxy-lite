#!/usr/bin/env python3
"""
Generate events in Azure Table Storage.

Usage:
    python generate_events.py <storage_account_name> [--num-events N] [--owner-id OWNER]

Example:
    python generate_events.py aiprxy10jhyrnwu5tnnbkst --num-events 1000 --owner-id admin
"""

import argparse
import hashlib
import uuid
from datetime import datetime, timezone, timedelta
from concurrent.futures import ThreadPoolExecutor, as_completed

from azure.data.tables import TableServiceClient, UpdateMode
from azure.identity import DefaultAzureCredential


def generate_event_id() -> str:
    """Generate a unique event ID in format: xxxx-xxxx"""
    guid_string = f"{uuid.uuid4()}{uuid.uuid4()}"
    hash_hex = hashlib.sha256(guid_string.encode("utf-8")).hexdigest()
    return f"{hash_hex[:4]}-{hash_hex[4:8]}"


def generate_events(
    storage_account: str,
    num_events: int = 1000,
    owner_id: str = "admin",
    connection_string: str = "",
):
    """Generate events for the owner."""

    # Connect to Azure Table Storage
    if connection_string:
        service = TableServiceClient.from_connection_string(connection_string)
    else:
        credential = DefaultAzureCredential()
        service_url = f"https://{storage_account}.table.core.windows.net"
        service = TableServiceClient(endpoint=service_url, credential=credential)

    # Get table clients
    events_table = service.get_table_client("events")
    owner_events_table = service.get_table_client("ownerevents")
    owners_table = service.get_table_client("owners")

    # Ensure owner exists
    try:
        owner = owners_table.get_entity("owner", owner_id)
        print(f"Found owner: {owner.get('Name', owner_id)}")
    except Exception:
        print(f"Owner '{owner_id}' not found, creating...")
        owners_table.upsert_entity(
            {
                "PartitionKey": "owner",
                "RowKey": owner_id,
                "Name": owner_id,
                "Email": f"{owner_id}@example.com"
            },
            mode=UpdateMode.REPLACE,
        )
        print(f"✓ Created owner '{owner_id}'")

    print(f"\nGenerating {num_events} events for owner '{owner_id}'...")

    generated_events = []
    now = datetime.now(timezone.utc)

    def create_event(index):
        """Create a single event."""
        event_id = generate_event_id()

        # Create event
        events_table.upsert_entity(
            {
                "PartitionKey": event_id,
                "RowKey": event_id,
                "OwnerId": owner_id,
                "EventCode": f"Event {index + 1}",
                "EventMarkdown": f"This is test event number {index + 1}",
                "StartTimestamp": (now - timedelta(days=1)),
                "EndTimestamp": (now + timedelta(days=30)),
                "TimeZoneOffset": 0,
                "TimeZoneLabel": "UTC",
                "OrganizerName": owner_id,
                "OrganizerEmail": f"{owner_id}@example.com",
                "MaxTokenCap": 4096,
                "DailyRequestCap": 10000,
                "Active": True,
                "EventImageUrl": None,
                "EventSharedCode": None,
            },
            mode=UpdateMode.REPLACE,
        )

        # Create owner-event mapping
        owner_events_table.upsert_entity(
            {
                "PartitionKey": owner_id,
                "RowKey": event_id,
                "Creator": True
            },
            mode=UpdateMode.REPLACE,
        )

        return event_id

    # Create events in parallel for speed
    with ThreadPoolExecutor(max_workers=20) as executor:
        futures = [executor.submit(create_event, i) for i in range(num_events)]

        completed = 0
        for future in as_completed(futures):
            try:
                event_id = future.result()
                generated_events.append(event_id)
                completed += 1

                # Progress indicator every 100 events
                if completed % 100 == 0:
                    print(f"  Created {completed}/{num_events} events...")
            except Exception as e:
                print(f"Error creating event: {e}")

    print(f"\n✓ Successfully generated {len(generated_events)} events for owner '{owner_id}'")

    # Save event IDs to file
    import json
    output_file = f"events_{owner_id}.json"
    with open(output_file, "w") as f:
        json.dump({
            "owner_id": owner_id,
            "generated_at": datetime.now(timezone.utc).isoformat(),
            "count": len(generated_events),
            "event_ids": generated_events
        }, f, indent=2)

    print(f"✓ Event IDs saved to {output_file}")


def main():
    parser = argparse.ArgumentParser(
        description="Generate events for Azure OpenAI Proxy"
    )
    parser.add_argument(
        "storage_account",
        help="Azure Storage account name"
    )
    parser.add_argument(
        "--num-events",
        type=int,
        default=1000,
        help="Number of events to generate (default: 1000)"
    )
    parser.add_argument(
        "--owner-id",
        default="admin",
        help="Owner ID for the events (default: admin)"
    )
    parser.add_argument(
        "--connection-string",
        default="",
        help="Azure Storage connection string (optional, uses DefaultAzureCredential if not provided)"
    )

    args = parser.parse_args()

    generate_events(
        storage_account=args.storage_account,
        num_events=args.num_events,
        owner_id=args.owner_id,
        connection_string=args.connection_string,
    )


if __name__ == "__main__":
    main()
