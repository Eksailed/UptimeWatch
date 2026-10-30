# UptimeWatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Собрать сервис мониторинга сайтов: фоновые HTTP-проверки, инциденты, живой дашборд, алерты в Telegram, пользователи.

**Architecture:** Модульный монолит по Clean Architecture (`Domain ← Application ← Infrastructure/Web`). Один процесс ASP.NET Core: Blazor Server, SignalR-хаб, фоновый сервис проверок. Состояние планировщика хранится в PostgreSQL, проверки идут через интерфейс `IHealthChecker`.

**Tech Stack:** .NET 9, ASP.NET Core Blazor Server, SignalR, EF Core + Npgsql, ASP.NET Core Identity, xUnit, NSubstitute, Testcontainers.PostgreSql, Docker Compose, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-30-uptimewatch-design.md`

## Global Constraints

- Целевая платформа .NET 9, `Nullable` и `ImplicitUsings` включены, предупреждения считаются ошибками.
- Зависимости направлены внутрь: `Web → Application ← Infrastructure`; `Domain` не ссылается ни на что (кроме BCL).
- Сущность `Monitor` из спецификации в коде называется `SiteMonitor` (конфликт с `System.Threading.Monitor`).
- Порог падения: **3 подряд неудачных проверки**; повторных алертов при открытом инциденте нет.
- Параллельных проверок не больше **20** (`SemaphoreSlim`).
- Минимальный интервал монитора **30 секунд**; лимит мониторов на пользователя **20**; длина URL не больше 2048.
- Допустимые схемы URL: только `http` и `https`. Запрещены loopback, приватные сети и link-local адреса, проверка после DNS-резолва.
- Таймаут проверки 10 с, чтение тела ответа не больше 256 КБ (значения по умолчанию, лежат в `HealthCheckerOptions`).
- Результаты хранятся **30 дней**; аптайм считается за **24 часа, 7 и 30 дней**.
- Все выборки и изменения мониторов фильтруются по `OwnerId`; чужой id неотличим от несуществующего.
- Токен Telegram-бота не хранится в репозитории: `Telegram:BotToken` из user-secrets локально и из переменной окружения `Telegram__BotToken` в Docker.
- Недоступность Telegram логируется и не ломает цикл проверок.
- Время берётся из `TimeProvider`, а не из `DateTime.UtcNow` напрямую.
- Состояние (`NextCheckAt`, открытые инциденты) хранится в БД; перезапуск ничего не теряет.

## Review Focus

Входы и условия, которые спецификация подразумевает, но явно не описывает. Каждый закреплён тестом в указанной задаче.

1. Имя хоста резолвится в приватный адрес, в IPv4-mapped IPv6 (`::ffff:127.0.0.1`), или среди нескольких адресов есть приватный; редирект ведёт на внутренний адрес. Ожидание: проверка заблокирована, сервис не падает (Task 4).
2. Два тика планировщика подряд или перезапуск приложения посреди проверки. Ожидание: монитор не проверяется дважды одновременно и снова становится доступным после истечения аренды (Task 3, Task 5).
3. Монитор удалён, пока его проверка в полёте. Ожидание: результат тихо отбрасывается, уведомлений нет, цикл продолжается (Task 2, Task 3).
4. У пользователя не задан Telegram `chat_id`, токен бота пуст или Telegram отвечает ошибкой. Ожидание: алерт пропускается или логируется, инцидент всё равно сохраняется (Task 2, Task 8).
5. Запрос чужого монитора по id и подписка на хаб другого пользователя. Ожидание: «не найдено» и ни одного чужого события (Task 2, Task 7).

---

### Task 0: Каркас решения и CI

**Files:**
- Create: `UptimeWatch.sln`, `Directory.Build.props`, `.gitignore`, `.github/workflows/ci.yml`
- Create: проекты `src/UptimeWatch.{Domain,Application,Infrastructure}` (classlib) и тестовые `tests/UptimeWatch.{Domain,Application,Integration}.Tests` (xunit)
- Modify: `docs/superpowers/specs/2026-10-30-uptimewatch-design.md` (термин `Monitor` → `SiteMonitor` в разделе «Сущности»)

**Interfaces:**
- Produces: решение, которое собирается; ссылки проектов по правилу зависимостей; пакеты тестов (`Microsoft.Extensions.TimeProvider.Testing` для `FakeTimeProvider` во всех тестовых проектах, `NSubstitute`, `Testcontainers.PostgreSql`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.AspNetCore.SignalR.Client`) в `Integration.Tests`, `NSubstitute` в `Application.Tests`.

