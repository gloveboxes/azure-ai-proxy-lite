#!/usr/bin/env python3
"""
Generate attendees for events in Azure Table Storage.

Usage:
    # For a specific event:
    python generate_attendees.py <storage_account_name> <event_id> [--num-attendees N]

    # For all events:
    python generate_attendees.py <storage_account_name> --all-events [--num-attendees N]

Examples:
    python generate_attendees.py aiprxy10jhyrnwu5tnnbkst 0d54-dc71 --num-attendees 1000
    python generate_attendees.py aiprxy10jhyrnwu5tnnbkst --all-events --num-attendees 500
"""

import argparse
import uuid
import json
from datetime import datetime, timezone, timedelta
from concurrent.futures import ThreadPoolExecutor, as_completed

from azure.data.tables import TableServiceClient, UpdateMode
from azure.identity import DefaultAzureCredential


def create_attendee_for_event(attendees_table, lookup_table, event_id, event, index):
    """Create a single attendee and lookup entry for an event."""
    user_id = f"user-{uuid.uuid4().hex[:12]}"
    api_key = str(uuid.uuid4())

    # Extract event details
    event_code = event.get("EventCode", "")
    organizer_name = event.get("OrganizerName", "")
    organizer_email = event.get("OrganizerEmail", "")
    max_token_cap = event.get("MaxTokenCap", 4096)
    daily_request_cap = event.get("DailyRequestCap", 100000)
    event_active = event.get("Active", True)
    start_timestamp = event.get("StartTimestamp")
    end_timestamp = event.get("EndTimestamp")
    time_zone_offset = event.get("TimeZoneOffset", 0)

    # Create attendee entry
    attendees_table.upsert_entity(
        {
            "PartitionKey": event_id,
            "RowKey": user_id,
            "ApiKey": api_key,
            "Active": True,
        },
        mode=UpdateMode.REPLACE,
    )

    # Create lookup entry (for fast API key lookup)
    # Partition key is first 2 chars of API key for distribution
    lookup_table.upsert_entity(
        {
            "PartitionKey": api_key[:2].lower(),
            "RowKey": api_key,
            "EventId": event_id,
            "UserId": user_id,
            "Active": True,
            "EventCode": event_code,
            "OrganizerName": organizer_name,
            "OrganizerEmail": organizer_email,
            "MaxTokenCap": max_token_cap,
            "DailyRequestCap": daily_request_cap,
            "EventActive": event_active,
            "StartTimestamp": start_timestamp,
            "EndTimestamp": end_timestamp,
            "TimeZoneOffset": time_zone_offset,
        },
        mode=UpdateMode.REPLACE,
    )

    return api_key


def generate_attendees_for_event(
    events_table,
    attendees_table,
    lookup_table,
    event_id: str,
    num_attendees: int = 500,
    skip_existing: bool = True,
):
    """Generate attendees for a specific event."""

    # Verify event exists and get event details
    try:
        event = events_table.get_entity(partition_key=event_id, row_key=event_id)
    except Exception as e:
        print(f"  ✗ Event '{event_id}' not found: {e}")
        return None

    event_code = event.get('EventCode', 'Unknown')

    # Check for existing attendees
    if skip_existing:
        try:
            existing = list(attendees_table.query_entities(
                query_filter=f"PartitionKey eq '{event_id}'"
            ))
            existing_count = len(existing)

            if existing_count >= num_attendees:
                print(f"  ⊘ Skipping {event_code} ({event_id}) - already has {existing_count} attendees")
                return {
                    "event_id": event_id,
                    "event_code": event_code,
                    "count": 0,
                    "skipped": True,
                    "existing_count": existing_count,
                    "api_keys": []
                }
            elif existing_count > 0:
                remaining = num_attendees - existing_count
                print(f"  ⟳ Resuming {event_code} ({event_id}) - has {existing_count}, creating {remaining} more")
                num_attendees = remaining
            else:
                print(f"  Processing event: {event_code} ({event_id})")
        except Exception as e:
            print(f"  Processing event: {event_code} ({event_id}) - couldn't check existing: {e}")
    else:
        print(f"  Processing event: {event_code} ({event_id})")

    generated_keys = []

    # Create attendees in parallel for speed
    with ThreadPoolExecutor(max_workers=20) as executor:
        futures = [
            executor.submit(create_attendee_for_event, attendees_table, lookup_table, event_id, event, i)
            for i in range(num_attendees)
        ]

        for future in as_completed(futures):
            try:
                api_key = future.result()
                generated_keys.append(api_key)
            except Exception as e:
                print(f"    Error creating attendee: {e}")

    print(f"  ✓ Created {len(generated_keys)} attendees for {event_id}")

    return {
        "event_id": event_id,
        "event_code": event_code,
        "count": len(generated_keys),
        "skipped": False,
        "api_keys": generated_keys
    }


