; ============================================================================
;  Frida.ai — script de instalador (Inno Setup 6+)
;  Empaqueta la app y, opcionalmente, instala el runtime de WebView2, Ollama
;  y los modelos locales (triaje/RAG gratis). Compilar con:  iscc frida-setup.iss
;  (antes ejecuta ..\installer\publish.ps1 para generar la carpeta 'publish').
; ============================================================================

#define AppName "Frida.ai"
#define AppVersion "1.0.0"
#define AppPublisher "Frida.ai"
#define AppExe "TeamsJarvis.exe"   ; nombre interno del ejecutable (la marca es Frida.ai)

[Setup]
AppId={{7F3B9C2E-4D1A-4E7B-9A2C-FR1DA0000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Frida.ai
DefaultGroupName=Frida.ai
UninstallDisplayIcon={app}\{#AppExe}
OutputBaseFilename=Frida.ai-Setup-{#AppVersion}
OutputDir=dist
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
SetupIconFile=..\src\TeamsJarvis\frida.ico
PrivilegesRequired=lowest   ; se instala por usuario (no requiere admin)

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos:"
Name: "startup"; Description: "Arrancar Frida.ai al iniciar Windows"; GroupDescription: "Inicio:"; Flags: unchecked
Name: "ollama"; Description: "Instalar Ollama y los modelos locales (gratis: triaje, RAG y resúmenes sin coste de API)"; GroupDescription: "Extras recomendados:"

[Files]
; Salida de 'dotnet publish' (self-contained win-x64): todo lo que necesita la app.
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; Instaladores auxiliares (colócalos en installer\redist antes de compilar).
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: dontcopy deleteafterinstall
Source: "redist\OllamaSetup.exe"; DestDir: "{tmp}"; Flags: dontcopy deleteafterinstall; Tasks: ollama
Source: "scripts\pull-models.cmd"; DestDir: "{app}\scripts"; Flags: ignoreversion; Tasks: ollama

[Icons]
Name: "{group}\Frida.ai"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\Frida.ai"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Arranque con Windows (misma clave que usa la app: HKCU\...\Run, valor 'TeamsJarvis').
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "TeamsJarvis"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; 1) Runtime de WebView2 (Evergreen): silencioso; no reinstala si ya está.
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
    StatusMsg: "Instalando el runtime de WebView2..."; Flags: waituntilterminated; \
    BeforeInstall: ExtractWebView2
; 2) Ollama (opcional): instalador silencioso.
Filename: "{tmp}\OllamaSetup.exe"; Parameters: "/VERYSILENT /NORESTART"; \
    StatusMsg: "Instalando Ollama (LLM local)..."; Flags: waituntilterminated skipifdoesntexist; Tasks: ollama; \
    BeforeInstall: ExtractOllama
; 3) Descargar los modelos locales (barra de progreso en una consola).
Filename: "{app}\scripts\pull-models.cmd"; StatusMsg: "Descargando modelos locales (llama3.2:3b, nomic-embed-text)..."; \
    Flags: waituntilterminated runasoriginaluser; Tasks: ollama
; 4) Lanzar la app al terminar.
Filename: "{app}\{#AppExe}"; Description: "Iniciar Frida.ai"; Flags: nowait postinstall skipifsilent

[Code]
procedure ExtractWebView2;
begin
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
end;

procedure ExtractOllama;
begin
  ExtractTemporaryFile('OllamaSetup.exe');
end;
