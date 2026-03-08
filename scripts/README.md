# Scripts

Utility scripts for Azure OpenAI Proxy management.

## Generate Events

Generate events in Azure Table Storage.

### Prerequisites

Install required Python packages:

```bash
pip install -r scripts/requirements.txt
```

### Usage

```bash
python scripts/generate_events.py <storage_account_name> [--num-events N] [--owner-id OWNER]
```

### Examples

Generate 1000 events for owner "admin":

```bash
# Using DefaultAzureCredential (logged in with az login)
python scripts/generate_events.py mystorageaccount --num-events 1000 --owner-id admin

# Using connection string
python scripts/generate_events.py mystorageaccount --num-events 1000 --owner-id admin \
  --connection-string "DefaultEndpointsProtocol=https;AccountName=..."
```

### What it does

The script:

1. **Verifies/creates the owner** in the `owners` table
2. **Generates unique event IDs** in format `xxxx-xxxx` (8 character hash)
3. **Creates entries in two tables**:
   - `events` table: PartitionKey=event_id, RowKey=event_id
   - `ownerevents` table: PartitionKey=owner_id, RowKey=event_id (for ownership mapping)
4. **Saves the generated event IDs** to a JSON file (`events_<owner_id>.json`)

### Event Properties

Each event is created with:
- Active status
- 30-day duration from now
- Token cap: 4096
- Daily request cap: 10000
- Time zone: UTC

## Generate Attendees

Generate attendees for an existing event in Azure Table Storage.

### Prerequisites

Install required Python packages:

```bash
pip install -r scripts/requirements.txt
```

### Usage

```bash
python scripts/generate_attendees.py <storage_account_name> <event_id> [--num-attendees N]
```

### Examples

Generate 1000 attendees for event `0d54-dc71`:

```bash
# Using DefaultAzureCredential (logged in with az login)
python scripts/generate_attendees.py mystorageaccount 0d54-dc71 --num-attendees 1000

# Using connection string
python scripts/generate_attendees.py mystorageaccount 0d54-dc71 \
  --num-attendees 1000 \
  --connection-string "DefaultEndpointsProtocol=https;AccountName=..."
```

### What it does

The script:

1. **Verifies the event exists** in the `events` table
2. **Generates unique API keys** for each attendee (UUID format)
3. **Creates entries in two tables**:
   - `attendees` table: PartitionKey=event_id, RowKey=user_id
   - `attendeelookup` table: PartitionKey=first 2 chars of API key (for distribution)
4. **Saves the generated API keys** to a JSON file (`attendees_<event_id>.json`)

### Authentication

The script uses Azure DefaultAzureCredential by default, which tries:
- Azure CLI credentials (`az login`)
- Managed Identity
- Environment variables

Make sure you're logged in:

```bash
az login
```

Or provide a connection string with `--connection-string`.

### Output

The script creates a JSON file with all generated API keys:

```json
{
  "event_id": "0d54-dc71",
  "event_code": "My Workshop",
  "generated_at": "2026-03-06T12:34:56.789Z",
  "count": 1000,
  "api_keys": [
    "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    ...
  ]
}
```

You can use these API keys for testing or distribute them to attendees.
