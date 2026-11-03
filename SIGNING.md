# Firma de código y confianza

Objetivo: que los usuarios sientan que el software es seguro y que Windows no lo trate
como "editor desconocido". Aquí están las opciones **reales**, con lo que cada una da y
lo que no, sin prometer de más.

## Resumen honesto

| Opción | Coste | Integridad/identidad | Quita "editor desconocido" | Quita aviso SmartScreen (otros PCs) |
|---|---|---|---|---|
| **Autofirmado** (incluido) | Gratis | ✅ | ✅ solo si instalas el cert como editor de confianza en ese equipo | ❌ |
| **SignPath (OSS)** | Gratis (solo proyectos open source) | ✅ | ✅ | Parcial (mejora con reputación) |
| **Certificado OV** comercial | ~150-300 €/año | ✅ | ✅ | Mejora con el tiempo (reputación) |
| **Certificado EV** comercial | ~300-500 €/año | ✅ | ✅ | ✅ inmediato |

**Conclusión**: para uso en tu equipo y tu organización, el **autofirmado gratuito** es
suficiente (integridad + sin aviso de editor desconocido si instalas el certificado). Para
distribución pública sin ningún aviso hace falta un certificado de pago (EV). No hay una
opción gratuita que elimine SmartScreen para terceros de forma inmediata.

## Uso del firmado gratuito (incluido)

```powershell
# Compila en Release, crea el certificado la primera vez y firma el .exe
pwsh ./sign/sign.ps1

# Además, confía en el certificado en ESTE equipo (quita "editor desconocido"). Requiere admin.
pwsh ./sign/sign.ps1 -InstallToTrusted
```

El script (`sign/sign.ps1`):
1. Compila el proyecto en Release.
2. Crea (o reutiliza) un certificado de firma de código autofirmado en tu almacén de usuario.
3. Firma `TeamsJarvis.exe` con sello de tiempo (la firma sigue siendo válida tras caducar el cert).
4. Con `-InstallToTrusted`, instala el certificado como Editor de confianza del equipo.

## Sobre Defender / Firewall / SmartScreen

- **No hace falta abrir puertos ni reglas de firewall**: la app solo hace conexiones
  salientes (Graph, el proveedor de IA, Fabric/Azure, MCP). No escucha en ningún puerto.
- **Defender** no bloquea un binario que tú compilas y ejecutas en tu equipo. Si SmartScreen
  avisa la primera vez, *Más información → Ejecutar de todas formas*, o firma con el script.
- El proyecto incluye metadatos de ensamblado (Company/Product/Version) para que el binario
  se identifique correctamente.

## Si en el futuro quieres distribuirlo sin avisos

- **SignPath.io** ofrece firma gratuita para proyectos open source (habría que publicar el
  repo como OSS y configurar su GitHub Action).
- Un **certificado EV** de una CA (DigiCert, Sectigo…) elimina el aviso de SmartScreen desde
  el primer momento, pero es de pago y requiere validación de identidad.
