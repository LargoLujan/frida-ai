---
name: teams-jarvis
description: >
  Contexto profundo del proyecto TeamsJarvis (asistente .NET/WPF que lee chats de
  Microsoft Teams y usa un LLM como agente para sugerir respuestas fundamentadas).
  Cárgala SIEMPRE antes de trabajar en este repositorio: arquitectura, decisiones,
  requisitos acumulados del usuario, hechos verificados sobre costes y seguridad, y
  el roadmap de funcionalidades pendientes. Evita re-derivar el contexto y alucinar.
---

# TeamsJarvis — Skill de contexto del proyecto

Usa esta skill como fuente de verdad del proyecto. Si algo aquí contradice al código,
verifica el código (esta skill refleja el estado e intención en el momento de escribirla)
y actualiza la skill.

## 1. Qué es y para quién

Asistente personal ("Jarvis") de escritorio en **.NET 10 / WPF** para **Windows**. Corre en
la bandeja del sistema. Lee los chats de Microsoft Teams que el usuario elige, entiende el
contexto completo (texto **e imágenes**), y cuando llega un mensaje que merece respuesta usa
un **LLM como agente** (con herramientas de solo lectura a SharePoint, Fabric, Azure y
servidores MCP) para **sugerir** una respuesta fundamentada en datos reales del sistema.

**La app NUNCA envía nada.** Solo muestra la sugerencia; el usuario la revisa, la edita y la
copia. La última palabra siempre es del usuario. Esto es una decisión de diseño deliberada
que reduce los permisos necesarios (Graph de solo lectura) y el riesgo.

**Usuario objetivo inicial**: ingeniero de datos especializado en Microsoft
Fabric. Ese perfil se inyecta como contexto y guardarraíl. **Requisito nuevo**: la app debe
ser **extrapolable a cualquier persona de cualquier empresa** vía configuración (tenant,
proveedor de IA, etc.), no hardcodear nada específico de ninguna organización.

## 2. Estado actual (implementado y en verde)

- **Solución**: `TeamsJarvis.slnx` con `src/TeamsJarvis` (app) y `tests/TeamsJarvis.Tests` (xUnit, **98 tests verdes**).
- **Compila con 0 errores / 0 warnings**. Arranque verificado (smoke test).
- **Núcleo** (`src/TeamsJarvis/Core/`):
  - `GraphAuthService` — **dos modos** (`AuthMode`): `"azcli"` (por defecto, reutiliza la sesión de
    `az login` vía `AzureCliCredential` con scope `https://graph.microsoft.com/.default`; **no
    necesita Tenant/Client ID**) y `"app"` (`InteractiveBrowserCredential`, login en navegador, requiere
    Tenant+Client ID, con caché `AuthenticationRecord`). Tokens de Fabric/ARM siempre vía `az`. Scopes
    delegados del modo app en `AppScopes`. **Verificado que azcli funciona** en el tenant del usuario
    (autenticó `/me` correctamente).
    ⚠️ Bug histórico resuelto: el monitor autenticaba al arrancar con el tenant placeholder y abría una
    web de login rota. Ahora el modo por defecto es azcli (sin navegador) y el modo app lanza error claro
    si faltan tenant/client en vez de abrir la web rota. **No hay valores por defecto de tenant ni de
    perfil en appsettings** (el usuario los mete a mano).
  - `TeamsService` — lista chats (`/me/chats` con nombre legible), lee mensajes
    (`/chats/{id}/messages`), descarga imágenes (`hostedContents/$value` por HTTP manual),
    `HtmlToPlainText` (regex, testeado).
  - `ContextBuilder` — arma transcripción + imágenes recientes + **digest de otros chats**
    monitorizados (con caché TTL 60 s, thread-safe). Multi-conversación relacionada.
  - `ClaudeAgentService` — bucle agéntico manual con `BetaToolRunner`-equivalente hecho a mano
    sobre `client.Messages.Create`; reconstruye thinking/tool_use, ejecuta tools, realimenta
    resultados; `Thinking = ThinkingConfigAdaptive`, `OutputConfig.Effort` configurable, prompt
    caching (`CacheControlEphemeral`) en el system prompt estable. Veredicto JSON.
  - `VerdictParser` (static, testeado) — parseo tolerante del JSON del agente; degrada a
    sugerencia de baja confianza si el formato falla.
  - `TeamsMonitorService` (`BackgroundService`) — polling (`PollSeconds`, def 20) + debounce
    (`DebounceSeconds`, def 10); paralelo por chat, nunca dos análisis del mismo chat; avanza
    cursor `last_processed` para no reprocesar; si el último mensaje es del usuario, no sugiere.
  - `SuggestionStore` — SQLite (`Microsoft.Data.Sqlite`, SQL directo). Tablas: `monitored_chats`,
    `chat_state`, `suggestions`, `chat_memory`. Contenido sensible cifrado con DPAPI.
  - `CryptoHelper` — DPAPI (`ProtectedData`, `CurrentUser`) con entropía y prefijo `enc:v1:`.
  - `ImageHelper` — reescala imágenes >2 MB / >1568 px a JPEG antes de enviarlas al LLM.
  - `FileLogger` — log a archivo diario en `%LOCALAPPDATA%\TeamsJarvis\logs`.
