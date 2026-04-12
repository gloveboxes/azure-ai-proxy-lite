# Security Architecture

This page documents the security architecture of the Azure AI Proxy when deployed to Azure, following least-privilege principles. The deployment is designed for **B2B/enterprise (BAMI) tenants** where Entra ID governs identity and access.

## Architecture overview

```mermaid
graph LR
    attendee["👤 Attendee"] -->|"GitHub OAuth"| reg["Registration<br/>(Static Web App)"]
    sdk["🖥️ SDK Client"] -->|"api-key (TLS)"| proxy
    reg -->|"Linked Backend"| proxy
    admin_user["👤 Admin"] -->|"Entra ID"| admin

    subgraph Azure["Azure — Entra ID Tenant"]
        proxy["API Proxy"]
        admin["Admin UI"]
        storage[("Table Storage<br/>Shared Key: Off")]
        ai["AI Foundry +<br/>OpenAI Models"]
    end

    proxy -->|"Managed Identity"| storage
    proxy -->|"Managed Identity"| ai
    admin -->|"Managed Identity"| storage
    admin -.->|"Cache Invalidation<br/>(internal)"| proxy
```

## Identity model

All runtime service-to-service communication uses **User-Assigned Managed Identities** scoped via `AZURE_CLIENT_ID`. No shared keys, connection strings, or API keys flow between Azure services.

```mermaid
graph LR
    subgraph Identities["User-Assigned Managed Identities"]
        proxyId["Proxy Identity<br/>(id-proxy)"]
        adminId["Admin Identity<br/>(id-admin)"]
    end

    subgraph Roles["RBAC Role Assignments"]
        r1["Storage Table Data<br/>Contributor"]
        r2["Cognitive Services<br/>OpenAI User"]
        r3["Storage Table Data<br/>Contributor"]
    end

    subgraph Resources["Target Resources"]
        st[("Table Storage")]
        ai["AI Services"]
    end

    proxyId -->|"Scoped to<br/>storage account"| r1
    proxyId -->|"Scoped to<br/>AI Services"| r2
    adminId -->|"Scoped to<br/>storage account"| r3

    r1 --> st
    r2 --> ai
    r3 --> st
```

| Identity | RBAC Role | Scope | Purpose |
|----------|-----------|-------|---------|
| **Proxy** (User-Assigned) | Storage Table Data Contributor | Storage account | Read/write attendees, events, metrics, rate limits |
| **Proxy** (User-Assigned) | Cognitive Services OpenAI User | AI Services account | Forward requests to OpenAI models via managed identity |
| **Admin** (User-Assigned) | Storage Table Data Contributor | Storage account | Manage events, resources, attendees, reporting |

!!! note "Least-privilege design"
    The system-assigned identities on the Container Apps have **no role grants** — only the user-assigned identities carry RBAC permissions. `AZURE_CLIENT_ID` is set explicitly so `DefaultAzureCredential` selects the correct identity at runtime.

## Authentication flows

### Attendee registration (GitHub OAuth)

```mermaid
sequenceDiagram
    participant A as Attendee Browser
    participant S as Static Web App
    participant E as Easy Auth Sidecar
    participant P as Proxy API
    participant T as Table Storage

    A->>S: Navigate to /event/{id}
    S->>E: /.auth/login/github
    E->>A: GitHub OAuth redirect
    A->>E: OAuth callback
    E->>P: POST /attendee/event/{id}/register<br/>x-ms-client-principal: {userId}
    P->>T: Create attendee + lookup rows
    T-->>P: API key (GUID)
    P-->>A: 201 Created {api_key}
```

### API request (SDK client)

```mermaid
sequenceDiagram
    participant C as SDK Client
    participant P as Proxy API
    participant R as Rate Limiter
    participant T as Table Storage
    participant AI as Azure AI Services

    C->>P: POST /api/v1/openai/.../completions<br/>api-key: {key}
    P->>P: Validate key format<br/>(length, chars, control chars)
    P->>T: Lookup attendee by API key
    T-->>P: Event + attendee context
    P->>R: Check daily request count
    R-->>P: count < cap ✓
    P->>P: Enforce max_tokens cap
    P->>AI: Forward request<br/>(Managed Identity token)
    AI-->>P: Completion response
    P-->>C: 200 OK + response
    P->>P: Async: log metrics
```

### Admin authentication (Entra ID)

