
◆ Да. Главный вывод аудита: NOT READY — репозиторий пока docs-only с пустым solution, а решения по шине сообщений, раскладке баз   
  данных и шифрованию SQLite противоречат друг другу и не подтверждены прототипами.                                                
                                                                                                                                   
  Комплексный экспертный аудит плана CyberPipeline                                                                                 
                                                                                                                                   
  A. Executive Summary                                                                                                             
                                                                                                                                   
  • Зрелость: развитая концептуальная документация при нулевой реализации.                                                         
  • Главные проблемы: пустой solution, противоречия Channels/MassTransit, три несовместимые раскладки БД, неподтверждённое         
    шифрование SQLite, декларативная песочница.                                                                                    
  • Главные риски: переделка ядра после старта, ложные гарантии безопасности, нетестируемые критерии готовности спринтов.          
  • Готовность: начинать реализацию продукта нельзя. Допустимы только фиксация ADR и PoC из раздела K.1.                           
                                                                                                                                   
  B. Карта проверенных документов                                                                                                  
                                                                                                                                   
  Проверена инвентаризация всех 28 файлов репозитория.                                                                             
                                                                                                                                   
  ┌─────────────────┬────────────────────────────────────────────────────────────────────┬────────────────────────────────────────┐  │ Группа          │ Документы                                                          │ Ответственность                        │  ├─────────────────┼────────────────────────────────────────────────────────────────────┼────────────────────────────────────────┤  │ Принципы и вход │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/README.m │ Цели, принципы, карта плана            │  │                 │ d:1,                                                               │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-princ │                                        │  │                 │ iples.md:1                                                         │                                        │  │ Архитектура     │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-archi │ Компоненты, стек, БД, устойчивость     │  │                 │ tecture-diagram.md:1,                                              │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech- │                                        │  │                 │ stack-libraries.md:1,                                              │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solut │                                        │  │                 │ ion-structure.md:1,                                                │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data- │                                        │  │                 │ protection-and-state-hub.md:1,                                     │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/14-fault │                                        │  │                 │ -isolation-and-resilience.md:1                                     │                                        │  │ Безопасность    │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/05-secur │ SecurityGuard, Brainstem, IPC, CLI     │  │                 │ ity-agent.md:1,                                                    │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/10-sandb │                                        │  │                 │ ox-telemetry-and-heuristics.md:1,                                  │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-a │                                        │  │                 │ nd-execution-modes.md:1                                            │                                        │  │ Продукт и этапы │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent │ Роутер, UI, roadmap, скиллы, Netrunner │  │                 │ -mesh-proposals.md:1,                                              │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/07-ui-is │                                        │  │                 │ olation-and-arch-tests.md:1,                                       │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-imple │                                        │  │                 │ mentation-roadmap.md:1,                                            │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/12-assis │                                        │  │                 │ tant-capabilities-and-skills.md:1,                                 │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/13-model │                                        │  │                 │ -advisor-and-tier-configuration.md:1,                              │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/16-netru │                                        │  │                 │ nner-etl-and-workflow-engine.md:1                                  │                                        │  │ Служебные       │ /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/11-chang │ История решений, стандарты пакетов     │  │                 │ elog.md:1,                                                         │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/.agents/skills/netr │                                        │  │                 │ unner-script-package/SKILL.md,                                     │                                        │  │                 │ /C:/Users/Conqueror/source/repos/CyberPipeline/.agents/skills/rabb │                                        │  │                 │ itmq-windows/SKILL.md                                              │                                        │  └─────────────────┴────────────────────────────────────────────────────────────────────┴────────────────────────────────────────┘                                                                                                                                   
  Ограничение полноты: все файлы инвентаризированы, спорные места перепроверены точечным чтением и поиском по телам документов в   
  этой сессии. Полное построчное перечитывание каждого документа в финальной сверке не выполнялось, файл .gitattributes не читался,  состояние Git из-за dubious-ownership в песочнице не подтверждено. Поэтому реестр ниже — подтверждённое ядро находок, а не       
  гарантия отсутствия дефектов в остальных фрагментах.                                                                             
                                                                                                                                   
  C. Критические проблемы                                                                                                          
                                                                                                                                   
  • AUD-01, Critical, подтверждённый дефект. Репозиторий docs-only: 0 файлов .cs/.csproj, а                                        
    /C:/Users/Conqueror/source/repos/CyberPipeline/CyberPipe.slnx:1 содержит только <Solution/>. При этом                          
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md:7 задаёт спринты с критериями DoD.      
    Последствие: «готовность» этапов нельзя проверить, оценки сроков не на чем базировать.                                         
  • AUD-02, Critical, противоречие. /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/README.md:16 заявляет                
    System.Threading.Channels, а /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md:11 и         
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md:9 — MassTransit с транспортами InMemory, SQLite и   
    RabbitMQ. У вариантов разная семантика доставки, повторов и персистентности.                                                   
  • AUD-03, High, противоречие. Единой раскладки БД нет: мастер-модель cyberpipe_master.db плюс data/subsystems/*.db из            
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md:45 соседствует с cyberpipe.db в  
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md:225 и разными путями hangfire.db.         
    Миграции, бэкапы и тесты изоляции писать рано.                                                                                 
  • AUD-04, Critical, гипотеза с блокирующим риском. Шифрование через Password= заявлено в                                         
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md:63,                                       
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md:237 и                                     
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md:125, но SQLCipher-провайдер нигде    не назван, упоминаний SQLCipher в документах нет. Стоковый Microsoft.Data.Sqlite сам по себе шифрование страниц паролем не     
    даёт. Пока PoC не откроет зашифрованную БД и не провалится на неверном ключе, гарантию считать недоказанной.                   
  • AUD-05, High, подтверждённый дефект. Ключ SemanticResponseCache задан как (UserRole, ExpiresAtUtc) с TTL 24 часа в             
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md:43 и повторён в                           
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md:42. В ключе нет хэша запроса: разные    
    вопросы одной роли получат один кэшированный ответ, возможна и выдача чужих данных внутри роли.                                
  • AUD-06, High, риск и неоднозначность. Заявлены Fast-Path < 20 мс, Tier 0/1 в единицах миллисекунд и /stop < 50 мс при входе    
    через Telegram. Сетевой путь Telegram один превышает эти бюджеты, методика замера и точка отсчёта не определены. DoD с такими  
    SLO нетестируем.                                                                                                               
                                                                                                                                   
  D. Междокументные противоречия                                                                                                   
                                                                                                                                   
  ┌───────────────────────┬─────────────────────────────────┬──────────────────────────────────┬──────────────────────────────────┐  │ Проблема              │ Документ A                      │ Документ B                       │ Решение                          │  ├───────────────────────┼─────────────────────────────────┼──────────────────────────────────┼──────────────────────────────────┤  │ Шина сообщений        │ /C:/Users/Conqueror/source/repo │ /C:/Users/Conqueror/source/repos │ Один ADR, проигравший вариант    │  │                       │ s/CyberPipeline/docs/plans/READ │ /CyberPipeline/docs/plans/03-tec │ удалить везде                    │  │                       │ ME.md:16: Channels              │ h-stack-libraries.md:11:         │                                  │  │                       │                                 │ MassTransit                      │                                  │  │ Имя главной БД        │ /C:/Users/Conqueror/source/repo │ /C:/Users/Conqueror/source/repos │ Единый реестр БД, путей и        │  │                       │ s/CyberPipeline/docs/plans/09-d │ /CyberPipeline/docs/plans/03-tec │ владельцев                       │  │                       │ ata-protection-and-state-hub.md │ h-stack-libraries.md:225:        │                                  │  │                       │ :45: cyberpipe_master.db        │ cyberpipe.db                     │                                  │  │ Путь Hangfire         │ data/subsystems/hangfire.db по  │ data/hangfire.db в стеке         │ Один канонический путь в         │  │                       │ мастер-модели                   │                                  │ конфигурации                     │  │ Простота без брокеров │ Принцип Simplicity в            │ MassTransit, RabbitMQ, Hangfire, │ Либо упростить стек, либо        │  │                       │ /C:/Users/Conqueror/source/repo │ Docker, Wasmtime в               │ переписать принцип               │  │                       │ s/CyberPipeline/docs/plans/01-p │ /C:/Users/Conqueror/source/repos │                                  │  │                       │ rinciples.md:8                  │ /CyberPipeline/docs/plans/03-tec │                                  │  │                       │                                 │ h-stack-libraries.md:11          │                                  │  │ IPC-аутентификация    │ /C:/Users/Conqueror/source/repo │ Спецификация IPC в               │ Один протокол: nonce, проверка   │  │                       │ s/CyberPipeline/docs/plans/02-a │ /C:/Users/Conqueror/source/repos │ PID, авторизация команд, тесты   │  │                       │ rchitecture-diagram.md:19:      │ /CyberPipeline/docs/plans/15-cli │ подмены                          │  │                       │ PID/Nonce в Auth Guard          │ -and-execution-modes.md:91 не    │                                  │  │                       │                                 │ доводит механизм до проверяемого │                                  │  │                       │                                 │ протокола                        │                                  │  │ Статус решений        │ /C:/Users/Conqueror/source/repo │ Кода нет,                        │ Помечать записи как              │  │                       │ s/CyberPipeline/docs/plans/11-c │ /C:/Users/Conqueror/source/repos │ «спроектировано», а не           │  │                       │ hangelog.md:1 описывает         │ /CyberPipeline/CyberPipe.slnx:1  │ «реализовано»                    │  │                       │ улучшения как внесённые         │ пуст                             │                                  │
  └───────────────────────┴─────────────────────────────────┴──────────────────────────────────┴──────────────────────────────────┘                                                                                                                                   
  E. Архитектурные недостатки                                                                                                      
                                                                                                                                   
  • Нет единого источника истины по шине, путям БД и шифрованию. Это AUD-02, AUD-03, AUD-04.                                       
  • Семантика доставки не определена: при InMemory сообщения теряются при рестарте, при SQLite и RabbitMQ появляются повторы и     
    дубли. Идемпотентность потребителей не специфицирована.                                                                        
  • Кэш ответов спроектирован с некорректным ключом, AUD-05.                                                                       
  • SLO не привязаны к методике измерения, железу и точке отсчёта, AUD-06.                                                         
  • Кросс-платформенная изоляция AppContainer, bwrap и Seatbelt подаётся как эквивалентная, хотя гарантии, доступность API и модель    разрешений у платформ разные. Нужен PoC на каждой целевой ОС, а не общая абстракция на веру.                                   
  • Жизненный цикл воркеров описан фрагментарно: нет полной матрицы «запуск, отмена, таймаут, падение, перезапуск, восстановление  
    чекпоинта» для Brainstem и Netrunner в одном документе.                                                                        
                                                                                                                                   
  F. Недостающие требования и компоненты                                                                                           
                                                                                                                                   
  • Точные поддерживаемые версии ОС и статус .NET 10 на момент старта, включая план на случай сдвига релиза.                       
  • Методика измерения SLO и перечень железа, на котором цифры обязательны.                                                        
  • Политика идемпотентности, повторов и дедупликации сообщений шины.                                                              
  • Ротация и восстановление ключей БД и секретов, поведение при утере мастер-ключа.                                               
  • Тестируемые процедуры бэкапа и восстановления БД, а не только упоминание бэкапов.                                              
  • Обновление, откат и миграция данных между версиями.                                                                            
  • Модель владения: кто утверждает ADR по шине, БД и песочнице.                                                                   
                                                                                                                                   
  G. Риски безопасности                                                                                                            
                                                                                                                                   
  • Декларативная песочница Netrunner. Манифест пакета и контроль SHA-256 из                                                       
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/16-netrunner-etl-and-workflow-engine.md:62 доказывают целостность    
    файлов, но не ограничивают выполнение. Без OS-механизма скрипт работает с правами хоста.                                       
  • DevTools как выполнение кода. Разрешение запуска dotnet и git по имени исполняемого файла не является белым списком действий:  
    сборка и тесты исполняют произвольный код проекта, Git — хуки и внешние помощники. Это unsandboxed code execution, пока запуск 
    не уведён в Brainstem.                                                                                                         
  • IPC. Заявленный в диаграмме PID/Nonce guard не превращён в проверяемый протокол с тестами подмены клиента, перехвата endpoint и    повторной отправки команд.                                                                                                     
  • Шифрование БД. Гарантия держится на AUD-04. До PoC считать, что БД на диске не защищена от чтения при доступе к файлу.         
  • Fail-open не определён. Не описано поведение при недоступности SecurityGuard, сейфа секретов или песочницы: система обязана    
    останавливать выполнение, а не продолжать без защиты.                                                                          
  • Журналирование отказов безопасности и обнаружение обходов заявлены слабее, чем сами запреты. Нужны негативные тесты: чтение    
    чужой БД из воркера, запуск запрещённого инструмента, подмена IPC-клиента.                                                     
                                                                                                                                   
  H. Ошибки плана реализации                                                                                                       
                                                                                                                                   
  • Порядок спринтов в /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md:7 ставит Storage,    
    архитектурные тесты и Brainstem раньше ADR по шине, БД и шифрованию. Эти решения фундаментные, их смена ломает уже написанное. 
  • PoC песочницы и шифрования отложены, хотя от них зависят структура проектов и тесты изоляции.                                  
  • Критерии DoD опираются на неизмеримые SLO, AUD-06.                                                                             
  • Этап интеграционного тестирования не содержит негативных сценариев безопасности из раздела G.                                  
  • Netrunner и каталог скиллов запланированы шире, чем проверенное ядро: сначала нужен один сквозной сценарий Telegram → роутер → 
    песочница → ответ.                                                                                                             
                                                                                                                                   
  I. Избыточность                                                                                                                  
                                                                                                                                   
  • Три транспорта шины для локального помощника одного пользователя избыточны. Достаточно одного: внутрипроцессного или SQLite.   
    RabbitMQ оставить опцией за отдельным ADR.                                                                                     
  • Hangfire при собственной очереди задач и чекпоинтах дублирует функциональность. Оставить одну систему планирования.            
  • Дублирование описания кэшей, БД и роутера в README, принципах, стеке и changelog порождает противоречия. Один канонический     
    документ на тему, остальные — ссылки.                                                                                          
  • Каталог из десятков скиллов и моделей в                                                                                        
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/12-assistant-capabilities-and-skills.md:1 и                          
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/13-model-advisor-and-tier-configuration.md:1 до работающего ядра —   
    преждевременное расширение объёма.                                                                                             
                                                                                                                                   
  J. Реестр замечаний                                                                                                              
                                                                                                                                   
  Формат каждой записи: категория и тип; источники; обоснование и последствия; рекомендация; проверка исправления.                 
                                                                                                                                   
  • AUD-01. Critical. Реализация, подтверждённый дефект. Источники:                                                                
    /C:/Users/Conqueror/source/repos/CyberPipeline/CyberPipe.slnx:1,                                                               
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md:7. Кода нет, этапы нельзя принять.      
    Рекомендация: статус плана «pre-implementation». Проверка: сборка solution и зелёные тесты в CI.                               
  • AUD-02. Critical. Архитектура, противоречие. Источники: раздел D, строка 1. Разная семантика доставки ломает контракты.        
    Рекомендация: ADR по шине. Проверка: одно имя шины во всех документах, тест доставки и повторов.                               
  • AUD-03. High. Данные, противоречие. Источники: раздел D, строки 2–3. Миграции и бэкапы неопределимы. Рекомендация: реестр БД.  
    Проверка: тест создания всех БД по реестру на чистой машине.                                                                   
  • AUD-04. Critical. Безопасность, гипотеза. Источники:                                                                           
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md:63,                                       
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md:125. Без SQLCipher пароль не     
    шифрует страницы. Рекомендация: PoC с неверным ключом. Проверка: открытие БД неверным ключом падает, верным — работает.        
  • AUD-05. High. Требования, подтверждённый дефект. Источники:                                                                    
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/04-agent-mesh-proposals.md:43,                                       
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md:42. Ключ без хэша запроса даёт чужие    
    ответы. Рекомендация: ключ из нормализованного запроса, роли и версии данных. Проверка: два разных вопроса одной роли дают два 
    разных ответа.                                                                                                                 
  • AUD-06. High. Тестирование, риск. Источники: /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/README.md:13,           
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/README.md:19. SLO меньше сетевой задержки Telegram. Рекомендация:    
    разделить локальный и сквозной бюджеты, задать методику. Проверка: воспроизводимый бенчмарк с процентилями.                    
  • AUD-07. High. Безопасность, риск. Источники:                                                                                   
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/16-netrunner-etl-and-workflow-engine.md:62,                          
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md:41. Целостность пакета не равна изоляции. 
    Рекомендация: запуск только через Brainstem. Проверка: скрипт не читает файлы вне рабочей папки.                               
  • AUD-08. High. Безопасность, риск. Источники:                                                                                   
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/06-solution-structure.md:52,                                         
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md:20. dotnet и git исполняют код проекта и  
    хуки. Рекомендация: песочница и список разрешённых подкоманд. Проверка: вредоносная цель сборки не получает доступ к хосту.    
  • AUD-09. High. Безопасность, противоречие. Источники:                                                                           
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/02-architecture-diagram.md:19,                                       
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md:91. Guard заявлен, протокол не доказан.    Рекомендация: спецификация nonce, PID и авторизации. Проверка: подменённый клиент получает отказ.                              
  • AUD-10. Medium. Архитектура, противоречие цели и дизайна. Источники:                                                           
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/01-principles.md:8,                                                  
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/03-tech-stack-libraries.md:11. Стек тяжелее заявленной простоты.     
    Рекомендация: сократить стек раздела I. Проверка: список зависимостей MVP помещается в один ADR.                               
  • AUD-11. High. План реализации, подтверждённый дефект порядка. Источники:                                                       
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md:7. Фундаментные решения приняты после   
    старта работ. Рекомендация: порядок раздела K. Проверка: roadmap начинается с ADR и PoC.                                       
  • AUD-12. Medium. Требования, пропуск. Источники:                                                                                
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/08-implementation-roadmap.md:97. Нет версий ОС, восстановления       
    ключей, отката обновлений. Рекомендация: дополнить по разделу F. Проверка: у каждого пункта есть критерий приёмки.             
  • AUD-13. High. Реализуемость, гипотеза. Источники:                                                                              
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md:53,                                    
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/15-cli-and-execution-modes.md:78. Эквивалентность AppContainer, bwrap    и Seatbelt не доказана. Рекомендация: PoC на каждой ОС. Проверка: одинаковый набор негативных тестов проходит везде или        
    различия задокументированы.                                                                                                    
  • AUD-14. Medium. Безопасность, пропуск. Источники:                                                                              
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/09-data-protection-and-state-hub.md:36. Пароли БД хранятся в сейфе,  
    восстановление при утере ключа не описано. Рекомендация: процедура ротации и восстановления. Проверка: учебная ротация ключа   
    без потери данных.                                                                                                             
  • AUD-15. Medium. Документация, подтверждённый дефект. Источники:                                                                
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/11-changelog.md:70,                                                  
    /C:/Users/Conqueror/source/repos/CyberPipeline/docs/plans/11-changelog.md:79. Changelog описывает проектные решения языком     
    внедрённых изменений. Рекомендация: статусы «спроектировано» и «реализовано». Проверка: ни одна запись не утверждает           
    несуществующий код.                                                                                                            
                                                                                                                                   
  K. План исправлений                                                                                                              
                                                                                                                                   
  K.1. До начала реализации                                                                                                        
                                                                                                                                   
  • ADR по шине, AUD-02. Зависимости: AUD-10. Готово, когда выбран один транспорт MVP и остальные удалены из канонических          
    документов.                                                                                                                    
  • Реестр БД и путей, AUD-03. Готово, когда тест создаёт все БД по реестру.                                                       
  • PoC шифрования SQLite, AUD-04. Готово, когда неверный ключ доказуемо не открывает БД.                                          
  • PoC изоляции Brainstem минимум на Windows, AUD-07, AUD-13. Готово, когда негативные тесты раздела G падают с отказом доступа.  
  • Исправленный ключ кэша, AUD-05. Готово, когда тест двух разных запросов проходит.                                              
                                                                                                                                   
  K.2. До реализации затронутого компонента                                                                                        
                                                                                                                                   
  • Протокол IPC, AUD-09, до Brainstem.                                                                                            
  • Песочница DevTools, AUD-08, до скиллов разработчика.                                                                           
  • Методика SLO, AUD-06, до роутера.                                                                                              
  • Ротация ключей, AUD-14, до хранилища секретов.                                                                                 
                                                                                                                                   
  K.3. До интеграционного тестирования                                                                                             
                                                                                                                                   
  • Негативные тесты безопасности из раздела G.                                                                                    
  • Тест восстановления БД из бэкапа, AUD-12.                                                                                      
  • Сквозной сценарий Telegram → роутер → песочница → ответ.                                                                       
                                                                                                                                   
  K.4. После первой рабочей версии                                                                                                 
                                                                                                                                   
  • RabbitMQ как опциональный транспорт.                                                                                           
  • Расширение каталога скиллов и моделей.                                                                                         
  • Поддержка Linux и macOS, если MVP стартует на Windows.                                                                         
                                                                                                                                   
  L. Вопросы, требующие решения                                                                                                    
                                                                                                                                   
  • Какая шина канонична для MVP и нужна ли персистентность сообщений при рестарте?                                                
  • Какая раскладка и имена БД каноничны?                                                                                          
  • Какой провайдер даёт шифрование SQLite и кто хранит ключи?                                                                     
  • Какая ОС является единственной платформой MVP?                                                                                 
  • Допустим ли запуск DevTools вне песочницы хотя бы для доверенных репозиториев владельца?                                       
  • Какие SLO обязательны и на каком железе они измеряются?                                                                        
                                                                                                                                   
  M. Итоговое заключение                                                                                                           
                                                                                                                                   
  NOT READY.                                                                                                                       
  
  Обоснование: подтверждённые AUD-01, AUD-02, AUD-03 и AUD-05 блокируют старт, а AUD-04, AUD-07 и AUD-13 оставляют недоказанными   
  шифрование и изоляцию — то есть именно те гарантии, ради которых система проектируется. Реализация допустима только в объёме K.1:  ADR, реестр БД и PoC. После закрытия K.1 статус может быть пересмотрен до READY WITH CONDITIONS.