# Copilot Customization Reference

Detailed anatomy and examples for each customization type. Read this file when the SKILL.md decision framework points to a type and the user needs implementation guidance.

## Instructions

### Flavors

| Flavor | File | Scope |
|--------|------|-------|
| Repo-wide | `.github/copilot-instructions.md` | Every chat request |
| Path-specific | `.github/instructions/*.instructions.md` | Files matching `applyTo` glob |
| Cross-agent | `AGENTS.md` or `CLAUDE.md` at repo root | Detected by VS Code, Claude Code |

### Path-specific frontmatter

```yaml
---
name: 'React Components'
description: 'Standards for React component files'
applyTo: 'src/components/**/*.tsx'
---
```

### Good candidates for instructions

- Team coding standards and naming conventions
- Build/test/lint commands
- Architectural constraints
- Security guardrails
- Technology stack declarations

### Bad candidates

- One-off tasks (use prompt files)
- Personal preferences (use user-level VS Code settings)

## Prompt Files

### Location

`.github/prompts/*.prompt.md`

### Frontmatter

```yaml
---
name: 'New API Endpoint'
description: 'Scaffold a complete REST endpoint with validation, handler, and test'
tools:
  - run_in_terminal
  - file_search
---
```

### Key features

- Support `${input:varName}` for user input at invocation time
- Support workspace variables: `${file}`, `${selection}`, `${workspaceFolder}`
- Can reference agents with `agent:` frontmatter field
- Can reference tools in frontmatter
- Invoked with `/` in chat

## Skills

### Location

```
.github/skills/{skill-name}/
├── SKILL.md          ← required entry point
├── supporting-file   ← optional reference files
└── templates/        ← optional templates
```

### Frontmatter

```yaml
---
name: 'skill-name'           # max 64 chars, lowercase/numbers/hyphens
description: 'What it does and when to use it'  # max 1024 chars
---
```

### Key characteristics

- Auto-discovered by task match (agent reads SKILL.md when relevant)
- Portable across agents and repos
- Can include supporting scripts, templates, and reference files
- SKILL.md body should stay under 500 lines
- Use progressive disclosure: core logic in SKILL.md, details in separate files

### Good candidates

- Repeatable capabilities with project-specific knowledge
- Workflows the agent should discover on its own
- Knowledge bundles that enrich any agent's ability

### Bad candidates

- General programming knowledge the model already has
- Simple rules (use instructions instead)
- Tasks requiring a distinct persona (use an agent)

## Agents

### Location

`.github/agents/*.agent.md`

### Frontmatter

```yaml
---
name: 'agent-name'           # the @mention handle
description: 'What I do'     # shown in agent picker
tools:                        # optional: restrict available tools
  - read_file
  - grep_search
  - run_in_terminal
agents:                       # optional: restrict which sub-agents I can call
  - implementer
  - tester
---
```

### Key characteristics

- Full persona with identity, system prompt, tool set, and model preference
- Invoked by `@mention` in chat
- Can spawn sub-agents for delegation
- Can be restricted from user invocation (`user-invokable: false`) to serve only as a sub-agent
- Body defines behavior, approach, output format, and constraints

### Good candidates

- Specialist roles: reviewer, planner, implementer, security auditor
- Tasks requiring a distinct persona or constrained tool access
- Workflows where the agent makes autonomous tool decisions

## Sub-Agents

### Key characteristics

- Same `.agent.md` file format as agents
- Spawned by another agent, not by the user
- Each gets its own isolated context window
- Prevents one task's details from polluting another
- Can run in parallel when tasks are independent

### Controlling access

```yaml
# Agent can be invoked by user AND by other agents (default)
user-invokable: true

# Agent can ONLY be used as a sub-agent
user-invokable: false

# Agent can ONLY be invoked by user (never as sub-agent)
disable-model-invocation: true
```

## Handoffs

Handoffs are an alternative to sub-agents that keep the user in control.

```yaml
handoffs:
  - label: Start Implementation
    agent: implementer
    prompt: Implement the tasks outlined above.
    send: false    # false = user clicks to send; true = auto-sends
```

- Use handoffs when learning a workflow or when human judgment is needed between steps
- Use sub-agents when the workflow is proven and throughput matters

## Hooks

### Location

`.github/hooks/*.json`

### Configuration

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "type": "command",
        "command": "npx prettier --write \"$TOOL_INPUT_FILE_PATH\""
      }
    ]
  }
}
```

### Lifecycle events

| Event | Fires when | Use case |
|-------|-----------|----------|
| SessionStart | New agent session begins | Inject project context |
| UserPromptSubmit | User sends a prompt | Audit requests |
| PreToolUse | Before any tool runs | Block dangerous operations |
| PostToolUse | After a tool completes | Auto-format, lint, log |
| PreCompact | Before context is compacted | Export state before truncation |
| SubagentStart | Sub-agent is spawned | Track nested agent usage |
| SubagentStop | Sub-agent completes | Aggregate results |
| Stop | Agent session ends | Generate reports, cleanup |

### Key characteristics

- Deterministic: runs shell commands, not AI guidance
- Cannot be bypassed by prompts
- Exit code 0 = success, exit code 2 = blocking error

## The Layering Principle

These types are layers, not alternatives. A mature project uses multiple types together:

```
Instructions  → the constitution (always-on rules)
Prompt files  → the playbook (repeatable recipes)
Skills        → the expertise (teachable capabilities)
Agents        → the team (specialist personas)
Sub-agents    → the delegation (isolated execution)
Hooks         → the enforcement (deterministic automation)
```

## Cross-Agent Compatibility

| Concept | Copilot (VS Code) | Claude Code |
|---------|-------------------|-------------|
| Instructions | `copilot-instructions.md` | `CLAUDE.md` |
| Modular rules | `*.instructions.md` | `.claude/rules/*.md` |
| Skills | `.github/skills/` | `.claude/skills/` |
| Agents | `.github/agents/*.agent.md` | `.claude/agents/*.md` |
| Hooks | `.github/hooks/*.json` | `.claude/settings.json` |
| Model override | Not supported | `model:` in agent frontmatter |

The mental model is portable. `SKILL.md` is even the same file format across both ecosystems.

## Source

This reference is based on content from [coderandhiker/copilot-when-to-use-what](https://github.com/coderandhiker/copilot-when-to-use-what), verified against documentation as of February 2026.
