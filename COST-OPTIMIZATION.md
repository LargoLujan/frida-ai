# Costes y optimización de tokens

> Cifras estimadas por el simulador offline (`CostSimulator`), sin llamar a ninguna API.
> Se recalculan con `dotnet test` (test `PrintMonthlyCostTable`). Son **coste de API**, que se
> factura **aparte** de cualquier suscripción (Claude Max, ChatGPT Plus, etc. NO lo cubren).

## Tu patrón de uso (base de la simulación)

- ~50 mensajes/día laborable; la mitad tuyos → **~25 entrantes/día** a analizar.
- ~22 días laborables/mes → **550 mensajes/mes** que pasan por el triaje.
- Todos requieren contexto de **Fabric + Azure CLI** → ~3 iteraciones de herramientas por análisis.
- ~55% de los entrantes merecen respuesta → **~302 análisis profundos/mes**.

## Coste mensual estimado por configuración

| Configuración | $/mes | Análisis profundos | Notas |
|---|---:|---:|---|
| Opus 4.8, **sin** triaje | **54,88** | 550 | lo más caro; analiza todo aunque no lo necesite |
| Opus 4.8 + triaje Haiku | 30,39 | 302 | máxima calidad, con ahorro por triaje |
| **Sonnet 5 + triaje Haiku** | **18,34** | 302 | **recomendado**: calidad casi Opus a 1/3 del coste |
| gpt-4o + triaje 4o-mini | 15,40 | 302 | alternativa OpenAI |
| Gemini Pro + triaje Flash | 7,71 | 302 | muy económico |
| Haiku 4.5 + triaje Haiku | 6,29 | 302 | el más barato dentro de Claude |
| **Gemini Flash + triaje Flash** | **0,64** | 302 | mínimo coste; menor capacidad de razonamiento |

## Las tres palancas de ahorro (implementadas)

1. **Triaje en dos niveles** (`TriageEnabled`). Un modelo barato (Haiku / Flash / 4o-mini) decide
   primero si el mensaje merece respuesta. Solo ~55% llegan al modelo potente con herramientas.
   Ahorro directo ≈ 45% de los análisis caros. Coste del triaje: céntimos al mes.
2. **Caché del system prompt** (Claude). El prompt de sistema (fijo) se cachea: en las iteraciones
   del bucle de herramientas se lee a ~0,1× en vez de pagarlo entero cada vez.
3. **Modelo adecuado, no el más caro.** Sonnet 5 rinde casi como Opus en trabajo agéntico/datos a
   1/3 del precio. Para triaje, el modelo más barato del proveedor.

## Recomendación

- **Equilibrio calidad/coste**: **Sonnet 5 + triaje Haiku** (~18 $/mes). Es la opción por defecto
  recomendada para tu trabajo con Fabric/Azure.
- **Mínimo coste**: **Gemini Flash + triaje Flash** (~0,6 $/mes) si te vale algo menos de profundidad.
- Deja el **triaje activado** siempre: su coste es insignificante y evita pagar análisis completos
  de mensajes que no requieren respuesta.
- Usa el **modo Simulación** (Proveedor = "Simulación") para probar la app end-to-end sin gastar nada:
  ejecuta el flujo completo y registra costes estimados en el panel 💶 Costes.

## Otras optimizaciones aplicadas

- Truncado de resultados de herramientas y del contexto (límites en `MaxContextMessages`, `MaxImages`).
- Reescalado de imágenes antes de enviarlas (menos tokens de visión).
- Caché breve del "digest de otros chats" para no golpear Graph en cada análisis.
- GC de estación de trabajo + compilación por niveles (menos RAM/CPU en escritorio).
