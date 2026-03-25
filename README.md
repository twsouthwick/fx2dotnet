# fx2dotnet

A GitHub Copilot agent plugin that guides developers through migrating .NET Framework applications to modern .NET (targeting .NET 10 by default).

The goal is not to automate the migration — it's to **decompose it into small, reviewable chunks** that a developer can reason about and commit independently. The plugin identifies what needs to change, breaks the work into focused steps, and walks you through them one at a time. Each step produces a minimal, understandable diff rather than a sweeping transformation, so you stay in control and can validate every change before moving on.

## Prerequisites

- **VS Code** with [GitHub Copilot](https://marketplace.visualstudio.com/items?itemName=GitHub.copilot-chat) installed
- **.NET SDK 10** (preview) — see `global.json` for the pinned version
- **MCP servers** configured in `.mcp.json` (included in this repo) — VS Code starts them automatically

## Quick Start

1. Open your .NET Framework solution folder in VS Code alongside this plugin folder (multi-root workspace).
2. In Copilot Chat, invoke the **`.NET Framework to Modern .NET`** agent with your solution path:

   > @.NET Framework to Modern .NET `path/to/YourSolution.sln`

3. The orchestrator runs Assessment → Planning first, then walks you through each migration phase. Every step produces a small, committable diff — review and commit as you go.

To run a single phase instead of the full orchestration, invoke any agent directly (e.g., **`@Assessment of .NET Solution for Migration`**).

## How It Works

The plugin decomposes a .NET Framework → modern .NET migration into strictly ordered phases, each handled by a purpose-built agent:

| Phase | Agent | What it does |
|-------|-------|-------------|
| 1. Assessment | **Assessment** | Classifies every project (web host, Windows Service, library, etc.), audits NuGet package compatibility, identifies blockers, and produces a baseline report. Uses **Project Type Detector** for per-project classification. |
| 2. Planning | **Migration Planner** | Synthesizes assessment findings into a phased execution plan — topological project order, chunked package updates, SDK conversion candidates, and multitarget sequencing. Read-only; no code changes. |
| 3. SDK Conversion | **SDK-Style Project Conversion** | Converts legacy `.csproj` files to SDK-style format in dependency order, invoking **Build Fix** after each conversion to validate. |
| 4. Package Compatibility | **Package Compatibility Core** | Applies the planner's chunked NuGet update schedule (no-change → minor → major), with **Build Fix** validation after each chunk. |
| 5. Multitargeting | **Multitarget Migration** | Adds the modern target framework (`net10.0`) alongside the existing one, fixing API incompatibilities before and after the `TargetFrameworks` switch. |
| 6. Web Migration | **ASP.NET Framework → ASP.NET Core** | Creates a new ASP.NET Core host side-by-side, ports routes incrementally (using **Legacy Web Route Inventory** for endpoint discovery), and validates endpoint parity. |
| 7. Deferred Work | — | Post-migration items: EF6 → EF Core upgrade, System.Web adapter removal, and other flagged tasks. |

**Build Fix** runs an iterative build → diagnose → fix loop and is called throughout every phase to catch regressions early.

## Migration Flow

The diagram below maps the full end-to-end workflow across all phases. Each phase feeds into the next — from initial analysis through deferred post-migration work. Sub-steps are expanded within each phase to show the granular tasks. Green nodes are prep work, blue nodes are planning steps, gold nodes are committable tasks that produce code changes, lavender-shaded phases are processed layer by layer using dependency layers (see [Dependency Layers](#dependency-layers)), and peach-shaded phases are applied solution-wide.

```mermaid
flowchart TD
    %% ── Main horizontal flow ──
    A([Analysis]):::prep
    B([Solution Planning]):::planning

    A --> B

    %% ── SDK-Style Conversion (Layer by Layer with Build-Fix) ──
    subgraph SDK ["SDK-Style Projects"]
        direction LR
        SDKP([Convert Projects to SDK-Style]):::planning
        SDKP --> S1
        S1[Convert Project 1]:::task --> S2[Convert Project 2]:::task --> SN[Convert Project N]:::task
    end
    B --> SDK

    %% ── Package Update Plan (Solution-Wide) ──
    subgraph PKG ["Package Updates"]
        direction LR
        PKGP([Identify Package Update Plan]):::planning
        PKGP --> P1
        P1[Package 1]:::task --> P2[Package ...]:::task --> PN[Package N]:::task
    end
    SDK --> PKG

    %% ── In-Place Upgrades per Project (Layer by Layer) ──
    subgraph INPLACE ["Projects"]
        direction LR

        subgraph LIB1 ["Project 1"]
            direction TB
            L1P([Plan]):::planning
            L1P --> L1PHASE1
            subgraph L1PHASE1 ["Pre-multitarget: APIs that work on both"]
                L1PRE1["Change API/Pattern 1"]:::task
                L1PREN["Change API/Pattern N"]:::task
                L1PRE1 --> L1PREN
            end
            L1PHASE1 --> L1MT[Multitarget]:::task
            subgraph L1PHASE3 ["Post-multitarget: Remaining APIs"]
                L1POST1["Change API/Pattern 1"]:::task
                L1POSTN["Change API/Pattern N"]:::task
                L1POST1 --> L1POSTN
            end
            L1MT --> L1PHASE3
        end

        subgraph LIB2 ["Project 2"]
            direction TB
            L2P([Plan]):::planning
            L2P --> L2PHASE1
            subgraph L2PHASE1 ["Pre-multitarget: APIs that work on both"]
                L2PRE1["Change API/Pattern 1"]:::task
                L2PREN["Change API/Pattern N"]:::task
                L2PRE1 --> L2PREN
            end
            L2PHASE1 --> L2MT[Multitarget]:::task
            subgraph L2PHASE3 ["Post-multitarget: Remaining APIs"]
                L2POST1["Change API/Pattern 1"]:::task
                L2POSTN["Change API/Pattern N"]:::task
                L2POST1 --> L2POSTN
            end
            L2MT --> L2PHASE3
        end

        subgraph LIBN ["Project N"]
            direction TB
            LNP([Plan]):::planning
            LNP --> LNPHASE1
            subgraph LNPHASE1 ["Pre-multitarget: APIs that work on both"]
                LNPRE1["Change API/Pattern 1"]:::task
                LNPREN["Change API/Pattern N"]:::task
                LNPRE1 --> LNPREN
            end
            LNPHASE1 --> LNMT[Multitarget]:::task
            subgraph LNPHASE3 ["Post-multitarget: Remaining APIs"]
                LNPOST1["Change API/Pattern 1"]:::task
                LNPOSTN["Change API/Pattern N"]:::task
                LNPOST1 --> LNPOSTN
            end
            LNMT --> LNPHASE3
        end

        LIB1 --> LIB2 --> LIBN
    end
    PKG --> INPLACE

    %% ── Side-by-Side Web App Migration ──
    subgraph MVC ["Web App"]
        direction LR
        MVCP([Plan Side-by-Side Upgrade]):::planning
        MVCP --> M1
        M1[Create new Project]:::task
        subgraph STATIC ["Static Files & Bundling"]
            direction TB
            STP([Plan]):::planning
            ST1[Migrate Static Assets]:::task
            ST2[Migrate and configure CSS / TypeScript / JavaScript Bundling]:::task
            STP --> ST1 --> ST2
        end
        subgraph MXCUT ["Cross-Cutting Concerns"]
            direction TB
            MCC1[Logging]:::task
            MCC2[Configuration]:::task
            MCC3[Dependency Injection]:::task
            MCC4[Authentication]:::task
            MCC5[Authorization]:::task
            MCC6[Other]:::task
            MCC1 ~~~ MCC2
            MCC3 ~~~ MCC4
            MCC5 ~~~ MCC6
            MCC1 ~~~ MCC3 ~~~ MCC5
        end
        subgraph MART ["Migrate Routes"]
            direction LR
            subgraph MART1 ["Route 1"]
                direction TB
                MA1[Models / Views / Controllers]:::task
                MA1D[Dependencies]:::task
                MA1 --> MA1D
            end
            subgraph MART2 ["Route 2"]
                direction TB
                MA2[Models / Views / Controllers]:::task
                MA2D[Dependencies]:::task
                MA2 --> MA2D
            end
            subgraph MARTN ["Route N"]
                direction TB
                MAN[Models / Views / Controllers]:::task
                MAND[Dependencies]:::task
                MAN --> MAND
            end
            MART1 --> MART2 --> MARTN
        end
        M1 --> STATIC --> MXCUT --> MART
    end
    INPLACE --> MVC
    MVC --> DEFERRED

    %% ── Deferred Work ──
    subgraph DEFERRED ["Deferred Work"]
        direction TB
        G([Deferred Work Review and Final Validation]):::planning
        DW1[Upgrade EF6 → EF Core]:::task
        DW2[Remove System.Web Adapters]:::task
        DW3[Other Deferred Items]:::task
        G --> DW1
        G --> DW2
        G --> DW3
    end

    %% ── Styles ──
    classDef prep fill:#90EE90,stroke:#333,color:#000
    classDef planning fill:#87CEEB,stroke:#333,color:#000
    classDef task fill:#FFD700,stroke:#333,color:#000

    %% ── Dependency-layer phase shading ──
    style SDK fill:#C9B1E0,stroke:#6C4FA0,color:#000
    style INPLACE fill:#C9B1E0,stroke:#6C4FA0,color:#000

    %% ── Solution-wide phase shading ──
    style PKG fill:#F5C78E,stroke:#B85C0A,color:#000

    %% ── Legend ──
    subgraph Legend
        direction TB
        LEG1([Prep Work]):::prep
        LEG2([High-Level Planning]):::planning
        LEG3[Committable Task]:::task
        LEG4[Dependency Layers\n‹layer-by-layer›]:::topo
        LEG5[Solution-Wide]:::swide
    end
    Legend ~~~ A
    classDef topo fill:#C9B1E0,stroke:#6C4FA0,color:#000
    classDef swide fill:#F5C78E,stroke:#B85C0A,color:#000
```

## Dependency Layers

Phases 3 and 5 (SDK conversion and multitargeting) process projects **layer by layer**. During assessment, the `ComputeDependencyLayers` tool groups the topological project list into dependency layers:

- **Layer 1** — leaf projects with no in-solution dependencies.
- **Layer 2** — projects that depend only on Layer 1 projects.
- **Layer *N*** — projects that depend only on projects in earlier layers.

All projects in a layer must complete before the next layer begins, but projects **within** the same layer are independent and can be processed in any order. This guarantees every project's dependencies are already migrated before it is touched.

The diagrams below illustrate this with a hypothetical solution dependency graph.

**Dependency Graph:**

```mermaid
flowchart BT
    WebApp([WebApp])
    Services([Services])
    Data([Data])
    Auth([Auth])
    Common([Common])
    Logging([Logging])

    Common --> Data
    Common --> Auth
    Logging --> Services
    Data --> Services
    Auth --> Services
    Services --> WebApp
```

**Dependency Layers:**

```mermaid
flowchart LR
    subgraph Layer1 ["Layer 1 — leaves"]
        Common
        Logging
    end
    subgraph Layer2 ["Layer 2"]
        Data
        Auth
    end
    subgraph Layer3 ["Layer 3"]
        Services
    end
    subgraph Layer4 ["Layer 4"]
        WebApp
    end

    Layer1 --> Layer2 --> Layer3 --> Layer4
```

## Architecture

### Skills (Domain Policies)

Skills encode migration best practices that override default agent behavior in specific domains:

- **EF6 Migration Policy** — Retain Entity Framework 6 during the framework migration; upgrade to EF Core only as a separate post-migration effort.
- **System.Web Adapters** — Use `Microsoft.AspNetCore.SystemWebAdapters` to minimize code changes for `System.Web` types; rewrite to native ASP.NET Core APIs post-migration.
- **Windows Service Migration** — Replace `ServiceBase` with `BackgroundService` + Generic Host using `Microsoft.Extensions.Hosting.WindowsServices`.

### MCP Tool Servers

- **Microsoft.GitHubCopilot.AppModernization.MCP** — Project analysis, SDK-style conversion, build tooling.
- **Swick.Mcp.Fx2dotnet** — Discovers minimum NuGet package versions needed for a target framework, resolves feeds, and reports legacy packaging patterns.
