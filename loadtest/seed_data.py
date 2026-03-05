#!/usr/bin/env python3
"""
Seed script: Populates Azure Table Storage with test data for load testing.

Usage:
    python seed_data.py <storage_account_name> <encryption_key> [--events N] [--catalogs-per-event M] [--attendees-per-event K]

Example:
    python seed_data.py aiprxy10jhyrnwu5tnnbkst "my-encryption-key" --events 10 --catalogs-per-event 3 --attendees-per-event 50
"""

import argparse
import hashlib
import json
import os
import uuid
from datetime import datetime, timezone, timedelta
from base64 import b64encode

from azure.data.tables import TableServiceClient, UpdateMode
from azure.identity import DefaultAzureCredential

# AES encryption compatible with the C# EncryptionService
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives import padding as sym_padding


def encrypt_value(plain_text: str, encryption_key: str) -> str:
    """Encrypt a value using AES-256-CBC, compatible with C# EncryptionService."""
    key = hashlib.sha256(encryption_key.encode("utf-8")).digest()
    iv = os.urandom(16)
    padder = sym_padding.PKCS7(128).padder()
    padded_data = padder.update(plain_text.encode("utf-8")) + padder.finalize()
    cipher = Cipher(algorithms.AES(key), modes.CBC(iv))
    encryptor = cipher.encryptor()
    cipher_bytes = encryptor.update(padded_data) + encryptor.finalize()
    return b64encode(iv + cipher_bytes).decode("utf-8")


def generate_event_id() -> str:
    guid_string = f"{uuid.uuid4()}{uuid.uuid4()}"
    hash_hex = hashlib.sha256(guid_string.encode("utf-8")).hexdigest()
    return f"{hash_hex[:4]}-{hash_hex[4:8]}"


