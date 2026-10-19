# TASK-012 — Хранилище эмбеддингов на pgvector

## Цель

Дать Tier 1 и семантическому кэшу быстрое векторное хранилище в той же PostgreSQL: сохранение эмбеддингов с метаданными и поиск ближайших соседей с настраиваемым порогом близости.

## Контекст

- pgvector включён и база создана в TASK-010; таблица эмбеддингов и базовая схема — TASK-011.
- Стандартная мультиязычная модель эмбеддингов `multilingual-e5-small` предлагается при первом запуске для Tier 1; модель не встроена в приложение, размерность берётся из конфигурации модели, а не зашивается в код.
- Потребители: семантический роутер TASK-031, семантический кэш TASK-034, поиск по знаниям TASK-053, эвристическая оценка риска TASK-071. Здесь — только хранилище, без классификации и эвристик.
- Лимиты и пороги конфигурируемы (TASK-003); SLO по задержке поиска — цели, а не ворота приёмки.

## Объём работ

- `IEmbeddingStore` / `PgVectorEmbeddingStore` (к созданию в `src/CyberPipe.Data`, к созданию): `UpsertAsync`, `SearchAsync`, `DeleteAsync`, `DeleteByOwnerScopeAsync` поверх таблицы из TASK-011.
- Модель записи: `EmbeddingRecord { Id, Namespace, SourceRef, ModelId, Dimension, Vector, TextHash, Metadata(jsonb), CreatedAt }`; пространства имён (`router`, `semantic-cache`, `knowledge`, `safety`) изолируют потребителей.
- Поиск: косинусная близость (оператор `<=>`), параметры `TopK`, `MinScore`, `Namespace`, опциональный фильтр по `ModelId`; результат — `EmbeddingMatch { Id, SourceRef, Score, Metadata }`, отсортированный по убыванию score.
- ANN-индекс (HNSW или IVFFlat — выбор зафиксировать в коде/миграции TASK-011 дополнением через новую миграцию при необходимости) и точный поиск при малых объёмах; переключение стратегии — конфигурацией, без смены контракта.
- Валидация: размерность входящего вектора сверяется с `ModelId`/конфигурацией; несовпадение — явная ошибка, без молчаливой обрезки/паддинга. Пакетный `Upsert` в одной транзакции.
- Нормализация векторов — ответственность вызывающего (модели); хранилище хранит как есть и документирует ожидание нормализации для косинусной метрики.

## Вне рамок

- Генерация эмбеддингов и ONNX-инференс — TASK-031; установка моделей — TASK-081/TASK-063.
- Семантический кэш как политика (TTL, инвалидация, пороги попадания) — TASK-034; здесь только поиск/запись, используемые кэшем.
- Классификация намерений, оценка риска контента/аудио — TASK-031/TASK-071.
- Смена модели эмбеддингов и переиндексация корпуса — отдельная процедура вне задачи (здесь только поле `ModelId` и фильтр по нему).

## Зависимости

- TASK-002
- TASK-011

## Контракты и интерфейсы

- `IEmbeddingStore` (контракт в терминах TASK-002, реализация — `src/CyberPipe.Data`, к созданию):
  - `UpsertAsync(EmbeddingRecord, CancellationToken)`, `UpsertManyAsync(IReadOnlyList<EmbeddingRecord>, CancellationToken)` — идемпотентно по `(Namespace, SourceRef, ModelId)`.
  - `SearchAsync(EmbeddingQuery { Vector, Namespace, TopK, MinScore, ModelId? }, CancellationToken) -> IReadOnlyList<EmbeddingMatch>`.
  - `DeleteAsync(Namespace, SourceRef, CancellationToken) -> bool`, `CountAsync(Namespace, CancellationToken) -> long`.
- `EmbeddingDimensionMismatchException(expected, actual, modelId)` при несовпадении размерности; пустой вектор и `TopK <= 0` — `ArgumentException`.
- Score в контракте — косинусное сходство в диапазоне `[-1, 1]` (не расстояние pgvector); преобразование расстояния в score инкапсулировано в реализации.

## Результаты

- Записанные эмбеддинги находятся поиском с ожидаемым порядком близости на контрольном наборе.
- Изоляция пространств имён и фильтра по модели подтверждена тестами.
- Идемпотентный повторный `Upsert` не создаёт дублей.

## Критерии приёмки

- [ ] На контрольном наборе (как минимум 5 векторов с известными попарными близостями) `SearchAsync` возвращает соседей в правильном порядке и с score в `[-1, 1]`.
- [ ] Запрос с `MinScore` выше всех близостей возвращает пустой список, а не ближайший «хоть какой-нибудь» результат.
- [ ] Вектор неверной размерности отклоняется `EmbeddingDimensionMismatchException`; пустой вектор и `TopK=0` — `ArgumentException`.
- [ ] Повторный `Upsert` той же тройки `(Namespace, SourceRef, ModelId)` обновляет запись; `CountAsync` не растёт.
- [ ] Поиск в одном `Namespace` не возвращает записи другого; фильтр по `ModelId` исключает чужие модели.

## Тестирование

- Уровень: integration (основной) + unit для валидации аргументов и преобразования расстояние→score.
- Integration: Testcontainers PostgreSQL + pgvector, схема из TASK-011; детерминированные синтетические векторы (без реальной ONNX-модели), проверка порядка, порога, изоляции namespace/model, пакетной записи.
- Unit: фейковый `IEmbeddingStore` не используется для проверки SQL; unit покрывает только валидацию входных данных до обращения к БД.
- Как: категория `Integration`; без Docker/локальной БД тесты пропускаются с явным сообщением.

## Команды проверки

```powershell
dotnet build CyberPipe.slnx
dotnet test tests/CyberPipe.Data.Tests (к созданию) --filter "Category!=Integration"
dotnet test tests/CyberPipe.Data.Tests (к созданию) --filter "Category=Integration"
```

## Статус: Planned