- **Tools del agente** (`src/TeamsJarvis/Tools/`), todas `IAgentTool`, todas solo lectura:
  `search_sharepoint`, `read_sharepoint_file` (txt/md/csv/json/docx; PDF/xlsx fuera de v1),
  `fabric_api` (GET a `api.fabric.microsoft.com/v1`), `run_cli` (az/fab con
  `CliCommandValidator`, testeado), `list_chats`, `get_chat_messages`, y `McpToolBridge`
  (servidores MCP stdio/http → tools `mcp_{servidor}_{tool}`).
- **UI** (`src/TeamsJarvis/UI/`): `MainWindow` estilo canal de Teams (lista chats · conversación ·
  tarjeta de sugerencia editable), adaptable (colapsa lista bajo 720 px), tray
  (`TrayIconController`), toasts (`ToastService`, botón Copiar), `ChatSelectionWindow`,
  `HistoryWindow`, `DiagnosticsWindow`/`DiagnosticsRunner` (Graph/Anthropic/az/MCP).
- **DI**: host genérico (`Microsoft.Extensions.Hosting`) dentro de WPF en `App.xaml.cs`.

### Trampas técnicas ya resueltas (no repetir)
- **WPF + WinForms juntos** rompen los `ImplicitUsings` (meten `System.Windows.Forms` global,
  quitan `System.IO`) → ambigüedades `Application`/`MessageBox`/`Clipboard`/`Message` y
  `Path/File/Directory` no resueltos. **Fix aplicado** en el `.csproj`:
  `<Using Remove="System.Windows.Forms"/>`, `<Using Remove="System.Drawing"/>`,
  `<Using Include="System.IO"/>`. WinForms solo se usa explícitamente en el tray.
- **`response.StopReason`** del SDK Anthropic se compara **directamente con el string wire**
  (`response.StopReason == "tool_use"`), NO con `.ToString()` (que daría PascalCase y rompería
  el bucle de tools). Patrón oficial del SDK C#.
- **`System.Drawing.Common`**: `H.NotifyIcon.Wpf` exige v10; se eliminó ese paquete y se usa el
  tray con `System.Windows.Forms.NotifyIcon` directamente para evitar el downgrade NU1605.
