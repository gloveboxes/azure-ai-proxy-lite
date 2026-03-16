"""MCP Client - connects to an MCP server via the Azure AI Proxy and calls tools.

Set the following environment variables (or add them to .env in the examples root):
  PROXY_ENDPOINT  - The proxy base URL (e.g. https://<proxy-host>/api/v1)
  PROXY_API_KEY   - Your proxy API key
  MCP_DEPLOYMENT  - The MCP deployment name registered in the proxy (default: mcp-demo)
"""

import asyncio
import os
import sys
from pathlib import Path

from dotenv import load_dotenv
from fastmcp import Client
from fastmcp.client.transports import StreamableHttpTransport

# Load .env from the root of the examples folder
load_dotenv(Path(__file__).resolve().parents[2] / ".env")


async def main():
    proxy_url = os.environ.get("PROXY_ENDPOINT", "").rstrip("/")
    api_key = os.environ.get("PROXY_API_KEY", "")
    deployment = os.environ.get("MCP_DEPLOYMENT", "mcp-demo")

    if not proxy_url or not api_key:
        print("Error: PROXY_ENDPOINT and PROXY_API_KEY environment variables are required.")
        print("  export PROXY_ENDPOINT=https://<your-proxy-host>/api/v1")
        print("  export PROXY_API_KEY=<your-api-key>")
        sys.exit(1)

    server_url = f"{proxy_url}/mcp/{deployment}/mcp"
    print(f"Connecting 50 workers to MCP server via proxy at {server_url}")

    async def worker(worker_id: int):
        t = StreamableHttpTransport(
            url=server_url,
            headers={"api-key": api_key},
        )
        async with Client(t) as client:
            for i in range(200):
                tools = await client.list_tools()
                echo_result = await client.call_tool("echo", {"message": f"Worker {worker_id} iteration {i}"})
                time_result = await client.call_tool("get_current_utc_time", {})
                print(f"[Worker {worker_id:2d} | {i+1:3d}/200] echo={echo_result.data}  time={time_result.data}")
        print(f"[Worker {worker_id:2d}] Done")

    tasks = [worker(i) for i in range(50)]
    await asyncio.gather(*tasks)


if __name__ == "__main__":
    asyncio.run(main())
