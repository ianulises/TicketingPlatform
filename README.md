# TicketingPlatform

Plataforma backend para administrar eventos, apartar asientos, procesar pagos simulados, emitir boletos y cancelar compras con reembolso. La solución usa .NET 10, PostgreSQL, RabbitMQ y MassTransit.

## Estructura

- `Events.Api`: autoridad única de eventos, zonas, precios y estado de asientos.
- `Bookings.Api`: reservas, expiración, pagos solicitados, boletos y cancelaciones.
- `Payments.Api`: simulador idempotente de pagos y reembolsos.
- `Contracts`: únicos tipos compartidos entre servicios; contiene contratos de mensajes, no modelos de persistencia.
- `tests`: pruebas unitarias y prueba de concurrencia con PostgreSQL real mediante Testcontainers.

Cada API conserva su propia base de datos y sus migraciones. Ningún servicio consulta tablas de otro servicio.

## Ejecución

Requisitos: Docker Desktop con Docker Compose.

```bash
docker compose up --build
```

Puertos:

| Componente | URL o puerto |
|---|---|
| Events Swagger | http://localhost:8081/swagger |
| Bookings Swagger | http://localhost:8082/swagger |
| Payments Swagger | http://localhost:8083/swagger |
| RabbitMQ Management | http://localhost:15673 |
| RabbitMQ AMQP | `localhost:5673` |
| PostgreSQL Events | `localhost:5433` |
| PostgreSQL Bookings | `localhost:5434` |
| PostgreSQL Payments | `localhost:5435` |

Las credenciales de desarrollo predeterminadas son `ticketing`/`ticketing`. Se pueden reemplazar copiando `.env.example` a `.env`. Las migraciones se aplican al iniciar cada API, con reintentos limitados mientras PostgreSQL termina de arrancar.

Para detener los contenedores sin borrar datos:

```bash
docker compose down
```

## Identidad simulada

Los endpoints de usuario requieren `X-User-Id`. Las operaciones administrativas requieren además `X-User-Role: organizer`. No se implementó JWT porque no forma parte del alcance.

Las creaciones de reserva, pagos y reembolsos utilizan `Idempotency-Key`. Repetir una solicitud con la misma clave recupera o reproduce el mismo resultado sin crear otra operación económica.

## Endpoints principales

### Events.Api

- `POST /events`: crea un evento con zonas y asientos; requiere organizador.
- `GET /events`: catálogo público.
- `GET /events/{id}`: detalle del evento.
- `GET /events/{id}/availability`: disponibilidad y precio actual por zona.
- `PUT /events/{eventId}/zones/{zoneId}/price`: cambia el precio; solo el organizador propietario.
- `GET /health`: estado de PostgreSQL.

### Bookings.Api

- `POST /bookings`: aparta entre uno y seis asientos durante diez minutos.
- `GET /bookings`: reservas del usuario actual.
- `GET /bookings/{id}`: reserva propia.
- `POST /bookings/{id}/payments`: inicia el pago de una reserva vigente.
- `POST /bookings/{id}/cancel`: solicita reembolso si faltan al menos 48 horas.
- `GET /tickets`: boletos del usuario; `cancelledAt` indica revocación.
- `GET /health`: estado de PostgreSQL.

### Payments.Api

- `POST /payments`: entrada directa al simulador para pruebas.
- `GET /payments/{bookingId}`: pago y reembolsos del usuario.
- `GET /health`: estado de PostgreSQL.

El flujo normal de compra debe iniciarse desde Bookings; el endpoint directo de Payments existe para observar el simulador de manera aislada.

## Ejemplo de flujo

Crear un evento:

```bash
curl -X POST http://localhost:8081/events \
  -H "Content-Type: application/json" \
  -H "X-User-Id: organizer-1" \
  -H "X-User-Role: organizer" \
  -d '{
    "name":"Concierto",
    "venue":"Foro Central",
    "startsAt":"2030-12-20T20:00:00Z",
    "zones":[{"name":"Platea","price":650,"seatLabels":["A-1","A-2"]}]
  }'
```

Con el `eventId` y un `seatId` de la respuesta, crear una reserva:

```bash
curl -X POST http://localhost:8082/bookings \
  -H "Content-Type: application/json" \
  -H "X-User-Id: user-1" \
  -H "Idempotency-Key: booking-001" \
  -d '{"eventId":"EVENT_ID","seatIds":["SEAT_ID"]}'
```

