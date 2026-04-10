---
name: copilot-customization-skill-authoring
description: Create and review skill files in .github/skills/. Use this skill when asked to create a new skill, review an existing skill, or improve skill quality. Applies best practices from agent-skills-best-practices.md to ensure skills are concise, discoverable, and effective.
---

# Skill Authoring

Create, review, and improve skill files following established best practices.

## Reference

Best practices guide: `.github/skills/copilot-customization-skill-authoring/agent-skills-best-practices.md`

> This is an external reference adapted from Anthropic's Claude skill-authoring docs. It uses Claude-specific terminology but the principles apply to Copilot skills. Where it says "Claude", read "the agent".

Read this file before creating or reviewing any skill. Key principles:

- **Concise is key** — only add context the agent doesn't already have
- **SKILL.md body under 500 lines** — use progressive disclosure for longer content
- **Description drives discovery** — write in third person, include what it does AND when to use it

## Value Gate

Before adding content to a skill, ask:

> "Would a capable model (e.g., GPT-3.5-Turbo) already know this without being told?"

If yes, leave it out. Skills should focus on **project-specific** knowledge — not general programming concepts. This keeps skills concise and avoids wasting context window tokens on things the agent already knows.

- ✅ "Run `python3 scripts/check_prerequisites.py` before starting the pipeline" — project-specific workflow
- ✅ "Use `uv` instead of `pip` for dependency management" — team decision
- ✅ "Source images go in `source/calendar-joke-images/`" — repo-specific path
- ❌ "Python virtual environments isolate dependencies" — general knowledge
- ❌ "Use `git add` to stage files" — any model knows this
- ❌ "JSON is a data format" — obvious

## Creating a Skill

1. Create directory: `.github/skills/{skill-name}/`
2. Create `SKILL.md` with YAML frontmatter (`name`, `description`)
3. Add body content: purpose, when to use, steps, examples
4. Run the review checklist below
5. Iterate until the skill passes review

### Frontmatter Rules

```yaml
---
name: my-skill-name      # max 64 chars, lowercase/numbers/hyphens only
description: Does X when Y happens. Use when the user asks about Z.  # max 1024 chars, third person
---
```

### Naming Conventions

- Prefer gerund form: `processing-pdfs`, `managing-todos`
- Acceptable alternatives: `pdf-processing`, `process-pdfs`
- Avoid: `helper`, `utils`, `tools`, `data` (too vague)

### Body Structure

A good SKILL.md body follows this pattern:

```markdown
# {Title}

{One-line summary of what this skill does.}

## When to Use (optional)
{Specific triggers — only include if the description doesn't fully convey when to activate.}

## {Core Instructions}
{Steps, rules, examples — the actionable content.}
```

A **When to Use** section is helpful when activation context is nuanced or multi-faceted. If the YAML `description` already has clear trigger terms, this section is redundant — don't repeat yourself.

## Reviewing a Skill

When creating or reviewing a skill, evaluate it against the checklist below. Reference `agent-skills-best-practices.md` for detailed rationale.

### Review Checklist

#### Discovery
- [ ] Description is specific and includes key trigger terms
- [ ] Description states both what the skill does AND when to use it
- [ ] Description is written in third person
- [ ] Name uses lowercase/hyphens, max 64 characters

#### Content Quality
- [ ] SKILL.md body is under 500 lines
- [ ] Only includes context the agent doesn't already have
- [ ] Examples are concrete, not abstract
- [ ] Consistent terminology throughout
- [ ] No time-sensitive information

#### Structure
- [ ] Progressive disclosure used — details in separate files if needed
- [ ] File references are one level deep (no chains of references)
- [ ] Workflows have clear, numbered steps
- [ ] Repo-relative paths and links use forward slashes; OS-specific absolute paths are allowed when required
- [ ] Long-running processes (servers, watchers) use background terminal pattern — see [long-running-processes.md](long-running-processes.md)

#### Actionability
- [ ] Steps are specific enough to follow without guessing
- [ ] Error handling and edge cases are addressed
- [ ] Degrees of freedom are appropriate — prescriptive for fragile/critical steps, flexible where the agent can reasonably decide

### Review Process

After creating or modifying a skill:

1. Read the skill file end-to-end
2. Walk through the review checklist above — flag any unchecked items
3. For each flag, suggest a specific improvement (not just "fix this")
4. Apply fixes and re-check until all items pass
5. If the skill references scripts or tools, verify they exist and work

### Nudges

When a skill doesn't meet standards, suggest improvements conversationally:

- "The description says 'helps with X' — can we make it more specific about when to activate?"
- "This SKILL.md is getting long — want to move the reference tables into a separate file?"
- "The steps assume the agent knows about Y — should we add a quick note?"
- "This name is pretty generic — would `managing-deployments` be clearer than `deploy`?"

## Reviewing Existing Skills

To review all skills in the repo:

1. List directories in `.github/skills/`
2. For each skill, read `SKILL.md` and run the review checklist
3. Report findings grouped by skill, with specific improvement suggestions
