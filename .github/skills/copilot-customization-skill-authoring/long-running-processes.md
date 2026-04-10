# Long-Running Processes in Skills

Skills that launch servers, file watchers, or any process that runs indefinitely **must** instruct the agent to use a background terminal. A foreground terminal blocks all further interaction and requires the user to manually intervene (e.g., pressing `q` or `Ctrl+C`).

## Pattern

1. **Launch with `isBackground: true`** — the process gets its own terminal and returns a terminal ID immediately.
2. **Save the terminal ID** — use `get_terminal_output` with the ID to check logs or status anytime.
3. **Stop with `kill_terminal`** — use the saved terminal ID to shut down cleanly when done.
4. **Keep the foreground terminal free** — use it for follow-up commands like `curl`, build, or test.

## Example

### Launch the server

**IMPORTANT**: Launch as a **background process** so the terminal stays available.
Use `isBackground: true` when calling `run_in_terminal`.

Save the terminal ID. Use `get_terminal_output` to check server logs.

### Verify

In the **foreground terminal**, test the server:
```powershell
curl http://localhost:6001/swagger
```

### Stop the server

Use `kill_terminal` with the saved terminal ID.

## Why this matters

Without the background pattern, the agent loses control of the terminal session. The user has to manually stop the process before the agent can do anything else, breaking the autonomous workflow.
