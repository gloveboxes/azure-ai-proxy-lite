""" Test Azure OpenAI Chat Completions Stream API with Azure AI Search (Your Data) """

# Create a new Azure AI Search index and load an index with Azure content
# https://microsoftlearning.github.io/mslearn-knowledge-mining/Instructions/Labs/10-vector-search-exercise.html
# https://learn.microsoft.com/en-us/azure/ai-services/openai/use-your-data-quickstart


import os
import time

from dotenv import load_dotenv
from openai import AzureOpenAI

load_dotenv()

ENDPOINT_URL = os.environ.get("PROXY_ENDPOINT")
API_KEY = os.environ.get("PROXY_API_KEY")
AZURE_AI_SEARCH_ENDPOINT = os.environ.get("AZURE_AI_SEARCH_ENDPOINT")
AZURE_AI_SEARCH_KEY = os.environ.get("AZURE_AI_SEARCH_KEY")
AZURE_AI_SEARCH_INDEX_NAME = os.environ.get("AZURE_AI_SEARCH_INDEX_NAME")

API_VERSION = "2024-10-21"
MODEL_NAME = "gpt-4.1-mini"


client = AzureOpenAI(
    azure_endpoint=ENDPOINT_URL,
    api_key=API_KEY,
    api_version=API_VERSION,
)

messages = [
    {
        "role": "user",
        "content": (
            "What are the differences between Azure Machine Learning " "and Azure AI services?"
        ),
    },
]

body = {
    "data_sources": [
        {
            "type": "azure_search",
            "parameters": {
                "endpoint": AZURE_AI_SEARCH_ENDPOINT,
                "index_name": AZURE_AI_SEARCH_INDEX_NAME,
                "authentication": {
                    "type": "api_key",
                    "key": AZURE_AI_SEARCH_KEY,
                },
            },
        }
    ]
}

response = client.chat.completions.create(
    model=MODEL_NAME,
    messages=messages,
    extra_body=body,
    stream=True,
    max_tokens=250,
    temperature=0.0,
)

# turn off print buffering
# https://stackoverflow.com/questions/107705/disable-output-buffering


for chunk in response:
    if chunk.choices and len(chunk.choices) > 0:
        content = chunk.choices[0].delta.content
        if content:
            print(content, end="", flush=True)
        # delay to simulate real-time chat
        time.sleep(0.05)

print()
