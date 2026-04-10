---
name: copilot-customization-advisor
description: Recommends the right GitHub Copilot customization type (instructions, prompt files, skills, agents, sub-agents, hooks) for a given intent. Use when the user asks which customization to use, whether something should be a skill or agent, or how to structure Copilot repo customizations.
---

# Copilot Customization Advisor

Recommend the right customization type for a user's intent by applying the decision framework below.

## Decision Flow

Walk through these questions in order. Stop at the first "yes."

1. **Is this a rule that should ALWAYS apply?** → Instructions (`.github/copilot-instructions.md` or `.github/instructions/*.instructions.md`)
2. **Is this a repeatable recipe the user will invoke by name?** → Prompt file (`.prompt.md`)
3. **Is this a capability that any agent should be able to use?** → Skill (`SKILL.md`)
4. **Does it need its own identity, persona, or tool set?** → Agent (`.agent.md`)
5. **Does an agent need to delegate focused work with context isolation?** → Sub-agent (same `.agent.md`, spawned by another agent)
6. **Must something deterministically happen at a lifecycle point (no AI discretion)?** → Hook (`.json` in `.github/hooks/`)

## Quick Reference

| Type | Activation | Persistence | File | One-liner |
|------|-----------|-------------|------|-----------|
| Instructions | Automatic (always on) | Permanent | `*.instructions.md` | "Always do this" |
| Prompt files | `/` slash command | Permanent | `*.prompt.md` | "When I ask, do this sequence" |
| Skills | Agent selects when relevant | Permanent | `SKILL.md` | "Here's how to do this" |
| Agents | `@mention` in chat | Permanent | `*.agent.md` | "You are this person" |
| Sub-agents | Spawned by another agent | Ephemeral (per task) | Same `*.agent.md` | "Delegate this with isolation" |
| Hooks | Lifecycle event fires | Permanent | `*.json` | "Enforce this, no exceptions" |

## Rules of Thumb

Use these to validate your recommendation:

- **Instructions**: "If violating it would cause a code review rejection, it belongs in instructions."
- **Prompt files**: "If you'd save it as a snippet or template you invoke by name, it's a prompt file."
- **Skills**: "If it teaches the agent a new ability with project-specific knowledge, it's a skill."
- **Agents**: "If you'd assign it to a specific person on your team, it's an agent."
- **Sub-agents**: "If you'd CC someone on the email vs. assign them a separate ticket, that's the difference between sharing context and spawning a sub-agent."
- **Hooks**: "Instructions tell the agent what to think. Hooks control what actually happens."
- **Handoffs vs. sub-agents**: "Start with handoffs (user in the loop) to learn the workflow. Graduate to sub-agents once the flow is proven."

## Distinguishing Close Calls

When two types seem equally valid, use these tiebreakers:

**Skill vs. Agent**: Does it need its own identity? If the default Copilot agent could do the task once it has the knowledge, it's a skill. If the task requires a distinct persona, constrained tool set, or model preference, it's an agent.

**Instruction vs. Skill**: Is it a constraint or a capability? "Always use Zod for validation" is an instruction. "Here's how to create a Zod schema including the template, the test, and the registration" is a skill.

**Prompt file vs. Skill**: Does the user invoke it explicitly by name, or should the agent discover it by task match? Explicit invocation with `/` is a prompt file. Auto-discovery is a skill.

**Instruction vs. Hook**: Is compliance optional or mandatory? Instructions guide the AI (non-deterministic). Hooks execute shell commands (deterministic, guaranteed). If the agent might forget or skip it, use a hook.

## Responding to the User

When advising, follow this pattern:

1. Restate the user's intent in one sentence
2. Walk the decision flow, showing which questions apply
3. Name the recommended type and explain why
4. If relevant, note what the file structure would look like
5. If the intent spans multiple types, recommend layering (e.g., an instruction for the rule plus a skill for the how-to)

For detailed reference on each type including anatomy, frontmatter fields, directory structure, and real-world examples, read `copilot-customization-reference.md` in this skill directory.
