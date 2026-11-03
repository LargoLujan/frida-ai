@echo off
REM Descarga los modelos locales de Frida.ai (gratis, corren en tu CPU).
REM Se ejecuta tras instalar Ollama. Si Ollama no está en PATH todavía, espera un poco.
echo Descargando modelos locales de Frida.ai (esto puede tardar unos minutos)...
where ollama >nul 2>nul
if errorlevel 1 (
  echo Ollama aun no esta en el PATH. Abre una consola nueva y ejecuta:
  echo    ollama pull llama3.2:3b
  echo    ollama pull nomic-embed-text
  exit /b 0
)
ollama pull llama3.2:3b
ollama pull nomic-embed-text
echo Listo. Modelos locales instalados.
