# 05. SecurityAgent: Скрытная инспекция, OSINT и аудит

**SecurityAgent** — независимый аналитический агент безопасности. Его миссия — предоставить исчерпывающую оценку рисков по любому фрагменту кода, сетевому адресу, ссылке или email-адресу, не раскрывая присутствие системы перед потенциальным злоумышленником.

---

### Принцип Stealth OPSEC (Скрытность расследований)

> [!CAUTION]
> **Категорический запрет прямых запросов:**
> SecurityAgent **никогда не обращается напрямую** к проверяемым URL, IP-адресам или доменам!
> * Запрещены прямые пинги (`ping`, `icmp`).
> * Запрещены прямые сетевые сканирования (`nmap`).
> * Запрещены прямые HTTP-запросы (`curl`, `HttpClient.GetAsync`) с хоста оркестратора на целевой ресурс.
> Любой прямой контакт раскрывает IP-адрес оркестратора, позволяет атакующему зафиксировать факт проверки и активировать контрмеры (например, выдать безвредный контент защитному боту и вредоносный — реальному пользователю).

```
                      ┌─────────────────────────────────┐
                      │    SECURITY AGENT INSPECTOR     │
                      │   (Never touches target directly)│
                      └────────────────┬────────────────┘
                                       │
                  ┌────────────────────┼────────────────────┐
                  ▼                    ▼                    ▼
       ┌────────────────────┐ ┌──────────────────┐ ┌────────────────────┐
       │   PASSIVE OSINT    │ │  EMAIL REPUTATION│ │   ISOLATED FILE    │
       │     GATEWAY        │ │     SCANNER      │ │   ANALYSIS SANDBOX │
       │ - VirusTotal API   │ │ - Disposable check│ │ - Ephemeral Wasm/  │
       │ - URLhaus / Feodo  │ │ - Passive MX/DNS │ │   Docker container │
       │ - AbuseIPDB API    │ │ - Breach DBs     │ │ - Unpack zip/tar   │
       │ - Passive DNS (DoH)│ │ - Spoofing check │ │ - De-obfuscation   │
       └────────────────────┘ └──────────────────┘ └────────────────────┘
```

---

### Аналитические модули SecurityAgent

#### 1. Модуль пассивного OSINT (URL & IP Intelligence)
* Все проверки осуществляются через сторонние REST API и агрегаторы:
  * **Проверка URL/доменов:** Запросы к базам URLhaus, PhishTank, Google Safe Browsing, VirusTotal.
  * **Проверка IP:** Запросы к репутационным базам AbuseIPDB, Shodan API, GreyNoise.
  * **DNS-запросы:** Выполняются через зашифрованные сторонние рекурсоры (DNS-over-HTTPS / DoH: Cloudflare, Quad9) либо через пул ротируемых SOCKS5/Tor-прокси, если стороннее API недоступно.
* **SSRF-фильтр:** Проверка IP на принадлежность к внутренним и зарезервированным сетям:
  * `127.0.0.0/8`, `::1` (Loopback);
  * `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` (Приватные сети RFC 1918);
  * `169.254.169.254` (Облачные метаданные AWS/GCP/Azure);
  * Carrier-grade NAT (`100.64.0.0/10`).

#### 2. Модуль инспекции Email (Email Security)
* **Детект одноразовых почт (Disposable Email):** Проверка домена по обновляемым реестрам временных почтовых сервисов (Mailinator, GuerrillaMail, TempMail).
* **Пассивная валидация почтового домена:** Проверка наличия валидных MX-записей через DoH.
* **Анализ аутентичности домена:** Проверка наличия записей SPF (`v=spf1`), DKIM и DMARC для исключения спуфинга.
* **Проверка утечек (Breach Check):** Запрос через k-Anonymity API (HaveIBeenPwned) на наличие адреса в скомпрометированных базах данных.

#### 3. Модуль скриптового AST-анализа
* **PowerShell AST (`System.Management.Automation`):**
  * Поиск вызовов `Invoke-Expression`, `iex`, вызовов методов через рефлексию (`[System.Reflection.Assembly]::Load`).
  * Анализ загрузок из сети (`DownloadString`, `DownloadData`, `Net.WebClient`).
  * Детект обфускации: параметры `-enc`, строковая конкатенация опасных команд, скрытые обратные кавычки (`` ` ``).
* **Bash Heuristics:**
  * Деструктивные действия (`rm -rf`, `mkfs`, `> /dev/sda`).
  * Реверс-шеллы (`/dev/tcp/`, вызовы `nc -e`, `socat`).

#### 4. Одноразовая песочница для динамического анализа (Ephemeral Sandbox)
Если скрипт или вложение требует распаковки архива, вычисления контрольных сумм или безопасной статической деобфускации:
* SecurityAgent запрашивает у `ISandboxProvider` **одноразовую песочницу без сетевого интерфейса** (`NetworkMode = "none"`).
* Анализ выполняется с жестким лимитом по памяти (64 МБ), CPU (0.2 core) и таймаутом (2 секунды).
* По завершении песочница немедленно уничтожается.

---

### Асинхронное неблокирующее взаимодействие

SecurityAgent работает как асинхронный потребитель сообщений из шины:

```csharp
namespace CyberPipe.Agents.Security.Consumers;

public class InspectPayloadConsumer : IConsumer<InspectPayloadCommand>
{
    private readonly ISecurityInspectorService _inspector;
    private readonly IPublishEndpoint _publishEndpoint;

    public async Task Consume(ConsumeContext<InspectPayloadCommand> context)
    {
        var command = context.Message;
        
        // Полностью асинхронный анализ без блокировки вызывающей стороны
        SecurityInspectionReport report = await _inspector.InspectAsync(
            command.CorrelationId,
            command.PayloadType,
            command.RawContent,
            context.CancellationToken
        );

        // Публикуем результат в шину событий
        await _publishEndpoint.Publish(new SecurityInspectionReportPublished(
            report.AuditId,
            command.CorrelationId,
            command.ProposalId,
            report.OverallRisk,
            report.RiskScore,
            report.Findings,
            report.AuditLogSummary,
            DateTimeOffset.UtcNow
        ));
    }
}
```

---

### Результат проверки: Модель `SecurityInspectionReport`

```csharp
namespace CyberPipe.Contracts.Models;

public record SecurityInspectionReport(
    Guid AuditId,
    Guid CorrelationId,
    Guid ProposalId,
    RiskLevel OverallRisk, // Safe, Low, Medium, High, Critical
    int RiskScore,         // 0 (безопасно) до 100 (критическая угроза)
    IReadOnlyList<SecurityFinding> Findings,
    string AuditLogSummary,
    DateTimeOffset InspectedAtUtc
);

public record SecurityFinding(
    string RuleId,
    RiskLevel Level,
    string Category, // "NetworkSSRF", "MaliciousUrl", "ScriptHeuristic", "EmailThreat"
    string Description,
    string Evidence,
    string Recommendation
);
```
