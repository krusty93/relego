#!/bin/sh
# Writes the runtime configuration the SPA reads before it boots, so one static
# image can point at any relego-server without being rebuilt.
set -eu

api_url="${RELEGO_API_URL:-http://localhost:8080}"
api_url="${api_url%/}"

cat > /usr/share/nginx/html/config.js <<EOF
window.__RELEGO__ = { apiUrl: "${api_url}" };
EOF

echo "relego-web: API URL set to ${api_url}"
