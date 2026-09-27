# Карта архітектури

Це початкова карта. Під час ЛР 1 доповніть її власним трасованим запитом,
конкретними файлами та спостереженнями з DevTools і журналу PostgreSQL.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає звовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку або деталей інциденту |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері |

## Досліджений наскрізний маршрут

Маршрут: `GET /api/incidents/{id}` — перегляд деталей одного інциденту.

```text
клік по картці інциденту у Client/index.html
  → loadIncidentDetails у Client/app.js
  → GET /api/incidents/{id}
  → IncidentEndpoints.GetDetailsAsync (Presentation/Endpoints/IncidentEndpoints.cs)
  → IncidentQueries.GetDetailsAsync (Application/Incidents/IncidentQueries.cs)
  → SecureLabDbContext.Incidents (Data/SecureLabDbContext.cs)
  → PostgreSQL: таблиця incidents
  → IncidentDetailsResponse (Presentation/Contracts/IncidentResponses.cs)
  → JSON response
  → renderIncidentDetails у Client/app.js
  → DOM-вузли через textContent і document.createTextNode
```

Реалізована точка розширення: `GET /api/incidents/severity-summary`.

```text
натискання кнопки "Показати підсумок" у Client/index.html
  → loadSeveritySummary у Client/app.js
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeveritySummaryAsync
  → IncidentQueries.GetSeveritySummaryAsync (GroupBy + Count, повний перелік enum)
  → SecureLabDbContext.Incidents
  → PostgreSQL: таблиця incidents
  → IncidentSeveritySummaryResponse
  → JSON response
  → renderSeveritySummary у Client/app.js
  → DOM-вузли через textContent
```

## Межі довіри

| Межа | Дані, що її перетинають | Хибне припущення | Контроль на цьому маршруті |
|---|---|---|---|
| Браузер → API | method, URL, path parameter `id`, query parameter `status` | клієнт може запитати лише ID/статус із показаного інтерфейсу | маршрутне обмеження `:guid`; серверна валідація `status` через `Enum.TryParse` + `Enum.IsDefined` (allowlist), 400 для некоректного значення |
| API → PostgreSQL | id та умови запиту (`Where`, `GroupBy`) | збережений текст автоматично безпечний | параметризація EF Core, `AsNoTracking()`, явна проєкція в DTO |
| API → браузер | JSON-поля `IncidentDetailsResponse` / `IncidentSeveritySummaryResponse` | право прочитати ресурс означає право одержати всі поля entity | окремі response DTO без `OwnerUserId`, email, внутрішніх коментарів |
| Дані response → DOM | текстові значення з JSON (у т.ч. `description` з seed-текстом `<script>`) | текст можна інтерпретувати як HTML | вивід лише через `textContent` / `document.createTextNode`, підтверджено спостереженням у браузері |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.

## Спосіб повернення до відомого стану