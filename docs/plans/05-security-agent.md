# 05. SecurityAgent: Движок инспекции и аудита

**SecurityAgent** — ключевой компонент безопасности в CyberPipe. Он действует как беспристрастный офицер безопасности и аналитик угроз.

> [!IMPORTANT]
> **Принцип работы SecurityAgent:**
> SecurityAgent **не блокирует** выполнение в императивном стиле, а формирует прозрачный, структурированный отчет безопасности (**Risk Report**) с оценкой угрозы и детальным журналом аудита. Окончательное решение принимает **CEO Agent** (или человек-оператор в случае высоких рисков).

---

### Аналитические модули SecurityAgent

```
                           ┌───────────────────────────────┐
                           │      INCOMING PAYLOAD         │
                           │   (Script / URL / IP / Text)  │
                           └──────────────┬────────────────┘
                                          │
                  ┌───────────────────────┼───────────────────────┐
                  ▼                       ▼                       ▼
       ┌───────────────────────┐ ┌──────────────────┐ ┌───────────────────────┐
       │     URL & DOMAIN      │ │   IP & NETWORK   │ │    SCRIPT & CMD       │
       │       SCANNER         │ │   REPUTATION     │ │    AST ANALYZER       │
       │ - Regex extraction    │ │ - SSRF Detection │ │ - PowerShell AST      │
       │ - URLhaus / Blocklist │ │ - Cloud Metadata │ │   (Parser.ParseInput) │
       │ - Phishing heuristic  │ │ - Private IP leak│ │ - Bash destructive ops│
       └──────────┬────────────┘ └────────┬─────────┘ └───────────┬───────────┘
                  │                       │                       │
                  └───────────────────────┼───────────────────────┘
                                          ▼
                           ┌───────────────────────────────┐
                           │   RISK EVALUATION ENGINE      │
                           │  - Aggregate Risk Score (0-100│
                           │  - Categorize: Safe to Crit   │
                           │  - Immutable Audit Event      │
                           └──────────────┬────────────────┘
                                          ▼
                           ┌───────────────────────────────┐
                           │   SecurityInspectionReport    │
                           └───────────────────────────────┘
```

#### 1. Модуль инспекции скриптов и команд (Script & AST Analyzer)
* **PowerShell AST Analysis:**
  * Использует `System.Management.Automation.Language.Parser`.
  * Детектирует опасные конструкции:
    * Неявный запуск кода: `Invoke-Expression`, `iex`, `.Invoke()`.
    * Загрузка полезной нагрузки: `DownloadString`, `DownloadFile`, `Invoke-WebRequest` на подозрительные адреса.
    * Обфускация: параметры `-enc`, `-EncodedCommand`, переменные со скрытыми символами backtick (`` ` ``).
    * Попытки повысить привилегии или отключить AMSI/Defender.
* **Bash Heuristics:**
  * Поиск деструктивных файловых команд: `rm -rf /`, `mkfs`, перезапись разделов `dd if=... of=/dev/sd*`.
  * Форк-бомбы (`:(){ :|:& };:`).
  * Реверс-шеллы: `bash -i >& /dev/tcp/...`, вызовы `nc -e`, `socat`.

#### 2. Модуль сетевой безопасности (URL & IP Scanner)
* **SSRF Prevention:** Проверка адресов назначения команд. Запрет обращений к:
  * Localhost / Loopback (`127.0.0.1`, `::1`).
  * Приватным подсетям RFC 1918 (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`).
  * Облачным метаданным (AWS/GCP/Azure link-local: `169.254.169.254`).
* **URL Reputation:** Парсинг всех ссылок в ответах агента, сопоставление с локальными кэшами черных списков (DNSBL, фишинговые фиды).

---

### Структуры данных отчета

```csharp
namespace CyberPipe.Contracts.Enums;

public enum RiskLevel
{
    Safe = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

namespace CyberPipe.Contracts.Models;

public record SecurityFinding(
    string RuleId,
    RiskLevel Level,
    string Description,
    string Evidence,
    string RemediationRecommendation
);

public record SecurityInspectionReport(
    Guid AuditId,
    Guid ProposalId,
    RiskLevel OverallRisk,
    int RiskScore, // 0 (абсолютно безопасно) - 100 (критическая угроза)
    IReadOnlyList<SecurityFinding> Findings,
    string AuditLogSummary,
    DateTimeOffset InspectedAtUtc
);
```

---

### Неизменяемый журнал аудита (Audit Trail)
Все проверки SecurityAgent в обязательном порядке фиксируются в неизменяемом аудит-логе (таблица `SecurityAuditLogs` в PostgreSQL/SQLite и структурированные события в Seq/Elasticsearch):
* Кто запросил (AgentId, TaskId).
* Что проверялось (Исходный скрипт или текст).
* Вердикт (RiskLevel, сработавшие правила).
* Решение CEO (Утверждено, отклонено, запрошен аппрув у оператора).
