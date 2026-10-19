# 05. Безопасность Хоста (Anti-Wipe & Media Guard), Периметр Telegram и Скрытный SecurityGuard

Система безопасности **CyberPipe** решает две практические задачи без сжигания токенов:
1. **Железная защита домашнего/рабочего ПК (хоста) при управлении из Telegram:** исключить любую возможность вайпнуть систему, удалить файлы или включить порно/шок-контент на полную громкость (через взлом чата, шутку друзей или промпт-инъекцию).
2. **Скрытная инспекция кода и внешних данных (Stealth SecurityGuard):** быстрая детерминированная проверка скриптов (AST), ссылок и телеметрии песочниц для сложных задач.

---

### 1. Защита при управлении через Telegram (`Host & Media Safety Guard`)

> [!IMPORTANT]
> **Золотое правило безопасности умного помощника:**
> Ни одно сообщение из Telegram (даже от владельца или после обработки LLM) **НЕ ИМЕЕТ ПРАВА** напрямую вызывать командную строку хоста (`cmd.exe`, `powershell.exe`, `bash`) или открывать произвольные сайты в браузере со звуком.

```mermaid
flowchart TD
    Msg["Сообщение из Telegram"] --> Auth{"1. TelegramAuthGuard<br/>UserId в белом списке?"}
    Auth -->|Нет| Drop["Мгновенный Drop (< 0.1 мс)<br/>0 токенов, запись в лог"]
    Auth -->|Да (Owner / Guest (удалён решением Q18: только Owner))| Router["Каскадный Роутер (Tier 0–2)"]
    
    Router --> Guard{"2. HostAndMediaSafetyGuard<br/>(Детерминированный C# фильтр)"}
    
    Guard -->|Попытка Shell / Удаления файлов| BlockWipe["🚫 БЛОКИРОВКА: Anti-Wipe Guard"]
    Guard -->|18+ / Порно / Неизвестный URL| BlockNSFW["🚫 БЛОКИРОВКА: NSFW & Domain Filter"]
    Guard -->|Громкость > Лимита| ClampVol["🔉 СРЕЗ ГРОМКОСТИ: Volume Clamp<br/>(Макс 60% днем / 25% ночью)"]
    
    ClampVol --> SafeSkill["✅ Выполнение безопасного C# Скилла<br/>(Spotify / Яндекс.Музыка / Аудиомикшер)"]
```

#### А. Строгая привязка Telegram ID и Ролей (`TelegramAuthGuard`)
* **Жесткий Whitelist (`AllowedTelegramUsers`):** Бот реагирует исключительно на явно прописанные числовые `TelegramUserId` (проверяемые на уровне API Telegram). Любое сообщение от постороннего аккаунта или из чужой группы отбрасывается на входе за `< 0.1 мс` без вызова роутера и с расходом **0 токенов**.
* **Ролевая модель (`Owner` vs `Guest (удалён решением Q18: только Owner)`):**
  * `Owner` (Владелец): Полный доступ к скиллам помощника и подтверждению бюджетов.
  * `Guest (удалён решением Q18: только Owner) / Family` (Гость или участник семейного чата): Может только переключать разрешенные плейлисты в пределах тихой громкости (`<= 40%`), смотреть статус и ставить таймеры. Не может запускать агентные задачи Tier 3 или менять лимиты.

#### Б. Защита от вайпа системы (`Anti-Wipe & Capability Isolation`)
* **Только компилированные скиллы (`IHostSkill`):** Взаимодействие с операционной системой хоста ограничено закрытым набором C#-классов (`MediaControlSkill`, `AudioVolumeSkill`, `TimerSkill`, `SystemStatusSkill`). В них физически нет методов удаления файлов, форматирования дисков или выполнения произвольного кода.
* **Реальная изоляция ОС для `Tier 3` + AST как эшелон Defense-in-Depth (`HostSafetyGuard`):**
  * Статический черный список команд сам по себе не является границей безопасности при запуске интерпретатора. Поэтому сгенерированные агентом `Tier 3` скрипты никогда не выполняются через неизолированный `CliWrap` / `Process.Start` с правами пользователя — они запускаются исключительно внутри **`Wasmtime`**, **`Docker`** или **платформенной ОС-песочницы (`AppContainer` в Windows / `Bubblewrap` в Linux / `Seatbelt` в macOS)** с правом записи строго во временную папку `EphemeralWorkspace` и запретом модификации любых папок пользователя (`Access Denied` на уровне ядра ОС).
  * Любой запуск нативных процессов на хосте вне `Wasmtime`/`Docker` требует обязательного ручного подтверждения владельцем (`RiskLevel.RequiresApproval`, кнопка `[✅ Разрешить выполнение]` в Telegram/UI).
  * Дополнительно AST-фильтр безусловно блокирует деструктивные паттерны (`Format-Volume`, `diskpart`, `cipher /w`, `mkfs`, `dd if=`, `bcdedit`, `vssadmin delete shadows`, `Remove-Item`, `[System.IO.Directory]::Delete`, `[System.IO.File]::Delete`, `del /s /q`, `rd /s /q`, `rm -rf`, модификацию реестра и системных папок `C:\Windows`, `C:\Program Files`, `/etc`, `/boot`, `~/.ssh`).