- **`SQLitePCLRaw`** transitivo tenía CVE → se fijó `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3.
- Plantilla de test crea `net11.0`; hay que fijar `net10.0-windows10.0.17763.0` para referenciar
  el proyecto WPF, y replicar el `Using Remove` de WinForms.

## 3. SDK de Claude / API (hechos verificados; ver skill `claude-api`)

- Modelo por defecto **`claude-opus-4-8`** (configurable). El paquete NuGet es `Anthropic` (v12.35.1).
- `client.Messages.Create(new MessageCreateParams { ... })`; bloques con `TryPickText/Thinking/ToolUse`.
- **Adaptive thinking**: `Thinking = new ThinkingConfigAdaptive()`. `budget_tokens` da 400 en Opus 4.8.
- **Effort** dentro de `OutputConfig` (`Effort.Low/Medium/High/Max`), no top-level.
- **Prompt caching**: `CacheControlEphemeral` en el último bloque estable de `System`.
- **Tools**: `new Tool { Name, Description, InputSchema = { Properties (JsonElement), Required } }`
  envuelto en `new ToolUnion(tool)`. Round-trip del assistant reconstruido a mano (no hay `.ToParam()`).
- **Streaming** no se usa (corre en background); `MaxTokens = 16000`.

## 4. Requisitos acumulados del usuario (todos vigentes)

1. Leer chats de Teams elegidos, entender texto **e imágenes**, sugerir qué contestar.
2. El usuario elige en qué chats actúa.
3. Conectar Fabric (API + CLI), SharePoint (Graph), Azure (az CLI + ARM), y **servidores MCP** configurables.
4. Usar un LLM como agente que explora y responde basándose en lo que hay **realmente** en el sistema.
5. Tenant **fijo por config** (extrapolable a cualquier empresa). API key personal.
6. Varias conversaciones a la vez, **cada una con su contexto**; pueden estar relacionadas → tener en
   cuenta el **interlocutor**.
7. Perfil del usuario (rol/empresa/especialidad) como **guardarraíl anti-alucinación** (RAG ligero con
   `.md` de conocimiento personal + system prompt).
8. **Interfaz** tipo canal de Teams (fecha, interlocutor, conversación), usable maximizada o pequeña en
   un monitor curvo grande.
9. **Memoria por conversación** persistida y **cifrada en local**, accesible desde la UI solo con sesión
   de Windows autenticada (DPAPI).
10. Todo el contexto de trabajo (MCP/API/CLI) va en el mismo marco profesional.
11. El agente puede **ver a qué chats tiene acceso** el usuario.
12. **Seguridad máxima**: confidencialidad de datos (nadie ajeno puede leer conversaciones/sugerencias).
13. Anti-inyección: el contenido de chats/docs/tools son **datos, no instrucciones** (cláusula en prompt).
14. **Sin fricción con Defender/Firewall**: que el sistema entienda que el software es seguro.

### Requisitos NUEVOS (pendientes de implementar — roadmap §6)
15. **Config extrapolable + wizard de primer arranque** guiado: poner tenant de forma segura y todo lo
    necesario, con ayuda de qué poner en cada campo y para qué sirve. Todas las configuraciones posibles.
16. **Multi-proveedor de IA**: poder usar agentes de las principales APIs del mercado (Claude, OpenAI,
    Gemini, ...), no solo Claude.
17. **Panel de costes en pantalla**: mostrar los costes **fuera de la suscripción pagada** y los problemas
    de un uso diario laboral. (Ver §5: hecho crítico sobre Claude Max.)
18. **Búsqueda por grafos** de conversaciones almacenadas (búsquedas complejas sobre lo guardado).
19. **Rendimiento limpio** sin machacar el PC.
20. **Certificados de firma de código sin coste** para que los usuarios se sientan seguros.
21. Interfaz moderna con funcionalidades sugeridas que añadan lo necesario.

## 5. HECHOS CRÍTICOS sobre costes (no alucinar aquí)

- **La suscripción Claude Max (~90 €+IVA/mes) NO incluye el uso de la API de Anthropic.** Max cubre
  claude.ai y Claude Code (uso interactivo con límites de uso). El `ANTHROPIC_API_KEY` que usa esta app
  se factura **aparte, por tokens (pay-as-you-go)**, contra la cuenta de plataforma/consola, no contra Max.
  → Cada sugerencia que genera la app cuesta dinero de API **encima** del Max. Esto es exactamente el
  "coste fuera de la suscripción pagada" que el usuario quiere ver. Hay que dejarlo clarísimo en pantalla.
- **Precios de referencia (por 1M tokens, verificar en la skill `claude-api`/models antes de mostrar cifras):**
  Opus 4.8: entrada $5 / salida $25. Sonnet 5: $3/$15 (intro $2/$10). Haiku 4.5: $1/$5.
  El prompt caching abarata lecturas (~0,1×) pero la escritura cuesta ~1,25×.
- **Coste por sugerencia** depende de: nº de mensajes de contexto, imágenes (visión encarece), iteraciones
  de tools, y `Effort`. Una sugerencia con investigación puede ser de decenas de miles de tokens de entrada.
- El panel de costes debe **estimar y acumular** el gasto real leyendo `response.Usage`
  (`InputTokens`, `OutputTokens`, `CacheReadInputTokens`, `CacheCreationInputTokens`) por llamada y
  multiplicando por el precio del modelo activo. Mostrar coste diario/mensual y proyección.
- Para **otros proveedores** (OpenAI, Gemini) los precios son distintos; el panel debe ser por-proveedor.
- Problemas de uso diario laboral a advertir en pantalla: coste acumulado, límites de rate de la API,
  latencia (polling+debounce+agente), y que datos del tenant salen a la API del proveedor de IA.

## 6. Roadmap de features nuevas (orden sugerido)

**HECHO** ✅ Panel de costes: `CostEstimator` (precios por modelo, cálculo por componente, 6 tests) +
tabla `usage_log` en SQLite + el agente devuelve `TokenUsage` real y el monitor lo persiste por llamada +
`CostWindow` (hoy/mes/proyección, desglose por modelo, aviso Max) + botón "💶 Costes" + línea en Diagnóstico.

**HECHO** ✅ Config extrapolable + wizard de primer arranque + icono robot + auth azcli (ver §2 y §4).

**HECHO** ✅ Multi-proveedor de IA: `ILlmAgent` (SelfTest + Analyze→(veredicto,uso)); `AgentPromptBuilder`
compartido (system prompt, texto de usuario, tools, imágenes); `ClaudeAgent` (SDK Anthropic),
`OpenAiAgent` y `GeminiAgent` (HTTP REST directo con function calling y visión, sin SDK para no
alucinar tipos). Selección por `Provider` en DI (`App.xaml.cs`). Precios de OpenAI/Gemini en
`CostEstimator`. Modelos por proveedor en la ventana de Configuración. Clave por proveedor en la env var
correcta (`ApplyApiKeyToEnvironment`). `OpenAiAgent.ParseArgs`/`ParametersSchema` reutilizados por Gemini
y testeados (`InternalsVisibleTo`). 52 tests verdes.
  - Nota: los agentes OpenAI/Gemini usan HTTP REST estable (chat/completions y generateContent). Si se
    tocan, verificar formatos contra la doc oficial; el bucle de tools es análogo al de Claude.

**HECHO** ✅ Búsqueda por grafos: `ConversationGraphStore` (mensajes cifrados + grafo persona↔chat↔persona
por SQL), el monitor indexa cada revisión, `GraphSearchWindow` (búsqueda por palabra clave + filtros +
relaciones), botón "🔎 Buscar".

**HECHO** ✅ Optimización de tokens + simulación de costes:
  - **Triaje en dos niveles** (`TriageEnabled`, `TriageModel`): modelo barato decide should_reply antes de
    gastar el modelo potente + tools. `ILlmAgent.TriageAsync` en los 3 proveedores; el monitor lo ejecuta
    primero y registra su coste. `VerdictParser.ShouldReplyTriage` (ante duda → true). Toggle en Configuración.
  - **`SimulatedAgent`** (Provider="simulation"): ejecuta el flujo completo SIN API, estimando tokens con
    `TokenEstimator`. Deja probar la app end-to-end sin gastar.
  - **`CostSimulator`** + `TokenEstimator`: calculan el coste mensual del patrón del usuario (25 entrantes/día,
    22 días, ~55% con respuesta, 3 iteraciones de tools). Test `PrintMonthlyCostTable` imprime la tabla.
    Ver `COST-OPTIMIZATION.md`. Recomendación: **Sonnet 5 + triaje Haiku ≈ 18 $/mes**; mínimo Gemini Flash ≈ 0,6 $/mes.
  - **Rendimiento**: GC de estación de trabajo + concurrente, TieredPGO en el csproj.
  - 63 tests verdes.

**HECHO** ✅ Fuente por navegador (sin tocar el tenant): el usuario NO quiere pasar por Graph/registro de app
por seguridad. Alternativa construida:
  - **Extensión Edge/Chrome** (`browser-extension/`): `content.js` lee el DOM de Teams web (autor/hora/texto,
    detecta "yo"), `background.js` lo reenvía a `http://127.0.0.1:<BridgePort>`. `manifest.json` MV3, `popup`
    para fijar puerto/probar. Los selectores del DOM están en `SELECTORS` (content.js) — **pendiente afinarlos
    contra el Teams web real del usuario** (cambian por versión).
  - **App**: `BrowserBridge` (BackgroundService) escucha SOLO en 127.0.0.1 (TcpListener, HTTP mínimo, CORS),
    convierte el payload en `ChatMessageDto` y llama al pipeline. `AnalysisPipeline` (extraído del monitor)
    es compartido por Graph y navegador (cursor, triaje, análisis, guardado, coste). `SourceMode` = "graph"|
    "browser" en config y UI; en "browser" el monitor de Graph queda inactivo. Selector de fuente + ayuda en
    Configuración. **Validado end-to-end sin navegador** (POST loopback → sugerencia creada, en simulación).
  - Motivo de la decisión: captura+visión se descartó (gasta ~1200+ tokens/imagen y es lento); el DOM da
    texto estructurado barato. Las herramientas/conectores/MCP de Teams existentes usan Graph por debajo
    (mismo tenant), no lo evitan. Contexto en la conversación con el usuario.

