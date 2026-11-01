# CSMS — нотатки з проєкту claude.ai

Перенесено 2026-10-22 з проєкту claude.ai «CSMS (Charging Station Management System)»
(три чати + артефакт «CSMS: список завдань»). Живий чек-лист з прогресом:
https://claude.ai/artifact/8NuirMLjD1o7Jc7ZHhKuJW

Це робочі нотатки, а не документація проєкту. `CLAUDE.md`, `docs/ARCHITECTURE.md` і ADR
за планом пишуться **руками** (пункти m0-7, m0-8, m0-9) — тут лише вихідні рішення для них.

## Ролі та ритм

- Claude у чаті claude.ai — архітектор / лід / сеньйор: ставить задачі, робить рев'ю PR.
  Розробник — мідл фулстек: реалізує, ставить питання, сперечається.
- 5 годин на тиждень: дві сесії по 2,5 год, кожна закінчується комітом.
  Нову сесію починати з читання останнього коміту і `docs/`.
- Рев'ю раз на тиждень: PR або ключові файли — у чат проєкту.
- Правило перевищення: задача з'їла в 1,5 раза більше часу — зупинитись і написати в чат.
- План ~25 тижнів, основна робота вручну (рішення від 2026-10-22, не віддавати все Claude Code).

## Правило щодо Claude Code

- **Руками** (Claude Code — лише рецензент): парсер фреймів, диспетчер повідомлень,
  кореляція вихідних запитів (TaskCompletionSource + таймаут), WebSocket-цикл,
  ключові рішення M3 (outbox, дедуплікація, порядок подій), CLAUDE.md, ARCHITECTURE.md, ADR.
- **Можна генерувати** (з перевіркою): скаффолдинг solution, EF-міграції, docker-compose, CI,
  емулятор, CRUD-ендпоінти, сторінки Next.js, C#-records контрактів за JSON-схемами
  (бо їх перевіряє контрактний тест).
- Заборонено: MediatR, AutoMapper, MassTransit (RabbitMQ.Client напряму).

## Що будуємо

CSMS — бекенд, до якого зарядні станції під'єднуються по WebSocket
`ws://csms/ocpp/{chargePointId}` (сабпротокол `ocpp1.6`) і обмінюються JSON-повідомленнями OCPP.

- Протокол OCPP 1.6J, профіль Core. 2.0.1 — у беклог.
- Фрейми: `[2, id, Action, payload]` CALL · `[3, id, payload]` CALLRESULT ·
  `[4, id, ErrorCode, description, details]` CALLERROR. Запити ініціюють обидві сторони,
  відповідь зіставляється за id.

## Архітектура v1 — модульний моноліт

```
src/
  Csms.Ocpp            протокол: фрейми, контракти, диспетчер; БЕЗ залежності від ASP.NET
  Csms.Domain          ChargePoint, Connector, Transaction, IdTag, MeterValue; не знає про EF Core
  Csms.Infrastructure  EF Core + PostgreSQL
  Csms.Host            ASP.NET Core: WebSocket-ендпоінт, REST API, SignalR для UI
  Csms.Emulator        консольний емулятор N станцій зі сценаріями
  (Csms.Workers        з M3: окремий процес-споживач RabbitMQ)
web/                   Next.js — з M2
tests/
  Csms.Ocpp.Tests                юніт-тести протоколу
  Csms.Host.IntegrationTests     WebApplicationFactory + реальний ClientWebSocket, Testcontainers
```

Стек: .NET 10, PostgreSQL 16, MongoDB, RabbitMQ (з M3), xUnit, EF Core з міграціями,
Testcontainers, Next.js. Репозиторій публічний на GitHub (портфоліо).
Документація й ADR — англійською.

## Ключові рішення (кожне — ADR у `docs/adr/`)

1. **0001** — OCPP 1.6J, профіль Core.
2. **0002** — модульний моноліт, одне з'єднання на станцію, реєстр з'єднань у пам'яті;
   горизонтальне масштабування (sticky routing, backplane) свідомо поза MVP.
   З M3 доповнити: воркери — окремий процес `Csms.Workers` у тому ж solution.
3. **0003** — PostgreSQL для станцій, сесій, тарифів; MongoDB для журналу OCPP-повідомлень
   і meter values (TTL-індекс на журнал, складений індекс `(chargePointId, час)`).
   У наслідках згадати: RabbitMQ пізніше може маршрутизувати команди на інстанс зі з'єднанням станції.
4. **JSON-схеми OCPP** (у чаті теж названо «ADR 0003» — номер треба розвести):
   схеми профілю Core лежать у `src/Csms.Ocpp/Schemas/`; records можна генерувати;
   контрактні тести руками — кожен вихідний payload валідується за офіційною схемою
   (draft-04, напр. NJsonSchema). PDF специфікації не комітити, у README — посилання на OCA.
