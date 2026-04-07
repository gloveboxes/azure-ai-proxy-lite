# Azure OpenAI Proxy — Local Docker Deployment

Run the Azure OpenAI Proxy on a local Linux server using Docker Compose with Azurite (OSS Azure Table Storage emulator), the proxy/admin UI, and the registration SPA.

## Quick Start

```bash
cp .env.example .env
```

Edit `.env` — you **must** update the following before starting the stack:

### 1. Set `REGISTRATION_HOST`

Update `REGISTRATION_HOST` to your server's hostname or IP address. The admin UI generates attendee registration links using this value. If left as `localhost`, registration links will only work from the server itself.

```env
REGISTRATION_HOST=192.168.1.50
```

Use your machine's hostname (e.g. `rpi58`) or LAN IP address so that attendees on the same network can reach the registration page.

### 2. Generate a secure `ENCRYPTION_KEY`

The `ENCRYPTION_KEY` is used to encrypt sensitive data (API keys, deployment credentials) stored in Azurite Table Storage. **Do not use the default value in production.**

Generate a secure random key with `openssl`:

```bash
openssl rand -base64 32
```

Copy the output into your `.env` file:

```env
ENCRYPTION_KEY=<paste-your-generated-key-here>
```

> **Important:** If you change this key after data has been written, existing encrypted data becomes unreadable. Keep a record of the key if you need to preserve data across restarts.

### 3. Start the stack

```bash
docker compose up -d
```

Four containers will start:

- **Azurite** — Table Storage emulator (port `10002` internally)
- **Proxy** — OpenAI proxy API (default port `8900`)
- **Admin** — Admin management UI (default port `8901`)
- **Registration** — Event registration SPA with email auth (default port `4280`)

Access the admin UI at `http://<your-host>:8901` and the registration page at `http://<your-host>:4280/event/<event-id>`.

## Configuration

All settings are in `.env`. See `.env.example` for defaults.

| Variable | Default | Description |
|---|---|---|
| `PROXY_PORT` | `8900` | Host port for the proxy API |
| `ADMIN_PORT` | `8901` | Host port for the admin UI |
| `ADMIN_USERNAME` | `admin` | Admin UI login username |
| `ADMIN_PASSWORD` | `admin` | Admin UI login password — **change this** |
| `ENCRYPTION_KEY` | *(insecure default)* | Encryption key for stored secrets — **change this** |
| `USE_MOCK_PROXY` | `false` | Set `true` to return mock OpenAI responses (no real backend needed) |
| `REGISTRATION_PORT` | `4280` | Host port for the registration SPA |
| `REGISTRATION_HOST` | `localhost` | Hostname/IP used in registration links — **set this to your server** |
| `AZURITE_TABLE_PORT` | `10002` | Host port for Azurite Table Storage |
| `PROXY_IMAGE` | `glovebox/aoai-proxy:latest` | Docker Hub image for the proxy |
| `ADMIN_IMAGE` | `glovebox/aoai-proxy-admin:latest` | Docker Hub image for the admin UI |
| `REGISTRATION_IMAGE` | `glovebox/aoai-proxy-registration:latest` | Docker Hub image for the registration SPA |

## Building and Pushing Multi-Architecture Images

The images support both `linux/amd64` and `linux/arm64` (e.g. Raspberry Pi, Apple Silicon).

### Prerequisites

```bash
# One-time: create a multi-platform builder
docker buildx create --name multiarch --driver docker-container --use

# Log in to Docker Hub
docker login
```

### Build and push

From the repository root:

```bash
./docker/build-and-push.sh <your-dockerhub-username> [tag]
```

Examples:

```bash
# Push as latest
./docker/build-and-push.sh glovebox

# Push a specific version
./docker/build-and-push.sh glovebox v1.2.0
```

This builds both images for `linux/amd64` and `linux/arm64` and pushes them to Docker Hub:

- `<username>/aoai-proxy:<tag>`
- `<username>/aoai-proxy-admin:<tag>`
- `<username>/aoai-proxy-registration:<tag>`

Update `PROXY_IMAGE`, `ADMIN_IMAGE`, and `REGISTRATION_IMAGE` in `.env` if you use a different username or tag.

## Managing the Stack

```bash
# Start
docker compose up -d

# Stop
docker compose down

# View logs
docker compose logs -f

# View logs for a specific service
docker compose logs -f proxy

# Pull latest images
docker compose pull

# Restart after config changes
docker compose down && docker compose up -d

# Reset all data (removes Azurite volume)
docker compose down -v

# Force re-pull images, recreate containers, and reset all data
docker compose down -v && docker compose pull && docker compose up -d --force-recreate
```
