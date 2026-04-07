# Azure AI Proxy

![](media/openai_proxy_banner.jpeg)

## Introduction to the Azure AI Proxy

The goal of the Azure AI Proxy is to simplify access to Azure AI resources for the `VS Code AI Toolkit`, Azure OpenAI SDKs, LangChain, and REST endpoints for developer events, workshops, and hackathons. Access is granted using a time bound `API Key`.

There are several primary use cases for the Azure AI Proxy:

1. You are running a hackathon and users can't provision their own Azure OpenAI resources as they don't have a corporate email address.
1. Accessing and experimenting with models, prompts, and MCP Servers from the VS Code AI Toolkit.
1. Access to developer APIs via REST endpoints and the OpenAI SDKs and LangChain. Access to these services is granted using a time bound event code.
1. Access to Azure AI Foundry Agent Service via the proxy, including agent, assistant, thread, file, conversation, and response operations.
1. Access to MCP (Model Context Protocol) Servers via the proxy.
1. Access to Azure AI Search queries using the proxy. Access to these services is granted using a time bound event code.

## Getting Started with the Azure AI Proxy

Watch this 5-minute video to learn how to get started with the Azure AI Proxy.

<iframe width="672" height="378" src="https://www.youtube.com/embed/x9N1qivjlfw?si=tdgJv9bDAUabpnPt" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" referrerpolicy="strict-origin-when-cross-origin" allowfullscreen></iframe>

## Bring your own models

The Azure AI Proxy provides the infrastructure to support the deployment of your own models. You need to provide the models supporting your event. The proxy supports the following APIs:

- **Chat Completions** (Azure OpenAI and Azure Inference)
- **Embeddings** (text and image)
- **Azure AI Foundry Agents** (agents, assistants, threads, files, conversations, responses)
- **MCP Servers** (Model Context Protocol)
- **Azure AI Search** (search queries)
- **AI Toolkit** (VS Code AI Toolkit extension integration)

## The VS Code AI Toolkit

The VS Code AI Toolkit extension is the first-class experience for experimenting with models, prompts, and MCP Servers using a time bound event code and key. Attendees can configure the AI Toolkit extension to connect through the proxy using their API key and the proxy endpoint URL.

## Azure AI Proxy Architecture

The Azure AI Proxy consists of the following components:

1. Self-service event management. Configure and manage events and resources for the events.
1. Self-service attendee registration. Attendees can register for an event and receive a time bound API Key to access the AI Proxy service.
1. The AI Proxy service. The AI Proxy service provides access to the Azure AI resources using a time bound API Key.

## Multi-Tenant Architecture

The Azure AI Proxy is a multi-tenanted solution where **events are the tenant boundary**. A single deployment of the proxy can host multiple concurrent events, each with its own attendees, resource assignments, and usage limits, while sharing the same underlying infrastructure.

### Tenant isolation

- **Event-scoped data** — Attendees, metrics, and configuration are partitioned by Event ID. Each event's data is isolated from other events.
- **Time-bound access** — API keys are valid only during the event's configured start and end time window. Once an event ends, attendee API keys are no longer authorized.
- **Per-event capacity controls** — Each event has its own Max Token Cap and Daily Request Cap, preventing one event from consuming resources allocated to another.
- **Per-attendee usage tracking** — Daily request counts are tracked per attendee and reset at midnight UTC, ensuring fair usage within an event.
- **Object ownership** — For Azure AI Foundry Agents, the proxy tracks object ownership so that each attendee can only access the agents, threads, files, and other objects they created.

### Shared resources across events

Azure AI resources (models, endpoints, agents, MCP servers) are configured once and can be reused across multiple events. This means organizers can set up their resource catalog independently of event scheduling, and assign the same resources to different events as needed.

## Open Source

The Azure AI Proxy is an open-source project, licensed under MIT. You can find the source code on [GitHub](https://gloveboxes.github.io/azure-ai-proxy-lite/){:target="_blank"}.

This project would not be possible without contributions from multiple people. Please feel free to contribute to the project by submitting a pull request or opening an issue.
