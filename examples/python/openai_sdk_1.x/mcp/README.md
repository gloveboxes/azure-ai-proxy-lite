# MCP Server & Client Demo

A sample MCP (Model Context Protocol) server built with [FastMCP](https://github.com/jlowin/fastmcp) and a Python client, deployable to Azure Container Apps via `azd`.

## Tools

| Tool | Description |
|------|-------------|
| `echo` | Returns the message you send it |
| `get_current_utc_time` | Returns the current UTC time in ISO 8601 format |

## Project Structure

```
mcp/
├── azure.yaml              # azd project configuration
├── server/
│   ├── server.py           # FastMCP server with tools
│   ├── requirements.txt
│   └── Dockerfile
├── client/
│   ├── client.py           # MCP client that calls the server
│   └── requirements.txt
└── infra/                  # Bicep templates for Azure deployment
    ├── main.bicep
    ├── main.parameters.json
    ├── abbreviations.json
    ├── app/
    │   └── mcp-server.bicep
    └── core/
        ├── host/
        │   ├── container-apps-environment.bicep
        │   └── container-registry.bicep
        └── monitor/
            └── monitoring.bicep
```

## Run Locally

### Start the server

```bash
cd server
pip install -r requirements.txt
python server.py
```

The server starts on `http://localhost:8000` with the MCP endpoint at `http://localhost:8000/mcp`.

### Run the client

In a separate terminal:

```bash
cd client
pip install -r requirements.txt
python client.py
```

## Deploy to Azure

### Prerequisites

- [Azure Developer CLI (azd)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- [Docker](https://docs.docker.com/get-docker/)
- An Azure subscription

### Deploy

From the `mcp/` directory:

```bash
azd auth login
azd init
azd up
```

After deployment, `azd` outputs the `MCP_SERVER_URL`. Use it with the client:

```bash
cd client
export MCP_SERVER_URL="https://<your-container-app>.azurecontainerapps.io/mcp"
python client.py
```
