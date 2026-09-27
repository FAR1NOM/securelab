# Звіт до лабораторної роботи № 1

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Гілка: `lab/1-system`.
- Фінальний тег: `v0.1.0` (буде створено після фінального commit і merge — див. розділ «Git-цикл» нижче).
- Commit hash: заповнити після `git rev-parse HEAD` на злитому стані main.

## 2. Змінений маршрут

Реалізовано наскрізне розширення `GET /api/incidents/severity-summary`
(раніше повертав `501 Not Implemented`, тепер повертає `200 OK`).

```text
натискання кнопки "Показати підсумок" у Client/index.html
  → loadSeveritySummary у Client/app.js
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeveritySummaryAsync
      (src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs)
  → IncidentQueries.GetSeveritySummaryAsync
      (src/SecureLab.Api/Application/Incidents/IncidentQueries.cs)
      — AsNoTracking(), опційний Where(status), GroupBy(Severity), Count()
  → SecureLabDbContext.Incidents
      (src/SecureLab.Api/Data/SecureLabDbContext.cs)
  → PostgreSQL: таблиця incidents
  → IncidentSeveritySummaryResponse
      (src/SecureLab.Api/Presentation/Contracts/IncidentResponses.cs)
  → JSON response
  → renderSeveritySummary у Client/app.js
  → DOM-вузли через textContent
```

Політика нульових груп: **повний перелік enum** `IncidentSeverity` —
рівні, відсутні в результаті (наприклад Critical на baseline seed),
повертаються з `Count: 0`. Сталий порядок: порядок оголошення enum
(Low, Medium, High, Critical) — порядок критичності.

## 3. Виконані зміни

1. Додано `IncidentSeveritySummaryResponse(string Severity, int Count)` —
   окремий response DTO без зайвих полів entity.
2. Додано `IncidentQueries.GetSeveritySummaryAsync` — асинхронний query через
   `AsNoTracking()`, `GroupBy`, `Count()`, `ToListAsync(cancellationToken)`,
   з опційним фільтром за `status` та доповненням відсутніх severity-груп.
3. Замінено baseline-заглушку `501` в `IncidentEndpoints.cs` на реальний
   endpoint з DI (`IncidentQueries`), типізованою metadata
   `Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()`.
4. Додано (добрий рівень) необов'язковий query parameter `status` до
   `severity-summary`: сервер перевіряє значення за явним allowlist
   (`Enum.TryParse` + `Enum.IsDefined`) і повертає `400 Validation Problem
   Details` для некоректного значення — той самий паттерн, що й у
   `GET /api/incidents?status=`.
5. У `Client/index.html` додано секцію "Підсумок за severity" з кнопкою.
6. У `Client/app.js` додано `loadSeveritySummary`/`renderSeveritySummary` з
   трьома станами: завантаження, порожній результат ("Даних немає."),
   безпечна помилка (без stack trace/деталей). Вивід — лише через
   `textContent`.
7. Додано структуроване журналювання (`logger.LogInformation`) початку і
   результату операції (кількість сформованих груп), без чутливих даних.
8. Оновлено `tests/http/incidents.http`: заміщено очікування `501` на `200`,
   додано сценарії з коректним і некоректним `status`.

## 4. Перевірка

| ID | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|
| T-01 | PostgreSQL healthy, API запущено | `GET /health` | 200, API з'єднано з БД | 200, `{"status":"ready"}` | `.http`/curl response |
| T-02 | відновлений seed | `GET /api/incidents?status=Triaged` | 200, список відповідає фільтру | 200, 1 інцидент (Medium/Triaged) | DevTools Network + response |
| T-03 | відновлений seed | `GET /api/incidents?status=Resolved` | 200 з порожнім масивом | 200, `[]` | curl response |
| T-04 | відновлений seed | `GET /api/incidents/99999999-...` | 404 Problem Details | 404, `title: "Інцидент не знайдено"` | curl response |
| T-05 | відновлений seed | `GET /api/incidents?status=Unknown` | 400 Validation Problem Details | 400, allowlist-повідомлення | curl response |
| T-06 | реалізовано Етап 3, відновлений seed | `GET /api/incidents/severity-summary` | 200; Low:1, Medium:1, High:1, Critical:0 (повний перелік) | 200, точно цей результат | curl response |
| T-06b | реалізовано query parameter | `GET /api/incidents/severity-summary?status=Triaged` | 200; лише Medium:1, решта 0 | 200, `Low:0, Medium:1, High:0, Critical:0` | curl response |
| T-06c | реалізовано query parameter | `GET /api/incidents/severity-summary?status=Unknown` | 400 Validation Problem Details | 400, той самий allowlist | curl response |
| T-07 | API і клієнт запущено | натиснути "Показати підсумок" | UI безпечно показує Low/Medium/High/Critical з лічильниками | підтверджено скриншотом, DevTools Network — 200 | Network + скриншот UI |
| T-08 | після експериментів із даними | `--reset-database`, повторити T-02 і T-06 | seed повертає стенд до відомого стану | підтверджено: `dotnet test` → 4/4 passed після reset | вивід команди reset + test |

Автоматизовані тести: `dotnet test tests/SecureLab.Api.Tests/...` —
**4 з 4 пройдено, 0 збоїв**, перевірено двічі (до і після додавання query
parameter `status`).

## 5. Security-сценарій

Не застосовується для ЛР 1: варіант 2-A не передбачає навмисно вразливого
стану — лише завершення підготовленої точки розширення без зміни схеми БД.
Наскрізна перевірка меж довіри виконана в Етапі 2 (див. `docs/architecture.md`,
розділ «Межі довіри»): підтверджено, зокрема, що seed-текст із `<script>`
у полі `description` відображається на сторінці як звичайний текст, а не
виконується як HTML (вивід через `textContent`).

## 6. Висновок

Наскрізний маршрут `GET /api/incidents/{id}` простежено від кліку в браузері
до рядка таблиці `incidents` у PostgreSQL і назад, із фактичними доказами на
кожному рівні (Network, код, SQL). Точку розширення `GET
/api/incidents/severity-summary` реалізовано відповідно до контракту:
окремий DTO, `AsNoTracking()+GroupBy+Count()`, повна політика нульових груп
зі сталим порядком за критичністю, безпечний DOM-вивід із трьома станами
клієнта, структуроване журналювання та валідація опційного параметра
`status` за allowlist. Усі перевірки (T-01…T-08) відтворено і задокументовано
фактичними відповідями API; автоматизовані тести залишаються зеленими (4/4)
після reset і після зміни коду. Виконана робота відповідає вимогам доброго
рівня.