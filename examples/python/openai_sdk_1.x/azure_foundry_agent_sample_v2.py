"""
Azure AI Foundry Agent Service example using the azure-ai-agents SDK via the AI Proxy.

The proxy accepts api-key auth and forwards using managed identity.
A custom authentication policy injects the api-key header instead of a Bearer token.

Usage:
  export PROXY_ENDPOINT=https://<your-proxy>.azurecontainerapps.io/api/v1
  export PROXY_API_KEY=<your-event-api-key>
  python azure_foundry_agent_sample_v2.py
"""

import os
from azure.ai.agents import AgentsClient
from azure.ai.agents.models import (
    AgentThreadCreationOptions,
    MessageRole,
    MessageTextContent,
    RunStatus,
    ThreadMessageOptions,
)
from azure.core.credentials import AzureKeyCredential
from azure.core.pipeline.policies import AzureKeyCredentialPolicy
from dotenv import load_dotenv

load_dotenv()

PROXY_ENDPOINT = os.environ["PROXY_ENDPOINT"]  # e.g. https://<proxy>/api/v1
PROXY_API_KEY = os.environ["PROXY_API_KEY"]     # event API key
MODEL_NAME = "gpt-4o"


agents_client = AgentsClient(
    endpoint=PROXY_ENDPOINT,
    credential=AzureKeyCredential(PROXY_API_KEY),
    authentication_policy=AzureKeyCredentialPolicy(AzureKeyCredential(PROXY_API_KEY), name="api-key"),
)

with agents_client:
    # Step 1: Create an agent
    print("Creating agent...")
    agent = agents_client.create_agent(
        model=MODEL_NAME,
        name="MathTutor",
        instructions="You are a personal math tutor. Help students solve math problems step by step.",
    )
    print(f"Agent created: id={agent.id}, name={agent.name}")
    print()

    # Step 2: Create a thread with an initial message and run the agent
    print("Creating thread and running agent...")
    run = agents_client.create_thread_and_process_run(
        agent_id=agent.id,
        thread=AgentThreadCreationOptions(
            messages=[
                ThreadMessageOptions(
                    role=MessageRole.USER,
                    content="I need to solve the equation `3x + 11 = 14`. Can you help me?",
                )
            ]
        ),
    )
    print(f"Run completed with status: {run.status}")
    print()

    # Step 3: Print the assistant's response
    if run.status == RunStatus.COMPLETED:
        messages = agents_client.messages.list(thread_id=run.thread_id)
        for msg in messages:
            if msg.role == MessageRole.AGENT:
                for item in msg.content:
                    if isinstance(item, MessageTextContent):
                        print("Response:")
                        print("-" * 50)
                        print(item.text.value)
                break
    else:
        print(f"Run did not complete successfully: {run.last_error}")

    # Step 4: Cleanup
    print()
    print("-" * 50)
    print("Cleaning up...")
    agents_client.threads.delete(thread_id=run.thread_id)
    print(f"Thread {run.thread_id} deleted")
    agents_client.delete_agent(agent_id=agent.id)
    print(f"Agent {agent.id} deleted")
