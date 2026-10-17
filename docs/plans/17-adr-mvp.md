# 17. Реестр решений MVP (ADR-001 – ADR-003): ответы на вопросы L1–L6 аудита

Канонические решения, закрывающие [раздел L](muse-wonder-audit.md#l-вопросы-требующие-решения) аудита.
Статус каждого решения: **принято в проекте, подлежит проверке в Фазе 0** ([`08-implementation-roadmap.md`](08-implementation-roadmap.md)).

---

## ADR-001. Шина сообщений (L1, AUD-02, AUD-10)

* **Решение:** каноническая шина — `MassTransit`.
* **MVP:** транспорт `InMemory` по умолчанию (нулевые внешние зависимости). `SQLite WAL`
  (`data/subsystems/masstransit.db`, только для MassTransit) — опциональный персистентный режим.
  `RabbitMQ` — только enterprise-опция за отдельным ADR.
* **Семантика:** `InMemory` теряет сообщения при рестарте; `SQLite`/`RabbitMQ` дают повторы.
  Потребители обязаны быть идемпотентными (дедупликация по `MessageDeduplicationId`).
* **Упоминания `System.Threading.Channels` как межсервисной шины недействительны** (в `Channels`
  остаются только внутренние bounded-очереди, см. `14`).

## ADR-002. Раскладка БД (L2, AUD-03)

* **Решение:** мастер `data/cyberpipe_master.db` (только `AppGlobalSettings` и `SubsystemDatabaseRegistry`)
  + изолированные БД подсистем строго в `data/subsystems/*.db`
  (`hangfire`, `masstransit`, `knowledge`, `crm`, `finance`, `budget`, `health`, `netrunner`).
* **Имена `cyberpipe.db`, `data/hangfire.db`, `data/masstransit.db` вне `subsystems/` недействительны.**
* **Проверка Фазы 0:** тест создает все БД по реестру на чистой машине.

## ADR-003. Шифрование SQLite (L3, AUD-04)

* **Решение:** шифрование только через SQLCipher-провайдер (`SQLitePCLRaw.bundle_e_sqlcipher`
  или эквивалент). Стоковый `Microsoft.Data.Sqlite` без него страницы не шифрует.
* **Статус — гипотеза до PoC:** неверный ключ доказуемо ломает открытие БД, верный — открывает.
  До PoC считать, что БД на диске не защищена от чтения при прямом доступе к файлу.
* **Ключи:** уникальный 256-битный ключ на БД в `SecretsVault` (DPAPI / LibSecret / Keychain);
  ротация и аварийное восстановление — см. `09`, разд. 7 (fail-closed при утере мастер-ключа).

## L4. Целевая ОС MVP

Windows 10/11. Linux/macOS — после первой рабочей версии (K.4). Эквивалентность
`AppContainer` / `bwrap` / `Seatbelt` — гипотеза до PoC на каждой ОС (AUD-13).

## L5. DevTools вне песочницы

Запрещены. `dotnet`/`git` — только фиксированные подкоманды через `IDevToolProcessRunner`
внутри песочницы/изоляции (AUD-08). Сборка исполняет произвольный код проекта —
это unsandboxed code execution, пока запуск не уведен в песочницу.

## L6. SLO и методика измерения

Все миллисекундные бюджеты (`Tier 0 < 1 мс`, `Tier 1 5–15 мс`, Fast-Path `< 20 мс`,
`/stop < 50 мс`, CLI round-trip `p95 < 25 мс`) — **локальные**, на эталонном железе
(4+ ядер CPU, SSD NVMe, прогретый процесс), отчетность `p95/p99`. Сеть Telegram
не входит; сквозной сценарий `Telegram → роутер → песочница → ответ` — best-effort (AUD-06).
