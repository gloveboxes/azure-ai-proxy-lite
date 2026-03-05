# Load Testing Guide

This guide covers how to load test the Azure AI Proxy to validate Table Storage performance, rate limiting accuracy, and metric tracking under load.

## Prerequisites

- Azure CLI authenticated (`az login`)
- Python 3.11+ with dependencies:
  ```bash
  pip install azure-data-tables azure-identity aiohttp cryptography
  ```
- A deployed proxy environment (`azd up`)

## Overview

The load test uses the proxy's built-in **mock proxy mode** (`UseMockProxy=true`), which returns canned responses with realistic usage tokens without hitting Azure OpenAI. This lets you test the full auth → rate limit → catalog lookup → metric tracking pipeline at scale.

Three scripts in the `loadtest/` directory:

| Script | Purpose |
|---|---|
| `seed_data.py` | Populates Table Storage with events, catalogs, and attendees |
| `load_test.py` | Sends concurrent requests using seeded API keys |
| `validate.py` | Verifies metrics and rate limit data match expectations |

## Step 1: Enable Mock Proxy

```bash
az containerapp update \
  --name <PROXY_APP_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --set-env-vars UseMockProxy=true
```

Get the values from azd:
```bash
PROXY_NAME=$(azd env get-value SERVICE_PROXY_NAME)
RG_NAME=$(azd env get-value AZURE_ENV_NAME)-rg
az containerapp update --name "$PROXY_NAME" --resource-group "$RG_NAME" --set-env-vars UseMockProxy=true
```

## Step 2: Seed Test Data

Get the encryption key and storage connection string:
```bash
ENC_KEY=$(azd env get-value SERVICE_ENCRYPTION_KEY)
STORAGE_ACCT=$(azd env get-value SERVICE_STORAGE_ACCOUNT_NAME)
CONN_STR=$(az storage account show-connection-string --name "$STORAGE_ACCT" --resource-group "$RG_NAME" --query connectionString -o tsv)
```

Run the seed script:
```bash
python loadtest/seed_data.py "$STORAGE_ACCT" "$ENC_KEY" \
  --events 20 \
  --catalogs-per-event 5 \
  --attendees-per-event 100 \
  --connection-string "$CONN_STR"
```

This creates:
- 20 events with 100,000 daily request cap each
- 10 catalogs (5 randomly assigned per event)
- 2,000 attendees with denormalized lookup entries
- API keys written to `loadtest/api_keys.json`

Example output:
```
Owner 'admin' ready
Creating 10 catalogs...
Creating 20 events with 100 attendees each...
  Created 5/20 events
  Created 10/20 events
  Created 15/20 events
  Created 20/20 events

Seeding complete!
  Events: 20
  Catalogs: 10
  Attendees: 2000
  API keys written to: loadtest/api_keys.json
```

### Seed Parameters

| Parameter | Default | Description |
|---|---|---|
| `--events` | 10 | Number of events to create |
| `--catalogs-per-event` | 3 | Catalogs assigned per event (total created = 2x this) |
| `--attendees-per-event` | 50 | Attendees per event (each gets a unique API key) |
| `--owner` | admin | Owner ID |
| `--connection-string` | | Storage connection string (uses DefaultAzureCredential if omitted) |

## Step 3: Run Load Test

```bash
PROXY_URI=$(azd env get-value SERVICE_PROXY_URI)
python loadtest/load_test.py "$PROXY_URI" \
  --concurrency 50 \
  --duration-seconds 60
```

### Load Test Parameters

| Parameter | Default | Description |
|---|---|---|
| `--concurrency` | 50 | Number of concurrent workers |
| `--duration-seconds` | 60 | How long to run the test |
| `--keys-file` | loadtest/api_keys.json | Path to seeded API keys |

### Example Output

```
Starting load test:
  Target: https://myproxy.azurecontainerapps.io
  Concurrency: 50
  Duration: 60s
  API keys: 2000
  Deployments: ['gpt-4-0', 'gpt-4-3', 'gpt-4-4', 'gpt-4-2', 'gpt-4-1']

  [5s] 255 requests (51 rps) | 2xx: 109 | 429: 0 | 5xx: 0
  [10s] 607 requests (70 rps) | 2xx: 284 | 429: 0 | 5xx: 0
  [15s] 959 requests (70 rps) | 2xx: 463 | 429: 0 | 5xx: 0
  [20s] 1318 requests (72 rps) | 2xx: 635 | 429: 0 | 5xx: 0
  [25s] 1691 requests (75 rps) | 2xx: 816 | 429: 0 | 5xx: 0
  [30s] 2060 requests (74 rps) | 2xx: 994 | 429: 0 | 5xx: 0
  [35s] 2385 requests (65 rps) | 2xx: 1173 | 429: 0 | 5xx: 0
  [40s] 2735 requests (70 rps) | 2xx: 1358 | 429: 0 | 5xx: 0
  [45s] 3099 requests (73 rps) | 2xx: 1530 | 429: 0 | 5xx: 0
  [50s] 3455 requests (71 rps) | 2xx: 1709 | 429: 0 | 5xx: 0
  [55s] 3809 requests (71 rps) | 2xx: 1892 | 429: 0 | 5xx: 0
  [60s] 4165 requests (71 rps) | 2xx: 2073 | 429: 0 | 5xx: 0

============================================================
LOAD TEST RESULTS
============================================================
  Duration:          61.6s
  Total requests:    4214
  Requests/sec:      68.4
  Success (2xx):     2112
  Rate limited (429):0
  Server error (5xx):0
  Other errors:      2102

  Latency p50:       570ms
  Latency p95:       1651ms
  Latency p99:       1881ms

  Status codes:
    200: 2112
    404: 2102
============================================================
```

> **Note**: 404s are expected when the randomly selected deployment doesn't exist in the randomly selected event's catalog. In real usage, attendees only use deployments assigned to their event so 404s won't occur.

## Step 4: Validate Results

Wait ~10 seconds for the background metric flush, then:

```bash
python loadtest/validate.py "$STORAGE_ACCT"
```

### Example Output

```
============================================================
VALIDATION
============================================================

Metrics per event:
  615e-3d8f: 125 requests, 7731 tokens
  a2dc-3522: 93 requests, 3498 tokens
  2515-a036: 78 requests, 3240 tokens
  ...

  Total metric requests: 2113
  Total metric tokens:   93500

  Rate limit entries: 1248 keys
  Total rate limit requests: 4180

  Metric vs rate limit delta: 2067
  ✗ Significant mismatch — investigate background flush or rate limit logic

  Load test reported 2112 successful requests
  Metric table recorded 2113 requests
  ✓ Within tolerance (delta: 1, likely flush timing)

  Lookup partition spread: 256 partitions
  Min/Avg/Max per partition: 8/16.7/30
  ✓ Good partition distribution
============================================================
```

### Understanding the Results

| Check | What it means |
|---|---|
| **Metric vs load test match** | Metric background writer accurately captured all successful requests |
| **Rate limit vs metrics mismatch** | Expected — rate limiter counts *all* requests (including 404s), metrics only count successful proxied requests |
| **Partition spread** | Validates the `api_key[0..2]` partition key strategy prevents hot partitions |

## Step 5: Disable Mock Proxy

After testing, disable mock mode:

```bash
az containerapp update --name "$PROXY_NAME" --resource-group "$RG_NAME" --set-env-vars UseMockProxy=false
```

## Cleanup

The seeded test data remains in Table Storage. To remove it, either:
- Delete and recreate the storage account via `azd down` + `azd up`
- Or clear tables manually via Azure Portal / Storage Explorer