⚠️ **Hecho verificado**: el Azure CLI **no tiene `Chat.Read`** en el tenant del usuario → `/me/chats` da
403. Por eso el modo azcli NO puede leer chats (sí Fabric/Azure/perfil). Para Graph haría falta registro de app
con Chat.Read (bloqueado por el clasificador crear la app automáticamente; el usuario decide). De ahí la
apuesta por la fuente de navegador.

⚠️ **PENDIENTE**: afinar selectores DOM cargando la extensión en Edge con Teams web abierto (ronda con el
usuario). No usar API real de Claude para costes: usar simulación.

Pendiente:

1. **Wizard de primer arranque** (`SetupWizard`) más guiado (ya hay uno básico): detecta config incompleta y guía paso a paso
   (tenant/clientId con enlace al registro de app y explicación de permisos; proveedor de IA + API key
   guardada **cifrada con DPAPI** —no en texto plano en appsettings—; az login; MCP; perfil). Validación
   en vivo de cada credencial. Guardar en un store cifrado, no en appsettings para secretos.
4. **Búsqueda por grafos**: modelar conversaciones/mensajes/entidades como grafo en SQLite (nodos:
   chats, personas, temas/entidades extraídas; aristas: participa, menciona, responde-a, relacionado-con).
   Búsqueda compleja (p. ej. "todo lo que Ana dijo sobre el pipeline X en cualquier chat"). Considerar
   extracción de entidades por el LLM al guardar memoria. UI de búsqueda con resultados navegables.
