""" Test Azure AI Foundry Agent Service using Managed Identity """

# See documentation at https://learn.microsoft.com/azure/foundry/agents/quickstart

import os

from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import PromptAgentDefinition
from azure.identity import DefaultAzureCredential
from dotenv import load_dotenv

load_dotenv()

# Project endpoint of the form:
# https://<your-ai-services-account-name>.services.ai.azure.com/api/projects/<your-project-name>
ENDPOINT_URL = os.environ.get("ENDPOINT_URL")
MODEL_NAME = "gpt-4o"  # Model deployment name

# Entra ID is the only authentication method supported by AIProjectClient.
# DefaultAzureCredential uses managed identity in Azure, or 'az login' locally.
credential = DefaultAzureCredential()

client = AIProjectClient(
    endpoint=ENDPOINT_URL,
    credential=credential,
)

with client.get_openai_client() as openai_client:

    # Step 1: Create an Agent
    print("Creating agent...")
    agent = client.agents.create_version(
        agent_name="MathTutor",
        definition=PromptAgentDefinition(
            model=MODEL_NAME,
            instructions="You are a personal math tutor. Write and run code to answer math questions.",
        ),
    )
    print(f"Agent created: name={agent.name}, version={agent.version}")
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
        extra_body={"agent_reference": {"name": agent.name, "type": "agent_reference"}},
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
    client.agents.delete_version(agent_name=agent.name, agent_version=agent.version)
    print(f"Agent {agent.name} (version {agent.version}) deleted")
