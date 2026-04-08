# Azure AI Proxy

A managed proxy that sits between workshop attendees and Azure AI services, giving organisers full control over access, capacity, and usage tracking.

The solution documentation is published [here](https://gloveboxes.github.io/azure-ai-proxy-lite/).

![](docs/static/img/openai_proxy_banner.jpeg)

## Architecture

```mermaid
graph LR
    Attendees --> Reg[Registration Portal]
    Attendees --> T1[VS Code AI Toolkit]
    Attendees --> T2[Model SDKs]
    Attendees --> T3[Microsoft Agent Framework]
    Attendees --> T4[Foundry Agent Service]
    Attendees --> T5[LangGraph, REST...]

    Organiser[Event Organiser] --> Admin[Admin Portal]

    Reg --> Azure_AI_Proxy
    T1 --> Azure_AI_Proxy
    T2 --> Azure_AI_Proxy
    T3 --> Azure_AI_Proxy
    T4 --> Azure_AI_Proxy
    T5 --> Azure_AI_Proxy
    Admin --> Azure_AI_Proxy

    subgraph Azure_AI_Proxy[Azure AI Proxy]
        direction LR
        P1[Auth]
        P2[Rate Limiter]
        P3[Usage Metrics]
        P4[Event Management]
    end

    Azure_AI_Proxy --> A1[Foundry Models]
    Azure_AI_Proxy --> A2[Foundry Service Agents]
    Azure_AI_Proxy --> A3[Azure AI Search]
    Azure_AI_Proxy --> A4[MCP Servers]
```

### Broad AI Service Support

- VS Code AI Toolkit integration for hands-on model experimentation
- Azure OpenAI chat completions & embeddings (including streaming)
- Azure AI Foundry Service Agents (assistants, threads, files, conversations, responses)
- Azure AI Search pass-through for RAG scenarios
- MCP Server endpoints with streamable HTTP transport

### Event & Attendee Management

- Time-bound events with start/end windows — API keys only work during your workshop
- Self-service attendee registration via GitHub OAuth or shared codes (great for in-person sessions where not everyone has GitHub)
- Per-event resource assignment — choose exactly which models each event can access
- Full admin portal for creating events, managing resources, and viewing metrics

### Capacity Controls

- Daily request cap per attendee — prevents any one person from consuming all capacity
- Max token cap per request — stops runaway token usage

### Security

- Attendees never see your real Azure API keys or endpoints
- Encrypted storage for all sensitive configuration (AES encryption)
- Managed Identity support (eliminate API key storage entirely with RBAC)
- Object ownership isolation — attendees can only access their own Foundry Service agents/threads/files

### Reporting & Analytics

- Per-event usage dashboards: request counts, token usage, active registrations over time
- Per-model breakdown of prompt/completion tokens
- Exportable backup of all configuration data

### Deployment

- One-command deploy with `azd up` (Container Apps + Static Web App + Table Storage)
- Docker Compose for local development
- Multi-tenant — run multiple workshops simultaneously with full data isolation

### Developer Experience

- Drop-in compatible with Azure OpenAI SDKs (Python, .NET, LangChain, REST)
- Attendees just swap their endpoint URL and use their issued API key
- Registration page shows available models and copy-paste configuration
