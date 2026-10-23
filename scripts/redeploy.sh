#!/usr/bin/env bash
# Rebuilds the MatPhone container and redeploys the dev stack (live-reload / test workflow).
#   ./scripts/redeploy.sh          rebuild + recreate, keep data
#   ./scripts/redeploy.sh --fresh  also delete the data and database volumes
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "${1:-}" == "--fresh" ]]; then
  docker compose -f docker-compose.dev.yml down -v
fi

docker compose -f docker-compose.dev.yml up -d --build --remove-orphans
echo
echo "MatPhone local-$(date -u +%Y%m%d) is running on http://localhost:6050 (SIP 5060, audio 6100-6199/udp)"
