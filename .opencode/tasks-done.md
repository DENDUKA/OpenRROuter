# Выполненные задачи

Архив завершённых задач. Файл только на дозапись; ручное редактирование не требуется.

---

## T-017 — Устранить горизонтальный скролл документации при viewport 320 px
- status: done
- category: responsive-ui
- priority: low
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:24:21.5609224+04:00
- completed: 2026-08-30T21:30:26.7820857+04:00
- desc: Страница документации с обычным вертикальным скроллбаром Windows/Chromium. body min-width=320px и width=320px, document scrollWidth=320 при clientWidth=305. Внизу виден горизонтальный скролл; отступы текста x=24 слева и около 9 px справа вместо симметричных 24. Требуется: Страница помещается в доступные 305 px, внешние поля согласованы; горизонтально прокручиваются только широкие code/table блоки. Сброшен body { min-width: 0 } в custom.css темы VitePress.

### Доказательства и причина

- URL: `https://rawrter.com/docs/`, `https://rawrter.com/docs/en/`.
- Локали: RU/EN, все 28 проверенных страниц; viewport: 320×900; clientWidth=305.
- Причина: Наследуемый VitePress base.css задаёт body min-width:320px без учёта классического вертикального скроллбара. В проектной теме переопределения нет.
- Файл/строка: `docs-site/node_modules/vitepress/dist/client/theme-default/styles/base.css:34 (источник зависимости); docs-site/.vitepress/theme/custom.css:1 (место проектного переопределения)`.
- Скриншоты: [docs-ru-320.png](../docs/audits/spacing-2026-08-30/screenshots/docs-ru-320.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Отдельная VitePress-тема, не относится к app.css и T-002–T-008. Дефект условный: не заявлять воспроизведение на всех физических телефонах.

### Воспроизведение

1. Открыть документацию в браузере с не-overlay scrollbar при 320×900.
2. Прокрутить страницу и проверить общий горизонтальный scrollbar и поля.
3. Повторить EN и другие документы; отдельно проверить окружение с overlay-scrollbar.

### Критерии приёмки

- При innerWidth 320/clientWidth 305 нет общего горизонтального скролла.
- Локальная прокрутка кода сохранена, меню/поиск работают.
- Не редактировать node_modules; применять ограниченное правило темы и проверить docs:build.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.

## T-016 — Уместить десктопный логотип в высоту шапки
- status: done
- category: design-system
- priority: low
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:23:38.8560996+04:00
- completed: 2026-08-30T21:41:38.7602848+04:00
- desc: Общая шапка WebApp на ПК. На 1920: topbar height=56 px, logo height=67 px, y=-5,9 px. Логотип выступает выше viewport и ниже шапки; верх рисунка обрезан. Требуется: Логотип целиком внутри 56 px шапки с симметричными вертикальными полями. Рекомендация — 40 px высоты и около 8 px полей, согласованная с существующим вариантом <=768. Исправлена высота базового `.topbar-logo` до 40px в app.css и app.min.css; добавлены регрессионные тесты TopbarLogoResponsiveTests.

### Доказательства и причина

- URL: `https://rawrter.com/ru/`, `https://rawrter.com/en/`.
- Локали: RU/EN; viewport: 1024×900,1366×768,1440×900,1536×864,1920×1080.
- Причина: Базовая .topbar-logo height:67px превышает --topbar-height:56px. T-003 задаёт 40/32 px только на мобильных брейкпоинтах.
- Файл/строка: `src/OpenRussiaRouter.WebApp/wwwroot/app.css:50,745,1237,1258`.
- Скриншоты: [home-ru-1920.png](../docs/audits/spacing-2026-08-30/screenshots/home-ru-1920.png), [home-proof-ru-1024.png](../docs/audits/spacing-2026-08-30/screenshots/home-proof-ru-1024.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Десктопный случай вне первоначального мобильного исправления T-003; не переоткрывать архив.

### Воспроизведение

1. Открыть любую страницу WebApp на 1920×1080.
2. Сравнить верх/низ логотипа с границами шапки.
3. Повторить ноутбуки и переход 768 px.

### Критерии приёмки

- На 1024–1920 px логотип не обрезан и имеет симметричные поля.
- Пропорции изображения сохранены; мобильные 40/32 px и вместимость меню из T-003 не сломаны.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.

## T-009 — Адаптировать сетки главной к доступной ширине контента
- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:23:37.1379634+04:00
- completed: 2026-08-30T21:37:37.6640081+04:00
- desc: Авторизованная главная; hero и четыре карточки преимуществ. Hero: колонка заголовка 344,8 px, scrollWidth текста 416 px; соседняя панель начинается x=656,8, текст доходит примерно до x=688 (перекрытие ≈31 px). Карточки: ширина 165,7 px, padding 20 px, контент 124,1 px; RU «Безопасность» требует ≈140 px, оставляя справа около 4 px вместо 20. Настроены сетки в Index.cshtml: добавлен брейкпоинт 1100px (hero 1fr, proof-grid 2 колонки), скорректирован clamp заголовка h1, minmax для quickstart (280px), min-width:0 и word-break для proof-item/h2; добавлены регрессионные тесты HomeResponsiveTests.

### Доказательства и причина

- URL: `https://rawrter.com/ru/`, `https://rawrter.com/en/`.
- Локали: RU (hero и карточки); EN (карточки); viewport: 1024×900; дополнительно переход 900 px и вся базовая матрица.
- Причина: Двухколоночный hero с минимумом 320 px для quickstart и четырёхколоночная proof-grid сохраняются до viewport 900 px, хотя фиксированная боковая панель забирает 240 px.
- Файл/строка: `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml:281,313,673,752`.
- Скриншоты: [home-ru-1024.png](../docs/audits/spacing-2026-08-30/screenshots/home-ru-1024.png), [home-proof-ru-1024.png](../docs/audits/spacing-2026-08-30/screenshots/home-proof-ru-1024.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Независима от T-002–T-008. Согласовать с T-010 только общий контейнер главной; тексты и стиль не переписывать.

### Воспроизведение

1. Открыть RU главную с авторизованным пользователем при 1024×900.
2. Проверить слово «совместимый» в hero, затем прокрутить к преимуществам; повторить EN.
3. Сравнить с 900/902 и 1366 px, не скрывая переполнение глобально.

### Критерии приёмки

- На 1024 px заголовок целиком читается и не пересекается с quickstart.
- Во всех карточках текст сохраняет внутренний отступ; нет посимвольного разрушения заголовков.
- Повторить RU/EN 320,375,768,1024,1366,1440,1536,1920 и границы изменённого CSS.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.

## T-013 — Переносить внутренний ряд действий Public Models на мобильных
- status: done
- category: responsive-ui
- priority: high
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:23:54.9934669+04:00
- completed: 2026-08-30T21:38:41.5998280+04:00
- desc: Public Models; список, action=add и action=edit. При 375 px ряд действий начинается x≈32,8, Add Model заканчивается x≈521,4 при clientWidth=360. scrollWidth документа=522: +162 px (на 320: +217). В .adm-btn-row добавлен flex-wrap: wrap в app.css и app.min.css; обновлены регрессионные тесты AdminCardHeaderResponsiveTests.

### Доказательства и причина

- URL: `https://rawrter.com/ru/admin/models`, `https://rawrter.com/en/admin/models`.
- Локали: RU/EN; viewport: 320×900 и 375×900.
- Причина: T-008 исправила внешний .adm-card-header--split, но дочерняя .adm-btn-row остаётся nowrap; несжимаемые кнопки и вложенная form превышают ширину.
- Файл/строка: `src/OpenRussiaRouter.WebApp/wwwroot/app.css:1089,1098; src/OpenRussiaRouter.WebApp/Pages/Admin/Models.cshtml:384`.
- Скриншоты: [admin-models-ru-375.png](../docs/audits/spacing-2026-08-30/screenshots/admin-models-ru-375.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Продолжение проверки после T-008 и T-004; выполненные задачи не переоткрывать. Исправлять внутренний ряд, не возвращать старый отступ.

### Воспроизведение

1. Открыть Models при 375×900, затем 320×900.
2. Проверить доступность Enrich, Sync, Price Comparison и Add Model, ничего не запускать.
3. Повторить GET формы добавления/редактирования и EN.

### Критерии приёмки

- Все кнопки помещаются/переносятся внутри панели, без внешнего скролла или обрезки.
- Сохранены margin-top:0 из T-008, gap 12 и поля 16.
- Таблица остаётся в .table-scroll; 768–1920 px без регрессии.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.

## T-011 — Сохранить боковые поля RU-подвала на ширине 320 px
- status: done
- category: responsive-ui
- priority: low
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:23:48.2419790+04:00
- completed: 2026-08-30T21:40:29.2852405+04:00
- desc: Низ страницы после закрытия cookie-баннера; общий WebApp footer. Footer padding 16px 24px; content width 256,8 px, gap 24 px, flex-wrap:nowrap. Первая ссылка x=-2,94, последняя заканчивается x=307,74: боковые поля исчезают, текст обрезается с двух сторон. В .app-footer-content добавлен flex-wrap: wrap и gap: var(--spacing-md) var(--spacing-xl) в app.css и app.min.css; добавлены регрессионные тесты FooterResponsiveTests.

### Доказательства и причина

- URL: `https://rawrter.com/ru/legal/privacy`.
- Локали: RU; EN контроль; viewport: 320×900, clientWidth=305.
- Причина: Сумма минимальных ширин трёх русских подписей и двух gap не помещается в контейнер; центрированный flex не разрешает перенос.
- Файл/строка: `src/OpenRussiaRouter.WebApp/wwwroot/app.css:1297,1303`.
- Скриншоты: [footer-ru-320.png](../docs/audits/spacing-2026-08-30/screenshots/footer-ru-320.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Общий footer; не дублирует T-002. Может устраняться независимо.

### Воспроизведение

1. Открыть любую RU-страницу WebApp на 320×900.
2. Закрыть cookie-баннер и прокрутить до футера.
3. Проверить начала/концы ссылок; сравнить EN и ширину 375.

### Критерии приёмки

- При 320 px все три ссылки целиком видны и доступны, scrollWidth footer не выходит за clientWidth.
- Сохранены симметричные боковые поля и понятный межстрочный интервал.
- RU/EN и остальные 7 размеров без регрессии.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.

## T-015 — Добавить интервал между промо-блоком и CTA на главной
- status: done
- category: design-system
- priority: low
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:23:55.5857207+04:00
- completed: 2026-08-30T21:46:02.4542255+04:00
- desc: Отображается предложение стартового баланса. В Index.cshtml добавлены стили для .promo-badges (flex, flex-wrap, gap: var(--spacing-sm), margin-bottom: var(--spacing-md)) и .promo-badge (pill-бейдж с padding и зеленым акцентом), обеспечивающие 12px отступ до .cta-row и гармоничный перенос нескольких бейджей. Добавлены регрессионные проверки в HomeResponsiveTests.

### Доказательства и причина

- URL: `https://rawrter.com/ru/`, `https://rawrter.com/en/`.
- Локали: RU/EN; viewport: 1920×1080 и 1024×900; также проверить мобильные.
- Причина: В разметке есть .promo-badges/.promo-badge, но отсутствуют правила этих классов; обе группы получают нулевые margin.
- Файл/строка: `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml:29,332; src/OpenRussiaRouter.WebApp/wwwroot/app.css:31`.
- Скриншоты: [home-ru-1920.png](../docs/audits/spacing-2026-08-30/screenshots/home-ru-1920.png), [home-ru-1024.png](../docs/audits/spacing-2026-08-30/screenshots/home-ru-1024.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Не требует изменения бизнес-флагов. Может выполняться вместе с T-009 в тех же стилях, но причина самостоятельная.

### Воспроизведение

1. Открыть главную с текущим включённым стартовым балансом.
2. Сравнить низ промо-текста с верхом кнопки регистрации на 1920 и 1024.
3. Условие двух бейджей проверить на локальной фикстуре при последующем исправлении, не переключать прод-флаги.

### Критерии приёмки

- При одном/двух бейджах и их отсутствии вертикальный ритм не скачет, нет склеивания с кнопками.
- Используется существующий токен; сохранены стиль и текущие действия.
- RU/EN, мобильные и 1920 px без наложений.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.

## T-018 — Ограничить раскрытые фильтры Requests границами viewport
- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:24:36.7289217+04:00
- completed: 2026-08-30T21:46:48.3476501+04:00
- desc: Provider/Node при 320 px; API Key при 375 px, фокус в поле поиска списка. Добавлено динамическое выравнивание панели внутри viewport (repositionPanel) и preventScroll при фокусе поиска в Requests.cshtml, ограничение max-width: calc(100vw - 16px) и max-height: min(260px, 45vh) в app.css / app.min.css.

### Доказательства и причина

- URL: `https://rawrter.com/ru/admin/requests`, `https://rawrter.com/en/admin/requests`.
- Локали: RU/EN; viewport: 320×900,375×900 и те же ширины при высоте 568 px.
- Причина: Абсолютный .ms-panel привязан left:0 к кнопке и имеет min-width:240px; нет позиционирования с учётом правой/нижней границы. Фокус search вызывает горизонтальный автоскролл к выходящему за экран элементу.
- Файл/строка: `src/OpenRussiaRouter.WebApp/wwwroot/app.css:1384,1400; src/OpenRussiaRouter.WebApp/Pages/Admin/Requests.cshtml:227`.
- Скриншоты: [requests-provider-ru-320.png](../docs/audits/spacing-2026-08-30/screenshots/requests-provider-ru-320.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Новая геометрическая проблема; T-007 повторно проверена и работает, её не переоткрывать. Учесть существующий перенос фильтров T-004.

### Воспроизведение

1. Открыть Requests на 320×568, раскрыть Provider или Node.
2. Наблюдать сдвиг всей шапки/страницы и обрезанный правый край панели.
3. Повторить API Key на 375×568, все 5 фильтров в RU/EN и нажать Escape без применения фильтра.

### Критерии приёмки

- Все 5 списков помещаются в viewport на базовых ширинах и высоте 568, scrollX страницы остаётся 0.
- Длинный список прокручивается внутри, поиск и элементы доступны.
- Сохранены закрытие по Escape, aria-expanded=false и возврат фокуса из T-007.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.


## T-010 — Совместить края и затухание карусели с видимой областью контента
- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:23:41.0969201+04:00
- completed: 2026-08-30T21:46:19.9345725+04:00
- desc: Флагманская карусель, авторизованная главная с боковой панелью. 1920: carousel x=112,4, width=1920, right=2032,4 при clientWidth=1905; область контента начинается после sidebar x=240. Левое затухание находится под sidebar, правое — за экраном. scrollWidth документа 2065 (+160). На 1024 scrollWidth 1137 при clientWidth 1009 (+128). Требуется: Мягкое затухание по обоим видимым краям ленты, симметричное позиционирование относительно доступной области; декоративная подсветка не расширяет документ. Намеренная внутренняя прокрутка/циклические копии сохраняются. Выровнены margin-inline/padding-inline карусели по области контента, добавлен overflow-x:clip на flagship-showcase, декоративный ::before ограничен right:0.

### Доказательства и причина

- URL: `https://rawrter.com/ru/`, `https://rawrter.com/en/`.
- Локали: RU/EN; viewport: 1920×1080, 1024×900; воспроизводится геометрически на всех 8 размерах.
- Причина: margin-inline:calc(50% - 50vw) рассчитывает полноэкранную ленту относительно контейнера, уже смещённого sidebar. ::before с right:-12% дополнительно расширяет геометрию секции. Это не замечание к самим обрезанным крайним карточкам: они предусмотрены дизайном.
- Файл/строка: `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml:422,461; docs/superpowers/specs/2026-08-30-flagship-carousel-design.md:17`.
- Скриншоты: [home-ru-1920.png](../docs/audits/spacing-2026-08-30/screenshots/home-ru-1920.png), [home-proof-ru-1024.png](../docs/audits/spacing-2026-08-30/screenshots/home-proof-ru-1024.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Учесть контракт бесконечной карусели; T-009 может менять ширину родителя. Не удалять intentional overflow ленты.

### Воспроизведение

1. Открыть главную при 1920×1080 с боковой панелью.
2. Сравнить видимые границы ленты и положение mask с границами sidebar и правым краем.
3. Измерить carousel и documentElement; повторить 1024,768,375,320.

### Критерии приёмки

- Маска видна у обоих фактически доступных краёв; нет лишнего внешнего scrollWidth от секции.
- Сохранены autoplay, drag, внутренняя прокрутка, буферные копии и reduced-motion контракт.
- RU/EN и все базовые размеры не получают новых наложений.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.

## T-012 — Не обрезать длинный заголовок политики конфиденциальности
- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T21:08:25+04:00
- started: 2026-08-30T21:24:18.0485346+04:00
- completed: 2026-08-30T21:47:17.7544690+04:00
- desc: Верх юридической страницы. h1 x=16, width=272,8, scrollWidth=313; текст доходит примерно до x=329 при clientWidth=305. Слово «конфиденциальности» обрезано; ожидаемое правое поле 16 px потеряно. В .legal-document, .legal-document h1, .legal-document h2, .legal-document a добавлены правила переполнения overflow-wrap: break-word; добавлен адаптивный размер h1 (28px) на @media (max-width: 480px) в app.css и app.min.css; добавлены регрессионные тесты LegalResponsiveTests.

### Доказательства и причина

- URL: `https://rawrter.com/ru/legal/privacy`, `https://rawrter.com/en/legal/privacy`.
- Локали: RU/EN (в обеих версиях отрендерен русский текст документа); viewport: 320×900.
- Причина: Общий размер h1 и отсутствие безопасного переноса длинного слова в .legal-document; body overflow-x:hidden маскирует проблему вместо решения.
- Файл/строка: `src/OpenRussiaRouter.WebApp/wwwroot/app.css:86,1322,1328; docs/mandatory/privacy-policy.md:1`.
- Скриншоты: [privacy-ru-320.png](../docs/audits/spacing-2026-08-30/screenshots/privacy-ru-320.png).
- Отчёт/матрица: [аудит 2026-08-30](../docs/audits/spacing-2026-08-30/README.md).
- Зависимости: Независима; общий дефект footer учитывать отдельно как T-011.

### Воспроизведение

1. Открыть /ru/legal/privacy на 320×900.
2. Проверить правый край первой строки длинного слова.
3. Повторить /en/legal/privacy и 375 px.

### Критерии приёмки

- Нет обрезания заголовка/длинных ссылок на 320 px; контейнер и текст не выходят в правое поле.
- Не менять юридический текст ради вёрстки.
- Сохранить текущую типографическую иерархию на ноутбуках и 1920 px.
- При реализации добавить регрессионную проверку и выполнить обязательную сборку. В рамках текущего аудита код не исправлялся.

## W1-005 — Офлайн-конверсии: серверная отправка topup_completed и first_api_request
- status: done
- category: backend
- priority: high
- created: 2026-09-01T00:00:00Z
- started: 2026-09-02T10:00:00Z
- completed: 2026-09-02T11:20:00Z
- desc: Серверная отправка в Метрику 111582266 двух событий с ClientID пользователя: topup_completed и first_api_request. Связка user_id -> client_id пишется WebApp в analytics.yandex_metrika_identities с 1ч кэшем и fail-open. Outbox billing.yandex_metrika_offline_conversion_outbox наполняется атомарно с подтверждением оплаты и первой записью usage. Фоновый worker арендует события, разрешает ClientID, формирует CSV и загружает в Management API с OAuth-токеном и дедупликацией. Проверено E2E-тестами и боевой валидацией на сервере 201.51.6.246.

## T-001 — Включить авторизацию через Google
- status: done
- category: auth
- priority: medium
- created: 2026-08-15T00:00:00Z
- started: 2026-08-15T10:00:00Z
- completed: 2026-09-02T11:30:00Z
- desc: Авторизация через Google настроена и работает на проде. В Google Cloud Console создан OAuth-клиент, в /opt/orr/.env прописаны GOOGLE_CLIENT_ID и GOOGLE_CLIENT_SECRET, compose.prod.yml передает параметры в WebApp. Кнопка «Войти через Google» активна на страницах /login и /signup, цель cta_oauth_google отправляется в аналитику.











## W1-006 — «Оплата рублями по СБП» на первом экране
- status: done
- category: webapp
- priority: high
- created: 2026-09-01T00:00:00Z
- started: 2026-09-01T00:00:00Z
- completed: 2026-09-01T00:00:00Z
- desc: На `/ru` и `/ru/models` добавить явный текст «Оплата рублями по СБП» рядом с кнопкой «Пополнить», видимый на первом экране без скролла (десктоп и мобайл). Сейчас способ оплаты виден только после клика — на QR-странице, а объявления волны 1 обещают СБП: посадочная должна подтверждать это до клика, иначе провал ожиданий и потеря CR. Критерий: на обеих страницах текст про СБП находится в одном визуальном блоке с CTA пополнения и виден без прокрутки. ТЕХУТОЧНЕНИЯ ПО КОДУ (2026-09-01): кнопки «Пополнить» на / и /models НЕ существует — на главной hero содержит только signup + «Смотреть модели» (шаг 02 quickstart-панели ведёт на /workspace/credits, но это ссылка-шаг, не кнопка), на /models CTA пополнения нет вовсе. Поэтому: на / — плашка «Оплата рублями по СБП» в блоке promo-badges/cta-row внутри home-hero-copy (на мобайле ≤900px hero складывается в одну колонку и quickstart-панель уезжает под скролл — hero-copy единственный гарантированно первый экран); на /models — добавить CTA-блок с текстом про СБП в models-hero. Локализация — две .resx (ru/en). Инварианты: плашка не должна зависеть от условных промо-блоков (promo-badges рендерятся только при StartBalanceEnabled/FirstTopUpEnabled); опционально гейтить по Payments:ActiveProvider=rollypay, чтобы не обещать СБП при выключенных платежах.


## T-019 — Показать баланс рядом с никнеймом в верхней панели
- status: done
- category: webapp
- priority: medium
- created: 2026-09-09T19:53:53+03:00
- started: 2026-09-09T16:59:08Z
- completed: 2026-09-09T17:05:00Z
- desc: WebApp. В общей верхней панели вывести текущий баланс авторизованного пользователя непосредственно слева от элемента с аватаром и никнеймом в правом верхнем углу. Баланс отображать только при активной пользовательской сессии; для анонимного посетителя не рендерить элемент и не запрашивать баланс. Использовать фактическое значение из BillingService, а не захардкоженные `$0.50`: приветственные 0,5 USD после регистрации должны отображаться как `$0.50`, но последующие списания и пополнения обязаны отражаться тем же элементом. Оформление согласовать с существующим `user-menu-trigger`: сопоставимые высота, типографика, цвет, фон, граница и скругление; между балансом и никнеймом оставить явный отступ через существующий spacing-токен. Ошибка или недоступность BillingService не должна ломать загрузку страницы: в этом случае скрыть только баланс. Учесть RU/EN, доступное имя для скринридера и адаптивность верхней панели, чтобы на мобильных баланс, никнейм и остальные действия не перекрывались и не создавали горизонтальный скролл. Критерии приёмки: (1) у залогиненного пользователя баланс расположен слева от никнейма и показывает актуальную USD-сумму с двумя знаками после точки, включая `$0.00` и приветственные `$0.50`; (2) у незалогиненного пользователя элемента нет; (3) визуальный стиль совпадает с элементом никнейма, между ними заметен стабильный отступ; (4) проверены RU/EN и основные desktop/mobile ширины; (5) добавлены регрессионные тесты на авторизованный/анонимный рендеринг и формат суммы, обязательная сборка проходит.


## T-020 — Перенести production Telegram-бота в tg_bot и общий процесс обновления
- status: done
- category: deployment
- priority: high
- created: 2026-09-09
- started: 2026-09-09T18:00:00Z
- completed: 2026-09-10T00:18:00Z
- desc: Актуальная версия из работающего контейнера перенесена в tg_bot; исходные src сохранены, сборка 10 JS-файлов совпала по SHA-256. Восстановлены старые тесты, добавлены проверки новых маршрутов и сводки. Добавлены CI publish и отдельный Compose updater с health-gate и rollback образа. Дополнительно устранён баг с таймаутом и обрывом соединения Undici (таймаут увеличен до 30с, добавлен безопасный retry при ConnectTimeoutError/UND_ERR_CONNECT_TIMEOUT).
- validation: 83 Vitest tests, typecheck/build, 41 релевантный WebApp test, dotnet solution build (0 warnings/errors), Docker build, изолированный runtime health, Compose validation, 4 mock deployment scenarios. Прод: update-prod.sh exit 0, health-checks (API Gateway, Collector, WebApp login, tg-bot /healthz) — ok. Реальная доставка в Telegram подтверждена боевыми тестами для всех 4 типов событий (support_ticket, topup_completed, first_api_request, user_registered).
- result: production deploy and live verification complete. В чат успешно доставлены все типы событий, отработал механизм retry при задержках сокета.

## SEO-004 — Исправить soft 404 в каталогах
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. Обработчик /tools/{id} (Pages/ToolDetails.cshtml.cs:43): отсутствующий или неопубликованный tool → Response.StatusCode = 404. Обработчик /providers/{id} (Pages/ProviderDetails.cshtml.cs:41): отсутствующий provider → 404. Реализовано. Тесты: ProviderDetailsPageTests.cs (MissingProvider → 404), ToolPagesTests.cs (MissingTool → 404, DraftTool → 404), ModelDetailsPageTests.cs (UnknownModel → 404).

## SEO-005 — Добавить robots.txt
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. Добавить доступный по /robots.txt файл (wwwroot/robots.txt). Разрешить обход публичных страниц; закрыть служебные разделы: /admin/, /workspace/, /webhook/, /internal/, /health, /metrics. Добавить абсолютную ссылку Sitemap: https://openrussiarouter.com/sitemap.xml. Критерий: /robots.txt отвечает 200 и валиден. Сборка обязательна.

## SEO-006 — Реализовать динамический sitemap.xml
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. Добавить эндпоинт /sitemap.xml (новый Api/SitemapEndpoint.cs + MapGet в Program.cs). Включить статические индексируемые URL; динамически — активные модели, активных providers, опубликованные tools. Для каждой записи canonical URL и корректный lastmod при наличии даты обновления. Абсолютный базовый URL брать через ApiBaseUrlResolver. Не включать: URL с query-параметрами, личный кабинет, админку, платежи, несуществующие/неопубликованные сущности, черновики, страницы ошибок. Добавить тест генерации sitemap. Критерий: sitemap валиден, содержит только URL со статусом 200, отражает изменения каталога.

## SEO-007 — Canonical, robots meta и SEO-head в layout
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. В Pages/Shared/_Layout.cshtml создать общий механизм SEO-метаданных. Self-referencing canonical для каждой индексируемой публичной страницы (ViewData["CanonicalUrl"], абсолютный URL через ApiBaseUrlResolver). Query-параметры в canonical не включать (/tools?category=x → /tools; UTM → чистый URL). meta robots="noindex,follow" для служебных и приватных страниц. Open Graph: og:title, og:description, og:url, og:type, единое бренд-изображение; Twitter Card. Добавить тесты рендеринга canonical, description и noindex. Критерий: в HTML /, /models, detail-страниц есть уникальные title, description и canonical; приватные страницы — noindex. Бренд-изображение для OG уточнить у продукта.

## SEO-008 — Meta description для ключевых публичных маршрутов
- status: done
- priority: medium
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-08T00:00:00+00:00
- desc: WebApp. Добавить ViewData["MetaDescription"] на публичные страницы: / (Index), /models, /models/{provider}/{slug} (ModelDetails), /providers, /providers/{id} (ProviderDetails), /tools, /tools/{id} (ToolDetails), /docs, /support, /legal/privacy, /legal/offer, /signup, /login. Описания уникальные, без шаблона с заменой одного названия; локализация через существующие .resx (ru/en). Критерий: нет дублирующихся descriptions среди индексируемых публичных страниц. Сборка обязательна.

## SEO-020 — Добавить sitemap в поисковые системы
- status: done
- priority: high
- created: 2026-08-06T00:00:00Z
- started: 2026-08-09T00:00:00+00:00
- completed: 2026-08-09T16:19:17Z
- desc: https://openrussiarouter.com/sitemap.xml добавлен в Google Search Console и Яндекс Вебмастер; статус зелёный (принят без ошибок) в обеих системах. Критерий выполнен.

## SEO-002 — Подключить системы вебмастеров и аналитику
- status: done
- priority: low
- created: 2026-08-06T00:00:00Z
- started: 2026-08-08T00:00:00+00:00
- completed: 2026-08-09T16:19:17Z
- desc: Код внедрён и задеплоен: AnalyticsOptions + теги GA4 (G-BDD3DYVCLL) и Метрики (111420552) в _Layout.cshtml, условные CSP-хосты в Program.cs, конверсионные события signup/email_verified/api_key_created/topup_completed через ViewData["AnalyticsEvents"], прод-значения в compose.prod.yml, тесты SeoHeadTests/LegalConsentTests. Домен подтверждён в GSC и Яндекс Вебмастере через TXT-записи. Цели/конверсии добавлены в оба кабинета, связка GSC↔GA4 работает. События фиксируются (email_verified подтверждено); signup на тестовом первом визите не отобразился в Метрике — известный edge case «hit до добивки cookie», механизм работает. Runbook — docs/seo/webmasters-analytics-runbook.md. Юридическая точка (cookies Метрики/GA4 в privacy-policy) вынесена в SEO-023.

## SEO-016 — Решение по единому контуру документации
- status: done
- priority: medium
- created: 2026-08-06T00:00:00Z
- started: 2026-08-09T00:00:00+00:00
- completed: 2026-08-09T00:00:00+00:00
- desc: Решение принято и реализовано: документация — VitePress под `/docs/` на апекс-домене (Caddy: `@docs path /docs /docs/*` → контейнер `docs`, префикс не стрипается). Отдельный поддомен docs.openrussiarouter.com не заводится, Razor-страница `Pages/Docs.cshtml` в проде перекрыта роутингом и не развивается. Условие «не держать две одинаковые документации без canonical/redirect стратегии» выполнено: на всех страницах `/docs/**` проставлен self-referencing canonical (`transformHead` в `.vitepress/config.mts`), продублированные юридические страницы удалены из docs-site — единственный дом `/legal/*` в WebApp, меню документации ведёт туда абсолютными ссылками. Решение зафиксировано в корневом `CLAUDE.md`, раздел «SEO-контур docs-site». Остаточная техническая настройка (frontmatter по файлам) — в SEO-017.

## SEO-024 — Адресуемая английская локаль (/en) + hreflang
- status: done
- priority: high
- created: 2026-08-10T00:00:00Z
- started: 2026-08-10T00:00:00Z
- completed: 2026-08-10T00:00:00Z
- desc: Отменяет решение SEO-002/SEO-003 «hreflang для cookie-локализации не строить». Причина отмены — расширение целевого рынка за пределы РФ (Иран, Турция, Центральная Азия): там английский это основной язык поиска, а при cookie-локализации EN-контент был непроиндексируем в принципе (Googlebot краулит с US-IP и Accept-Language не шлёт, поэтому всегда получал русскую версию). Реализовано в WebApp: русский остаётся на голом пути, английский под префиксом /en; префикс переезжает в PathBase (LocalePrefixMiddleware), поэтому маршруты страниц, ModelDetailRoute и подсветка навигации не менялись вообще. Контракт URL — Services/Localization/LocaleUrl.cs, через него обязаны ходить canonical, hreflang, sitemap, переключатель языка и tag helper ссылок. Цепочка культур: RouteCultureProvider (URL) → CookieRequestCultureProvider; AcceptLanguageHeaderRequestCultureProvider удалён — с ним один URL отдавал два языка по заголовку, что противоречит его же canonical. app.UseRouting() вызван явно: без этого WebApplication вставляет свой UseRouting раньше UseLocalePrefix и всё английское дерево отдаёт 404. Ссылки в разметке — сырые href="/...", asp-page не используется, поэтому их префиксует LocalePathTagHelper по PathBase (новые ссылки локализуются автоматически). Canonical self-referencing на локаль, hreflang взаимный ru/en/x-default, x-default → английский (версия для тех, чей язык не совпал ни с одной нашей). Дубли схлопнуты 301: /ru/* → голый путь, /en/ → /en, /EN/* → /en/*; редирект канонического нормализатора несёт PathBase. /docs исключён из контура — VitePress держит своё дерево внутри собственной базы, /en/docs* → 301 на /docs/en*. Sitemap отдаёт обе локали с взаимными xhtml:link + x-default, /docs одной нелокализованной записью. Приватные разделы (workspace/admin/auth) префикса не имеют и остаются cookie-driven. Тесты: LocaleUrlTests (новый), плюс расширены LocalizationTests, SeoHeadTests, SitemapEndpointTests, LanguagePreferenceTests; solution собирается, 1067 тестов зелёные. Документация — src/OpenRussiaRouter.WebApp/CLAUDE.md раздел «Локали и URL» и корневой CLAUDE.md. НЕ ВХОДИЛО В ЗАДАЧУ: английский контент это перевод русских текстов под русский спрос — ранжироваться на новых рынках он сам по себе не будет, нужны отдельные англоязычные посадочные (переразметка SEO-010/011); и платёжные рельсы (СБП/Yandex Pay) для новой аудитории не работают.

## SEO-003 — Список index/noindex URL
- status: done
- priority: medium
- created: 2026-08-06T00:00:00Z
- started:
- completed: 2026-08-11T00:00:00Z
- desc: Языковая часть задачи отменена решением SEO-024 (hreflang, /en prefix). Оставшаяся часть — согласованный с продуктом список index/noindex URL: индексируемые — /, /models, /models/{provider}/{slug}, /providers, /providers/{id}, /tools, /tools/{id} (опубликованные с описанием), /docs, /support, юридические страницы и их зеркала под /en (кроме /docs); неиндексируемые — /login, /signup, /forgot-password, /reset-password, /verify-email, /invite/*, /workspace/*, /admin/*, /pay/*, /error, служебные API и webhooks. Техническая реализация (canonical, robots meta, noindex) покрыта SEO-007. Список URL согласован и задокументирован.

## RNM-001 — Бренд интерфейса
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-13T00:00:00Z
- completed: 2026-08-13T00:00:00Z
- desc: WebApp. Перед правкой прочитать `src/OpenRussiaRouter.WebApp/CLAUDE.md`. Заменить отображаемое название продукта `OpenRussiaRouter` на `Rawrter` во всех пользовательских текстах Razor Pages, layout, ресурсах ru/en, SEO-title/description, административном интерфейсе, email-шаблонах, `Email:SenderName`, платёжных назначениях, юридических шаблонах, alt/aria-подписях и пользовательских сообщениях. Проверить `wwwroot` на логотипы и изображения со старым текстом; при наличии обновить исходные assets без смены визуального стиля. Не менять namespace, assembly/project names, каталоги, `ORR_*`, `orr_*`, `sk-or-*`, `ORR/*`, URL API и почтовые адреса. Обновить затронутые тесты. Критерий: отрендеренные страницы и письма показывают `Rawrter`, технические исключения сохранены; `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1` и тесты `OpenRussiaRouter.WebApp.Tests` проходят. ПРИМЕЧАНИЕ (2026-08-13): `wwwroot/og-image.png` в этой задаче НЕ менять (logo-квадрат `rawr` → `Rawrter` и баннер) — ручная перегенерация ассета планируется отдельно, вне RNM-001; состав работ зафиксирован в todo-листе. `favicon.png` также остаётся. Реализовано: бренд `Rawrter` в разметке, ресурсах, email-шаблонах, юридических шаблонах и SEO-метаданных (коммиты 42ed0d1c, 7686257c, f472777e); `og-image.png` перегенерирован под новый бренд (коммит f472777e). Для зелёных тестов заодно переведены на `rawrter.com` сайтовые дефолты `LegalDocumentService` (`ORR_PUBLIC_DOMAIN`, `ORR_OPERATOR_DETAILS`) и Sitemap-пути в `wwwroot/robots.txt` — домен сайта целиком добивается в RNM-002.

## RNM-002 — Домен сайта
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-13T19:08:43Z
- completed: 2026-08-13T19:21:20Z
- desc: Перевести только публичный сайт с `https://openrussiarouter.com` на `https://rawrter.com`. Обновить актуальные значения и ссылки в WebApp (`PublicSiteBaseUrl`, юридический `ORR_PUBLIC_DOMAIN`, robots и связанные тесты), docs-site, `deploy/compose`, `PROD-ACCESS.md`, актуальных deploy/runbook-файлах и комментариях, описывающих текущий прод. Имя переменной `ORR_PUBLIC_DOMAIN` сохранить; её production-значение должно стать `rawrter.com`. Caddy развязать так, чтобы apex обслуживался на `rawrter.com`, а API продолжал обслуживаться только на `api.openrussiarouter.com`; не создавать `api.rawrter.com`. Удалить redirect-блоки старого/опечаточного сайта, новые редиректы не добавлять. Не менять `PublicApiBaseUrl`, API-примеры, почтовые адреса и API DNS. Критерий: рабочие ссылки сайта используют `rawrter.com`, `api.openrussiarouter.com` сохранён как явное исключение; `docker compose -f deploy/compose/compose.prod.yml config` валиден и обязательная сборка solution проходит. Сделано (2026-08-13): Caddy развязан на `rawrter.com` (apex + docs), API закреплён за `api.openrussiarouter.com` без `api.rawrter.com`, redirect-блок старого/опечаточного сайта удалён; обновлены `deploy/compose/{Caddyfile,compose.prod.yml,.env.example}`, `PROD-ACCESS.md`, `docs/DEPLOYMENTS.md`, `src/OpenRussiaRouter.WebApp/CLAUDE.md`, комментарий монтажа `docs-site/.vitepress/config.mts`; SEO-сигналы `config.mts` и runbook вебмастеров остались задачам RNM-003/RNM-005; сборка solution и `OpenRussiaRouter.WebApp.Tests` (375/375) зелёные.


## RNM-003 — SEO-контур
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-14T00:30:00Z
- completed: 2026-08-14T00:50:00Z
- desc: WebApp + docs-site. Перед правкой WebApp прочитать его `CLAUDE.md`. Перевести crawler-facing сигналы сайта на `https://rawrter.com`: canonical, `og:url`, `og:image`, Twitter image, JSON-LD, взаимные hreflang ru/en/x-default, `<loc>` динамической `/sitemap.xml`, hostname `/docs/sitemap.xml` и обе записи Sitemap в `/robots.txt`. Сохранить структуру локалей и `/docs/`, не подменять сайт API-origin'ом. Обновить `SeoHeadTests`, `SitemapEndpointTests`, `RobotsTxtTests` и другие затронутые ожидания. Не менять `https://api.openrussiarouter.com` в API-контракте и примерах. Критерий: сгенерированный SEO-контур содержит `rawrter.com` для сайта и документации, API-исключение сохранено; тесты WebApp, обязательная сборка solution и `npm run docs:build` в `docs-site` проходят.


## RNM-004 — Публичная документация
- status: done
- category: surface
- priority: medium
- created: 2026-08-13T17:26:20Z
- started: 2026-08-14T00:30:00Z
- completed: 2026-08-14T00:50:00Z
- desc: docs-site и актуальная пользовательская документация. Заменить отображаемый бренд на `Rawrter`, а ссылки на панель, главную и `/legal/*` — на `https://rawrter.com`. Обновить title, description, навигацию и обычный текст, не меняя техническое содержание API. Явно сохранить `https://api.openrussiarouter.com` во всех API reference, curl-командах, Python/Node.js примерах и `base_url`; не менять почтовые адреса, `sk-or-*` и `ORR/*`. Не редактировать `.opencode/tasks-done.md`, существующие блоки SEO-* и датированные исторические планы/specs. Критерий: документация использует бренд `Rawrter` и новый адрес сайта, API-примеры побайтно сохраняют прежний endpoint; `npm run docs:build` проходит.


## RNM-008 — Согласованность canonical и sitemap после переезда
- status: done
- category: surface
- priority: high
- created: 2026-08-14T15:26:08Z
- started: 2026-08-14T20:16:59.3875840+03:00
- completed: 2026-08-14T20:26:29.6948590+03:00
- desc: Исправить обнаруженные расхождения crawler-facing URL на `rawrter.com`, не меняя существующий redirect-контур. Для английской главной `/en` выводить self-referencing canonical `https://rawrter.com/en`, а не `/en/`, который редиректится обратно. В динамической `/sitemap.xml` публиковать конечный URL документации `https://rawrter.com/docs/` вместо `/docs`, чтобы URL из карты сайта сразу отвечал 200 без redirect-chain и промежуточного HTTP Location. Добавить или обновить тесты canonical, hreflang и sitemap. Критерий: canonical каждой проверяемой страницы совпадает с её конечным URL; все `<loc>` из обеих sitemap отвечают прямым HTTPS 200; существующие 301-редиректы остаются без изменений; тесты WebApp и обязательная сборка solution проходят. Сделано (2026-08-14): canonical английской главной теперь `.../en` без хвостового слэша (self-referencing, og:url тоже); sitemap публикует конечный URL `/docs/` вместо `/docs`; тесты canonical/hreflang/sitemap добавлены и обновлены; redirect-контур не менялся. Внешняя проверка «все <loc> отвечают прямым HTTPS 200 на проде» остаётся до деплоя (нет доступа к проду 201.51.6.246).


## RNM-009 — Запрет индексации API-origin
- status: done
- category: surface
- priority: medium
- created: 2026-08-14T15:26:09Z
- started: 2026-08-14T21:11:59.7284300+03:00
- completed: 2026-08-14T21:15:31.6031770+03:00
- desc: Закрыть `https://api.rawrter.com` от поисковой индексации, не меняя публичный OpenAI-совместимый API-контракт. Добавить доступный на API-host `/robots.txt` с `User-agent: *` и `Disallow: /` и/или единый `X-Robots-Tag: noindex, nofollow` для публичных API-ответов, включая `/v1/models` и health endpoints. Решение не должно менять JSON-body, HTTP-коды, streaming, authentication или CORS API. Существующие 301-редиректы и другие hostnames не изменять. Критерий: поисковики получают однозначный запрет индексации `api.rawrter.com`, а `/health`, `/v1/models` и completions сохраняют прежнее поведение; затронутые тесты и обязательная сборка solution проходят. Реализовано через API middleware и публичный `/robots.txt`, добавлены интеграционные проверки.

## RNM-005 — Внешний переход
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-14T11:00:00Z
- completed: 2026-08-14T23:30:00Z
- desc: Внешняя конфигурация без переименования внутренностей. DNS, TLS, сертификаты, GSC/Яндекс Вебмастер, GA4/Метрика, Brevo, OAuth redirect URIs — всё на `rawrter.com`. Сделано (2026-08-14): DNS A-записи на 201.51.6.246; `RWT_PUBLIC_DOMAIN=rawrter.com` в .env; Let's Encrypt сертификаты; GSC/Яндекс — смена адреса завершена; GA4 `G-38042Y9GW4` + Метрика `111582266`; Brevo `noreply@rawrter.com`; Яндекс OAuth redirect URI `https://rawrter.com/OAuth-yandex`; Rollypay webhook `https://rawrter.com/webhook/rollypay` зарегистрирован в кассе; `PAYMENTS_ACTIVE_PROVIDER=rollypay` + ключи в .env; `www.rawrter.com` → 301 на apex. Финальная проверка 2026-08-14: SSH 201.51.6.246, `docker compose ps` все контейнеры Up, health endpoints 200, canonical/og/hreflang/sitemap/robots на `rawrter.com`.

## RNM-007 — Публичный домен API
- status: done
- category: surface
- priority: high
- created: 2026-08-14T15:26:07Z
- started: 2026-08-14T18:00:00Z
- completed: 2026-08-14T23:30:00Z
- desc: Перевести видимую пользователям API-поверхность с `api.openrussiarouter.com` на `api.rawrter.com`. Сделано: docs-site (ru+en) переведён на `api.rawrter.com`; K6-тесты обновлены; добавлен unit-тест `ApiBaseUrlResolverTests` (3 теста); `platform_settings.public_api_base_url` обновлён в БД прода на `https://api.rawrter.com` (SSH 201.51.6.246); WebApp перезапущен для сброса кеша; docs-контейнер пересобран; `api.rawrter.com/health`=200, `/v1/models`=200; `api.rawrter.com/robots.txt`=Disallow: /; `dotnet build` 0 ошибок, `dotnet test` 1101 зелёный, `npm run docs:build` зелёный. Legacy `api.openrussiarouter.com` сохранён как контракт для существующих клиентов.

## RNM-006 — Проверка публичной поверхности
- status: done
- category: surface
- priority: high
- created: 2026-08-13T17:26:20Z
- started: 2026-08-14T23:00:00Z
- completed: 2026-08-14T23:30:00Z
- desc: Финальный surface-аудит. Результаты (2026-08-14, SSH 201.51.6.246): canonical на `/` = `https://rawrter.com/`; hreflang ru/en/x-default (3 шт.); og:url + og:image = rawrter.com; sitemap = 83 URL; docs sitemap = 200; robots.txt site = Allow + Disallow служебных; robots.txt API = `Disallow: /`; www.rawrter.com = 301; /docs/ = 200; OAuth Yandex callback = 500 (ожидаемо — ClientSecret не задан в .env); dotnet build 0 ошибок; dotnet test 1101 зелёный; npm run docs:build зелёный. Контракты `ORR_*`, `orr_*`, `sk-or-*`, `ORR/*`, namespace'ы, почтовые адреса, `api.openrussiarouter.com` как legacy не изменены.

## SEO-025 — 301-редирект запросов документации без /docs/ в WebApp
- status: done
- priority: high
- created: 2026-08-26T20:16:00Z
- started: 2026-08-26T20:28:45+03:00
- completed: 2026-08-26T20:37:00+03:00
- desc: WebApp. В `LocalePrefixMiddleware` и `LocaleUrl` настроен 301 Permanent Redirect для путей документации без префикса `/docs/`: `/guide/*` → `/docs/guide/*`, `/api/*` → `/docs/api/*`, `/integrations/*` → `/docs/integrations/*`, `/en/guide/*` → `/docs/en/guide/*`, `/en/api/*` → `/docs/en/api/*`, `/en/integrations/*` → `/docs/en/integrations/*` (и `/ru/...` аналогично), с полным сохранением query string. В `LocaleUrl` добавлены `DocsSectionPrefixes` (`/guide`, `/api`, `/integrations`), `IsDocsSectionPath` и расширены `IsDocsPath` и `DocsFor`. Добавлены unit и integration тесты в `LocaleUrlTests` и `LocalizationTests` (проверены обе локали, query string, коды 301 и работа существующих страниц).

## SEO-026 — Устранить Soft 404 на страницах документации docs/**/api/errors
- status: done
- priority: medium
- created: 2026-08-26T20:43:00Z
- started: 2026-08-26T20:55:40+03:00
- completed: 2026-08-26T21:26:30+03:00
- desc: docs-site (VitePress). Устранен риск Soft 404 на страницах документации /api/errors и /en/api/errors за счет расширения контента в развернутое руководство разработчика по обработке ошибок API (>1000 слов). Добавлен YAML frontmatter с title и description для SEO. Описан формат JSON-ответа OpenAI (message, type, code, param, request_id). Добавлена подробная сводная таблица HTTP-статусов (400, 401, 402, 403, 404, 429, 500, 502, 503) и платформенных кодов ошибок (validation_failed, authentication_required, invalid_api_key, insufficient_balance, api_key_limit_exceeded, access_denied, model_unavailable, rate_limited, internal_error, bad_gateway, router_unavailable) с причинами и действиями клиента. Разработаны практические примеры обработки ошибок с экспоненциальным backoff для Python (OpenAI SDK), TypeScript/Node.js и cURL (с разбором через jq). Описана retry-стратегия с обработкой заголовка Retry-After, джиттером и разделением на повторяемые/неповторяемые ошибки. Описано поведение проксирования ошибок upstream и автоматический failover нод. Обеспечена 1:1 зеркальность ru/en версий. Сборка `npm run docs:build` успешно протестирована.

## T-002 — Убрать сохранённый отступ бокового меню в мобильном кабинете и админке
- status: done
- category: responsive-ui
- priority: high
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T13:54:56.5972576+04:00
- completed: 2026-08-30T13:59:54.6437738+04:00
- desc: P1. В `wwwroot/app.css` и `wwwroot/app.min.css` в медиа-запросе `@media (max-width: 768px)` добавлено правило `.app-shell--no-sidebar .sidenav-content` для сброса `margin-left: 0`, перебивающее десктопное правило повышенной специфичности `.app-shell--no-sidebar .sidenav-content { margin-left: var(--sidebar-width); }`.

## T-003 — Уместить авторизованную шапку и меню пользователя в мобильный viewport
- status: done
- category: responsive-ui
- priority: high
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:01:11.2279823+04:00
- completed: 2026-08-30T14:06:53.4898900+04:00
- desc: P1. В `wwwroot/app.css` и `wwwroot/app.min.css` настроены адаптивные размеры и flex/grid ограничения `.topbar`, `.topbar-brand`, `.topbar-logo`, `.topbar-nav`, `.topbar-actions` и `.user-dropdown` (`max-width: calc(100vw - 16px)`, `max-height: calc(100vh - var(--topbar-height) - 16px)`, `overflow-y: auto`), добавлены брейкпоинты для мобильных ширин (360/480/768 px). В `Pages/Shared/_Layout.cshtml` синхронизирован класс `.user-menu--open` при открытии/закрытии dropdown для корректного состояния chevron. Добавлен тест `UserMenuLayoutTests`.

## T-004 — Восстановить перенос фильтров Top-ups и Requests и размеры кнопок
- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:08:12.8121271+04:00
- completed: 2026-08-30T14:20:45.2729702+04:00
- desc: P2. В `wwwroot/app.css` и `wwwroot/app.min.css` добавлены утилиты `.flex-wrap`, `.flex-nowrap`, `.align-items-start`, `.align-items-end`, правила для `.btn-outline-secondary`, `.btn-info`, `.form-control-sm` и `flex-shrink: 0` для `.btn-sm`, а полям фильтрации добавлены ограничения `max-width: 100%`. Добавлены регрессионные тесты в `AdminFilterResponsiveTests`.

## T-005 — Ограничить переполнение таблиц админки и истории баланса

- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:22:19.7787193+04:00
- completed: 2026-08-30T14:30:17.1211368+04:00
- desc: P2. Таблицы в `Pages/Admin/Users.cshtml`, `TopUps.cshtml`, `Nodes.cshtml`, `Models.cshtml`, `Requests.cshtml` и `Pages/Workspace/Credits.cshtml` обёрнуты в локальный скролл-контейнер `.table-scroll`. Для таблицы моделей добавлена минимальная ширина `.adm-models-table { min-width: 900px; }`, предотвращающая разрушение и посимвольный перенос Model ID. Добавлены unit-тесты в `TableScrollResponsiveTests`.

### Воспроизведение и влияние

1. Открыть `/ru/admin/models` при 1024 px: document.scrollWidth около 1086 px при clientWidth около 1009 px; Model ID переносится почти по одной букве, правые колонки обрезаны.
2. На `/ru/admin/requests` при 1024 px document.scrollWidth около 1222 px; здесь дополнительно влияет фильтр из T-004.
3. При 768 px наблюдаются ширины документа около 979 px в Users, 911 px в Top-ups и 914 px в Nodes. В истории `/ru/workspace/credits` при 375 px таблица шириной около 449 px выходит за панель.
4. У соответствующих таблиц родитель `.adm-card-body`/`.ws-panel-body` имеет overflow-x: visible. Глобальный body overflow-x: hidden маскирует дефект, а не делает правые колонки доступными.

### Причина и область исправления

- `Pages/Admin/Users.cshtml:104`, `TopUps.cshtml:48`, `Nodes.cshtml:59`, `Models.cshtml:402`, `Requests.cshtml:117`; `Pages/Workspace/Credits.cshtml:141`.
- В `wwwroot/app.css:380–388` уже есть `.table-wrap` и `.table-scroll`. Рабочие примеры: API-ключи, детализация активности, таблица сравнения цен (`.price-table-wrap`).
- Ввести локальную прокрутку в пределах панели; родительские grid/flex-элементы должны уметь сжиматься. Задать достаточную минимальную ширину таблицы/критичных колонок там, где иначе разрушаются ID, числа и кнопки. Не применять nowrap ко всему тексту без разбора: длинные описания могут переноситься.
- Сохранить семантику table/th, сортировку, копирование, ссылки и действия. Не скрывать Actions, цены или другие колонки как способ устранить overflow.
- Проверять после T-002/T-003; переполнение фильтра Requests устраняется отдельно в T-004.

### Критерии приёмки

- [ ] На всех шести страницах таблица не увеличивает scrollWidth документа сверх clientWidth; необходимая горизонтальная прокрутка находится внутри панели.
- [ ] До последней колонки и всех действий можно добраться мышью, touch и клавиатурой; фокусируемые элементы не остаются за недоступной областью.
- [ ] ID/даты/суммы читаемы, нет вынужденного переноса одного идентификатора по букве в узкую вертикальную колонку.
- [ ] Проверены длинные модельные ID, email и названия на синтетических данных, пустая таблица и много строк; на 1440 px нет лишнего скролла там, где таблица помещается.
- [ ] Существующие корректные таблицы API-ключей, активности и сравнения цен не получили регрессий.

Локальное свидетельство (вне git): `C:/Users/pro/.codex/visualizations/2026/08/30/01a051e8-345d-7082-bb5b-e199de7ed1a9/responsive-audit/admin-models-1024.png`.

## T-006 — Адаптировать сетку карточек обзора активности к узкому контейнеру

- status: done
- category: responsive-ui
- priority: medium
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:31:59.1737523+04:00
- completed: 2026-08-30T14:37:07.8575093+04:00
- desc: P2. В `wwwroot/app.css` и `wwwroot/app.min.css` сетка `.act-stat-row` переведена на `minmax(0, 1fr)` (4 колонки на десктопе, 2 колонки на ширине ≤ 768 px, 1 колонка на ширине ≤ 480 px), для `.act-stat-card` задан `min-width: 0`, для `.act-stat-label` разрешён перенос длинных названий (`white-space: normal; overflow-wrap: break-word`), а для `.act-stat-value` настроено безопасное предотвращение переполнения (`text-overflow: ellipsis; white-space: nowrap; overflow: hidden`). Добавлены регрессионные тесты в `ActivityResponsiveTests`.

### Воспроизведение и влияние

1. Открыть `/ru/workspace/activity?tab=overview` с доступной статистикой, установить ширину 768 px.
2. `.act-stat-row` имеет ширину около 481 px и scrollWidth около 522 px; последняя карточка заканчивается на x≈778 при clientWidth≈753.
3. Аналогичное переполнение наблюдается при 320/375 px. Сетка не перестраивается, хотя названия показателей и числовые значения требуют большего места.

### Причина и область исправления

- `wwwroot/app.css:1112–1136`: `.act-stat-row { grid-template-columns: repeat(4, 1fr); }`, intrinsic minimum у `.act-stat-card` и размеры текста.
- `Pages/Workspace/Activity.cshtml:31–47`: четыре показателя — запросы, входные/выходные токены, стоимость.
- Перестраивать колонки по доступной ширине (4/2/1 либо эквивалентная адаптивная сетка), разрешить безопасное сжатие/перенос текстовых частей. Сохранить `gap: var(--spacing-md)` и padding карточки из существующего компонента.
- Не сокращать суммы или токены с потерей точности ради вёрстки, не менять формат/источник статистики. Повторить измерения после T-002/T-003: исправленная ширина контейнера отличается от исходной ошибочной.

### Критерии приёмки

- [ ] Все четыре карточки полностью видны на базовых ширинах и около брейкпоинтов; ни одна не расширяет контейнер или документ.
- [ ] На широком экране сохраняется компактная четырёхколоночная раскладка, на узком число колонок уменьшается без наложений.
- [ ] Проверены нули, большие числа с разделителями групп и стоимость с четырьмя знаками, русские и английские подписи; текст не обрезается.
- [ ] Gap/padding одинаковы у всех карточек; переключение периода и вкладки детализации не получают регрессии.

## T-007 — Закрывать выпадающие фильтры Requests по Escape с восстановлением фокуса

- status: done
- category: accessibility
- priority: medium
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:38:44.3639107+04:00
- completed: 2026-08-30T14:41:08.9460814+04:00
- desc: P2. В `Pages/Admin/Requests.cshtml` добавлен обработчик клавиши Escape для закрытия всех открытых выпадающих фильтров `.filter-multiselect`, синхронизации атрибута `aria-expanded="false"` и возврата фокуса на связанную кнопку `.ms-toggle`. Функция `closeAllPanels` обновлена для синхронного сброса `aria-expanded="false"` на всех закрываемых выпадающих списках. Добавлены регрессионные тесты в `AdminFilterResponsiveTests`.

### Воспроизведение и влияние

1. Открыть `/ru/admin/requests` на ширине 1024 px, нажать кнопку Model.
2. Убедиться, что панель `.ms-panel.open` видна и кнопка имеет aria-expanded=true; нажать Escape.
3. Панель остаётся открытой, aria-expanded остаётся true. Повторить Escape из поискового поля внутри фильтра; на исправленной версии сценарий должен закрывать панель и возвращать фокус.

### Причина и область исправления

- `Pages/Admin/Requests.cshtml:63–71` (кнопка/панель), обработчики около строк 211–240: закрытие завязано на мышиные взаимодействия, нет обработки Escape.
- Распространить общий механизм на Model, Provider, API Key, Node, Account. При закрытии убрать `.open`, выставить aria-expanded=false на связанной кнопке и вернуть на неё фокус.
- Не сбрасывать чекбоксы/поиск и не отправлять GET-форму при Escape. Сохранить закрытие кликом снаружи, повторным нажатием кнопки и при переключении на другой фильтр.
- T-004 исправляет расположение формы, T-003 — другое меню в общей шапке; не подменять эту задачу их исправлениями.

### Критерии приёмки

- [x] Escape закрывает каждый из пяти фильтров с фокусом на кнопке, в поисковом поле и на чекбоксе.
- [x] aria-expanded соответствует фактической видимости, фокус возвращается на открывшую кнопку; выбранные значения остаются прежними.
- [x] Enter/Space на кнопке, Tab внутри формы, клик снаружи и повторное открытие работают без двойных обработчиков и неожиданной отправки формы.
- [x] Escape при закрытых фильтрах ничего не отправляет и не переводит фокус произвольно; меню пользователя продолжает закрываться штатно.
- [x] Клавиатурный сценарий покрыт E2E на тестовых данных, проверен на мобильной и десктопной ширине после исправления общей раскладки.

## T-008 — Устранить лишний верхний отступ ряда кнопок в заголовках админских панелей

- status: done
- category: design-system
- priority: low
- created: 2026-08-30T13:45:33.9630299+04:00
- started: 2026-08-30T14:42:39.0480492+04:00
- completed: 2026-08-30T14:48:32.1145159+04:00
- desc: P3. В `wwwroot/app.css` и `wwwroot/app.min.css` для `.adm-card-header--split` добавлены `flex-wrap: wrap` и `gap: var(--spacing-md)`, а для непосредственного потомка `.adm-card-header--split > .adm-btn-row` обнулён лишний верхний отступ (`margin-top: 0`), исключая вертикальное смещение кнопок действий относительно заголовка панели. Добавлены регрессионные тесты в `AdminCardHeaderResponsiveTests`.

### Воспроизведение и влияние

1. Открыть `/ru/admin/models` при 1024 или 1440 px, найти заголовок Public Models с кнопками Enrich/Sync/Price Comparison/Add Model.
2. У `.adm-card-header--split` padding равен 14px 16px, у вложенного `.adm-btn-row` дополнительно margin-top=16px.
3. Измеренный отступ кнопок сверху около 30 px, снизу около 14 px; текст заголовка и ряд действий визуально не центрированы относительно друг друга.

### Причина и область исправления

- `wwwroot/app.css:1052–1061`: контракт `.adm-card-header--split` и общий `.adm-btn-row`.
- `Pages/Admin/Models.cshtml:384–395` и другие потребители `.adm-card-header--split > .adm-btn-row` (Tags, Providers, Tools, сравнение цен — сверить актуальную разметку).
- Обнулить внешний верхний отступ только у ряда действий заголовка через контекстный селектор/модификатор. Сохранить 16px между полями и кнопками в обычных формах, 12px между действиями и штатные горизонтальные отступы панели.
- Не менять глобально все значения 14px на ближайший spacing-токен: дефект — дополнительный margin в конкретном контексте, а не доказанная ошибка padding самого компонента.
- Для многострочного заголовка/переноса действий применять согласованные gap и выравнивание без наложений; повторить проверку после T-002/T-003 и T-005.

### Критерии приёмки

- [x] В однострочной шапке ряд действий и текст вертикально выровнены; у кнопок симметричные верхний/нижний внутренние отступы с учётом границы панели.
- [x] Все потребители split-заголовка используют одинаковый контракт; кнопки не получают лишнего margin-top.
- [x] Ряды кнопок в формах добавления модели, узла и других формах сохранили отделение от полей; gap не заменён случайными локальными значениями.
- [x] На всех базовых ширинах, около брейкпоинтов и с длинными локализованными надписями нет наложений, обрезания текста или выхода действий за панель.


