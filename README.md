<div align="center">

# Frida.ai

**An AI reply assistant for Microsoft Teams that reads your conversations, checks the facts with read-only tools and suggests a grounded reply, without ever sending anything for you.**

[![CI](https://github.com/LargoLujan/frida-ai/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/LargoLujan/frida-ai/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-5C2D91)
![WebView2](https://img.shields.io/badge/WebView2-Microsoft%20Edge-0078D7?logo=microsoftedge&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)
![SQLite](https://img.shields.io/badge/SQLite-DPAPI%20encrypted-003B57?logo=sqlite&logoColor=white)
![LLM providers](https://img.shields.io/badge/LLM-Claude%20%7C%20OpenAI%20%7C%20Gemini%20%7C%20Ollama-D97757)
![MCP](https://img.shields.io/badge/MCP-client-1F1F1F)
![xUnit](https://img.shields.io/badge/tests-xUnit-5E1F87)

<br>

<img src="docs/media/frida-ai-demo.gif" alt="Frida.ai demo: fictional chat, reading the conversation, suggested reply with sources, settings and API costs" width="900">

<sub>Simulation mode with a fictional demo chat; no real Teams account or AI provider was used. The interface is in Spanish.</sub>

</div>

Frida.ai is a Windows desktop app that **embeds Microsoft Teams** and adds an assistant panel next to it. It reads the chat you have open, builds context from the conversation, your knowledge folder and the conversation's memory, and, **when you press the button**, asks an AI agent to investigate with read-only tools (Microsoft Fabric, Azure CLI, downloaded attachments) and any MCP servers you configure, and to propose a reply that cites where each fact came from. You review it, copy it and paste it into Teams yourself. It is an open-source personal project at version 1.0.0; the user interface, prompts and installer are in Spanish. Contributions are welcome.

> **Naming:** the product is branded **Frida.ai** everywhere in the UI and this repository is `frida-ai`. The internal identifiers (solution, namespace, `TeamsJarvis.exe`, the `%LOCALAPPDATA%\TeamsJarvis` data folder) keep the original name *TeamsJarvis* so existing installations and their encrypted data keep working. The logo is a schnauzer.

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [How It Works](#how-it-works)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Usage](#usage)
- [Configuration](#configuration)
- [Cost Control](#cost-control)
- [Security and Privacy](#security-and-privacy)
- [Testing](#testing)
- [License](#license)
- [Author](#author)

## Overview

Answering work chats often means leaving Teams to look something up: the status of a pipeline, the contents of a shared spreadsheet, a resource in Azure. Frida.ai keeps Teams and that research in one window and lets an LLM agent do the lookup, under a few deliberate design principles:

- **The last word is always yours: Frida.ai never sends anything.** It only proposes text. Not answering by accident is a design decision, and it also keeps the permissions it needs to a minimum.
- **Nothing spends credits automatically.** In the default embedded mode, triage and analysis run only when you click **Sugerir respuesta** (Suggest reply). Background reading only **indexes** messages locally for search, which is free. (In the optional Graph mode a background monitor does analyse monitored chats on its own; see [Optional Microsoft Graph mode](#optional-microsoft-graph-mode).)
- **Not intrusive with the tenant.** The normal mode is the **embedded Teams** (WebView2): it reads Teams without the Microsoft Graph API, needs no app registration and opens no listening port. Microsoft Graph mode is optional.
- **Minimal cost.** A cheap triage step decides whether a message deserves a reply before the expensive model is used, and free local work through [Ollama](https://ollama.com) handles triage, embeddings and RAG, summaries and tone rewriting.
- **Privacy.** Conversation text, memory, suggestions, notes and the API key are stored **encrypted** on your machine with Windows DPAPI, bound to your Windows user account.
- **Grounded answers.** The system prompt tells the agent never to invent data, to verify facts with tools, to list its sources and to lower its confidence when it cannot verify something. It also contains an anti-injection clause: chat content, documents and tool results are data, never instructions.

## Key Features

- **Embedded Teams.** The Teams web client runs inside the app (WebView2) with a persistent session in its own profile folder. Sign-in and call pop-ups open in their own windows, video calls work (camera, microphone, screen sharing) and a **Reconectar** (Reconnect) button clears the cache, cache storage and service workers without signing you out.
- **On-demand, grounded suggestions.** One click reads the open chat (optionally scrolling up to load history), runs triage and, if a reply is warranted, the full agent analysis. The result appears as a suggestion card with confidence and sources, plus a Windows toast with a **Copiar respuesta** (Copy reply) button when the window is in the background.
- **Two-tier triage.** A cheap or local model decides whether the new messages need your reply and explains why. Direct mentions skip triage. When nothing is needed, the panel says so instead of staying silent.
- **Multiple AI providers.** Claude through the official Anthropic SDK (adaptive thinking, configurable effort, prompt caching of the system prompt), OpenAI Chat Completions and Google Gemini over REST (both with function calling and vision), Ollama for free local models, and a **Simulation** provider that runs the whole flow without calling any API.
- **Read-only agent tools.** Microsoft Fabric REST API (GET only), `az` and `fab` CLI commands restricted to an allowlist of read verbs, attachments you downloaded from Teams (text formats, docx, xlsx) and, in Graph mode, SharePoint and OneDrive search and file reading plus access to other chats.
- **MCP servers.** Paste the standard `{"mcpServers": {...}}` JSON used by other MCP clients; stdio and HTTP servers are supported and their tools are offered to the agent.
- **Per-conversation memory.** After each analysis the agent updates a structured memory sheet for the chat (topics, decisions and agreements, pending tasks, people, dates and milestones). It is stored encrypted, editable in the Memory window and can be rebuilt locally with Ollama at no cost.
- **Local RAG over your notes.** Markdown and text notes in a knowledge folder are chunked and embedded locally (`nomic-embed-text` via Ollama); only the passages relevant to the new messages are sent to the model.
- **Search.** Keyword search over stored messages with person and chat filters and a person to chat graph (who talks where and with whom), plus a semantic search toggle backed by local embeddings.
- **Notes and reminders.** Encrypted project notes and reminders with toast notifications, and export of a reminder to an `.ics` file (with a 10-minute alarm) to import into your calendar without touching the tenant.
- **Today panel.** Mentions, questions and reminders for the day gathered from stored data, with an optional summary of the day produced by the local model.
- **Tone rewriting.** Rewrite a suggestion as cordial, formal, brief or technical with the local model, at no API cost.
- **Cost panel.** Estimated API spend today and this month, a month-end projection, a breakdown by model and all-time token totals, with a reminder that API usage is billed separately from any chat subscription.
- **Diagnostics.** One window that checks the AI provider, the triage provider, local embeddings, the Azure CLI session, MCP servers and, in Graph mode, the Graph sign-in.
- **Desktop polish.** Custom title bar, light and dark themes, collapsible suggestion card, recent suggestions per chat, a mascot that narrates progress and shows the triage decision with a green or red flag, optional soft barks, tray icon, start with Windows and close-to-tray.

## How It Works

The main window (`EmbeddedTeamsWindow`) hosts Teams web on the left and the suggestion panel on the right. A JavaScript reader (`Resources/teamsReader.js`) is injected into every page load; it reads the DOM of the open chat and posts it to the app through `window.chrome.webview.postMessage`.

```mermaid
flowchart TD
    T[Teams web client in WebView2] -->|DOM changes| R[Injected reader script]
    R -->|automatic read| IX[Index messages locally - no AI call]
    IX --> DB[Encrypted SQLite store]
    U[User clicks Suggest reply] --> R
    R -->|forced read with optional history| P[Analysis pipeline]
    P --> RAG[RAG lookup with local embeddings]
    RAG --> M{New message mentions the user}
    M -->|yes| A[LLM agent]
    M -->|no| TR{Triage model says reply}
    TR -->|no| O[Explanation shown in the panel]
    TR -->|yes| A
    A -->|tool calls| TL[Read only tools and MCP servers]
    TL -->|tool results| A
    A --> V[Verdict JSON parsed]
    V --> DB
    V --> S[Suggestion card and toast]
    S --> C[User reviews the reply and pastes it into Teams]
```

### Reading the Chat

- The reader script watches the page with a `MutationObserver` and, after a 2.5-second debounce, extracts up to the last 200 rendered messages: author, timestamp, text, attachment file names, image alt text and whether the message is yours or mentions you (Teams mention markup, or your profile name in the text). It also tracks which chat is open.
- **Automatic reads** (the **Auto-leer** toggle) only store the messages in the local, encrypted message index for search. They never call an AI model.
- **Sugerir respuesta** calls `window.__tjRead(true, rounds)`. If the **Histórico** checkbox is ticked, the script first scrolls up `HistoryRounds` times to load older messages. The payload is parsed by `BrowserPayloadParser` and handed to `AnalysisPipeline` with `force = true`.
- In embedded mode the pixels of images cannot be read (the Teams CDNs block cross-origin access), so the agent receives only their alt text. Real images are sent to vision models only in Graph mode.

### Building the Context

`AnalysisPipeline.RunAsync` selects the messages to analyse (the incoming messages after your last reply, or the last incoming message), then:

- indexes them in `ConversationGraphStore`;
- if RAG is enabled, embeds the last new messages locally and retrieves the top `RagTopK` passages (cosine similarity of at least 0.25) from the indexed knowledge folder;
- loads the chat's saved memory from `SuggestionStore`.

`AgentPromptBuilder` then assembles the system prompt (your name, role, reply language, extra instructions, the grounding and anti-injection rules, the reply style rules and the output format) and the user turn (chat name, saved memory, transcript, new messages, retrieved knowledge and, in Graph mode, a digest of other monitored chats). When RAG is not active, the top-level `.md` files of the knowledge folder are placed in the system prompt instead (up to 20 files, 20,000 characters each).

### Triage

Unless one of the new messages mentions you, and if `TriageEnabled` is on, a cheap model receives only the tail of the chat (last 6 messages, 200 characters each) and the new messages (300 characters each) and answers `{"reply": true|false, "reason": "..."}`. If the answer cannot be parsed, the pipeline errs on the side of analysing. The triage provider can differ from the drafting provider, for example local Ollama for triage and a cloud model for drafting. Default triage models per provider: `claude-haiku-4-5`, `gpt-4o-mini`, `gemini-2.0-flash`, `llama3.2:3b`.

### Agent Analysis

`AgentSelector` returns the `ILlmAgent` for the configured provider:

| Provider | Implementation | Tools | Images |
|---|---|---|---|
| `claude` | Anthropic SDK, adaptive thinking, `Effort` setting, system prompt cached with `CacheControlEphemeral`, up to 16,000 output tokens | Yes | Yes |
| `openai` | REST `chat/completions` with function calling | Yes | Yes |
| `gemini` | REST `generateContent` with function calling | Yes | Yes |
| `ollama` | Local `/api/chat`, single call | No | No |
| `simulation` | No API call; estimates tokens from the real prompts and records the estimated cost | Not executed | No |

The cloud agents run a tool loop of up to `MaxToolIterations` rounds and report each tool in the progress bubble. Tool results are truncated before going back to the model.

| Tool | What it does | Available in |
|---|---|---|
| `fabric_api` | GET requests to `https://api.fabric.microsoft.com/v1` with a token from your `az login` session | Both modes |
| `run_cli` | Runs `az` or `fab` commands that pass `CliCommandValidator`, with a 90-second timeout | Both modes |
| `read_attachment` | Lists or reads files in the local downloads folder (text formats, docx, xlsx) | Both modes |
| `mcp_<server>_<tool>` | Proxies the tools exposed by each configured MCP server | Both modes |
| `search_sharepoint` | Microsoft Graph search over files, list items and sites | Graph mode only |
| `read_sharepoint_file` | Downloads and extracts a SharePoint or OneDrive file (text formats, docx, xlsx; no PDF) | Graph mode only |
| `list_chats` | Lists the Teams chats you can access | Graph mode only |
| `get_chat_messages` | Reads up to 50 recent messages of another chat | Graph mode only |

In embedded mode, the tools that need Microsoft Graph are hidden from the agent so it does not waste tokens retrying calls that would fail.

### Verdict and Suggestion

The agent's last message must be a JSON object with `should_reply`, `suggested_reply`, `confidence`, `sources`, `reasoning` and `conversation_memory`. `VerdictParser` reads it tolerantly; if the format is wrong, the raw text becomes a low-confidence (0.2) suggestion. The pipeline then:

- saves the updated conversation memory (encrypted);
- stores the suggestion with the trigger summary and sources (encrypted) and raises `SuggestionAdded`, which shows the card and, if the window is not focused, a toast;
- logs token usage and estimated cost for both the triage and the analysis calls;
- reports a readable outcome when there is nothing to suggest or the provider fails (credits exhausted or billing problem, rate limit, invalid API key).

### Optional Microsoft Graph mode

With `SourceMode` set to `graph`, `TeamsMonitorService` (a hosted background service) polls the chats flagged as monitored in the `monitored_chats` table every `PollSeconds`, waits `DebounceSeconds` after the newest message in case the other person is still typing, never runs two analyses of the same chat at once, and runs the same pipeline **automatically** for new incoming messages (the first time it sees a chat it only records a starting point). In this mode `ContextBuilder` fetches up to `MaxContextMessages` messages through Graph, downloads up to `MaxImages` recent images (re-encoded to JPEG when larger than 2 MB, longest edge 1,568 px) and adds a digest of the last 5 messages of up to 8 other monitored chats, cached for 60 seconds. The tray menu can pause monitoring.

Authentication (`GraphAuthService`) has two modes: `azcli` reuses your Azure CLI session (no tenant or client ID needed) and `app` uses your own Entra ID app registration with interactive browser sign-in and the delegated read-only scopes `Chat.Read`, `User.Read`, `Sites.Read.All` and `Files.Read.All`. If the Azure CLI token lacks `Chat.Read`, listing chats fails with a 403 and the app explains that an app registration is needed. Note that the chat-selection window (`ChatSelectionWindow`) exists in the source but is not opened from the current main window, so the embedded mode is the primary, fully wired path.

## Tech Stack

| Area | Technology |
|---|---|
| Runtime | .NET 10 (`net10.0-windows10.0.17763.0`), C# with nullable reference types |
| Desktop UI | WPF with a custom `WindowChrome` title bar and light and dark themes; Windows Forms `NotifyIcon` for the tray |
| Embedded browser | Microsoft Edge WebView2 (`Microsoft.Web.WebView2` 1.0.4078.44) |
| Hosting and DI | `Microsoft.Extensions.Hosting` 10.0.9 (generic host inside WPF) |
| Claude | `Anthropic` SDK 12.35.1 |
| OpenAI and Gemini | REST over `HttpClient` (Chat Completions, `generateContent`) |
| Local AI | Ollama REST API: `llama3.2:3b` for triage, summaries and tone; `nomic-embed-text` for embeddings |
| Tool protocol | Model Context Protocol C# SDK (`ModelContextProtocol` 2.0.0-preview.2), stdio and HTTP clients |
| Microsoft cloud | `Microsoft.Graph` 6.2.0, `Azure.Identity` 1.21.0, Microsoft Fabric REST API, Azure CLI, Fabric CLI |
| Storage | SQLite via `Microsoft.Data.Sqlite` 10.0.9 and `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3; vectors stored as blobs with in-memory cosine similarity |
| Encryption | Windows DPAPI (`ProtectedData`, current-user scope) |
| Documents | `DocumentFormat.OpenXml` 3.5.1 (docx and xlsx text extraction) |
| Notifications | `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 (Windows toasts) |
| Tests | xUnit 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1, `coverlet.collector` 6.0.4 |
| Packaging | `dotnet publish` (self-contained, win-x64), Inno Setup 6+, PowerShell signing script |

## Project Structure

```text
frida-ai/
├── TeamsJarvis.slnx                  # Solution: app and test projects
├── src/TeamsJarvis/
│   ├── TeamsJarvis.csproj            # WPF app (.NET 10), package references, Frida.ai metadata
│   ├── App.xaml / App.xaml.cs        # Generic host, dependency injection, startup, RAG initialisation
│   ├── appsettings.json              # Default configuration ("Jarvis" section)
│   ├── frida.ico                     # Application icon
│   ├── Core/                         # Pipeline, agents, prompts, stores and pure logic (no UI)
│   ├── Tools/                        # Agent tools, CLI validator and MCP bridge
│   ├── UI/                           # Main window (partial classes), dialogs, tray, toasts, theme, mascot
│   └── Resources/teamsReader.js      # DOM reader injected into Teams web
├── tests/TeamsJarvis.Tests/          # xUnit unit and SQLite integration tests
├── installer/
│   ├── publish.ps1                   # Self-contained publish to installer/publish
│   ├── frida-setup.iss               # Inno Setup script
│   ├── scripts/pull-models.cmd       # Pulls the local Ollama models after install
│   └── README.md                     # Installer guide (Spanish)
├── sign/sign.ps1                     # Self-signed Authenticode signing script
├── SIGNING.md                        # Code-signing options and trade-offs (Spanish)
├── COST-OPTIMIZATION.md              # Offline cost simulation and savings levers (Spanish)
├── .claude/skills/teams-jarvis/      # Internal project-context notes for AI coding assistants (Spanish)
└── LICENSE
```

Key components in `src/TeamsJarvis`:

| Component | Role |
|---|---|
| `Core/AnalysisPipeline.cs` | Shared flow: message selection, RAG, triage, analysis, storage, cost logging, outcome events |
| `Core/AgentPromptBuilder.cs` | System prompt, user turn, triage prompts, tool list and knowledge loading |
| `Core/AgentSelector.cs`, `Core/ILlmAgent.cs` | Provider abstraction and selection |
| `Core/ClaudeAgent.cs`, `OpenAiAgent.cs`, `GeminiAgent.cs`, `OllamaAgent.cs`, `SimulatedAgent.cs` | Provider implementations |
| `Core/VerdictParser.cs` | Tolerant parsing of the verdict and triage JSON |
| `Core/SuggestionStore.cs` | Suggestions, chat memory, processing cursor, monitored chats, usage log |
| `Core/ConversationGraphStore.cs` | Encrypted message index and person to chat graph queries |
| `Core/KnowledgeIndexer.cs`, `KnowledgeStore.cs`, `EmbeddingService.cs`, `TextChunker.cs` | Local RAG: chunking, embeddings, vector store, retrieval |
| `Core/NotesStore.cs`, `ReminderService.cs`, `IcsExporter.cs`, `AttentionService.cs` | Notes, reminders, `.ics` export and the Today panel |
| `Core/LocalLlm.cs`, `MemoryBackfillService.cs`, `MemoryDoc.cs` | Free local tasks: day summary, memory rebuild, memory sections |
| `Core/CostEstimator.cs`, `CostSimulator.cs`, `TokenEstimator.cs` | Price table, cost estimation and offline monthly simulation |
| `Core/CryptoHelper.cs`, `UserSettingsService.cs` | DPAPI encryption and persisted user settings |
| `Core/GraphAuthService.cs`, `TeamsService.cs`, `TeamsMonitorService.cs`, `ContextBuilder.cs` | Optional Microsoft Graph mode |
| `Tools/CliCommandValidator.cs` | Allowlist validation for `run_cli` |
| `Tools/McpToolBridge.cs`, `Core/McpConfigParser.cs` | MCP client connections and configuration parsing |
| `UI/EmbeddedTeamsWindow.*.cs` | Main window split into WebView, Panel, Account and Mascot partial classes |

## Getting Started

### Prerequisites

- **Windows 10 (version 1809 or later) or Windows 11**, x64.
- **Microsoft Edge WebView2 Runtime** (the installer adds it).
- **.NET 10 SDK** to build from source.
- An **API key** for your AI provider (Anthropic, OpenAI or Google Gemini). Not needed for the Ollama or Simulation providers.
- Optional, free and recommended: **[Ollama](https://ollama.com)** with the local models:

  ```powershell
  ollama pull llama3.2:3b        # triage, summaries, tone rewriting, memory rebuild
  ollama pull nomic-embed-text   # embeddings for RAG and semantic search
  ```

  Without Ollama everything degrades gracefully: RAG falls back to the full knowledge folder, and tone rewriting, summaries and local memory rebuilds are unavailable.
- Optional: the **Azure CLI** signed in with `az login` for the Fabric and Azure tools, and the **Fabric CLI** (`fab`) on the `PATH` for `fab` commands.
- Optional, for the installer: **[Inno Setup 6+](https://jrsoftware.org/isinfo.php)** with `iscc.exe` on the `PATH`, and PowerShell 7 (`pwsh`).

### Build

```powershell
git clone https://github.com/LargoLujan/frida-ai.git
cd frida-ai
dotnet build src/TeamsJarvis
```

### Running

```powershell
dotnet run --project src/TeamsJarvis
```

On first launch:

1. Open **Configuración** (Settings) from the account menu (the avatar in the title bar).
2. In **IA & coste**, choose a provider and model and paste your API key (it is encrypted before being saved).
3. In **Perfil**, enter your name, role, reply language and any extra instructions.
4. If you use Ollama, set the triage provider to **Ollama (local)** in **Triaje & local**.
5. Save and let the app restart, sign in to Teams inside the app, open a chat and click **Sugerir respuesta** when you want a proposal.

The settings window also opens automatically at start-up when `AuthMode` is `app` and the tenant or client ID is missing.

### Installer

The installer is described in detail in [installer/README.md](installer/README.md) (Spanish). It produces a single setup `.exe` that installs the app per user (no administrator rights) under `%LOCALAPPDATA%\Programs\Frida.ai`, installs the WebView2 runtime silently and can optionally install Ollama and pull the local models.

```powershell
# 1) Publish the app (self-contained win-x64; .NET is not required on the target machine)
pwsh installer/publish.ps1

# 2) Download into installer/redist/:
#    - MicrosoftEdgeWebview2Setup.exe (Evergreen Bootstrapper, always required)
#    - OllamaSetup.exe (only if you want to bundle Ollama)

# 3) Compile the installer
iscc installer/frida-setup.iss
# -> installer/dist/Frida.ai-Setup-1.0.0.exe
```

Installer options: desktop shortcut, start with Windows (the same `HKCU\...\Run` value the app uses) and **install Ollama and the local models**. Installing over a previous version keeps your data, because it lives in `%LOCALAPPDATA%\TeamsJarvis`.

To avoid the "unknown publisher" warning, sign the executables. [SIGNING.md](SIGNING.md) (Spanish) compares the options honestly (self-signed, SignPath for open source, OV and EV certificates), and `sign/sign.ps1` builds in Release, creates or reuses a self-signed code-signing certificate and signs `TeamsJarvis.exe` with a timestamp:

```powershell
pwsh ./sign/sign.ps1                    # build, create the certificate if needed, sign
pwsh ./sign/sign.ps1 -InstallToTrusted  # also trust the certificate on this machine (requires admin)
```

## Usage

| Control | Where | What it does |
|---|---|---|
| **Sugerir respuesta** | Suggestion panel | Reads the open chat and runs triage and analysis (in embedded mode, the only action that generates suggestions) |
| **Copiar** | Suggestion panel | Copies the suggested reply to the clipboard |
| Tone selector and **Reescribir** | Suggestion panel | Rewrites the suggestion as Cordial, Formal, Breve or Técnico with the local model |
| **Recientes** / **Gasto de API** | Suggestion panel | Recent suggestions per chat and a quick cost summary in the mascot's bubble |
| **Auto-leer** | Toolbar | Turns automatic, free indexing of the open chat on or off (on by default) |
| **Histórico** | Toolbar | Scrolls up to load older messages before analysing (ticked by default) |
| **Hoy** | Toolbar | Today panel: mentions, questions and reminders, with a local summary of the day |
| **Buscar** | Toolbar | Keyword and semantic search over stored messages, filtered by person or chat |
| **Notas** | Toolbar | Notes and reminders, with `.ics` export |
| **Reconectar** / reload | Toolbar | Fixes the Teams "reconnect" state without signing out, or reloads the page |
| Account menu | Title bar | Settings, API costs, diagnostics, memory and history of the current chat, dark mode, barks, local memory rebuild, sign out of Teams (clears WebView2 data) and of Azure and Fabric (`az logout`) |
| Tray icon | Notification area | Open the app, pause Graph monitoring, exit; closing the window keeps the app running in the tray |

## Configuration

Settings are edited in the Settings window and saved to `%LOCALAPPDATA%\TeamsJarvis\settings.json`, which is layered on top of `src/TeamsJarvis/appsettings.json` at start-up. Both use a `Jarvis` section. Changes apply after a restart (the app offers to restart when you save).

| Setting | Default | Edited in | Description |
|---|---|---|---|
| `SourceMode` | `browser` | Settings, Teams tab | `browser` = embedded Teams (WebView2); `graph` = Microsoft Graph |
| `AuthMode` | `azcli` | Settings, Teams tab | Graph mode only: `azcli` (reuse `az login`) or `app` (own app registration) |
| `TenantId` / `ClientId` | empty | Settings, Teams tab | Required for `app` mode; `TenantId` is also passed to the Azure CLI credential if set |
| `Provider` | `claude` | Settings, AI tab | `claude`, `openai`, `gemini`, `ollama` or `simulation` |
| `Model` | `claude-opus-4-8` | Settings, AI tab | Drafting model; the list changes with the provider and free text is accepted |
| `Effort` | `high` | Settings, AI tab | `low`, `medium`, `high` or `max`; used by the Claude agent |
| `ApiKeyProtected` | empty | Settings, AI tab | The provider API key, DPAPI-encrypted with an `enc:v1:` prefix; leave the field empty to keep the current key |
| `TriageEnabled` | `true` | Settings, Triage tab | Run the cheap triage before the full analysis |
| `TriageProvider` | empty (same as `Provider`) | Settings, Triage tab | Provider for triage, for example `ollama` |
| `TriageModel` | empty (provider default) | `settings.json` only | Overrides the default triage model |
| `OllamaBaseUrl` | `http://127.0.0.1:11434` | Settings, Triage tab | Ollama endpoint for local triage, drafting, embeddings and local tasks |
| `EmbeddingModel` | `nomic-embed-text` | Settings, Triage tab | Local embedding model |
| `RagEnabled` | `true` | Settings, Triage tab | Inject only relevant knowledge passages instead of the whole folder |
| `RagTopK` | `5` | `settings.json` only | Number of passages injected per analysis |
| `IndexConversations` | `true` | Settings, Triage tab | Embed stored messages for semantic search |
| `Profile.Name` | empty | Settings, Profile tab | Your name; used in the prompt and for mention detection |
| `Profile.Role` | empty | Settings, Profile tab | Your role, used as context and guardrail |
| `Profile.Language` | `es` | Settings, Profile tab | Reply language (`es` or `en`); the agent may follow the other person's language |
| `Profile.ExtraInstructions` | empty | Settings, Profile tab | Free-form preferences appended to the system prompt |
| `MaxContextMessages` | `30` | Settings, System tab | Graph mode: messages fetched per analysis |
| `MaxImages` | `5` | Settings, System tab | Graph mode: maximum images sent per analysis |
| `StartWithWindows` | `false` | Settings, System tab | Adds or removes the `TeamsJarvis` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| `KnowledgeDir` | empty (`%LOCALAPPDATA%\TeamsJarvis\knowledge`) | Settings, Advanced tab | Folder with your `.md` and `.txt` notes |
| `Cli.Az` | enabled, verbs `list`, `show` | Settings, Advanced tab (enable only) | Azure CLI access for `run_cli`; verbs editable in the file |
| `Cli.Fab` | enabled, verbs `ls`, `get`, `exists`, `desc`, `api` | Settings, Advanced tab (enable only) | Fabric CLI access for `run_cli`; verbs editable in the file |
| `McpServers` | empty | Settings, Advanced tab | MCP servers (see below) |
| `MaxToolIterations` | `15` | `settings.json` only | Maximum tool-loop rounds per analysis |
| `HistoryRounds` | `3` | `settings.json` only | Scroll-up rounds when **Histórico** is ticked |
| `PollSeconds` | `20` | `settings.json` only | Graph mode polling interval (minimum 5) |
| `DebounceSeconds` | `10` | `settings.json` only | Graph mode wait after the newest message |
| `Theme` | `light` | Account menu | `light` or `dark` |
| `BarkEnabled` | `false` | Account menu | Soft mascot barks |

### Environment Variables

| Variable | Read by | Notes |
|---|---|---|
| `ANTHROPIC_API_KEY` | Anthropic SDK (`ClaudeAgent`) | Default when the provider is `claude` |
| `OPENAI_API_KEY` | `OpenAiAgent` | Used when the provider is `openai` |
| `GEMINI_API_KEY` | `GeminiAgent` | Used when the provider is `gemini` |

At start-up `UserSettingsService.ApplyApiKeyToEnvironment` decrypts the stored key and sets the variable that matches `Provider` for the app process only. Only one key is stored, so if the triage provider is a different cloud provider, its key must already be set in that provider's environment variable.

### MCP Servers

The Advanced tab accepts the standard format used by other MCP clients (a bare map or the internal array format also work). Use `url` instead of `command` for HTTP servers:

```json
{
  "mcpServers": {
    "azure": {
      "command": "npx",
      "args": ["-y", "@azure/mcp@latest", "server", "start"],
      "env": {}
    },
    "my-http-server": {
      "url": "https://example.com/mcp"
    }
  }
}
```

Each server's tools are exposed to the agent as `mcp_<server>_<tool>`. A server that fails to connect is reported in Diagnostics and does not stop the app.

## Cost Control

A chat subscription such as Claude Max covers claude.ai and Claude Code, **not** the API. Every suggestion produced by a cloud model is billed separately per token by the provider. The app keeps that spend low and visible:

- **Triage first:** only messages that need a reply reach the expensive model with tools.
- **Prompt caching (Claude):** the stable system prompt is cached, so tool-loop iterations read it at a fraction of the price.
- **Free local work:** triage, embeddings, RAG, summaries, tone rewriting and memory rebuilds can all run on Ollama.
- **Smaller payloads:** truncated tool results, RAG instead of the whole knowledge folder, image limits and resizing.
- **Visibility:** every call is recorded in a usage log and priced with the table in `CostEstimator` (USD per million tokens, local models at zero, unknown models priced conservatively at the Opus rate). The cost window shows today, this month, a projection and a breakdown by model.
- **Simulation provider:** runs the whole flow end to end without any API call and records estimated costs, so you can test at no cost.

[COST-OPTIMIZATION.md](COST-OPTIMIZATION.md) (Spanish) contains monthly estimates produced by the offline `CostSimulator` (regenerated by the `PrintMonthlyCostTable` test) for several model combinations, based on an assumed usage pattern described in that file. They are estimates, not measured bills.

## Security and Privacy

### What is stored and where

All data lives under `%LOCALAPPDATA%\TeamsJarvis` for the current Windows user.

| Location | Contents | Protection |
|---|---|---|
| `jarvis.db` (SQLite) | Indexed messages, suggestions (trigger summary, reply, sources), per-chat memory, knowledge chunks and embeddings, notes, reminders, processing cursors, monitored chats, usage and cost log | Message text, suggestion text and sources, memory, knowledge chunk text, note bodies and reminder text are DPAPI-encrypted. Chat names, author names, timestamps, note titles, embedding vectors and the usage log are stored in clear |
| `settings.json` | Your settings, including profile fields | Only the API key is encrypted (DPAPI) |
| `webview\` | WebView2 profile with the Teams session (cookies, cache) | Managed by WebView2 |
| `downloads\` | Attachments you download inside the embedded Teams | Plain files |
| `knowledge\` | Default knowledge folder for your notes | Plain files you provide |
| `auth_record.json` | Graph `app` mode sign-in record (the token cache is persisted by Azure.Identity) | Graph app mode only |
| `logs\jarvis-YYYYMMDD.log`, `logs\crash.log` | Information-level logs: chat and author names, tool calls with up to 300 characters of their input, CLI commands, errors and, when a verdict cannot be parsed, the first 500 characters of the model output | **Not encrypted** |

DPAPI uses the current-user scope with an application-specific entropy, so the encrypted data can only be decrypted by the same Windows user. Reminder `.ics` exports are written unencrypted to the Windows temporary folder.

### What is sent to the model provider

| Data | Triage call | Analysis call |
|---|---|---|
| Profile | Role | Name, role, language and extra instructions |
| Chat identity | Chat name | Chat name and chat ID |
| Recent messages | Last 6, cut to 200 characters | Full transcript as read (embedded mode: up to the last 200 rendered messages; Graph mode: up to `MaxContextMessages`) |
| New messages | Cut to 300 characters | Full text |
| Saved conversation memory | No | Yes |
| Personal knowledge | No | RAG passages, or the top-level `.md` notes when RAG is not active |
| Other chats and images | No | Graph mode only: digest of other monitored chats and up to `MaxImages` images |
| Tool output | No | Fabric API responses (up to 50,000 characters), CLI output (up to 40,000), attachment or SharePoint file text (up to 60,000), MCP tool output |

Calls go to the provider you configure (Anthropic, OpenAI or Google); what happens to the data there is governed by that provider's terms. With the `ollama` provider all of this goes to the Ollama endpoint (local by default) and with `simulation` nothing leaves the machine. Embeddings are always computed through the Ollama endpoint. Opening Diagnostics sends a one-line connectivity test to the configured provider.

### Safeguards in the code

- **The app never sends Teams messages.** Suggestions only go to the panel, a toast and the clipboard.
- **Native tools are read-only.** `fabric_api` only issues GET requests. `run_cli` accepts only `az` and `fab`, requires one of the allowed read verbs, rejects shell operators (`|`, `;`, `>`, `<`, `` ` ``, `&&`, `||`, `$(`) and unusual characters, and blocks `get-access-token`, `login` and `logout`. `read_attachment` only reads file names inside the downloads folder.
- **MCP tools are exposed as the server provides them**, so their behaviour depends on the servers you configure; only add servers you trust.
- **Anti-injection clause** in the system prompt: chat content, files and tool results are data, never instructions.
- **No listening ports in embedded mode**; the app only makes outbound connections.
- **Media permissions:** the embedded browser grants the permission requests of the pages it loads (camera, microphone, screen sharing, notifications) so that Teams calls work; the actual devices are chosen inside Teams.
- **Sign-out options:** clear the embedded Teams session data, or run `az logout`, from the account menu.

## Testing

```powershell
dotnet test tests/TeamsJarvis.Tests
```

The [CI workflow](.github/workflows/ci.yml) runs the same build and tests on Windows for every push to `main` and every pull request.

The xUnit project targets `net10.0-windows10.0.17763.0` so it can reference the WPF app, and contains 119 test cases (72 `[Fact]` tests and 47 `[InlineData]` cases, counted from the test sources). They cover:

| Test file | Area |
|---|---|
| `VerdictParserTests`, `TriageParserTests` | Verdict and triage JSON parsing, including malformed output |
| `CliCommandValidatorTests` | Allowed and rejected `az` and `fab` commands |
| `CostEstimatorTests`, `CostSimulatorTests` | Pricing, local models at zero and the monthly simulation (prints the cost table) |
| `OpenAiAgentTests` | Tool-argument parsing shared by the OpenAI and Gemini agents, and OpenAI and Gemini pricing |
| `McpConfigParserTests` | Standard, map and array MCP configuration formats |
| `TextChunkerTests` | Chunking, overlap, cosine similarity and hashing |
| `DocumentExtractorTests` | Text, docx and xlsx extraction |
| `HtmlToPlainTextTests` | Conversion of Teams message HTML |
| `MemoryDocTests` | Memory sheet sections |
| `IcsExporterTests` | iCalendar output |
| `AttentionServiceTests` | Today panel classification |
| `StoreIntegrationTests` | SQLite stores on a temporary database (`AppPaths.OverrideDataDir`), including encryption at rest |

## License

Distributed under the MIT License. See [LICENSE](LICENSE) for details.

## Author

**Manuel Luján Vílchez** — Senior Data Engineer

[![GitHub](https://img.shields.io/badge/GitHub-LargoLujan-181717?logo=github)](https://github.com/LargoLujan)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-Manuel%20Luj%C3%A1n%20V%C3%ADlchez-0A66C2?logo=linkedin)](https://www.linkedin.com/in/manuel-lujan-vilchez)
