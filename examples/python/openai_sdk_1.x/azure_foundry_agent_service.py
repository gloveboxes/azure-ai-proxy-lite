"""
Test Azure AI Foundry Agent Service via the AI Proxy.

The proxy accepts api-key auth and forwards using managed identity.
AIProjectClient requires a credential, but we only need it for the agents REST API.
The OpenAI client from get_openai_client() also needs the api-key header.

Usage:
  export PROXY_ENDPOINT=https://<your-proxy>.azurecontainerapps.io/api/v1
  export PROXY_API_KEY=<your-event-api-key>
  python azure_foundry_agent_service.py
"""

import os
import httpx
from openai import OpenAI
from dotenv import load_dotenv

load_dotenv()

PROXY_ENDPOINT = os.environ["PROXY_ENDPOINT"]  # e.g. https://<proxy>/api/v1
PROXY_API_KEY = os.environ["PROXY_API_KEY"]     # event API key
MODEL_NAME = "gpt-4o"

AGENTS_BASE = f"{PROXY_ENDPOINT}/agents"


def agents_request(method, path, **kwargs):
    """Make a request to the proxy's /agents endpoint."""
    url = f"{AGENTS_BASE}{path}"
    params = kwargs.pop("params", {})
    params.setdefault("api-version", "v1")
    resp = httpx.request(method, url, headers={"api-key": PROXY_API_KEY}, params=params, **kwargs)
    resp.raise_for_status()
    return resp.json()


# The OpenAI client for conversations/responses, pointed at the proxy
openai_client = OpenAI(
    base_url=f"{PROXY_ENDPOINT}/openai/v1",
    api_key="unused",
    default_headers={"api-key": PROXY_API_KEY},
)

if True:

    # Step 1: Create an Agent
    print("Creating agent...")
    agent = agents_request("POST", "/MathTutor/versions", json={
        "definition": {
            "kind": "prompt",
            "model": MODEL_NAME,
            "instructions": "You are a personal math tutor. Write and run code to answer math questions.",
        }
    })
    print(f"Agent created: name={agent['name']}, version={agent['version']}")
    print()

    # Step 2: Create a Conversation with the initial user message
    print("Creating conversation...")
    conversation = openai_client.conversations.create(
        items=[{
            "type": "message",
            "role": "user",
            "content": "I need to solve the equation `3x + 11 = 14`. Can you help me?",
        }]
    )
    print(f"Conversation created: {conversation.id}")
    print()

    # Step 3: Run the Agent
    print("Running agent...")
    response = openai_client.responses.create(
        conversation=conversation.id,
        extra_body={"agent_reference": {"name": agent["name"], "type": "agent_reference"}},
    )
    print()
    print("Response:")
    print("-" * 50)
    print(response.output_text)
    print()

    # Step 4: Cleanup
    print("-" * 50)
    print("Cleaning up...")
    openai_client.conversations.delete(conversation_id=conversation.id)
    print(f"Conversation {conversation.id} deleted")
    agents_request("DELETE", f"/{agent['name']}/versions/{agent['version']}")
    print(f"Agent {agent['name']} (version {agent['version']}) deleted")
