# Security Policy

Frida.ai reads the Microsoft Teams chat you have open, stores data locally and sends context to the AI provider you configure. Reports that help keep that data safe are welcome.

## Supported versions

Only the latest release receives security fixes.

| Version | Supported |
|---|---|
| [v1.0.0](https://github.com/LargoLujan/frida-ai/releases/tag/v1.0.0) (latest release) | Yes |
| Earlier builds and unreleased forks | No |

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Report it privately through GitHub:

1. Open the **Security & quality** tab of this repository.
2. Choose **Report a vulnerability**.

Direct link: https://github.com/LargoLujan/frida-ai/security/advisories/new

### What to include

- The version (release tag or commit) and your Windows version.
- The source mode (embedded Teams or Microsoft Graph) and the AI provider involved (`claude`, `openai`, `gemini`, `ollama` or `simulation`).
- A description of the issue and its impact.
- Steps to reproduce, or a minimal proof of concept.
- Any relevant settings, with secrets removed.

**Never include** real chat content, names of real people, API keys, tokens, or unredacted logs. The log files under `%LOCALAPPDATA%\TeamsJarvis\logs` contain chat and author names and parts of tool inputs, so redact them before sharing. Use fictional data (for example, Contoso) in any reproduction.

### Scope

Examples of relevant reports:

- A way around the read-only guarantees of the native tools: `fabric_api` issuing anything other than GET, `run_cli` running a command that `CliCommandValidator` should reject, or `read_attachment` reading outside the downloads folder.
- Data that the README describes as encrypted being stored or logged in clear.
- Chat content, documents or tool results causing the agent to act on them as instructions in a way that leads to an unsafe tool call.
- Anything that makes the app send a Teams message, which it is designed never to do.

Problems in third-party services (Microsoft Teams, the AI providers, the MCP servers you configure) should be reported to those projects.

## Response

Frida.ai is maintained by one person on a best-effort basis. I aim to acknowledge reports within 7 days, keep you updated while a fix is prepared, and credit you in the advisory unless you prefer otherwise.

## Data model in brief

This is a summary of the [Security and Privacy](https://github.com/LargoLujan/frida-ai#security-and-privacy) section of the README, which has the full tables.

**Stored locally.** All data lives under `%LOCALAPPDATA%\TeamsJarvis` for the current Windows user. Message text, suggestion text and sources, per-chat memory, knowledge chunk text, note bodies, reminder text and the API key are encrypted with Windows DPAPI (current-user scope, application-specific entropy), so only the same Windows user can decrypt them. Chat names, author names, timestamps, note titles, embedding vectors, the usage log, downloaded attachments and the log files are stored **in clear**. Reminder `.ics` exports are written unencrypted to the Windows temporary folder.

**Sent to the model provider.** The triage call receives your role, the chat name, the last 6 messages (cut to 200 characters) and the new messages (cut to 300 characters). The analysis call receives your profile, the chat name and ID, the transcript as read, the new messages, the saved conversation memory, relevant knowledge passages, tool output and, in Graph mode only, a digest of other monitored chats and images. Calls go to the provider you configure and are governed by its terms. With `ollama` the data goes to the Ollama endpoint (local by default) and with `simulation` nothing leaves the machine. Embeddings are always computed through the Ollama endpoint.

**Safeguards in the code.**

- The app never sends Teams messages; suggestions go only to the panel, a toast and the clipboard.
- Native tools are read-only: `fabric_api` issues GET requests only; `run_cli` accepts only `az` and `fab` with allowed read verbs, rejects shell operators and blocks `get-access-token`, `login` and `logout`; `read_attachment` reads only inside the downloads folder.
- MCP tools are exposed as each server provides them, so only add servers you trust.
- The system prompt treats chat content, files and tool results as data, never as instructions.
- No listening ports in embedded mode; the app only makes outbound connections.
