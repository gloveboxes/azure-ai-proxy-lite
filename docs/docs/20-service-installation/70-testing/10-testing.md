# Proxy service testing

There are various options to test the endpoint. The simplest is to use Curl from either PowerShell or a Bash/zsh terminal.

Replace `YOUR_PROXY_URL` with your proxy endpoint URL, `DEPLOYMENT_NAME` with your model deployment name, and `API_KEY` with your event API key.

From PowerShell 7 and above on Windows, macOS, and Linux:

```pwsh
curl -X 'POST' `
  'https://YOUR_PROXY_URL/api/v1/openai/deployments/DEPLOYMENT_NAME/chat/completions?api-version=2025-01-01-preview' `
  -H 'accept: application/json' `
  -H 'Content-Type: application/json' `
  -H 'api-key: API_KEY' `
  -d '{
  "messages": [
    {"role": "system", "content":"You are a helpful assistant."},
    {"role": "user", "content":"The quick brown fox jumps over the lazy dog"}
  ],
  "max_tokens": 1024,
  "temperature": 0,
  "top_p": 0,
  "frequency_penalty": 0,
  "presence_penalty": 0
}'  | ConvertFrom-Json | ConvertTo-Json
```

From Bash/zsh on macOS, Linux, and Windows WSL:

```bash
curl -X 'POST' \
  'https://YOUR_PROXY_URL/api/v1/openai/deployments/DEPLOYMENT_NAME/chat/completions?api-version=2025-01-01-preview' \
  -H 'accept: application/json' \
  -H 'Content-Type: application/json' \
  -H 'api-key: API_KEY' \
  -d '{
  "messages": [
    {"role": "system", "content":"You are a helpful assistant."},
    {"role": "user", "content":"The quick brown fox jumps over the lazy dog"}
  ],
  "max_tokens": 1024,
  "temperature": 0,
  "top_p": 0,
  "frequency_penalty": 0,
  "presence_penalty": 0
}'
```

Better still, pip or brew install `jq` to pretty print the JSON response.

```bash
curl -X 'POST' \
  'https://YOUR_PROXY_URL/api/v1/openai/deployments/DEPLOYMENT_NAME/chat/completions?api-version=2025-01-01-preview' \
  -H 'accept: application/json' \
  -H 'Content-Type: application/json' \
  -H 'api-key: API_KEY' \
  -d '{
  "messages": [
    {"role": "system", "content":"You are a helpful assistant."},
    {"role": "user", "content":"The quick brown fox jumps over the lazy dog"}
  ],
  "max_tokens": 1024,
  "temperature": 0,
  "top_p": 0,
  "frequency_penalty": 0,
  "presence_penalty": 0
}' | jq "."
```
