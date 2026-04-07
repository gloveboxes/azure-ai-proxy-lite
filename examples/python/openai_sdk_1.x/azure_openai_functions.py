""" Test Azure OpenAI Tools API """

import os

from dotenv import load_dotenv
from openai import AzureOpenAI

load_dotenv()

ENDPOINT_URL = os.environ.get("PROXY_ENDPOINT")
API_KEY = os.environ.get("PROXY_API_KEY")
API_VERSION = "2024-10-21"
DEPLOYMENT_NAME = "gpt-4o"

messages = [
    {
        "role": "system",
        "content": (
            "Don't make assumptions about what values to plug into functions. "
            "Ask for clarification if a user request is ambiguous."
        ),
    },
    {"role": "user", "content": "What's the weather like today in seattle"},
]

tools = [
    {
        "type": "function",
        "function": {
            "name": "get_current_weather",
            "description": "Get the current weather",
            "parameters": {
                "type": "object",
                "properties": {
                    "location": {
                        "type": "string",
                        "description": "The city and state, e.g. San Francisco, CA",
                    },
                    "format": {
                        "type": "string",
                        "enum": ["celsius", "fahrenheit"],
                        "description": "The temperature unit to use. Infer this from the users location.",
                    },
                },
                "required": ["location", "format"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "get_n_day_weather_forecast",
            "description": "Get an N-day weather forecast",
            "parameters": {
                "type": "object",
                "properties": {
                    "location": {
                        "type": "string",
                        "description": "The city and state, e.g. San Francisco, CA",
                    },
                    "format": {
                        "type": "string",
                        "enum": ["celsius", "fahrenheit"],
                        "description": "The temperature unit to use. Infer this from the users location.",
                    },
                    "num_days": {
                        "type": "integer",
                        "description": "The number of days to forecast",
                    },
                },
                "required": ["location", "format", "num_days"],
            },
        },
    },
]


client = AzureOpenAI(
    azure_endpoint=ENDPOINT_URL,
    api_key=API_KEY,
    api_version=API_VERSION,
)

completion = client.chat.completions.create(
    model=DEPLOYMENT_NAME,
    messages=messages,
    tools=tools,
)

print(completion.model_dump_json(indent=2))
print()
print(completion.choices[0].finish_reason)
if completion.choices[0].message.tool_calls:
    for tool_call in completion.choices[0].message.tool_calls:
        print(f"Tool: {tool_call.function.name}, Args: {tool_call.function.arguments}")