- [ ] **Step 1:** Создать решение и проекты командами `dotnet new sln/classlib/xunit` в каталогах из списка выше. `UptimeWatch.Web` создаётся в Task 6, до этого в решении его нет.
- [ ] **Step 2:** В `Directory.Build.props` задать `net9.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`. Добавить ссылки: `Application → Domain`, `Infrastructure → Application`, тестовые проекты → тестируемые.
- [ ] **Step 3:** `ci.yml`: триггеры `push` и `pull_request`; шаги checkout, `actions/setup-dotnet` с `9.0.x`, `dotnet restore`, `dotnet build --no-restore -c Release`, `dotnet test --no-build -c Release`. Раннер `ubuntu-latest` (там есть Docker для Testcontainers).
- [ ] **Step 4: Run** `dotnet build -c Release`. Expected: `Build succeeded`, 0 warnings.
- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "chore: scaffold solution and CI"
```

---

### Task 1: Доменная модель и правила

**Files:**
- Create: `src/UptimeWatch.Domain/{MonitorLimits,SiteMonitor,CheckResult,Incident,IncidentTransition,DomainException,UrlPolicy,IpAddressPolicy}.cs`
- Test: `tests/UptimeWatch.Domain.Tests/{SiteMonitorTests,UrlPolicyTests,IpAddressPolicyTests}.cs`

**Interfaces:**
- Produces:

```csharp
public static class MonitorLimits { public const int MinIntervalSeconds = 30, FailureThreshold = 3, MaxMonitorsPerUser = 20, MaxUrlLength = 2048; }
public enum IncidentTransition { None, Opened, Closed }
public sealed class DomainException(string message) : Exception(message);
public sealed class CheckResult {
  public Guid Id; public Guid MonitorId; public DateTime CheckedAt; public bool Success;
  public int? StatusCode; public int ResponseTimeMs; public string? Error;
  public static CheckResult Create(Guid monitorId, DateTime checkedAt, bool success, int? statusCode, int responseTimeMs, string? error);
}
public sealed class Incident { public Guid Id; public Guid MonitorId; public DateTime StartedAt; public DateTime? EndedAt; public string? LastError; public TimeSpan? Duration { get; } }
public sealed class SiteMonitor {
  public Guid Id; public string OwnerId; public string Url; public int IntervalSeconds; public int ExpectedStatusCode;
  public bool Enabled; public DateTime NextCheckAt; public int ConsecutiveFailures; public Incident? OpenIncident;
  public static SiteMonitor Create(string ownerId, string url, int intervalSeconds, int expectedStatusCode, DateTime now); // NextCheckAt = now
  public IncidentTransition ApplyResult(CheckResult result, DateTime now);  // обновляет счётчик, инцидент, NextCheckAt = now + interval
  public void SetEnabled(bool enabled, DateTime now);                        // при включении NextCheckAt = now
}
public static class UrlPolicy { public static bool TryValidate(string? url, out string? error); }
public static class IpAddressPolicy { public static bool IsPublic(System.Net.IPAddress address); }
```

- [ ] **Step 1: Write failing tests** `SiteMonitorTests`:
  - `Create_rejects_interval_below_30_seconds` (29 → `DomainException`, 30 принимается)
  - `Create_rejects_expected_status_outside_100_599` (99 и 600 → исключение)
  - `Create_rejects_url_that_fails_UrlPolicy`
  - `ApplyResult_two_failures_do_not_open_incident` (`None`, `OpenIncident is null`)
  - `ApplyResult_third_consecutive_failure_opens_incident` (`Opened`, `OpenIncident.StartedAt == now`, `LastError` = ошибка проверки)
  - `ApplyResult_success_between_failures_resets_counter` (F,F,S,F,F → `OpenIncident is null`)
  - `ApplyResult_fourth_failure_returns_None_while_incident_open`
  - `ApplyResult_success_closes_incident` (`Closed`, `EndedAt == now`, `Duration` = разница, `OpenIncident is null`, `ConsecutiveFailures == 0`)
  - `ApplyResult_sets_NextCheckAt_to_now_plus_interval`
  - `SetEnabled_true_makes_monitor_due_immediately`
- [ ] **Step 1b:** `UrlPolicyTests` (theory). Принимает: `http://example.com`, `https://example.com/p?x=1`. Отклоняет с непустым `error`: `""`, `null`, `example.com`, `ftp://x.com`, `file:///etc/passwd`, `javascript:alert(1)`, `http://user:pass@example.com`, `http://localhost`, `http://127.0.0.1:8080`, `http://[::1]/`, `http://10.0.0.5`, строку длиной 2049.
- [ ] **Step 1c:** `IpAddressPolicyTests` (theory). `IsPublic == true`: `8.8.8.8`, `2606:4700:4700::1111`. `false`: `127.0.0.1`, `10.0.0.1`, `172.16.0.1`, `192.168.1.1`, `169.254.169.254`, `0.0.0.0`, `100.64.0.1`, `::1`, `fe80::1`, `fc00::1`, `::ffff:127.0.0.1`, `::ffff:10.0.0.1`.
- [ ] **Step 2: Run** `dotnet test tests/UptimeWatch.Domain.Tests`. Expected: FAIL (типы не определены).
- [ ] **Step 3: Implement** типы из блока Interfaces. `UrlPolicy`: хост-литерал IP и `localhost` проверяются через `IpAddressPolicy` (DNS здесь не вызывается, он проверяется при соединении в Task 4). `IpAddressPolicy`: для IPv4-mapped IPv6 сначала `MapToIPv4()`.
- [ ] **Step 4: Run** те же тесты. Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): monitor, incident rules and URL/IP policies`

---

### Task 2: Application — интерфейсы и сценарии

**Files:**
- Create: `src/UptimeWatch.Application/{Abstractions.cs,Dtos.cs,MonitorService.cs,CheckProcessor.cs,CheckRunner.cs}`
- Test: `tests/UptimeWatch.Application.Tests/{Fakes/InMemoryMonitorRepository.cs,MonitorServiceTests.cs,CheckProcessorTests.cs,CheckRunnerTests.cs}`

**Interfaces:**
- Consumes: типы Task 1.
- Produces:

```csharp
public interface IMonitorRepository {
  Task AddAsync(SiteMonitor m, CancellationToken ct);
  Task<SiteMonitor?> GetAsync(Guid id, string ownerId, CancellationToken ct);
  Task<IReadOnlyList<SiteMonitor>> ListByOwnerAsync(string ownerId, CancellationToken ct);
  Task<int> CountByOwnerAsync(string ownerId, CancellationToken ct);
  Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct);
  Task UpdateAsync(SiteMonitor m, CancellationToken ct);
  // атомарно выбирает включённые мониторы с NextCheckAt <= now и сдвигает им NextCheckAt на now+lease
  Task<IReadOnlyList<SiteMonitor>> ClaimDueAsync(DateTime now, int limit, TimeSpan lease, CancellationToken ct);
  // в одной транзакции: результат, состояние монитора, открытый/закрытый инцидент. false, если монитор уже удалён
  Task<bool> SaveCheckAsync(SiteMonitor m, CheckResult r, CancellationToken ct);
}
public interface ICheckResultRepository {
  Task<double?> GetUptimePercentAsync(Guid monitorId, DateTime since, CancellationToken ct); // null, если проверок не было
  Task<CheckResult?> GetLastAsync(Guid monitorId, CancellationToken ct);
  Task<IReadOnlyList<CheckResult>> GetRecentAsync(Guid monitorId, int count, CancellationToken ct);
  Task<IReadOnlyList<Incident>> GetIncidentsAsync(Guid monitorId, int count, CancellationToken ct);
  Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct);
}
public interface IHealthChecker { Task<CheckResult> CheckAsync(SiteMonitor monitor, CancellationToken ct); } // не бросает на сетевых ошибках
public interface INotifier {
  Task NotifyDownAsync(SiteMonitor monitor, Incident incident, CancellationToken ct);
  Task NotifyRecoveredAsync(SiteMonitor monitor, Incident incident, CancellationToken ct);
}
public interface IMonitorNotifications { Task PublishAsync(string ownerId, MonitorStatusDto status, CancellationToken ct); }
public interface IUserProfileStore { Task<string?> GetTelegramChatIdAsync(string userId, CancellationToken ct); Task SetTelegramChatIdAsync(string userId, string? chatId, CancellationToken ct); }
public interface ICurrentUser { string UserId { get; } }

