/**
 * =============================================================================
 * LABORATORIO: Validación de Rate Limiting en Itm.Gateway.Api
 * =============================================================================
 *
 * OBJETIVO: Demostrar que la política "fixed-policy" (5 req / 10 s / IP)
 * del Gateway bloquea correctamente con HTTP 429 a partir de la 6ª petición.
 *
 * POLÍTICA CONFIGURADA EN Gateway/Program.cs:
 *   - PermitLimit : 5 peticiones
 *   - Window      : 10 segundos
 *   - QueueLimit  : 0 (sin cola, rechazo inmediato)
 *
 * CÓMO EJECUTAR:
 *   1. Levantar Itm.Inventory.Api  → http://localhost:5000
 *   2. Levantar Itm.Gateway.Api    → http://localhost:5183
 *   3. Generar un JWT en https://jwt.io con:
 *   4. Pegar el token en la constante JWT_TOKEN (línea ~40)
 *   5. Ejecutar:
 *        k6 run lab-rate-limit.js
 *
 * RESULTADO ESPERADO:
 *   - Peticiones 1-5  → 200 OK
 *   - Peticiones 6-20 → 429 Too Many Requests
 * =============================================================================
 */

import { check } from 'k6';
import http from 'k6/http';

// ─── CONFIGURACIÓN ────────────────────────────────────────────────────────────

// Puerto del Gateway según Itm.Gateway.Api/Properties/launchSettings.json
const GATEWAY_URL = 'http://localhost:5183';

// Ruta expuesta por YARP: /bodega/{id} → transforma a /api/inventory/{id}
const ENDPOINT = `${GATEWAY_URL}/bodega/1`;

const JWT_TOKEN =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJJdG1JZGVudGl0eVNlcnZlciIsImF1ZCI6Ikl0bVN0b3JlQXBpcyIsImVtYWlsIjoiYWRtaW5AaXRtLmVkdS5jbyIsInJvbGUiOiJBZG1pbmlzdHJhZG9yIn0.PaSdxe8NkHzbkrTA40janIgKn4gnVp63yWh_cenvUDw';

// ─── OPCIONES DE K6 ───────────────────────────────────────────────────────────

export const options = {
  // Un único VU (usuario virtual) dispara 20 iteraciones lo más rápido posible.
  // "maxDuration" evita que la prueba cuelgue si algo falla.
  vus: 1,
  iterations: 20,
  maxDuration: '10s',

  // Umbrales: la prueba FALLA si no se cumplen estas condiciones.
  thresholds: {
    // Al menos 1 respuesta debe ser 429 (prueba que el Rate Limiter está activo)
    rate_limited: ['rate>0'],
    // Al menos 1 respuesta debe ser 200 (prueba que el servicio funciona)
    requests_ok: ['rate>0'],
  },
};

// ─── CONTADORES GLOBALES ──────────────────────────────────────────────────────
// k6 no tiene estado compartido entre iteraciones de forma nativa,
// usamos __ITER (variable interna de k6) para numerar cada petición.

import { Counter, Rate } from 'k6/metrics';

// Métrica personalizada: cuenta cuántas respuestas fueron 429
const rateLimitedCount = new Counter('rate_limited_count');
// Métrica personalizada: tasa de respuestas 200
const okRate = new Rate('requests_ok');
// Métrica personalizada: tasa de respuestas 429
const blockedRate = new Rate('rate_limited');

// ─── FUNCIÓN PRINCIPAL ────────────────────────────────────────────────────────

export default function () {
  // __ITER es 0-based; lo convertimos a 1-based para los logs
  const requestNumber = __ITER + 1;

  const headers = {
    'Content-Type': 'application/json',
    // Elimina esta línea si el endpoint del Gateway no exige JWT
    Authorization: `Bearer ${JWT_TOKEN}`,
  };

  const response = http.get(ENDPOINT, {
    headers,
    tags: { request_num: requestNumber },
  });

  // ── Clasificación de la respuesta ──────────────────────────────────────────

  const isOk = response.status === 200;
  const isRateLimited = response.status === 429;

  // Registramos en las métricas personalizadas
  okRate.add(isOk);
  blockedRate.add(isRateLimited);
  if (isRateLimited) rateLimitedCount.add(1);

  // ── Validaciones con check (aparecen en el resumen final) ──────────────────

  if (requestNumber <= 5) {
    // Las primeras 5 peticiones DEBEN pasar
    check(response, {
      [`[Req #${requestNumber}] Primeras 5 → esperado 200, recibido ${response.status}`]:
        (r) => r.status === 200,
    });
  } else {
    // De la 6 en adelante DEBEN ser bloqueadas
    check(response, {
      [`[Req #${requestNumber}] Exceso → esperado 429, recibido ${response.status}`]:
        (r) => r.status === 429,
    });
  }

  // Log por consola para rastrear el orden en tiempo real
  const icon = isOk ? '✅' : isRateLimited ? '🚫' : '⚠️';
  console.log(
    `${icon} Req #${requestNumber} | Status: ${response.status} | ` +
      `Tiempo: ${response.timings.duration.toFixed(0)}ms`
  );

  // Sin sleep: queremos las 20 peticiones lo más rápido posible
  // para saturar la ventana de 10 segundos antes de que se resetee.
  // Si quieres ver la recuperación después de los 10s, descomenta la línea:
  // if (requestNumber === 5) sleep(11); // pausa para dejar que la ventana expire
}

// ─── RESUMEN FINAL ────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const ok = data.metrics['requests_ok']?.values?.passes ?? 0;
  const blocked = data.metrics['rate_limited_count']?.values?.count ?? 0;
  const total = ok + blocked;

  const summary = `
================================================================================
  RESUMEN DEL LABORATORIO - Rate Limiting (Itm.Gateway.Api)
================================================================================
  Total de peticiones  : ${total}
  ✅ Aceptadas (200)   : ${ok}
  🚫 Bloqueadas (429)  : ${blocked}

  Política activa:
    PermitLimit = 5 peticiones
    Window      = 10 segundos
    QueueLimit  = 0 (sin cola)  

  RESULTADO: ${blocked >= 15 && ok >= 5 ? '✅ LABORATORIO APROBADO' : '❌ REVISAR - el Rate Limiter no bloqueó como se esperaba'}
================================================================================
`;

  console.log(summary);

  // Devuelves también el resumen estándar de k6 en stdout
  return {
    stdout: summary,
  };
}
