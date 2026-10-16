# 05. SecurityAgent: Скрытная инспекция, OSINT, аудит и эвристический анализ телеметрии

**SecurityAgent** — независимый аналитический агент безопасности. Его миссия — предоставить исчерпывающую оценку рисков по любому фрагменту кода, сетевому адресу, ссылке, email-адресу или телеметрии выполненного скрипта, не раскрывая присутствие системы перед потенциальным злоумышленником.

---

### Принцип Stealth OPSEC (Скрытность расследований)

> [!CAUTION]
> **Категорический запрет прямых запросов:**
> SecurityAgent **никогда не обращается напрямую** к проверяемым URL, IP-адресам или доменам!
> * Запрещены прямые пинги (`ping`, `icmp`).
> * Запрещены прямые сетевые сканирования (`nmap`).
> * Запрещены прямые HTTP-запросы (`curl`, `HttpClient.GetAsync`) с хоста оркестратора на целевой ресурс.
> Любой прямой контакт раскрывает IP-адрес оркестратора, позволяет атакующему зафиксировать факт проверки и активировать контрмеры.

```
                      ┌─────────────────────────────────┐
                      │    SECURITY AGENT INSPECTOR     │
                      │   (Never touches target directly)│
                      └────────────────┬────────────────┘
                                       │
         ┌───────────────────┬─────────┴─────────┬───────────────────┐
         ▼                   ▼                   ▼                   ▼
┌──────────────────┐┌──────────────────┐┌──────────────────┐┌──────────────────┐
│  PASSIVE OSINT   ││ EMAIL REPUTATION ││ ISOLATED ANALYSIS││ HEURISTIC AUDIT  │
│    GATEWAY       ││     SCANNER      ││     SANDBOX      ││ OF SANDBOX DIGEST│
│- VirusTotal API  ││- Disposable check││- Ephemeral Wasm/ ││- Unique IP check │
│- URLhaus / Feodo ││- Passive MX/DNS  ││  Docker container││- SHA256 of files │
│- AbuseIPDB API   ││- Breach DBs      ││- Unpack zip/tar  ││- C2 / Exfil /    │
│- Passive DNS(DoH)││- Spoofing check  ││- De-obfuscation  ││  Ransomware rules│
└──────────────────┘└──────────────────┘└──────────────────┘└──────────────────┘
```

---

### Аналитические модули SecurityAgent

#### 1. Модуль пассивного OSINT (URL & IP Intelligence)
* Все проверки осуществляются через сторонние REST API и агрегаторы:
  * **Проверка URL/доменов:** Запросы к базам URLhaus, PhishTank, Google Safe Browsing, VirusTotal.
  * **Проверка IP:** Запросы к репутационным базам AbuseIPDB, Shodan API, GreyNoise.
  * **DNS-запросы:** Выполняются через зашифрованные сторонние рекурсоры (DNS-over-HTTPS / DoH: Cloudflare, Quad9).
* **SSRF-фильтр:** Проверка IP на принадлежность к внутренним и зарезервированным сетям:
  * `127.0.0.0/8`, `::1` (Loopback);
  * `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` (Приватные сети RFC 1918);
  * `169.254.169.254` (Облачные метаданные AWS/GCP/Azure);
  * Carrier-grade NAT (`100.64.0.0/10`).

#### 2. Модуль инспекции Email (Email Security)
* **Детект одноразовых почт (Disposable Email):** Проверка домена по реестрам временных почтовых сервисов (Mailinator, TempMail).
* **Пассивная валидация почтового домена:** Проверка наличия валидных MX-записей через DoH.
* **Анализ аутентичности домена:** Проверка записей SPF (`v=spf1`), DKIM и DMARC для исключения спуфинга.
* **Проверка утечек (Breach Check):** Запрос через k-Anonymity API (HaveIBeenPwned) на наличие адреса в скомпрометированных базах данных.

#### 3. Модуль скриптового AST-анализа
* **PowerShell AST (`System.Management.Automation`):**
  * Поиск вызовов `Invoke-Expression`, `iex`, рефлексии (`[System.Reflection.Assembly]::Load`).
  * Анализ загрузок из сети (`DownloadString`, `DownloadData`, `Net.WebClient`).
  * Детект обфускации: параметры `-enc`, строковая конкатенация опасных команд, скрытые обратные кавычки (`` ` ``).
* **Bash Heuristics:**
  * Деструктивные действия (`rm -rf`, `mkfs`, `> /dev/sda`).
  * Реверс-шеллы (`/dev/tcp/`, вызовы `nc -e`, `socat`).

#### 4. Эвристический анализ телеметрии песочниц (Решение проблемы 1 ГБ логов)
SecurityAgent **не парсит гигабайты сырого сетевого трафика (pcap) и журналов файловых операций**. Он получает очищенный и сжатый хостовым препроцессором объект `ExecutionTelemetryDigest` (~30-50 КБ JSON):
* **Анализ сетевой эвристики:** Проверяются только уникальные внешние IP и домены из `UniqueOutboundEndpoints` через пассивный OSINT. Сопоставляются аномальные порты (например, соединение с портом 4444 или 1337) и соотношение входящего/исходящего трафика (детект эксфильтрации данных).
* **Анализ файловой эвристики:** Проверка списка созданных и модифицированных файлов. Если обнаружено массовое создание файлов со случайными расширениями и высокой энтропией (признак Ransomware/вымогателя) или обращение к `/etc/shadow`, `/proc` — генерируется критический алерт.
* **Адресный запрос (On-Demand Drill-down):** Только если обнаружена специфическая аномалия, SecurityAgent запрашивает хеш конкретного созданного файла или ограниченный фрагмент лога из зашифрованного карантинного хранилища (`QuarantineStore`).

#### 5. Одноразовая песочница для динамического анализа (Ephemeral Sandbox)
Если скрипт или вложение требует распаковки архива, вычисления контрольных сумм или безопасной статической деобфускации:
* SecurityAgent запрашивает у `ISandboxProvider` **одноразовую песочницу без сетевого интерфейса** (`NetworkMode = "none"`).
* Анализ выполняется с жестким лимитом по памяти (64 МБ), CPU (0.2 core) и таймаутом (2 секунды).
* По завершении песочница немедленно уничтожается.

#### 6. Маркировка доверия к памяти (Memory Trust Tagging) и изоляция каналов
* **Происхождение данных (Data Provenance):** Каждый фрагмент контекста и памяти снабжается меткой доверия:
  ```csharp
  public enum MemoryTrustSource
  {
      SystemPrompt = 0,    // Абсолютно доверенный системный манифест
      UserExplicit = 1,    // Прямой ввод авторизованного пользователя
      ToolOutput = 2,      // Результат выполнения авторизованного инструмента
      UntrustedWeb = 3     // Текст из внешнего интернета (RAG / Scraped data)
  }
  ```
  Текст с меткой `UntrustedWeb` никогда не интерпретируется как системные инструкции (защита от непрямых промпт-инъекций / Indirect Prompt Injection).
* **Физическая изоляция очередей от пользовательских чатов:**
  * Системные события (heartbeat, health-check, отладочный аудит) передаются строго по выделенным внутренним топикам MassTransit.
  * Адаптеры мессенджеров (Telegram, Discord) слушают только топики `user.outgoing.messages`, что физически исключает утечку технического вывода или отладочных сообщений в пользовательские чаты.

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
    string Category, // "NetworkSSRF", "MaliciousUrl", "ScriptHeuristic", "EmailThreat", "SandboxTelemetryAnomaly"
    string Description,
    string Evidence,
    string Recommendation
);
```