public sealed record OperationResult<T>(T? Value, string? Error);
public sealed record UptimeDto(double? Last24h, double? Last7d, double? Last30d);
public sealed record MonitorStatusDto(Guid Id, string Url, int IntervalSeconds, int ExpectedStatusCode, bool Enabled,
    bool IsDown, int? LastStatusCode, int? LastResponseTimeMs, DateTime? LastCheckedAt, UptimeDto Uptime);

public sealed class MonitorService(IMonitorRepository monitors, ICheckResultRepository results, TimeProvider time) {
  Task<OperationResult<MonitorStatusDto>> CreateAsync(string ownerId, string url, int intervalSeconds, int expectedStatusCode, CancellationToken ct);
  Task<IReadOnlyList<MonitorStatusDto>> ListAsync(string ownerId, CancellationToken ct);
  Task<MonitorStatusDto?> GetAsync(Guid id, string ownerId, CancellationToken ct);
  Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct);
  Task<bool> SetEnabledAsync(Guid id, string ownerId, bool enabled, CancellationToken ct);
}
public sealed class CheckProcessor(IHealthChecker checker, IMonitorRepository monitors, ICheckResultRepository results,
    INotifier notifier, IMonitorNotifications notifications, TimeProvider time, ILogger<CheckProcessor> log) {
  Task ProcessAsync(SiteMonitor monitor, CancellationToken ct);
}
public sealed class CheckRunner(IServiceScopeFactory scopes, TimeProvider time) {
  const int MaxConcurrentChecks = 20, ClaimBatchSize = 100; static readonly TimeSpan ClaimLease = TimeSpan.FromSeconds(60);
  Task RunDueAsync(CancellationToken ct); // claim → параллельно ProcessAsync в отдельных scope
}
```

`MonitorStatusDto` собирается внутри `MonitorService` и `CheckProcessor` одним общим приватным методом-построителем (аптайм по трём окнам, последний результат). По 3 запроса на монитор допустимы из-за лимита в 20 мониторов на пользователя.

- [ ] **Step 1: Write failing tests** `MonitorServiceTests` (in-memory репозиторий, `FakeTimeProvider`):
  - `Create_persists_monitor_for_owner_and_returns_dto`
  - `Create_returns_error_for_invalid_url_and_does_not_persist` (`http://localhost`)
  - `Create_returns_error_when_owner_already_has_20_monitors`
  - `Get_returns_null_for_other_owners_monitor_and_for_unknown_id`
  - `Delete_by_other_owner_returns_false_and_keeps_monitor`
  - `List_returns_only_owners_monitors`
  - `SetEnabled_by_other_owner_returns_false`
