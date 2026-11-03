# Instalador de Frida.ai

Genera un único `.exe` de instalación que deja Frida.ai listo para usar: la app, el
runtime de **WebView2** y, opcionalmente, **Ollama + los modelos locales** (triaje/RAG/resúmenes
gratis). Se instala **por usuario** (no requiere permisos de administrador).

## Requisitos para compilar el instalador

1. **.NET 10 SDK** (para publicar la app).
2. **[Inno Setup 6+](https://jrsoftware.org/isinfo.php)** (`iscc.exe` en el PATH).
3. Descargar a `installer/redist/`:
   - `MicrosoftEdgeWebview2Setup.exe` — [Evergreen Bootstrapper](https://developer.microsoft.com/microsoft-edge/webview2/) (siempre necesario).
   - `OllamaSetup.exe` — de [ollama.com/download](https://ollama.com/download) (solo si quieres empaquetar Ollama).

## Pasos

```powershell
# 1) Publicar la app (self-contained, no requiere .NET instalado en el equipo destino)
pwsh installer/publish.ps1

# 2) Colocar los instaladores auxiliares en installer/redist/ (ver arriba)

# 3) Compilar el instalador
iscc installer/frida-setup.iss
# -> genera installer/dist/Frida.ai-Setup-1.0.0.exe
```

## Qué hace el instalador

- Copia la app a `%LOCALAPPDATA%\Programs\Frida.ai` (por usuario).
- Instala el runtime de **WebView2** en silencio (no reinstala si ya está).
- Tareas opcionales (casillas en el asistente):
  - **Arrancar con Windows** (clave `HKCU\...\Run`).
  - **Acceso directo en el escritorio**.
  - **Instalar Ollama y los modelos locales** → instala Ollama en silencio y ejecuta
    `ollama pull llama3.2:3b` y `ollama pull nomic-embed-text`.
- Crea accesos directos con el logo (schnauzer) y ofrece lanzar la app al terminar.

## Notas

- El ejecutable interno se llama `TeamsJarvis.exe` (nombre técnico); la marca visible es **Frida.ai**.
  Los datos del usuario siguen en `%LOCALAPPDATA%\TeamsJarvis` (clave/memoria cifradas con DPAPI),
  por lo que actualizar sobre una instalación previa conserva todo.
- **Firma de código**: para evitar el aviso de SmartScreen conviene firmar `Frida.ai-Setup-*.exe`
  y `TeamsJarvis.exe` con un certificado. Ver `../SIGNING.md`.
- **Automatización (CI)**: los tres pasos se pueden encadenar en un workflow de GitHub Actions
  (runner `windows-latest`, que ya trae Inno Setup vía Chocolatey: `choco install innosetup`).
