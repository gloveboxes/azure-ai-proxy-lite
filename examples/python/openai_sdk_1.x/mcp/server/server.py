"""MCP Server with FastMCP - provides echo and get_current_utc_time tools."""

import logging
import os
from datetime import datetime, timezone

from fastmcp import FastMCP
from starlette.middleware import Middleware
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.responses import JSONResponse

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

API_KEY = os.environ.get("MCP_API_KEY", "")

mcp = FastMCP("Demo MCP Server")


class ApiKeyMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request, call_next):
        provided_key = request.headers.get("api-key", "")
        masked_key = f"{provided_key[:4]}...{provided_key[-4:]}" if len(provided_key) > 8 else "***"
        logger.info("Received request with api-key: %s", masked_key)
        if API_KEY and provided_key != API_KEY:
            logger.warning("Unauthorized request - api-key mismatch")
            return JSONResponse({"error": "Unauthorized"}, status_code=401)
        return await call_next(request)


@mcp.tool()
def echo(message: str) -> str:
    """Echoes the provided message back to the caller."""
    return message


@mcp.tool()
def get_current_utc_time() -> str:
    """Returns the current UTC time in ISO 8601 format."""
    return datetime.now(timezone.utc).isoformat()


if __name__ == "__main__":
    app = mcp.http_app(transport="streamable-http", middleware=[Middleware(ApiKeyMiddleware)])

    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
