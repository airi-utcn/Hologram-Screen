# Installing on another machine

Everything here is Python and a browser. No build step, no database, no account: clone, make an environment,
run. The parts that actually cost people an afternoon are at the bottom, under **When it does not work** -- they
are about Windows and about networks, not about this code.

Tested on Windows 11 with **Python 3.14**. Python 3.11 or newer should do.

## 1. Get the code

```
git clone https://github.com/airi-utcn/5G-Virtual-Classroom-Concept-.git
cd 5G-Virtual-Classroom-Concept-
```

Moving it on a memory stick instead? Copy everything **except** `.venv`, `.certs`, `__pycache__` and
`.pytest_cache`. An environment holds absolute paths to the machine that made it and will not work anywhere else;
the certificates are made fresh for whatever machine serves them.

## 2. Make an environment

```
python -m venv .venv
```

```
.venv\Scripts\python -m pip install -r requirements.txt
```

On macOS or Linux the interpreter is `.venv/bin/python` instead. Six packages, no compilers needed: numpy,
FastAPI, uvicorn, matplotlib, cryptography and pytest.

## 3. Check it works

```
.venv\Scripts\python -m pytest tests -q
```

Three minutes, and it should end `151 passed, 2 skipped`. Two are skipped on purpose.

## 4. Run it

On the machine itself:

```
.venv\Scripts\python -m xrstream.server
```

Then open `http://localhost:8000`.

For a headset, the browser will only start a VR session over `https`, so the server makes its own certificate:

```
.venv\Scripts\python -m xrstream.server --https --port 8443
```

It prints the addresses to open in the headset. Take the one on the network the headset is on -- not the
`172.x` one, which is a virtual adapter nothing else can reach. The browser warns once that the certificate
signed itself: **Advanced -> Proceed**. Then the **Enter VR** button, bottom right.

## 5. Run the experiments

```
.venv\Scripts\python -m xrstream.bench            # the five rooms      -> results/
.venv\Scripts\python -m xrstream.bench --lesson all   # the six lessons -> results/lessons/
.venv\Scripts\python -m xrstream.bench_cell       # a class on one cell -> results/cell/
```

The cell sweep is the long one: about fifty minutes for seventy-two runs. `--replot` redraws its charts from the
last run without repeating it.

---

# When it does not work

## Windows blocks the server, silently

This is the one that wastes the afternoon. A page that never loads, or times out on every port, is almost always
the firewall rather than the code.

**First, look for a block rule.** Windows writes one every time somebody dismisses the "allow network access?"
prompt, and a block rule beats any allow rule you add afterwards:

```
Get-NetFirewallRule -Direction Inbound -Action Block -Enabled True | Where-Object { ($_ | Get-NetFirewallApplicationFilter).Program -match 'python' }
```

If that prints anything, disable it -- in a PowerShell opened **as administrator**:

```
Get-NetFirewallRule -Direction Inbound -Action Block -Enabled True | Where-Object { ($_ | Get-NetFirewallApplicationFilter).Program -match 'python' } | Disable-NetFirewallRule
```

**Then allow the ports**, also as administrator:

```
New-NetFirewallRule -DisplayName "UC7 lesson viewer" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8443,8000 -Profile Any
```

Firewall changes take effect at once; the server does not need restarting.

## The headset has internet but cannot reach the laptop

Guest and campus networks nearly always isolate their clients: every device reaches the internet, no device
reaches any other. Phone hotspots often do the same. Nothing on the laptop can change that.

What works is the laptop's own hotspot -- **Settings -> Network & Internet -> Mobile hotspot** -- with the headset
joined to it. The laptop is then the network, and its address is usually `192.168.137.1`.

To see whether the headset is reachable at all, ping the subnet:

```
$ping = 1..254 | ForEach-Object { $p = New-Object System.Net.NetworkInformation.Ping; $p.SendPingAsync("192.168.137.$_", 700) }; [Threading.Tasks.Task]::WaitAll($ping); $ping | Where-Object { $_.Result.Status -eq 'Success' } | ForEach-Object { $_.Result.Address.ToString() }
```

(The `WaitAll` matters: without it the pings go out one at a time and the sweep takes three minutes instead of
three seconds.)

Two addresses means the headset is there. One means it is not, whatever its own Wi-Fi screen says.

## The address changed

It does, whenever the machine joins another network. Restart the server and read the address it prints; the
certificate is rebuilt for the new one automatically.

## The headset shows an old version

The Quest browser keeps a page open across sessions and re-entering VR does not reload it. Reload the page in the
headset after any change. The server sends `no-store` on everything it serves, so a reload is always enough.

## adb says no devices

Only needed for the USB route, which also needs Developer Mode, which needs a Meta developer account. The
wireless route above avoids all of it. If you do want the cable: the headset must be awake, the cable must carry
data rather than only power, and the prompts inside the headset have to be accepted.

## `git status` is full of noise

The ignore list is deliberately not in the repository, so a fresh clone does not have one. Copy `.gitignore` from
a working checkout, or put its lines in `.git/info/exclude`, which stays local.