5. **Firma de código gratuita**: documentar/automatizar firma con certificado sin coste. Opciones reales:
   (a) **SignPath.io** (gratis para OSS), (b) certificado self-signed + instrucciones para confiar en él
   en el equipo (no elimina SmartScreen para terceros pero sí da integridad), (c) explicar que un
   cert EV/OV comercial es de pago. Ser honesto: la firma gratuita da integridad/identidad pero
   SmartScreen "reputation" tarda o requiere cert de pago. NO prometer "cero avisos" con opción gratuita.
6. **Rendimiento**: revisar que el polling no golpee Graph de más (ya hay caché de digest); throttling
   configurable; no bloquear UI; medir RAM/CPU; considerar `ServerGarbageCollection` off para escritorio;
   liberar imágenes tras el análisis; límites de tamaño de historial en memoria.
7. **Interfaz moderna**: tema claro/oscuro, feedback al copiar ("✓ Copiado"), color de confianza
   (verde/ámbar/rojo), icono `.ico` propio, estados de carga.

## 6b. HECHO ✅ (julio 2026) — stack local-first + Teams incrustado + productividad

Todo verificado, 98 tests verdes. Orientado a **coste mínimo** (lo caro solo cuando aporta) y a
**no ser intrusivo con el tenant** (fuente por navegador, sin Graph).

- **Teams incrustado como ventana principal** (`EmbeddedTeamsWindow`, WebView2): Teams web
  (`teams.cloud.microsoft`) a la izquierda + panel de sugerencia a la derecha, en UNA sola ventana
  ("Teams con superpoderes"). Sesión persistente, permisos/descargas/popups de login gestionados,
  lectura en tiempo real vía `Resources/teamsReader.js` inyectado (MutationObserver + `postMessage`).
  Cierra a bandeja. `MainWindow` antiguo eliminado.
- **Ollama (LLM local, coste 0)**: `OllamaAgent` (`ILlmAgent`) + `AgentSelector` (proveedor→agente).
  **Separación triaje/redacción**: `TriageProvider` puede ser `ollama` (triaje gratis en CPU) y la
  redacción un modelo de nube barato. `CostEstimator.IsLocal` → coste 0 para modelos locales.
