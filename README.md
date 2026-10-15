# 🖥 ServiceList (`sl`) — Windows Service Manager CLI

A professional, production-ready .NET 8 service management tool.  
Built with **Spectre.Console** for rich terminal output and **System.CommandLine** for a clean CLI experience.

---

## ✨ Features

| Feature | Details |
|---|---|
| **Rich color output** | Status, start-type, table borders — all themed |
| **Emojis** | ✅⛔⏳⏸🔴🟡🟢🔵🟣 — meaningful at a glance |
| **Custom theme** | `sl-config.json` lets you change every color |
| **3 display modes** | Table (wide), Detail list, Fast one-liner |
| **Auto mode** | Picks table vs list based on terminal width |
| **Filter** | Matches name, PID, display name, description, status |
| **Sort** | Any field, reversible |
| **Tail** | Show last N services |
| **Service control** | Start / Stop / Restart / Pause / Kill |
| **Filter + action** | `--filter-stop`, `--filter-start`, `--filter-restart`, `--filter-kill` |
| **Generate config** | `sl --gen-config` writes a sample config to disk |

---

## 🚀 Build

```powershell
cd ServiceList
dotnet build -c Release
# Or publish as single EXE:
dotnet publish -c Release -r win-x64 --self-contained false
```

Copy the output to somewhere on your `PATH`, e.g. `C:\TOOLS\pyx\sl.exe`.

### Wrapper bat (optional)

```bat
@echo off
sl.exe %*
```

---

## 📖 Usage

```
sl [options]
```

### List / Filter

| Option | Short | Description |
|---|---|---|
| `--filter <kw...>` | `-f` | Filter by keyword (name, PID, display, description, status) |
| `--status-only <s>` | `-ss` | Show only: `running`, `stopped`, `paused`, `startpending`… |
| `--start-only <s>` | `-cc` | Show only: `auto`, `manual`, `disabled`, `boot`, `system` |
| `--sort-by <field>` | `-S` | Sort: `name`, `displayname`, `pid`, `status`, `start`, `description` |
| `--reverse` | `-r` | Reverse sort order |
| `--tail <n>` | `-T` | Show last N results |

### Display mode

| Option | Short | Description |
|---|---|---|
| `--table` | `-t` | Force table mode |
| `--list` | `-l` | Force detail-list mode |
| `--fast` | `-z` | Force compact one-liner mode |

### Service control

| Option | Short | Description |
|---|---|---|
| `--start <name...>` | `-sa` | Start service(s) by name |
| `--stop <name...>` | `-st` | Stop service(s) by name |
| `--restart <name...>` | `-R` | Restart service(s) by name |
| `--pause <name...>` | `-sp` | Pause service(s) by name |
| `--kill <name\|pid...>` | `-k` | Force-kill service(s) by name or PID |

### Filter + action (combined)

| Option | Short | Description |
|---|---|---|
| `--filter-stop <kw...>` | `-fs` | Filter and STOP all matched |
| `--filter-start <kw...>` | `-fS` | Filter and START all matched |
| `--filter-restart <kw...>` | `-fr` | Filter and RESTART all matched |
| `--filter-kill <kw...>` | `-fk` | Filter and KILL all matched |

### Config

| Option | Short | Description |
|---|---|---|
| `--gen-config` | `-gc` | Write sample `sl-config.json` to current directory |

---

## 🎨 Color Configuration

Run `sl --gen-config` to generate `sl-config.json`.  
Place it in any of:

1. Current working directory
2. Same folder as `sl.exe`
3. `%USERPROFILE%\.sl\sl-config.json`

### Color values use Spectre.Console markup:

```
bold red | bold green | bold yellow | bold cyan | bold magenta | bold blue | bold white | grey
```

You can also combine: `"bold underline red"`, `"italic green"`, etc.

### `DefaultDisplay` values:

- `"auto"` — table if terminal ≥ 131 cols, else detail list
- `"table"` — always table
- `"list"` — always detail list

---

## 💡 Examples

```powershell
# List all services (auto mode)
sl

# Filter by keyword
sl -f wuauserv

# Filter multiple keywords
sl -f "windows update" sql

# Show only running services, sorted by name
sl -ss running -S name

# Show only AUTO-start services
sl -cc auto

# Stop all services matching "IIS"
sl --filter-stop IIS

# Restart a specific service
sl -R Spooler

# Force-kill by PID
sl -k 1234

# Fast mode, last 20
sl -z -T 20

# Table mode, sorted by status, reversed
sl -t -S status -r

# Generate config
sl --gen-config
```

---

## 📦 Dependencies

| Package | Purpose |
|---|---|
| `Spectre.Console` | Rich terminal colors, tables, spinners |
| `System.CommandLine` | Argument parsing |
| `Microsoft.Extensions.Configuration.Json` | `sl-config.json` loading |
| `System.Management` (Windows built-in) | WMI for PID, description, start type |
| `System.ServiceProcess` (Windows built-in) | Service enumeration & control |

---

## ⚠ Notes

- Must run **as Administrator** for service control operations (start/stop/restart/kill).
- Listing services works without elevation.
- `System.Management` (WMI) is Windows-only — the project targets `net8.0-windows`.

## 👤 Author
        
[Hadi Cahyadi](mailto:cumulus13@gmail.com)
    

[![Buy Me a Coffee](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/cumulus13)

[![Donate via Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/cumulus13)
 
[Support me on Patreon](https://www.patreon.com/cumulus13)