- [ ] **Step 1b:** `CheckProcessorTests` (NSubstitute для `IHealthChecker`, `INotifier`, `IMonitorNotifications`):
  - `Process_saves_result_and_publishes_status_to_owner`
  - `Process_third_failure_opens_incident_and_calls_NotifyDown_once`
  - `Process_fourth_failure_does_not_notify_again`
  - `Process_success_after_incident_calls_NotifyRecovered_with_closed_incident`
  - `Process_swallows_notifier_exception_and_state_is_still_saved`
  - `Process_does_not_notify_or_publish_when_SaveCheck_returns_false` (монитор удалён в полёте)
  - `Process_does_not_throw_when_health_checker_throws` (логирует, состояние не меняется)
- [ ] **Step 1c:** `CheckRunnerTests` (DI-контейнер с заглушками):
  - `RunDue_never_runs_more_than_20_checks_concurrently` (50 мониторов, `IHealthChecker` с задержкой и счётчиком; пик ≤ 20)
  - `RunDue_continues_when_one_check_throws`
- [ ] **Step 2: Run** `dotnet test tests/UptimeWatch.Application.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement** по сигнатурам. Подсказки: ошибки `DomainException` из `SiteMonitor.Create` и `UrlPolicy` превращаются в `OperationResult.Error`; `CheckProcessor` вызывает `INotifier` только после успешного `SaveCheckAsync` и оборачивает уведомления в `try/catch` с логированием; `CheckRunner` создаёт scope на каждую проверку и ограничивает параллелизм `SemaphoreSlim(20)`.
- [ ] **Step 4: Run** те же тесты. Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): monitor service, check processor and runner`

---

### Task 3: Персистентность (EF Core + PostgreSQL)

**Предусловие:** на машине установлен и запущен Docker Desktop (нужен для Testcontainers).

**Files:**
- Create: `src/UptimeWatch.Infrastructure/Persistence/{AppDbContext,MonitorRepository,CheckResultRepository}.cs`, `.../Identity/ApplicationUser.cs`, `.../DependencyInjection.cs`, `.config/dotnet-tools.json` (dotnet-ef)
- Create: `src/UptimeWatch.Infrastructure/Persistence/Migrations/*` (генерируется)
- Test: `tests/UptimeWatch.Integration.Tests/{PostgresFixture.cs,MonitorRepositoryTests.cs,CheckResultRepositoryTests.cs}`

**Interfaces:**
- Consumes: `IMonitorRepository`, `ICheckResultRepository` (Task 2); сущности Task 1.
- Produces: `AppDbContext : IdentityDbContext<ApplicationUser>` с `DbSet<SiteMonitor> Monitors`, `DbSet<CheckResult> CheckResults`, `DbSet<Incident> Incidents`; `ApplicationUser : IdentityUser { string? TelegramChatId }`; `IServiceCollection.AddInfrastructure(IConfiguration)` — регистрирует контекст (строка `ConnectionStrings:Default`), репозитории и `TimeProvider.System`; `PostgresFixture` (контейнер `postgres:17`, `IAsyncLifetime`, метод `AppDbContext CreateContext()` с применёнными миграциями).