5. **0004** — RabbitMQ (див. M3).
- Енергія — цілі Wh, час — тільки UTC.
- Рядок підключення — через змінні середовища або user-secrets, не в appsettings.json.

## Етапи

### M0 — каркас (тижні 1–2, ≈10 год), TASK-001
1. Завантажити специфікації OCPP 1.6 і OCPP-J 1.6 — ✅ (`OCPP_1.6_documentation.zip`
   є у файлах проєкту claude.ai: спека 1.6 ed.2, OCPP-J, errata, JSON-схеми).
2. Публічний репозиторій на GitHub, `.gitignore` для .NET, README з одним реченням.
3. Згенерувати solution (.NET 10): 5 проєктів у `src/`, 2 тестові в `tests/`. — Claude Code
4. Вручну перевірити граф залежностей (`dotnet list reference`). — руками
5. `docker-compose.yml` з PostgreSQL 16. — Claude Code
6. `/health` у Csms.Host: перевіряє БД, без БД — Unhealthy.
7. `CLAUDE.md` з правилами проєкту. — руками
8. `docs/ARCHITECTURE.md` англійською з Mermaid-діаграмою. — руками
9. ADR 0001 і 0002, по пів сторінки. — руками
10. GitHub Actions: build і test на кожен PR. — Claude Code
11. Прочитати: OCPP-J розд. 3–4 (особл. 4.2), проглянути 5.3–5.4; основна спека 3.3, 4.2, 4.6,
    гл. 6 (6.29–6.30) — разом з errata.
12. PR на рев'ю. В описі — відповідь на контрольне питання: що CSMS робить з CALL
    з непідтримуваним action і який ErrorCode повертає (з посиланням на розділ спеки).

Порядок: сесія 1 — пп. 2–6; сесія 2 — п. 11; сесії 3–4 — пп. 7–10, 12.
Приймання: `docker compose up` + `dotnet run` → `/health` 200; CI зелений; Mermaid у ARCHITECTURE.md;
відповідь на контрольне питання.

### M1 — підключення станцій (тижні 3–6, ≈20 год)
- **TASK-002 WebSocket і фрейми**: ендпоінт `/ocpp/{chargePointId}` з узгодженням `ocpp1.6` (руками);
  цикл прийому з фрагментацією до `EndOfMessage` і лімітом розміру (руками);
  парсер CALL/CALLRESULT/CALLERROR у типізовані записи, невалідний JSON → коректна відповідь (руками);
  юніт-тести парсера; інтеграційний тест з реальним ClientWebSocket.
- **TASK-003 BootNotification, Heartbeat, журнал**: диспетчер за action без MediatR,
  непідтримуваний action → CALLERROR (руками); обробники BootNotification/Heartbeat;
  MongoDB у docker-compose, `/health` перевіряє обидві БД; журнал у MongoDB; TTL-індекс;
  ADR про PostgreSQL + MongoDB (руками). Подумати, що робити зі станцією,
  яка порушує обмеження схем (напр. `maxLength: 20` у `chargePointVendor`).
- **TASK-004 емулятор**: консольний емулятор N станцій (Claude Code);
  перепідключення з наростаючою паузою.

### Далі (деталізуються пізніше)
- **M2 Статуси** (тижні 7–9): StatusNotification, стан конекторів, живий список станцій у Next.js через SignalR.
- **M3 Транзакції через RabbitMQ** (тижні 10–14): Authorize і StartTransaction відповідають синхронно
  (transactionId генерується в PostgreSQL), подія — через transactional outbox з publisher confirms.
  MeterValues і StatusNotification підтверджуються одразу й ідуть у чергу. Topic exchange `csms.events`
  (ключі на кшталт `station.status.changed`), retry-черги з TTL 5 с → 30 с → 5 хв, потім DLQ і ручний replay.
  Дедуплікація за (chargePointId, messageId) через inbox, ідемпотентність за transactionId,
  порядок подій станції — партиціювання за chargePointId (consistent hash exchange або single active consumer).
  At-least-once — прямо в ADR. Meter values у MongoDB. Testcontainers для PostgreSQL, MongoDB, RabbitMQ.
- **M4 REST API і звіти** (тижні 15–17): `/api/v1` (станції, сесії, тарифи), пагінація, ProblemDetails;
  JWT, ролі, оператор бачить лише свої станції; aggregation pipeline у MongoDB; сторінка звітів.
- **M5 Команди від CSMS** (тижні 18–20): RemoteStart/Stop, Reset, ChangeAvailability; кореляція й таймаути.
- **M6 Експлуатація** (тижні 21–23): Serilog, correlation ID; OpenTelemetry (трейс WebSocket → RabbitMQ → воркер),
  метрики черг, retry, DLQ, підключених станцій; Grafana; health checks.
- **M7 ClickHouse** (необов'язково): денні/місячні агрегати сесій з подій RabbitMQ.
- Беклог: smart charging (SetChargingProfile), резервування, OCPP 2.0.1, ML-прогноз навантаження.