- **RAG local con embeddings gratis** (`nomic-embed-text` vía Ollama): `EmbeddingService`,
  `KnowledgeStore` (vectores en SQLite, texto cifrado DPAPI, coseno en memoria), `KnowledgeIndexer`
  (indexa el vault Markdown incremental por hash). El pipeline recupera solo lo relevante a los
  mensajes nuevos y lo inyecta en el user turn; con RAG activo NO se vuelca el vault entero en el
  system prompt (`AgentPromptBuilder.RagActive`). `TextChunker` (troceado + coseno + hash) puro/testeado.
- **Búsqueda semántica de conversaciones**: `KnowledgeIndexer.IndexConversationsAsync` indexa los
  mensajes almacenados; `GraphSearchWindow` tiene toggle "🧠 Semántica" con chip de relevancia.
- **Notas + recordatorios**: `NotesStore` (cifrado), `ReminderService` (background, avisa por toast
  con botón "Hecho"), `IcsExporter` (.ics con VALARM para importar al calendario Teams/Outlook a mano,
  **sin Graph**), `NotesWindow` (pestañas Notas/Recordatorios), botón "📝 Notas".
- **"Requiere tu atención hoy"** (`AttentionService`, sin coste de IA): agrega menciones, preguntas y
  recordatorios del día desde lo ya guardado. `AttentionWindow` con Copiar/crear Recordatorio/Hecho y
  botón "Resumen del día (local · gratis)" vía `LocalLlm` (Ollama). Botón "☀ Hoy".
- **Selector de tono** (Cordial/Formal/Breve/Técnico) en el panel: reescribe la sugerencia con el
  modelo local (`LocalLlm`), coste 0.
- **Lectura de adjuntos**: `DocumentExtractor` (txt/csv/md/json/log…, docx, xlsx vía OpenXml; PDF no).
  `read_attachment` (`IAgentTool`) lee los adjuntos descargados de Teams en la carpeta local
  (`AppPaths.DownloadsDir`), sin tocar el tenant. `read_sharepoint_file` ahora también soporta xlsx.
- **API key**: se guarda **cifrada con DPAPI** en `%LOCALAPPDATA%\TeamsJarvis\settings.json`
  (`UserSettingsService`/`CryptoHelper`, prefijo `enc:v1:`), NUNCA en memoria ni en el repo.
- **Requisitos del usuario cubiertos aquí**: solo castellano; arranque con Windows (`StartupHelper`,
  HKCU Run); notificaciones tipo Teams; detección de menciones (prioriza atención, salta triaje);
  todo copia-a-mano (la app nunca envía). Sincronía con calendario = **.ics manual** (no Graph).

⚠️ Para que lo local funcione hace falta **Ollama instalado** + `ollama pull llama3.2:3b` (triaje/
resúmenes/tono) y `ollama pull nomic-embed-text` (RAG/semántica). Si no está, todo degrada con gracia
(RAG usa el vault completo, tono/resumen se desactivan) — nunca tumba la app.

## 6c. HECHO ✅ (UI/UX, julio 2026)

- **Configuración por pestañas** (SettingsWindow): Teams · IA & coste · Triaje & local ·
  Perfil · Sistema · Avanzado. Sin scroll largo. Incluye toggle "Arrancar con Windows",
  URL de Ollama, modelo de embeddings, RAG on/off e indexar conversaciones.
- **Barra de progreso en vivo** al generar sugerencia: `IProgress<string>` fluye
  UI → `AnalysisPipeline.RunAsync` → `ILlmAgent.AnalyzeAsync`. El pipeline reporta fases
  y `ClaudeAgent` avisa de cada tool (`FriendlyTool`). Panel: ProgressBar + texto de estado.
- **Destino en el panel** (RecipientChip): a qué chat/grupo y persona va la respuesta.
- **Notificaciones enriquecidas** (`ToastService.ShowSuggestion` con destino + texto completo
  + imagen opcional; se dispara si la ventana está en bandeja/sin foco). `ShowIncoming` lista
  para previsualizar mensajes entrantes. La app NUNCA envía: botón "Copiar respuesta".
- **Videollamadas**: `AllowCallPermissions` concede cámara/micro/compartir pantalla en la
  ventana principal y en las emergentes (llamada/login). Dispositivos se eligen dentro de Teams.
- **Optimización de coste**: en `SourceMode=browser` se ocultan las tools que necesitan Graph
  (`IAgentTool.RequiresGraph`), que si no fallaban con 403 y disparaban tokens. Triaje = Haiku.
