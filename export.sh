#!/usr/bin/env bash
# Export every Plotly OCT figure given on the command line into out/.
#   ./export.sh ~/Downloads/W.I._OS.html  more_scans/*.html
cd "$(dirname "$0")"
[ $# -eq 0 ] && { echo "usage: ./export.sh figure.html|figure.json ..."; exit 1; }
for f in "$@"; do python3 exporter/plotly_to_hld.py "$f" -o out || exit 1; done
