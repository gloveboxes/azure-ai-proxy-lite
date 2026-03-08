""" Test Azure OpenAI Assistants API """

# See documentation at https://gloveboxes.github.io/azure-openai-service-proxy/category/developer-endpoints/

import os
import time

from dotenv import load_dotenv
from openai import AzureOpenAI

load_dotenv()

ENDPOINT_URL = os.environ.get("ENDPOINT_URL")
API_KEY = os.environ.get("API_KEY")
API_VERSION = "2024-05-01-preview"
MODEL_NAME = "gpt-4o"  # Model deployment name
MODEL_VERSION = "2024-08-06"  # Model version deployed


client = AzureOpenAI(
    azure_endpoint=ENDPOINT_URL,
    api_key=API_KEY,
    api_version=API_VERSION,
)

# Step 1: Create an Assistant
print("Creating assistant...")
assistant = client.beta.assistants.create(
    name="Math Tutor",
    instructions="You are a personal math tutor. Write and run code to answer math questions.",
    model=MODEL_NAME,
)
print(f"Assistant created: {assistant.id}")
print()

# Step 2: Create a Thread
print("Creating thread...")
thread = client.beta.threads.create()
print(f"Thread created: {thread.id}")
print()

# Step 3: Add a Message to the Thread
print("Adding message to thread...")
message = client.beta.threads.messages.create(
    thread_id=thread.id,
    role="user",
    content="I need to solve the equation `3x + 11 = 14`. Can you help me?"
)
print(f"Message added: {message.id}")
print()

# Step 4: Run the Assistant
print("Running assistant...")
run = client.beta.threads.runs.create(
    thread_id=thread.id,
    assistant_id=assistant.id,
)
print(f"Run created: {run.id}")
print()

# Step 5: Poll the Run status
print("Waiting for completion...")
while run.status in ["queued", "in_progress"]:
    time.sleep(1)
    run = client.beta.threads.runs.retrieve(
        thread_id=thread.id,
        run_id=run.id
    )
    print(f"Status: {run.status}")

print()
print(f"Run completed with status: {run.status}")
print()

# Step 6: Retrieve the Messages
if run.status == "completed":
    messages = client.beta.threads.messages.list(
        thread_id=thread.id
    )

    print("Messages:")
    print("-" * 50)
    for msg in reversed(messages.data):
        role = msg.role
        content = msg.content[0].text.value
        print(f"{role.upper()}: {content}")
        print()
else:
    print(f"Run failed with status: {run.status}")

# Step 7: Cleanup - Delete the Assistant
print("-" * 50)
print("Cleaning up...")
client.beta.assistants.delete(assistant.id)
print(f"Assistant {assistant.id} deleted")
