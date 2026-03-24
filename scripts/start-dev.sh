#!/usr/bin/env bash
set -e

echo "=== Community Survival Platform - Local Dev ==="
echo ""
echo "Services will start on:"
echo "  Gateway:      http://localhost:4000  (+ WebSocket)"
echo "  Simulation:   http://localhost:4001"
echo "  Event Log:    http://localhost:4002"
echo "  Progression:  http://localhost:4003"
echo "  Operator API: http://localhost:4004"
echo "  Admin Web:    http://localhost:5173"
echo ""

# Start all services
npx concurrently \
  --names "gateway,sim,events,progress,op-api,admin" \
  --prefix-colors "blue,green,yellow,magenta,cyan,white" \
  "npm run dev -w @game/gateway" \
  "npm run dev -w @game/simulation" \
  "npm run dev -w @game/event-log" \
  "npm run dev -w @game/progression" \
  "npm run dev -w @game/operator-api" \
  "npm run dev -w @game/admin-web"