#### В. Защита от порно, шок-контента и орущих колонок (`Media & Audio Safety Policy`)
Чтобы никто через Telegram (ни шутник, ни галлюцинирующая LLM, ни промпт-инъекция) не мог включить 18+ контент или выкрутить звук на 100%:

1. **Жесткий ограничитель громкости и проверка перед стартом воспроизведения (`Deterministic Volume Clamp`):**
   ```csharp
   public record AudioSafetyOptions
   {
       public int MaxDaytimeVolumePercent { get; init; } = 60;  // Потолок днем (08:00 - 23:00)
       public int MaxNightVolumePercent { get; init; } = 25;    // Потолок ночью (23:00 - 08:00)
       public int MaxSingleStepJumpPercent { get; init; } = 20; // Запрет резкого скачка громкости > +20%
       public bool BlockLoudSpeakerWhenLocked { get; init; } = true; // Не орать в колонки, если ПК заблокирован
   }
   ```
   * Если из Telegram приходит команда *«включи музыку на 100%»*, код принудительно срезает уровень через `Math.Clamp(requested, 0, currentMaxLimit)` до `60%` (или `25%` ночью). Поднять громкость до 100% можно только физически за компьютером или через Desktop UI.
   * **Защита от ранее выкрученной вручную громкости (`EnsureSafeVolumeBeforePlayback`):** Перед любым вызовом `Play` / `Resume` из Telegram `MediaControlSkill` обязательно проверяет текущий системный уровень `MasterVolume` через `IAudioDeviceController`. Если пользователь ранее вручную оставил на ПК громкость `90%`, перед стартом воспроизведения она автоматически снижается до безопасного потолка `currentMaxLimit` (`60%` днем / `25%` ночью).
2. **Белый список медиа-источников (`Trusted Media Providers Only`):**
   * Команда *«включи...»* работает **только** через официальные API/URI-схемы музыкальных сервисов (`Spotify`, `Яндекс.Музыка`, `YouTube Music` без видео-всплытия) или локальную папку с проверенной фонотекой (`SafeLibrary`).
   * **Запрет открытия произвольных URL и видео в браузере:** Помощник никогда не открывает произвольные `http/https` ссылки или видеоролики из чата в браузере хоста.
3. **Многослойная защита от 18+ / NSFW / Adult-контента (`NsfwContentFilter` + Provider Policy):**
   * Поскольку текстовый фильтр может обходиться иноязычными названиями, главная гарантия безопасности обеспечивается сочетанием: (1) принудительного `Volume Clamp` на уровне системной аудиосессии (`IAudioDeviceController`), (2) полного запрета открытия браузера/видео и (3) принудительного флага семейного режима на стороне API музыкального провайдера (`ExplicitContentAllowed = false`) + DNS-резолвинга через семейный фильтр (`1.1.1.3` Cloudflare Family).
   * Дополнительно перед передачей поискового запроса в плеер строка проходит Unicode-нормализацию (`NormalizationForm.FormKC`), очистку от невидимых символов (`\u200B`), де-транслитерацию / замену гомоглифов и проверку за `< 0.5 мс` по локальному словарю стоп-слов и корней 18+.

---

### 2. Принцип Stealth OPSEC для анализа внешних угроз

> [!CAUTION]
> **Категорический запрет прямых запросов к неизвестным хостам:**
> При проверке внешних ссылок или IP-адресов в задачах `Tier 3` **SecurityGuard никогда не обращается к цели напрямую** (`ping`, `nmap`, `curl` с хоста запрещены), чтобы не раскрывать IP-адрес домашнего/рабочего ПК.

```
                      ┌─────────────────────────────────┐
                      │   SECURITY GUARD (0-Token Code) │
                      │   (Детерминированный анализатор)│
                      └────────────────┬────────────────┘
                                       │
         ┌───────────────────┬─────────┴─────────┬───────────────────┐
         ▼                   ▼                   ▼                   ▼
┌──────────────────┐┌──────────────────┐┌──────────────────┐┌──────────────────┐
│  HOST & MEDIA    ││  PASSIVE OSINT   ││  AST & SCRIPT    ││ HEURISTIC AUDIT  │
│  SAFETY GUARD    ││  (FAIL-CLOSED)   ││   INSPECTOR      ││ OF SANDBOX DIGEST│
│- Volume Clamp    ││- VirusTotal API  ││- PowerShell AST  ││- Unique IP check │
│- NSFW/18+ Filter ││- URLhaus / Abuse ││- Bash Anti-Wipe  ││- SHA256 of files │
│- Anti-Wipe Rules ││- Passive DNS(DoH)││- OS Sandbox Req  ││- Exfil / C2 rules│
└──────────────────┘└──────────────────┘└──────────────────┘└──────────────────┘
```

---

### 3. Модули быстрой проверки (0 токенов)

