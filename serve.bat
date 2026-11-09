@echo off
rem Windows version of serve.sh
cd /d "%~dp0"
echo viewer:  http://localhost:8000/viewer/
echo on HLD:  http://localhost:8000/viewer/?hld=1   (F11 on the HLD monitor)
python -m http.server 8000 --bind 127.0.0.1
