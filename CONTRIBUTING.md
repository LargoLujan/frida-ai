# Contributing to Frida.ai

Thanks for your interest in Frida.ai. This guide explains how to set up the project, how changes are reviewed and the rules that keep users' chat data safe. For what the app does and how it works, see the [README](https://github.com/LargoLujan/frida-ai#readme).

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). Security problems go through the [security policy](SECURITY.md), not public issues.

## Prerequisites

- **Windows 10 (version 1809 or later) or Windows 11, x64.** The app is WPF with WebView2 and only builds and runs on Windows.
- **.NET 10 SDK.** CI installs `10.0.x` with `actions/setup-dotnet`.
- **Microsoft Edge WebView2 Runtime**, needed to run the app (the embedded Teams client).
- Optional: **[Ollama](https://ollama.com)** with `llama3.2:3b` and `nomic-embed-text` for the local features (triage, embeddings and RAG, summaries, tone rewriting). Without it those features degrade as described in the README.
- Optional: the **Azure CLI** (`az login`) and the **Fabric CLI** (`fab`) if you work on the Fabric and Azure tools.
- Optional, for the installer: **[Inno Setup 6+](https://jrsoftware.org/isinfo.php)** (`iscc.exe` on the `PATH`) and PowerShell 7 (`pwsh`).

You do not need an API key to develop: the **Simulation** provider (`Provider = simulation`) runs the whole flow without calling any API and records estimated costs.

## Build, run and test

Run the same commands as the [CI workflow](https://github.com/LargoLujan/frida-ai/blob/main/.github/workflows/ci.yml) from the repository root:

```powershell
dotnet restore TeamsJarvis.slnx
dotnet build TeamsJarvis.slnx -c Release --no-restore
dotnet test TeamsJarvis.slnx -c Release --no-build
```

To run the app from source:

```powershell
dotnet run --project src/TeamsJarvis
```

For a quick test run during development, `dotnet test tests/TeamsJarvis.Tests` also works.

The test project is xUnit and targets `net10.0-windows10.0.17763.0` so it can reference the WPF app. At the latest CI run on `main` it ran 123 tests with 0 failures. The SQLite integration tests use a temporary database through `AppPaths.OverrideDataDir`, so they never touch your real data under `%LOCALAPPDATA%\TeamsJarvis`. Add tests for any logic you change; parsers, validators and estimators live in `src/TeamsJarvis/Core` and `src/TeamsJarvis/Tools` without UI dependencies so they can be tested directly.

## Installer

The installer is built with `installer/publish.ps1` and `installer/frida-setup.iss`. The steps, and the helper installers you must place in `installer/redist/`, are in [installer/README.md](https://github.com/LargoLujan/frida-ai/blob/main/installer/README.md) (Spanish). Code-signing options are in [SIGNING.md](https://github.com/LargoLujan/frida-ai/blob/main/SIGNING.md) (Spanish).

## Repository layout

| Path | Contents |
|---|---|
| `TeamsJarvis.slnx` | Solution with the app and the test project |
| `src/TeamsJarvis/Core/` | Pipeline, agents, prompts, stores and pure logic (no UI) |
| `src/TeamsJarvis/Tools/` | Agent tools, the CLI validator and the MCP bridge |
| `src/TeamsJarvis/UI/` | Main window (partial classes), dialogs, tray, toasts, theme, mascot |
| `src/TeamsJarvis/Resources/teamsReader.js` | DOM reader injected into Teams web |
| `src/TeamsJarvis/appsettings.json` | Default configuration (`Jarvis` section) |
| `tests/TeamsJarvis.Tests/` | xUnit unit and SQLite integration tests |
| `installer/` | Publish script, Inno Setup script and the Ollama model script |
| `sign/sign.ps1` | Self-signed Authenticode signing script |
| `.github/workflows/ci.yml` | Build and test on Windows |

The README has a fuller [project structure](https://github.com/LargoLujan/frida-ai#project-structure) with the role of each key component.

**Naming.** The product is Frida.ai, but the solution, namespace, `TeamsJarvis.exe` and the `%LOCALAPPDATA%\TeamsJarvis` data folder keep the original *TeamsJarvis* name so existing installations and their encrypted data keep working. Do not rename these internal identifiers.

**Language.** The user interface, prompts, installer and most code comments are in Spanish; the README is in English. Keep new UI text in the same language as the surrounding interface.

## How to propose a change

1. **Start with an issue.** Open a bug report or feature request (or comment on an existing issue) so the change can be discussed before you write code.
2. **Create a branch from `main`** named `type/short-description`, for example `fix/assistant-panel-ui` or `ci/github-actions`.
3. **Keep pull requests small** and focused on one change. Separate refactoring from behaviour changes.
4. **Open a pull request against `main`**, fill in the template and link the issue with `Closes #<number>`.
5. **CI must be green.** The workflow builds and tests every pull request to `main`.
6. Update the README or other docs when you change behaviour, settings or data handling.

### Commit messages

Follow the style already in the history:

- A subject line in the imperative mood, sentence case, no prefix and no trailing period, for example `Add a CI workflow that builds and tests on Windows`.
- A blank line, then a body that explains what changed and why, wrapped at about 72 characters.
- Reference the issue in the body, for example `Closes #3`.

## Privacy rules for contributions

Frida.ai reads work chats, so these rules are strict:

- **Never include real chats or real people** in test data, fixtures, screenshots, recordings, issues or pull requests. Use fictional names and organisations (for example, Contoso). The demo in the README was recorded in Simulation mode with a fictional chat.
- **Never commit or paste secrets**: API keys, tokens, `settings.json` with a key, `jarvis.db`, or anything from `%LOCALAPPDATA%\TeamsJarvis`.
- **Redact logs before sharing them.** The files in `%LOCALAPPDATA%\TeamsJarvis\logs` contain chat and author names, tool inputs and, in some cases, part of the model output.
- **Do not weaken the safeguards** described in the README: native tools stay read-only, the app never sends Teams messages, and chat content, files and tool results are treated as data, never as instructions.

## License

Frida.ai is distributed under the [MIT License](https://github.com/LargoLujan/frida-ai/blob/main/LICENSE). By submitting a contribution, you agree that it is licensed under the same terms.
