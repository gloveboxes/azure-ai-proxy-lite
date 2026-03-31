#!/bin/sh
set -e

CERT_DIR="/app/certs"
CERT_FILE="$CERT_DIR/cert.pem"
KEY_FILE="$CERT_DIR/key.pem"

# Generate a self-signed cert if one doesn't already exist
if [ ! -f "$CERT_FILE" ] || [ ! -f "$KEY_FILE" ]; then
  mkdir -p "$CERT_DIR"
  echo "Generating self-signed TLS certificate..."
  openssl req -x509 -newkey rsa:2048 -nodes \
    -keyout "$KEY_FILE" \
    -out "$CERT_FILE" \
    -days 365 \
    -subj "/CN=${TLS_HOST:-localhost}" \
    -addext "subjectAltName=DNS:${TLS_HOST:-localhost},DNS:localhost,IP:127.0.0.1" \
    2>/dev/null
  echo "TLS certificate generated for CN=${TLS_HOST:-localhost}"
fi

exec node /app/server.mjs