#### 1. Скриптовый AST-анализ (`System.Management.Automation` & Bash Rules)
Выполняется локальным C#-кодом за `1–3 мс` без участия LLM как фильтр Defense-in-Depth перед подачей в изолированную песочницу:
* **PowerShell / .NET AST:** Блокировка `Invoke-Expression` (`iex`), динамической компиляции и рефлексии (`Assembly::Load`), прямых вызовов `[System.IO.File]::Delete` / `[System.IO.Directory]::Delete` вне рабочей папки, скрытых загрузок (`DownloadString`, `Net.WebClient`), COM-объектов и обфусцированных параметров `-enc`.
* **Bash / CLI Heuristics:** Блокировка деструктивных команд (`rm -rf`, `mkfs`, `> /dev/sda`), реверс-шеллов (`/dev/tcp/`, `nc -e`, `socat`) и попыток чтения секретов.

#### 2. Модуль пассивного OSINT, SSRF-фильтр и политика `Fail-Closed`
* **SSRF-фильтр:** Мгновенная блокировка обращения скриптов к локальной сети (`127.0.0.0/8`, `192.168.0.0/16`, `10.0.0.0/8`, `169.254.169.254`, IPv6 Loopback/Link-Local).
* **Пассивная проверка URL/IP/Email:** Кэшируемые запросы к репутационным базам (URLhaus, VirusTotal, AbuseIPDB, DoH Cloudflare/Quad9, проверка одноразовых доменов почты и SPF/DMARC).
* **Политика отказа `Fail-Closed`:** Если внешние OSINT-провайдеры недоступны по таймауту, ошибке сети или исчерпанию квоты API, неизвестные внешние домены/IP в задачах `Tier 3` автоматически получают статус **`RiskLevel.RequiresApproval`** (запрос явного подтверждения у владельца в Telegram/UI), а не `Safe`.

#### 3. Защита от непрямых и отложенных промпт-инъекций (`Memory Trust Tagging` & `Hard Tool Stripping`)
Каждый фрагмент текста в оперативной памяти и **каждая запись в таблицах `KnowledgeVault`, `PersonalCrm` и кэшах PostgreSQL** имеет обязательный столбец источника доверия:
```csharp
public enum MemoryTrustSource
{
    SystemPrompt = 0,     // Доверенный системный манифест
    OwnerExplicit = 1,    // Прямой ввод владельца (подтвержденный Telegram ID / UI)
    Guest (удалён решением Q18: только Owner)Explicit = 2,    // Ввод гостя (ограниченные права, запрет Tier 3)
    ToolOutput = 3,       // Вывод локального доверенного скилла
    UntrustedExternal = 4 // Текст из интернета, пересланных чужих сообщений, RSS, PDF/DOCX или писем
}
```
Чтобы исключить как прямую, так и отложенную инъекцию через «Второй мозг» (`Stored Prompt Injection` при сохранении внешних ссылок или новостей в `KnowledgeVault`), действуют три жестких правила распространения недоверия (`Taint Propagation`):
1. **Фильтрация RAG по `TrustSource`:** При выполнении команд управления ПК автоматический RAG-поиск в `KnowledgeVault` отбирает только записи с `TrustSource <= ToolOutput`. Записи, сохраненные из пересланных чужих сообщений или внешних RSS (`UntrustedExternal`), не подмешиваются в контекст команд управления хостом.
2. **Физическое вырезание схем тулов (`Hard Tool Stripping` в `TaintedContext`):** Если в контекст запроса `Tier 2 / Tier 3` попал хотя бы один фрагмент с меткой `UntrustedExternal` (например, владелец попросил *«Проверь этот PDF-договор и создай задачи»*), сессия переводится в режим **`TaintedContext`**. Перед вызовом `IChatClient` из коллекции `ChatOptions.Tools` **физически удаляются** все схемы хостовых скиллов управления ПК (`Media`, `Volume`, `Workspace`, `Shell`).
3. **Подтверждение действий человеком (`Action Confirmation`):** Из контекста `TaintedContext` модель может вызывать только Read-Only инструменты анализа либо вернуть структурированное предложение (например, список задач или правок) с инлайн-кнопкой в Telegram/UI (`[➕ Сохранить задачи в трекер]`), где само действие выполняется детерминированным кодом только после явного нажатия кнопки владельцем.

---

### 4. Модель вердикта безопасности (`SecurityInspectionReport`)

```csharp
namespace CyberPipe.Contracts.Models;

public record SecurityInspectionReport(
    Guid AuditId,
    Guid CorrelationId,
    RiskLevel OverallRisk, // Safe, Clamped (скорректировано лимитом), Blocked (отклонено), RequiresApproval
    int RiskScore,         // 0..100
    IReadOnlyList<SecurityFinding> Findings,
    DateTimeOffset InspectedAtUtc
);

public record SecurityFinding(
    string RuleId,         // Напр.: "AUDIO_VOL_CLAMPED", "NSFW_QUERY_BLOCKED", "ANTI_WIPE_TRIGGERED"
    RiskLevel Level,
    string Category,       // "MediaSafety", "HostAntiWipe", "NetworkSSRF", "ScriptAst", "PromptInjection"
    string Description,
    string ActionTaken     // "Срезано до 60%", "Запрос заблокирован", "Требуется подтверждение в UI"
);
```


