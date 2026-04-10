# AzureAIProxy Tests

## How the tests work

The test suite is split into three tiers, each exercising a progressively wider slice of the application:

### 1. Unit tests (`Security/`, `Proxy/`)

These test individual components in isolation with **no external dependencies**. Middleware handlers, auth handlers, and the proxy service are constructed directly in test code with lightweight stubs/fakes from the `TestDoubles/` folder. They always run, regardless of environment.

### 2. Integration tests (`Integration/`)

These test service-layer logic (authorization, attendee registration, catalog resolution, event isolation) against **real Azure Table Storage via Azurite** (the local storage emulator). Each test creates its own unique event/attendee/catalog IDs so tests never conflict with each other.

All integration tests use `[SkippableFact]` from the `Xunit.SkippableFact` package. If Azurite is not running, they are **automatically skipped** (not failed). The shared `AzuriteHelper` class in `Fixtures/` handles connection detection across ports 10102 (docker-compose) and 10002 (standalone).

### 3. Route-level tests (`Routes/`)

These boot the **entire ASP.NET application** using `WebApplicationFactory<Program>` with `UseMockProxy=true`. Requests flow through the real middleware pipeline (auth → rate limiter → load properties → max tokens → route handler) and hit a mock proxy service that returns canned responses from the `MockResponses/` folder instead of calling upstream Azure AI endpoints.

The shared `ProxyAppFixture` (in `Fixtures/`) is an xUnit `IClassFixture` that:
- Starts the app once per test class
- Connects to Azurite for table storage
- Configures the `EncryptionKey` to match `appsettings.Development.json`
- Provides seed helpers (`SeedEventAsync`, `SeedCatalogAsync`, `SeedAttendeeAsync`) to set up test data
- Exposes an `HttpClient` that sends requests to the in-memory test server

These tests also use `[SkippableFact]` and skip when Azurite is unavailable.

## Prerequisites

- .NET 10 SDK
- Azurite (optional — integration and route tests skip gracefully without it)

## Running all tests

```bash
dotnet test src/AzureAIProxy.Tests/AzureAIProxy.Tests.csproj
```

Or use the VS Code task: **Terminal → Run Task → test**

## Test inventory (69 tests)

### Unit tests — no dependencies (26 tests)

| File | Tests | What it covers |
|------|-------|---------------|
| `Security/AuthenticationHandlersTests.cs` | 10 | `ApiKeyAuthenticationHandler` and `BearerTokenAuthenticationHandler` — missing, empty, invalid, and valid credentials; edge cases like `Bearer` prefix without space |
| `Security/MiddlewareSecurityTests.cs` | 6 | `LoadProperties` (JSON parsing/bad JSON → 400), `MaxTokensHandler` (token cap enforcement → 400), `RateLimiterHandler` (daily request cap → 429) |
| `Security/CacheInvalidationEndpointTests.cs` | 5 | Re-implementation of the `/internal/cache/invalidate` endpoint logic — shared-secret auth, missing key, wrong key, case sensitivity, missing config → 503 |
| `Proxy/ProxyServiceBehaviorTests.cs` | 3 | Auth header generation (`api-key` vs `Authorization: Bearer`), `max_tokens` → `max_completion_tokens` rewrite for AI Toolkit model type |
| `Proxy/ProxyServicesRegistrationTests.cs` | 2 | `UseMockProxy` flag correctly swaps `IProxyService` between `ProxyService` and `MockProxyService` |

### Integration tests — require Azurite (17 tests)

| File | Tests | What it covers |
|------|-------|---------------|
| `Integration/AzuriteAuthorizationTests.cs` | 5 | Full authorization flow — active/inactive attendees, active/inactive events, expired time windows, unknown API keys |
| `Integration/AttendeeRegistrationTests.cs` | 6 | `AttendeeService` — new attendee creation, idempotent re-registration, same user across events, key lookup, end-to-end key → authorize |
| `Integration/EventIsolationTests.cs` | 4 | Cross-event data isolation — keys resolve to correct event only, catalog deployments are scoped per event, full end-to-end key-A-cannot-access-event-B |
| `Integration/CryptoMismatchTests.cs` | 2 | `CatalogService` graceful degradation when `EncryptionKey` has changed — returns `null` instead of throwing `CryptographicException` (which would cause 500) |

### Route-level tests — require Azurite, use full app pipeline (26 tests)

| File | Tests | What it covers |
|------|-------|---------------|
| `Routes/AzureOpenAIRouteTests.cs` | 8 | `/openai/deployments/{name}/chat/completions` and `/embeddings` — no key → 401, invalid key → 401, deployment not found → 404, valid → 200, bearer token auth, max_tokens cap → 400, cross-event isolation → 404 |
| `Routes/AzureInferenceRouteTests.cs` | 8 | `/chat/completions`, `/embeddings` (Azure Inference / Mistral routes) — bearer-only auth (api-key header rejected), deployment not found → 404, valid → 200, `extra-parameters` header forwarding, cross-event isolation |
| `Routes/EventRouteTests.cs` | 6 | `/eventinfo` (requires auth) and `/event/{eventId}` (anonymous) — event data, organizer details, capabilities, proxy URL, non-existent event → 404 |
| `Routes/CacheInvalidationRouteTests.cs` | 4 | Real `/internal/cache/invalidate` endpoint from `Program.cs` — missing header → 401, wrong key → 401, correct key → 200, case-sensitive check |

## Running with Azurite

### Option 1: Docker Compose (recommended)

The project's `docker/docker-compose.yml` starts Azurite on port `10102`:

```bash
cd docker
docker compose up azurite -d
```

Then run tests:

```bash
dotnet test src/AzureAIProxy.Tests/AzureAIProxy.Tests.csproj
```

### Option 2: Standalone Azurite

```bash
npm install -g azurite
azurite-table --tablePort 10002
```

### Option 3: Custom connection string

Set the `AZURITE_CONNECTION_STRING` environment variable:

```bash
export AZURITE_CONNECTION_STRING="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;"
dotnet test src/AzureAIProxy.Tests/AzureAIProxy.Tests.csproj
```

## Key test infrastructure

| File | Purpose |
|------|---------|
| `Fixtures/AzuriteHelper.cs` | Shared Azurite connection detection — tries port 10102, then 10002, then `AZURITE_CONNECTION_STRING` env var |
| `Fixtures/ProxyAppFixture.cs` | `WebApplicationFactory<Program>` wrapper — boots the real app with mock proxy, seeds test data into Azurite |
| `TestDoubles/` | Stubs and fakes (`StubHttpClientFactory`, `RecordingHttpMessageHandler`, `NoopMetricService`, `TestData`) used by unit tests |

## Mock proxy mode

The `UseMockProxy` configuration flag (set to `true` in `ProxyAppFixture` and available for local dev) swaps the real `ProxyService` for `MockProxyService`. The mock service returns canned JSON from files in `src/AzureAIProxy/MockResponses/`:

- `foundry-model.txt` — non-streaming chat/embedding response
- `foundry-model.streaming.txt` — streaming chat response
- `azure-ai-search.txt` — search response

If no matching file exists, it returns a generic JSON message with the upstream URL.

## Verbose output

```bash
dotnet test src/AzureAIProxy.Tests/AzureAIProxy.Tests.csproj --verbosity normal
```