- **Regla de seguridad reconfirmada**: las sugerencias del agente van SIEMPRE al panel (copy-paste
  a mano); los mensajes normales se contestan en Teams. La app jamás envía. No auto-analiza todo:
  la sugerencia se genera bajo demanda con el botón.

## 6d. HECHO ✅ (rebrand Frida.ai + producto, julio 2026)

Sesión grande. **119 tests verdes.** El nombre visible pasó a **Frida.ai** (logo schnauzer), pero
los identificadores INTERNOS se mantienen a propósito para no perder datos: namespace `TeamsJarvis`,
`AssemblyName`=TeamsJarvis (exe `TeamsJarvis.exe`), carpeta de datos `%LOCALAPPDATA%\TeamsJarvis`,
entropía DPAPI `TeamsJarvis.v1`, valor de arranque `TeamsJarvis`. Si algún día se renombran, hará
falta migrar datos y regenerar la clave/memoria cifradas.

- **Solo el botón dispara IA**: la lectura en segundo plano solo indexa (gratis); triaje+análisis
  solo al pulsar «Sugerir respuesta». `AnalysisPipeline.OutcomeReported` muestra SIEMPRE el resultado
  en el panel (triaje descartó / no hace falta responder / **créditos agotados** vía `FriendlyError`).
- **Rebrand**: `FridaIcon` (schnauzer dibujado por código) sustituye a RobotIcon; `frida.ico`
  multi-tamaño; textos visibles → "Frida.ai"; metadata del csproj. Internos intactos (ver arriba).
- **Barra de título propia** (WindowChrome) integrada en la barra morada (min/max/cerrar).
- **UX**: modo claro/oscuro (`ThemeManager` + brochas `Theme*` DynamicResource en App.xaml,
  `JarvisOptions.Theme`), animación de entrada de la tarjeta + micro-interacciones, tarjeta de
  sugerencia plegable (nombre de chat primero), **lista de recientes por chat**, indicador del agente
  que pulsa al analizar, gráfica de gasto por modelo.
- **Menú de cuenta** (avatar): Config, Costes, Diagnóstico, Memoria, Historial, Reconstruir memoria,
  modo oscuro, y **cerrar sesión** de Teams (limpia WebView2) y de Azure/Fabric (`az logout`).
- **MemoryWindow** estructurada (secciones editables + reconstruir local) con `MemoryDoc` (parser).
- **MCP**: `McpConfigParser` acepta el formato estándar `{"mcpServers": {...}}` + `env`; se pasan
  como EnvironmentVariables al transporte stdio. `McpServerOptions.Env`.
- **Refactor**: `EmbeddedTeamsWindow` partido en parciales (.WebView.cs / .Panel.cs / .Account.cs).
- **Seguridad/limpieza**: se ELIMINÓ la extensión de navegador y `BrowserBridge` (puente TCP) — con
  el Teams incrustado no hacían falta y eran superficie de ataque. Se conserva `BrowserPayloadParser`.
- **Tests de integración** de stores sobre DB temporal (`AppPaths.OverrideDataDir`).
- **Instalador**: `installer/` (Inno Setup + publish.ps1 + pull-models.cmd) — instala app + WebView2
  y, opcional, Ollama + modelos. `README.md` y `LICENSE` (MIT) production-ready.
- **Lector**: capta el `alt` de las imágenes (los píxeles no, por CORS de los CDN de Teams; el
  preview con imagen real solo es viable en modo Graph/hostedContents).

## 7. Principios de trabajo en este repo

- **Todas las integraciones externas: solo lectura.** Nada que modifique sistemas del tenant.
- **Secretos nunca en texto plano** en appsettings ni en logs ni en el prompt. API keys → store cifrado DPAPI.
- **Anti-alucinación primero**: el agente debe verificar con tools antes de afirmar; citar fuentes; bajar
  confianza si no puede verificar. No relajar esto.
- **La app no envía mensajes.** Si alguna vez se propone auto-envío, es un cambio de modelo de riesgo que
  requiere confirmación explícita del usuario.
- **No prometer más de lo que la tecnología da** (costes, seguridad "indetectable", firma gratuita).
  Ser exacto y mostrar límites en pantalla.
- Mantener el proyecto testeable: lógica pura (parsers, validadores, estimadores de coste) en clases
  estáticas/sin UI, con tests en `tests/TeamsJarvis.Tests`.