def generate_attendees(
    storage_account: str,
    event_id: str = None,
    all_events: bool = False,
    num_attendees: int = 500,
    event_workers: int = 10,
    connection_string: str = "",
):
    """Generate attendees for events."""

    # Connect to Azure Table Storage
    if connection_string:
        service = TableServiceClient.from_connection_string(connection_string)
    else:
        credential = DefaultAzureCredential()
        service_url = f"https://{storage_account}.table.core.windows.net"
        service = TableServiceClient(endpoint=service_url, credential=credential)

    # Get table clients
    events_table = service.get_table_client("events")
    attendees_table = service.get_table_client("attendees")
    lookup_table = service.get_table_client("attendeelookup")

    all_results = []

    if all_events:
        # Get all events from the events table
        print(f"Fetching all events from storage...")
        events = list(events_table.list_entities())
        print(f"Found {len(events)} events")
        print(f"\nGenerating {num_attendees} attendees for each event (skipping events that already have attendees)...\n")
        print(f"Processing events in parallel with {event_workers} concurrent workers...\n")

        skipped_count = 0
        processed_count = 0
        completed_count = 0

        # Process events in parallel
        with ThreadPoolExecutor(max_workers=event_workers) as executor:
            # Submit all event processing tasks
            future_to_event = {
                executor.submit(
                    generate_attendees_for_event,
                    events_table, attendees_table, lookup_table,
                    event.get("RowKey"), num_attendees, True
                ): (idx, event.get("RowKey"))
                for idx, event in enumerate(events, 1)
            }

            # Process results as they complete
            for future in as_completed(future_to_event):
                idx, evt_id = future_to_event[future]
                completed_count += 1

                try:
                    result = future.result()
                    if result:
                        all_results.append(result)
                        if result.get("skipped", False):
                            skipped_count += 1
                        else:
                            processed_count += 1

                    # Progress indicator
                    if completed_count % 50 == 0:
                        print(f"\n[Progress: {completed_count}/{len(events)} events completed, {processed_count} processed, {skipped_count} skipped]\n")

                except Exception as e:
                    print(f"  ✗ Error processing event {evt_id}: {e}")

        # Save summary
        output_file = f"attendees_all_events.json"
        total_attendees = sum(r["count"] for r in all_results)

        with open(output_file, "w") as f:
            json.dump({
                "generated_at": datetime.now(timezone.utc).isoformat(),
                "total_events": len(all_results),
                "events_processed": processed_count,
                "events_skipped": skipped_count,
                "total_attendees_created": total_attendees,
                "attendees_per_event": num_attendees,
                "events": all_results
            }, f, indent=2)

        print(f"\n" + "="*60)
        print(f"✓ Successfully completed!")
        print(f"  Events processed: {processed_count}")
        print(f"  Events skipped: {skipped_count}")
        print(f"  Total attendees created: {total_attendees}")
        print(f"✓ Summary saved to {output_file}")
        print("="*60)

    else:
        # Single event mode
        if not event_id:
            print("Error: event_id required when not using --all-events")
            return

        print(f"Generating up to {num_attendees} attendees for event {event_id}...\n")
        result = generate_attendees_for_event(
            events_table, attendees_table, lookup_table, event_id, num_attendees, skip_existing=False
        )

        if result:
            # Save API keys to file
            output_file = f"attendees_{event_id}.json"
            with open(output_file, "w") as f:
                json.dump({
                    "event_id": result["event_id"],
                    "event_code": result["event_code"],
                    "generated_at": datetime.now(timezone.utc).isoformat(),
                    "count": result["count"],
                    "api_keys": result["api_keys"]
                }, f, indent=2)

            print(f"\n✓ Successfully generated {result['count']} attendees for event {event_id}")
            print(f"✓ API keys saved to {output_file}")


def main():
    parser = argparse.ArgumentParser(
        description="Generate attendees for Azure OpenAI Proxy events"
    )
    parser.add_argument(
        "storage_account",
        help="Azure Storage account name"
    )
    parser.add_argument(
        "event_id",
        nargs="?",
        help="Event ID to generate attendees for (e.g., 0d54-dc71). Not required with --all-events"
    )
    parser.add_argument(
        "--all-events",
        action="store_true",
        help="Generate attendees for all events in the events table"
    )
    parser.add_argument(
        "--num-attendees",
        type=int,
        default=500,
        help="Number of attendees to generate per event (default: 500)"
    )
    parser.add_argument(
        "--event-workers",
        type=int,
        default=10,
        help="Number of concurrent workers for processing events (default: 10, only used with --all-events)"
    )
    parser.add_argument(
        "--connection-string",
        default="",
        help="Azure Storage connection string (optional, uses DefaultAzureCredential if not provided)"
    )

    args = parser.parse_args()

    generate_attendees(
        storage_account=args.storage_account,
        event_id=args.event_id,
        all_events=args.all_events,
        num_attendees=args.num_attendees,
        event_workers=args.event_workers,
        connection_string=args.connection_string,
    )


if __name__ == "__main__":
    main()