- [ ] **Step 1: Write failing tests** (на реальном PostgreSQL):
  - `MonitorRepositoryTests`: `Get_filters_by_owner`; `ClaimDue_returns_only_enabled_monitors_with_NextCheckAt_in_the_past_ordered_by_NextCheckAt`; `ClaimDue_called_twice_returns_each_monitor_once` (параллельные вызовы через `Task.WhenAll`); `ClaimDue_monitor_becomes_due_again_after_lease_expires`; `SaveCheck_persists_result_state_and_opened_incident_together`; `SaveCheck_closes_incident_and_stores_EndedAt`; `SaveCheck_returns_false_when_monitor_was_deleted`; `Delete_cascades_results_and_incidents`; `Delete_by_other_owner_returns_false`.
  - `CheckResultRepositoryTests`: `Uptime_is_75_percent_for_3_successes_and_1_failure_in_window`; `Uptime_ignores_results_older_than_window`; `Uptime_is_null_without_results`; `DeleteOlderThan_removes_only_results_before_cutoff`.
- [ ] **Step 2: Run** `dotnet test tests/UptimeWatch.Integration.Tests --filter "FullyQualifiedName~Repository"`. Expected: FAIL.
- [ ] **Step 3: Implement** маппинг и репозитории. Подходы: `ClaimDueAsync` — один SQL-запрос `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING` через `FromSqlInterpolated`/`ExecuteSql`; `SaveCheckAsync` — транзакция, при отсутствии монитора возвращает `false` (ловить `DbUpdateConcurrencyException`/проверять наличие). Индексы: `CheckResults(MonitorId, CheckedAt)`, `Monitors(OwnerId)`, `Monitors(NextCheckAt)`. Каскадное удаление результатов и инцидентов. Время хранить как `timestamp with time zone` в UTC.
- [ ] **Step 4:** Установить `dotnet tool install dotnet-ef` локально (манифест), сгенерировать миграцию `InitialCreate` командой `dotnet ef migrations add InitialCreate -p src/UptimeWatch.Infrastructure -o Persistence/Migrations` (для запуска нужен стартовый проект; на этом этапе используйте `IDesignTimeDbContextFactory<AppDbContext>` в Infrastructure).
- [ ] **Step 5: Run** тесты из Step 2. Expected: PASS.
- [ ] **Step 6: Commit** `feat(infra): EF Core persistence with atomic claim and save`

---

### Task 4: HTTP-проверка с защитой от SSRF

**Files:**
- Create: `src/UptimeWatch.Infrastructure/Checks/{HealthCheckerOptions,AddressGuard,HttpHealthChecker}.cs`
- Modify: `src/UptimeWatch.Infrastructure/DependencyInjection.cs` (именованный `HttpClient` и регистрация `IHealthChecker`)
- Test: `tests/UptimeWatch.Integration.Tests/{AddressGuardTests.cs,HttpHealthCheckerTests.cs,Support/TestSite.cs}`

**Interfaces:**
- Consumes: `IHealthChecker`, `IpAddressPolicy`, `CheckResult.Create`.
- Produces:

```csharp
public sealed class HealthCheckerOptions { TimeSpan Timeout = 10s; int MaxResponseBytes = 262144; int MaxRedirects = 5; bool AllowPrivateAddresses = false; }
public interface IDnsResolver { Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct); }
public sealed class BlockedAddressException(string message) : Exception(message);
public static class AddressGuard {
  // IP-литерал проверяется сразу; иначе резолвит; если ЛЮБОЙ адрес непубличный — бросает BlockedAddressException
  public static Task<IPAddress[]> ResolveAllowedAsync(string host, IDnsResolver dns, bool allowPrivate, CancellationToken ct);
}
public sealed class HttpHealthChecker : IHealthChecker { /* SocketsHttpHandler.ConnectCallback вызывает AddressGuard и подключается к проверенному IP; редиректы разрешены (до MaxRedirects), каждое соединение проходит ту же проверку */ }
```

`TestSite` — вспомогательный Kestrel-сервер на случайном loopback-порту с управляемыми маршрутами (`/ok`, `/status/{code}`, `/slow`, `/endless`); для тестов используется `AllowPrivateAddresses = true`.

- [ ] **Step 1: Write failing tests** `AddressGuardTests` (поддельный `IDnsResolver`):
  - `Throws_when_host_resolves_to_loopback` (`127.0.0.1`)
  - `Throws_when_host_resolves_to_ipv4_mapped_private` (`::ffff:10.0.0.1`)
  - `Throws_when_any_of_several_addresses_is_private` (`8.8.8.8` + `192.168.1.1`)
  - `Throws_for_private_ip_literal_without_calling_resolver`
  - `Returns_addresses_when_all_are_public` (`93.184.216.34`)
  - `Allows_private_when_allowPrivate_is_true`