def seed(
    storage_account: str,
    encryption_key: str,
    num_events: int,
    catalogs_per_event: int,
    attendees_per_event: int,
    owner_id: str = "admin",
    connection_string: str = "",
):
    if connection_string:
        service = TableServiceClient.from_connection_string(connection_string)
    else:
        credential = DefaultAzureCredential()
        service_url = f"https://{storage_account}.table.core.windows.net"
        service = TableServiceClient(endpoint=service_url, credential=credential)

    events_table = service.get_table_client("events")
    catalogs_table = service.get_table_client("catalogs")
    attendees_table = service.get_table_client("attendees")
    lookup_table = service.get_table_client("attendeelookup")
    owner_events_table = service.get_table_client("ownerevents")
    owners_table = service.get_table_client("owners")

    # Ensure owner exists
    try:
        owners_table.get_entity("owner", owner_id)
    except Exception:
        owners_table.upsert_entity(
            {"PartitionKey": "owner", "RowKey": owner_id, "Name": owner_id, "Email": f"{owner_id}@test"},
            mode=UpdateMode.REPLACE,
        )
    print(f"Owner '{owner_id}' ready")

    all_api_keys = []
    all_event_ids = []

    # Create catalogs (shared across events)
    model_types = ["openai-chat", "openai-embedding", "openai-dalle3", "openai-completion"]
    deployment_names = [f"gpt-4-{i}" for i in range(catalogs_per_event * 2)]
    catalog_ids = []

    print(f"Creating {catalogs_per_event * 2} catalogs...")
    for i in range(catalogs_per_event * 2):
        catalog_id = str(uuid.uuid4())
        catalog_ids.append(catalog_id)
        catalogs_table.upsert_entity(
            {
                "PartitionKey": catalog_id,
                "RowKey": catalog_id,
                "OwnerId": owner_id,
                "DeploymentName": deployment_names[i % len(deployment_names)],
                "Active": True,
                "ModelType": model_types[i % len(model_types)],
                "Location": "Central US",
                "FriendlyName": f"Test Model {i}",
                "EncryptedEndpointUrl": encrypt_value("https://fake-endpoint.openai.azure.com", encryption_key),
                "EncryptedEndpointKey": encrypt_value("fake-api-key-12345", encryption_key),
            },
            mode=UpdateMode.REPLACE,
        )

    print(f"Creating {num_events} events with {attendees_per_event} attendees each...")
    for e in range(num_events):
        event_id = generate_event_id()
        all_event_ids.append(event_id)

        # Assign random subset of catalogs to this event
        import random
        event_catalog_ids = random.sample(catalog_ids, min(catalogs_per_event, len(catalog_ids)))

        now = datetime.now(timezone.utc)
        events_table.upsert_entity(
            {
                "PartitionKey": event_id,
                "RowKey": event_id,
                "OwnerId": owner_id,
                "EventCode": f"LoadTest Event {e}",
                "EventMarkdown": f"Load test event {e} for scale testing",
                "StartTimestamp": (now - timedelta(days=1)),
                "EndTimestamp": (now + timedelta(days=7)),
                "TimeZoneOffset": 0,
                "TimeZoneLabel": "UTC",
                "OrganizerName": "Load Tester",
                "OrganizerEmail": "loadtest@test.com",
                "MaxTokenCap": 4096,
                "DailyRequestCap": 100000,
                "Active": True,
                "CatalogIds": ",".join(event_catalog_ids),
            },
            mode=UpdateMode.REPLACE,
        )

        # Owner-event mapping
        owner_events_table.upsert_entity(
            {"PartitionKey": owner_id, "RowKey": event_id, "Creator": True},
            mode=UpdateMode.REPLACE,
        )

        # Create attendees (parallelized)
        from concurrent.futures import ThreadPoolExecutor, as_completed

        def create_attendee(e_idx, a_idx, event_id_inner, event_code):
            user_id = f"user-{e_idx}-{a_idx}"
            api_key = str(uuid.uuid4())

            attendees_table.upsert_entity(
                {
                    "PartitionKey": event_id_inner,
                    "RowKey": user_id,
                    "ApiKey": api_key,
                    "Active": True,
                },
                mode=UpdateMode.REPLACE,
            )

            lookup_table.upsert_entity(
                {
                    "PartitionKey": api_key[:2].lower(),
                    "RowKey": api_key,
                    "EventId": event_id_inner,
                    "UserId": user_id,
                    "Active": True,
                    "EventCode": event_code,
                    "OrganizerName": "Load Tester",
                    "OrganizerEmail": "loadtest@test.com",
                    "MaxTokenCap": 4096,
                    "DailyRequestCap": 100000,
                    "EventActive": True,
                    "StartTimestamp": (now - timedelta(days=1)),
                    "EndTimestamp": (now + timedelta(days=7)),
                    "TimeZoneOffset": 0,
                },
                mode=UpdateMode.REPLACE,
            )
            return api_key

        event_code = f"LoadTest Event {e}"
        with ThreadPoolExecutor(max_workers=20) as executor:
            futures = [
                executor.submit(create_attendee, e, a, event_id, event_code)
                for a in range(attendees_per_event)
            ]
            for f in as_completed(futures):
                all_api_keys.append(f.result())

        if (e + 1) % 5 == 0:
            print(f"  Created {e + 1}/{num_events} events")

    # Write api keys to file for load testing
    output_file = "loadtest/api_keys.json"
    os.makedirs("loadtest", exist_ok=True)
    with open(output_file, "w") as f:
        json.dump(
            {
                "api_keys": all_api_keys,
                "event_ids": all_event_ids,
                "deployment_names": list(set(deployment_names[:catalogs_per_event])),
            },
            f,
            indent=2,
        )

    print(f"\nSeeding complete!")
    print(f"  Events: {num_events}")
    print(f"  Catalogs: {len(catalog_ids)}")
    print(f"  Attendees: {num_events * attendees_per_event}")
    print(f"  API keys written to: {output_file}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Seed Table Storage with test data")
    parser.add_argument("storage_account", help="Azure Storage account name")
    parser.add_argument("encryption_key", help="Encryption key (must match deployed app)")
    parser.add_argument("--events", type=int, default=10, help="Number of events (default: 10)")
    parser.add_argument("--catalogs-per-event", type=int, default=3, help="Catalogs per event (default: 3)")
    parser.add_argument("--attendees-per-event", type=int, default=50, help="Attendees per event (default: 50)")
    parser.add_argument("--owner", default="admin", help="Owner ID (default: admin)")
    parser.add_argument("--connection-string", default="", help="Storage account connection string (bypasses DefaultAzureCredential)")
    args = parser.parse_args()

    seed(
        args.storage_account,
        args.encryption_key,
        args.events,
        args.catalogs_per_event,
        args.attendees_per_event,
        args.owner,
        args.connection_string,
    )