Solicitar el pago:

```bash
curl -X POST http://localhost:8082/bookings/BOOKING_ID/payments \
  -H "Content-Type: application/json" \
  -H "X-User-Id: user-1" \
  -H "Idempotency-Key: payment-001" \
  -d '{}'
```

El endpoint responde `202 Accepted`. La confirmación es asíncrona. Consultar `GET /bookings/BOOKING_ID` hasta observar `Confirmed` o `PaymentRejected`.

Cancelar:

```bash
curl -X POST http://localhost:8082/bookings/BOOKING_ID/cancel \
  -H "Content-Type: application/json" \
  -H "X-User-Id: user-1" \
  -H "Idempotency-Key: refund-001" \
  -d '{}'
```

Los asientos no se liberan al solicitar la cancelación. Se liberan después de recibir `RefundCompleted`. Si el reembolso falla, la reserva queda en `RefundFailed`, conserva los asientos y admite un nuevo intento con otra clave.

## Concurrencia y precios

Events bloquea en PostgreSQL las filas de todos los asientos solicitados mediante `SELECT ... FOR UPDATE`, dentro de una transacción. Después del bloqueo vuelve a comprobar disponibilidad y evento. Si falla un solo asiento, no modifica ninguno.

No se utiliza `lock` de C#; la garantía funciona entre varias instancias del servicio. El índice único de asiento por zona y las restricciones de boletos complementan el bloqueo.

El precio se copia a `BookingItem` al apartar el asiento. Un cambio posterior en `Zone.Price` solo afecta reservas nuevas.

## Expiración y pagos cercanos al límite

La fecha `ExpiresAt` se persiste en Bookings y también acompaña el apartado en Events. Un `BackgroundService` busca reservas vencidas cada cinco segundos y publica su liberación; al reiniciar continúa desde los datos persistidos.

Si el pago se aprueba cerca del minuto diez, Bookings comprueba nuevamente `ExpiresAt` al consumir el resultado. Si ya venció, no confirma asientos ni emite boletos: solicita un reembolso idempotente. Esta regla favorece un límite temporal estricto y evita vender un asiento que pudo volver a estar disponible.

## Mensajería, outbox e idempotencia

Los cambios de estado y los mensajes producidos usan el outbox de Entity Framework de MassTransit. Los mensajes se reintentan con intervalos limitados y conservan `CorrelationId`.

La idempotencia se aplica en dos niveles:

- índices únicos para reserva, pago, reembolso y boleto;
- consumidores que comprueban el estado actual antes de repetir una transición.

No hay transacciones distribuidas. Un fallo temporal deja una transición persistida y un mensaje recuperable en el outbox. Los mensajes que agotan sus reintentos terminan en las colas `_error` de MassTransit para inspección.

## Pagos simulados

Variables:

- `PAYMENT_LATENCY_SECONDS`: entero de 0 a 10; predeterminado `1`.
- `PAYMENT_FAILURE_RATE`: decimal de 0 a 1; predeterminado `0.10`.

La misma configuración se aplica a pagos y reembolsos. No se contacta a proveedores externos.

## Pruebas

```bash
dotnet test TicketingPlatform.slnx
```

La prueba `SeatConcurrencyTests` crea PostgreSQL con Testcontainers, aplica la migración y ejecuta 100 solicitudes concurrentes contra el mismo asiento. Verifica que exactamente una tenga éxito. Docker debe estar activo para esa prueba.

También hay pruebas de validación de eventos, expiración exacta, límite de 48 horas e índices de idempotencia. Se priorizaron reglas con riesgo real; no se añadieron pruebas de acceso trivial solo para aumentar cobertura.

## Decisiones y limitaciones

- Vertical Slice se aplica por funcionalidad sin repositorios genéricos ni MediatR. EF Core es la unidad de trabajo.
- RabbitMQ coordina servicios; no hay API Gateway ni frontend.
- La identidad mediante headers es deliberadamente simulada y no debe usarse en producción.
- El worker de expiración usa sondeo de cinco segundos. Es persistente y simple, pero puede liberar unos segundos después del límite.
- La observabilidad se limita a logs estructurados de Serilog, `CorrelationId`, Swagger y health checks. No se incluyó tracing distribuido.
- No hay selección automática de asientos ni administración avanzada de recintos; se reciben etiquetas numeradas explícitas.
- Las colas de error requieren intervención operativa; no se implementó una interfaz de replay.