- [ ] **Step 1b:** `HttpHealthCheckerTests` (с `TestSite`):
  - `Check_succeeds_with_status_and_elapsed_ms_when_code_matches`
  - `Check_fails_when_status_differs_from_expected` (`Error` содержит фактический код)
  - `Check_follows_redirect_to_expected_target`
  - `Check_fails_with_timeout_error_when_server_is_slower_than_timeout` (таймаут 500 мс)
  - `Check_fails_with_message_on_connection_refused`
  - `Check_fails_on_unresolvable_host` (`nonexistent.invalid`)
  - `Check_is_blocked_for_loopback_when_private_addresses_not_allowed` (`Error` содержит `blocked`, исключение не вылетает)
  - `Check_reads_at_most_MaxResponseBytes_from_endless_body` (завершается в пределах таймаута, `Success == true`)
- [ ] **Step 2: Run** `dotnet test tests/UptimeWatch.Integration.Tests --filter "FullyQualifiedName~AddressGuard|FullyQualifiedName~HttpHealthChecker"`. Expected: FAIL.
- [ ] **Step 3: Implement.** `HttpHealthChecker` ловит `HttpRequestException`, `TaskCanceledException` (таймаут), `BlockedAddressException` и превращает их в неуспешный `CheckResult` с понятным `Error`; время ответа меряется `Stopwatch`; тело читается потоково до `MaxResponseBytes` и отбрасывается; отмена по `ct` приложения отличается от таймаута проверки.
- [ ] **Step 4: Run** тесты из Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(infra): HTTP health checker with SSRF guard`

---

### Task 5: Полный цикл проверки

**Files:**
- Test: `tests/UptimeWatch.Integration.Tests/FullCycleTests.cs`
- Modify: `src/UptimeWatch.Infrastructure/DependencyInjection.cs` (если тесту не хватает регистраций)

**Interfaces:**
- Consumes: `CheckRunner.RunDueAsync`, `MonitorService`, `HttpHealthChecker`, `PostgresFixture`, `TestSite`.
- Produces: проверенный сквозной сценарий «монитор → проверка → инцидент → уведомление» без веб-слоя. Хостед-сервисы `MonitorScheduler` и `RetentionCleaner` добавляются в Task 6 как тонкие обёртки над уже протестированной логикой.

- [ ] **Step 1: Write failing tests** `FullCycleTests` (реальный PostgreSQL + `HttpHealthChecker` + `TestSite` + `INotifier` на NSubstitute + `FakeTimeProvider`, мониторы создаются через `MonitorService`):
  - `Full_cycle_three_failures_open_incident_and_recovery_closes_it` (сайт отвечает 500 три цикла → `NotifyDown` ровно 1 раз и `OpenIncident` сохранён; затем 200 → `NotifyRecovered` 1 раз с `Duration > 0`; промежуточные тики не вызывают повторных алертов)
  - `RunDue_checks_only_due_monitors` (монитор с интервалом 60 с не проверяется повторно через 10 с)
  - `Two_overlapping_RunDue_calls_check_each_monitor_once` (`Task.WhenAll`, счётчик запросов `TestSite` равен числу мониторов)
  - `Disabled_monitor_is_never_checked`
- [ ] **Step 2: Run** `dotnet test tests/UptimeWatch.Integration.Tests --filter "FullyQualifiedName~FullCycle"`. Expected: FAIL.
- [ ] **Step 3: Implement** недостающие регистрации; если тесты проходят сразу, менять код не нужно.
- [ ] **Step 4: Run** те же тесты. Expected: PASS.
- [ ] **Step 5: Commit** `test: full check cycle against real PostgreSQL`

---

### Task 6: Web-оболочка и Identity

**Files:**
- Create: `src/UptimeWatch.Web` (Blazor Server) через `dotnet new blazor --interactivity Server --auth Individual`; добавить в решение; ссылки на `Application` и `Infrastructure`
- Modify: `Program.cs` (подключить `AddInfrastructure`, Identity на `AppDbContext`, `TimeProvider`, хостед-сервисы Task 5, применение миграций при старте), удалить SQLite-миграции шаблона и шаблонный `ApplicationDbContext`
- Create: `src/UptimeWatch.Web/Auth/CurrentUser.cs`, `src/UptimeWatch.Web/Background/{MonitorScheduler,RetentionCleaner}.cs`
- Test: `tests/UptimeWatch.Integration.Tests/{WebAppFactory.cs,AuthTests.cs}`

**Interfaces:**
- Consumes: `AppDbContext`, `ApplicationUser`, `ICurrentUser`, `CheckRunner.RunDueAsync`, `ICheckResultRepository.DeleteOlderThanAsync`.
- Produces: `MonitorScheduler : BackgroundService` (цикл: `RunDueAsync`, пауза 5 с; исключения логируются, цикл не прерывается; корректная остановка по токену); `RetentionCleaner : BackgroundService` (раз в сутки удаляет результаты старше 30 дней); `CurrentUser : ICurrentUser` (берёт id из `AuthenticationStateProvider`/`IHttpContextAccessor`, бросает при анонимном пользователе); `WebAppFactory : WebApplicationFactory<Program>` (подключается к контейнеру `PostgresFixture`, `Telegram:BotToken` пуст); `public partial class Program`.

- [ ] **Step 1: Write failing tests** `AuthTests`:
  - `Anonymous_request_to_monitors_page_redirects_to_login` (код 302/`Location` содержит `/Account/Login`)
  - `Registered_user_can_sign_in_with_password` (через `UserManager`/`SignInManager`)
  - `Duplicate_email_registration_is_rejected`
  - `App_starts_without_telegram_token` (фабрика создаётся, `GET /` отвечает)
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~AuthTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** по списку Files. Страница `/monitors` помечается `[Authorize]`; регистрация и вход берутся из шаблона Identity.
- [ ] **Step 4: Run** тесты из Step 2 и `dotnet build`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(web): Blazor shell with Identity`

