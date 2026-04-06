#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------
# Build and push multi-architecture images for the proxy and
# registration SPA.
#
# Prerequisites:
#   docker buildx create --name multiarch --use   (one-time setup)
#   docker login                                   (Docker Hub)
#
# Usage:
#   ./build-and-push.sh <docker-hub-username> [tag]
#
# Examples:
#   ./build-and-push.sh myuser
#   ./build-and-push.sh myuser v1.2.0
# -----------------------------------------------------------------

REPO="${1:?Usage: $0 <docker-hub-username> [tag]}"
TAG="${2:-latest}"
PLATFORMS="linux/amd64,linux/arm64"

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

echo "==> Clearing buildx cache to ensure fresh builds"
docker buildx prune --force

echo "==> Building proxy image: ${REPO}/aoai-proxy:${TAG}"
docker buildx build \
    --platform "${PLATFORMS}" \
    --tag "${REPO}/aoai-proxy:${TAG}" \
    --file "${ROOT_DIR}/src/Dockerfile.proxy" \
    --no-cache \
    --push \
    "${ROOT_DIR}/src"

echo "==> Building registration image: ${REPO}/aoai-proxy-registration:${TAG}"
docker buildx build \
    --platform "${PLATFORMS}" \
    --tag "${REPO}/aoai-proxy-registration:${TAG}" \
    --file "${ROOT_DIR}/docker/Dockerfile.registration" \
    --no-cache \
    --push \
    "${ROOT_DIR}"

echo "==> Cleaning up local buildx cache to free disk space"
docker buildx prune --force

echo "==> Done. Update docker/.env or docker-compose.yml with:"
echo "    PROXY_IMAGE=${REPO}/aoai-proxy:${TAG}"
echo "    REGISTRATION_IMAGE=${REPO}/aoai-proxy-registration:${TAG}"
