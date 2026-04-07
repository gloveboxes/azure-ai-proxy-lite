"""Test Azure OpenAI GPT-4o Vision API"""

import os

from dotenv import load_dotenv
from openai import AzureOpenAI

load_dotenv()

ENDPOINT_URL = os.environ.get("PROXY_ENDPOINT")
API_KEY = os.environ.get("PROXY_API_KEY")
API_VERSION = "2024-10-21"
MODEL_NAME = "gpt-4o"

IMAGE_URL = "https://welovecatsandkittens.com/wp-content/uploads/2017/05/cute.jpg"

client = AzureOpenAI(
    azure_endpoint=ENDPOINT_URL,
    api_key=API_KEY,
    api_version=API_VERSION,
)

response = client.chat.completions.create(
    model=MODEL_NAME,
    messages=[
        {"role": "system", "content": "You are a helpful assistant."},
        {
            "role": "user",
            "content": [
                {"type": "text", "text": "Describe this picture:"},
                {
                    "type": "image_url",
                    "image_url": {"url": IMAGE_URL},
                },
            ],
        },
    ],
    max_tokens=2000,
)
print(response)