---

### Task 7: Дашборд и SignalR

**Files:**
- Create: `src/UptimeWatch.Web/Hubs/MonitorHub.cs`, `src/UptimeWatch.Web/Realtime/SignalRMonitorNotifications.cs`
- Create: `src/UptimeWatch.Web/Components/Pages/{Monitors,MonitorDetails}.razor` и компонент формы `AddMonitorForm.razor`
- Test: `tests/UptimeWatch.Integration.Tests/MonitorHubTests.cs`

**Interfaces:**
- Consumes: `MonitorService`, `ICheckResultRepository`, `IMonitorNotifications`, `ICurrentUser`.
- Produces: `MonitorHub` (`[Authorize]`, при подключении добавляет соединение в группу `owner-{userId}`); клиентское событие `MonitorUpdated(MonitorStatusDto)`; `SignalRMonitorNotifications : IMonitorNotifications` отправляет событие в группу владельца; маршрут хаба `/hubs/monitors`.

- [ ] **Step 1: Write failing tests** `MonitorHubTests` (`WebAppFactory`, два пользователя, `HubConnection` с cookie-аутентификацией):
  - `Update_for_owner_A_is_received_by_A`
  - `Update_for_owner_A_is_not_received_by_B`
  - `Anonymous_connection_to_hub_is_rejected`
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~MonitorHub"`. Expected: FAIL.
- [ ] **Step 3: Implement** хаб, `SignalRMonitorNotifications` и регистрацию в `Program.cs`. Страница `Monitors.razor`: список карточек (URL, бейдж Up/Down, последний код и время ответа, аптайм 24ч/7д/30д), форма добавления (ошибки из `OperationResult.Error` показываются рядом с формой), кнопки «Пауза/Включить» и «Удалить»; компонент подписывается на `MonitorUpdated` и обновляет карточку без перезагрузки, отписывается в `DisposeAsync`. `MonitorDetails.razor`: последние 50 результатов и 20 инцидентов монитора (чужой id → «Не найдено»).
- [ ] **Step 4: Run** тесты из Step 2. Expected: PASS.
- [ ] **Step 5: Verify вручную:** запустить `dotnet run --project src/UptimeWatch.Web` (PostgreSQL из Task 9 или временный контейнер), зарегистрироваться, добавить `https://example.com` с интервалом 30 с и убедиться, что карточка обновляется без перезагрузки; добавить `https://example.com/404` с ожидаемым кодом 200 и увидеть статус Down после трёх проверок.
- [ ] **Step 6: Commit** `feat(web): live dashboard with SignalR`

---

### Task 8: Telegram-алерты и профиль

**Files:**
- Create: `src/UptimeWatch.Application/ITelegramSender.cs`, `src/UptimeWatch.Infrastructure/Notifications/{TelegramOptions,TelegramSender,TelegramNotifier,NullNotifier}.cs`, `src/UptimeWatch.Infrastructure/Identity/UserProfileStore.cs`
- Create: `src/UptimeWatch.Web/Components/Pages/Profile.razor`
- Test: `tests/UptimeWatch.Integration.Tests/{TelegramSenderTests.cs,TelegramNotifierTests.cs}`