```mermaid
sequenceDiagram
    participant A as Admin User
    participant E as Easy Auth (Entra ID)
    participant Ad as Admin Container App
    participant T as Table Storage

    A->>E: Navigate to admin URL
    E->>A: Entra ID login redirect
    A->>E: Authenticate with org credentials
    E->>Ad: Authenticated request<br/>(x-ms-client-principal)
    Ad->>Ad: Verify user is registered owner
    Ad->>T: Manage events/resources<br/>(Managed Identity)
    T-->>Ad: Data
    Ad-->>A: Admin UI
```

## Network and data security

### Storage account hardening

| Setting | Value | Rationale |
|---------|-------|-----------|
| `allowSharedKeyAccess` | `false` | Forces Entra ID authentication only — no connection strings |
| `defaultToOAuthAuthentication` | `true` | Portal defaults to Entra ID instead of key-based auth |
| `publicNetworkAccess` | `Disabled` | No direct internet access to storage |
| `networkAcls.defaultAction` | `Deny` | Deny all network access by default |
| `networkAcls.bypass` | `AzureServices` | Allow trusted Azure services (Container Apps) |
| `minimumTlsVersion` | `TLS1_2` | Enforce modern TLS |

### Container Apps security

| Control | Implementation |
|---------|---------------|
| **Auth sidecar** | Container Apps Easy Auth injects `x-ms-client-principal` headers after authentication. The proxy trusts these headers only when running behind the sidecar. |
| **Internal communication** | Admin-to-proxy cache invalidation uses the Container Apps internal FQDN (`*.internal.{domain}`), not exposed to the internet. |
| **Secret management** | Encryption keys and App Insights connection strings are stored as Container Apps secrets, injected as environment variables. |
| **Constant-time comparisons** | Cache invalidation key and admin local login use `CryptographicOperations.FixedTimeEquals` to prevent timing attacks. |
| **Image pull** | Container Registry images pulled via managed identity (ACR pull role on user-assigned identity). |

### Input validation at auth boundaries

| Attack vector | Mitigation |
|---------------|------------|
| Malformed API keys (short, control chars) | `IsPlausibleApiKey()` rejects before table lookup — returns 401, not 500 |
| Invalid base64 in `x-ms-client-principal` | `TryDecodeClientPrincipal()` with safe fallback — returns 401 |
| Malformed JSON in client principal | `JsonException` caught, logged, returns null — 401 |
| Oversized headers | `MaxClientPrincipalHeaderLength` (16 KB) guard |
| Query parameter injection (MCP routes) | `Uri.EscapeDataString()` on all forwarded query parameters |

### Rate limiting

| Control | Implementation |
|---------|---------------|
| **Daily request cap** | Per-API-key, resets at midnight UTC. Enforced at `>=` cap (not `>`). |
| **Max token cap** | Per-event setting, limits `max_tokens` in each request. |
| **Admin login throttling** | IP-based lockout after failed login attempts. |

## BAMI tenant considerations

When deployed in a BAMI (Business Account Managed Identity) tenant:

1. **Entra ID app registration** — The admin UI registers an app in the tenant for OpenID Connect authentication. Users authenticate with their organizational credentials.
2. **Conditional Access** — Entra ID Conditional Access policies in the BAMI tenant apply to admin logins (MFA, device compliance, location restrictions).
3. **RBAC inheritance** — All role assignments are scoped to the resource group, not the subscription. No subscription-level permissions are granted.
4. **No cross-tenant access** — Storage and AI Services are accessed exclusively via managed identities in the same tenant. No cross-tenant federation is configured.
5. **Audit trail** — All Entra ID authentications and RBAC operations are logged in the tenant's Entra ID sign-in and audit logs. Application-level metrics flow to Application Insights.

## Secrets inventory

| Secret | Where stored | Rotation |
|--------|-------------|----------|
| Encryption key | Container Apps secret (derived from `uniqueString`) | Redeployed with `azd up` |
| App Insights connection string | Container Apps secret | Managed by Azure |
| Entra ID client credentials | Entra ID app registration | Managed by Entra ID |
| Attendee API keys | Table Storage (GUID per attendee) | Per-event, time-bound by event window |

!!! warning "No long-lived secrets"
    Storage connection strings are **not used**. The `allowSharedKeyAccess: false` setting ensures no key-based access exists. All service-to-service auth uses short-lived managed identity tokens.
