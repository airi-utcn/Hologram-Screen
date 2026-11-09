#!/usr/bin/env bash
# Serve the project so the browser viewer can load out/ (browsers block file:// fetches).
#   ./serve.sh            -> http://localhost:8000/viewer/
#   on the HLD PC:  open  http://localhost:8000/viewer/?hld=1  full-screen (F11) on the HLD monitor
cd "$(dirname "$0")"
PORT="${1:-8000}"
echo "viewer:  http://localhost:$PORT/viewer/"
echo "on HLD:  http://localhost:$PORT/viewer/?hld=1   (F11 on the HLD monitor)"
exec python3 -m http.server "$PORT" --bind 127.0.0.1