**Interfaces:**
- Consumes: `INotifier`, `IUserProfileStore`.
- Produces: `interface ITelegramSender { Task SendAsync(string chatId, string text, CancellationToken ct); }`; `TelegramSender` (POST `https://api.telegram.org/bot{token}/sendMessage`, JSON `chat_id` и `text`, на не-2xx бросает исключение); `TelegramNotifier : INotifier` (собирает текст, ищет `chat_id` владельца через `IUserProfileStore`); `NullNotifier : INotifier` (логирует предупреждение) регистрируется, если `Telegram:BotToken` пуст; `UserProfileStore : IUserProfileStore` на `UserManager<ApplicationUser>`.

- [ ] **Step 1: Write failing tests:**
  - `TelegramSenderTests` (заглушка `HttpMessageHandler`): `Sender_posts_chat_id_and_text_to_bot_endpoint`; `Sender_throws_on_non_success_status`.
  - `TelegramNotifierTests` (`ITelegramSender` и `IUserProfileStore` на NSubstitute): `Notifier_skips_silently_when_user_has_no_chat_id`; `Down_message_contains_url_and_last_error`; `Recovered_message_contains_url_and_downtime_duration`; `Notifier_propagates_sender_exception` (перехватывает `CheckProcessor`, см. Task 2).
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~Telegram"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Регистрация в `AddInfrastructure`: при непустом токене `TelegramNotifier`, иначе `NullNotifier`. `Profile.razor`: поле `chat_id`, кнопка «Сохранить» и «Отправить тест»; сообщение об ошибке отправки показывается пользователю. Инструкция в README (Task 9): как создать бота у BotFather и узнать `chat_id`; токен задаётся `dotnet user-secrets set "Telegram:BotToken" "<token>"`.
- [ ] **Step 4: Run** тесты из Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat: Telegram alerts and profile page`

---

### Task 9: Docker, README и финальная проверка

**Files:**
- Create: `Dockerfile`, `.dockerignore`, `docker-compose.yml`
- Modify: `README.md`; `src/UptimeWatch.Web/Program.cs` (применение миграций при старте — если не сделано в Task 6)

**Interfaces:**
- Consumes: всё из задач 0–8.
- Produces: `docker compose up --build` поднимает приложение на `http://localhost:8080` и PostgreSQL 17 с томом; переменные `ConnectionStrings__Default`, `Telegram__BotToken` берутся из окружения/`.env` (в репозитории только `.env.example`).

- [ ] **Step 1:** `Dockerfile` — multi-stage (`mcr.microsoft.com/dotnet/sdk:9.0` → `aspnet:9.0`), публикация `UptimeWatch.Web`. В `docker-compose.yml` сервис `db` с healthcheck `pg_isready`, сервис `web` с `depends_on: condition: service_healthy`.
- [ ] **Step 2: Run** `docker compose up --build -d`, затем открыть `http://localhost:8080`. Expected: страница входа открывается, регистрация и добавление монитора работают, миграции применились автоматически.
- [ ] **Step 3: Run** `dotnet test -c Release` в корне. Expected: все тесты PASS.
- [ ] **Step 4:** Обновить `README.md`: бейдж CI, описание, быстрый старт (`docker compose up`), настройка Telegram, схема архитектуры (Mermaid), скриншоты дашборда, раздел «Решения и компромиссы» (монолит вместо двух процессов; порог в 3 проверки; защита от SSRF на уровне соединения; аренда монитора вместо блокировок), раздел «Что дальше» (публичная статус-страница, графики, SSL-срок, повторная отправка алертов). Убрать пометку «проект в стадии проектирования».
- [ ] **Step 5: Commit и push**, убедиться, что CI на GitHub зелёный.

```bash
git add -A && git commit -m "feat: Docker Compose, README and release polish" && git push
```

---

## Self-Review (покрытие спецификации)

| Требование спецификации | Задача |
|---|---|
| Мониторы: URL, интервал, ожидаемый код; лимиты | 1, 2, 7 |
| Фоновые проверки, таймаут, ограничение параллелизма | 2, 4, 5 |
| История, аптайм 24ч/7д/30д, очистка старше 30 дней | 3, 5, 7 |
| Инциденты: порог 3, открытие/закрытие, без повторных алертов | 1, 2, 5 |
| Живой дашборд (SignalR), изоляция по владельцу | 7 |
| Алерты в Telegram, профиль, токен вне репозитория | 8, 9 |
| Пользователи, фильтрация по `OwnerId` | 2, 3, 6, 7 |
| Защита от SSRF, лимит ответа | 1, 4 |
| Состояние в БД, переживает перезапуск | 3, 5 |
| Тесты Domain / Application / Integration, CI | 0–8 |
| Docker Compose, миграции при старте, README | 6, 9 |

Допущения, принятые планом там, где спецификация молчит: лимит 20 мониторов на пользователя; таймаут 10 с; ограничение тела 256 КБ; URL с логином и паролем отклоняются; проверка продолжается по редиректам (до 5), каждое соединение проходит защиту; аренда монитора 60 с.
