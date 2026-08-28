import { defineConfig } from 'vitepress'

// Тот же measurement id, что у WebApp (`Analytics:GoogleTagId` в appsettings.json и
// compose.prod.yml). Один поток на весь rawrter.com — с отдельным свойством переход
// сайт → документация рвал бы сессию и терял источник трафика.
const GOOGLE_TAG_ID = 'G-38042Y9GW4'

// Канонический адрес для каждой страницы /docs/ строится от сырого пути
// (guide/models.md -> https://rawrter.com/docs/guide/models).
// og-image тот же бренд-баннер, что на основном сайте.
function canonicalUrl(page: string): string {
  const isIndex = page.replace(/\.md$/, '').match(/(^|\/)index$/) !== null
  const rel = page.replace(/\.md$/, '').replace(/\/?index$/, '')
  const path = rel === '' ? '' : `/${rel}`
  return `https://rawrter.com/docs${path}${isIndex ? '/' : ''}`
}

/**
 * Взаимные hreflang для пары ru/en. Деревья локалей зеркальны 1:1 (`guide/models.md` ↔
 * `en/guide/models.md`), поэтому пара вычисляется из пути. Ссылаться нужно и на себя:
 * без self-referencing записи Google отбрасывает кластер целиком. x-default — русская
 * версия, локаль по умолчанию.
 *
 * 404 исключена: у неё нет пары, а hreflang на несуществующий URL инвалидирует кластер.
 */
function hreflangLinks(page: string): [string, Record<string, string>][] {
  if (page.replace(/\.md$/, '') === '404') {
    return []
  }

  const isEn = page.startsWith('en/')
  const ru = canonicalUrl(isEn ? page.slice(3) : page)
  const en = canonicalUrl(isEn ? page : `en/${page}`)

  return [
    ['link', { rel: 'alternate', hreflang: 'ru', href: ru }],
    ['link', { rel: 'alternate', hreflang: 'en', href: en }],
    ['link', { rel: 'alternate', hreflang: 'x-default', href: ru }]
  ]
}

export default defineConfig({
  title: "Rawrter Docs",
  description: "Официальная документация платформы Rawrter",
  // Сайт смонтирован под https://rawrter.com/docs/ — все ассеты и навигация
  // VitePress префиксятся base, иначе внутренние ссылки уводят на апекс-домен.
  base: '/docs/',
  cleanUrls: true,
  // Генерирует /docs/sitemap.xml. Без него весь раздел документации не заявлен нигде:
  // динамический sitemap WebApp знает про /docs ровно один URL.
  // hostname обязан включать base — VitePress склеивает его с маршрутом как есть и `base`
  // сам не подмешивает; с голым origin в sitemap уезжают URL без /docs/, то есть 404.
  sitemap: { hostname: 'https://rawrter.com/docs/' },
  // Веб-аналитика. Документация — отдельный контейнер за тем же доменом, layout WebApp
  // сюда не доезжает, поэтому счётчик подключается здесь; без него весь раздел /docs
  // невидим в GA4, хотя это самая «намеренческая» часть трафика.
  head: [
    // Consent Mode обязан встать ДО gtag.js: после загрузки тега default уже не применить.
    // Согласие лежит в cookie orr_consent (Path=/, HttpOnly=false — читается тем же
    // origin'ом), её выставляет баннер WebApp. Своего баннера у документации нет и заводить
    // второй не надо: посетитель, пришедший сразу на /docs, остаётся в denied и считается
    // безкуковыми пингами — осознанный размен.
    ['script', {}, `window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments);}` +
      `gtag('consent','default',{analytics_storage:document.cookie.split('; ').indexOf('orr_consent=1')!==-1?'granted':'denied'});`],
    ['script', { async: '', src: `https://www.googletagmanager.com/gtag/js?id=${GOOGLE_TAG_ID}` }],
    // Переходы по внутренним ссылкам VitePress — это history.pushState, а не перезагрузка.
    // Их считает Enhanced Measurement («Page changes based on browser history events»,
    // включён у потока по умолчанию); ручной page_view здесь не шлём — он дал бы дубль.
    ['script', {}, `gtag('js',new Date());gtag('config','${GOOGLE_TAG_ID}');`]
  ],
  // Per-page SEO: canonical + Open Graph/Twitter. Возвращаемые HeadConfig[] mergeHead'ом
  // добавляются к уже собранному head (не перезаписывают его).
  transformHead({ page, title, description }) {
    const canonical = canonicalUrl(page)
    return [
      ['link', { rel: 'canonical', href: canonical }],
      ['meta', { property: 'og:url', content: canonical }],
      ['meta', { property: 'og:site_name', content: 'Rawrter' }],
      ['meta', { property: 'og:type', content: 'website' }],
      ['meta', { property: 'og:title', content: title }],
      ['meta', { property: 'og:description', content: description }],
      ['meta', { property: 'og:image', content: 'https://rawrter.com/og-image.png' }],
      ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
      ['meta', { name: 'twitter:title', content: title }],
      ['meta', { name: 'twitter:description', content: description }],
      ['meta', { name: 'twitter:image', content: 'https://rawrter.com/og-image.png' }],
      ...hreflangLinks(page)
    ]
  },
  appearance: 'dark', // по умолчанию темная тема как в aitunnel
  locales: {
    root: {
      label: 'Русский',
      lang: 'ru-RU',
      themeConfig: {
        nav: [
          { text: 'В панель', link: 'https://rawrter.com/ru' },
          { text: 'Модели', link: '/guide/models' },
          { text: 'Интеграции', link: '/integrations/ide' }
        ],
        sidebar: [
          {
            text: 'Начало работы',
            items: [
              { text: 'Быстрый старт', link: '/' },
              { text: 'Список моделей', link: '/guide/models' },
              { text: 'Управление API ключами', link: '/guide/api-keys' },
              { text: 'Баланс и биллинг', link: '/guide/billing' }
            ]
          },
          {
            text: 'API',
            items: [
              { text: 'Обзор API', link: '/api/reference' },
              { text: 'Аутентификация', link: '/api/authentication' },
              { text: 'Chat Completions', link: '/api/chat-completions' },
              { text: 'Reasoning (Мышление)', link: '/api/reasoning' },
              { text: 'Function Calling & Tools', link: '/api/tools' },
              { text: 'Structured Outputs', link: '/api/structured-outputs' },
              { text: 'Обработка ошибок', link: '/api/errors' }
            ]
          },
          {
            text: 'Интеграции',
            items: [
              { text: 'IDE и редакторы', link: '/integrations/ide' },
              { text: 'UI-клиенты', link: '/integrations/ui-clients' },
              { text: 'Фреймворки и SDK', link: '/integrations/frameworks' }
            ]
          },
          {
            // Юридические документы живут только в WebApp (/legal/*): там их рендерит
            // LegalDocumentService из docs/mandatory/*.md с подстановкой плейсхолдеров RWT_*.
            // Копии в docs-site отставали по содержанию (не было пунктов про «Яндекс Пэй»)
            // и показывали сырые {{RWT_SERVICE_NAME}} — заводить их здесь заново нельзя.
            text: 'Документы',
            items: [
              { text: 'Политика конфиденциальности', link: 'https://rawrter.com/ru/legal/privacy' },
              { text: 'Публичная оферта', link: 'https://rawrter.com/ru/legal/offer' }
            ]
          }
        ],
        outline: {
          label: 'Содержание страницы',
          level: [2, 3]
        },
        docFooter: {
          prev: 'Предыдущая страница',
          next: 'Следующая страница'
        }
      }
    },
    en: {
      label: 'English',
      lang: 'en-US',
      link: '/en/',
      themeConfig: {
        nav: [
          { text: 'Dashboard', link: 'https://rawrter.com/en' },
          { text: 'Models', link: '/en/guide/models' },
          { text: 'Integrations', link: '/en/integrations/ide' }
        ],
        sidebar: [
          {
            text: 'Getting Started',
            items: [
              { text: 'Quick Start', link: '/en/' },
              { text: 'Models List', link: '/en/guide/models' },
              { text: 'API Keys Management', link: '/en/guide/api-keys' },
              { text: 'Balance and Billing', link: '/en/guide/billing' }
            ]
          },
          {
            text: 'API',
            items: [
              { text: 'API Overview', link: '/en/api/reference' },
              { text: 'Authentication', link: '/en/api/authentication' },
              { text: 'Chat Completions', link: '/en/api/chat-completions' },
              { text: 'Reasoning', link: '/en/api/reasoning' },
              { text: 'Function Calling & Tools', link: '/en/api/tools' },
              { text: 'Structured Outputs', link: '/en/api/structured-outputs' },
              { text: 'Error Handling', link: '/en/api/errors' }
            ]
          },
          {
            text: 'Integrations',
            items: [
              { text: 'IDE & Editors', link: '/en/integrations/ide' },
              { text: 'UI Clients', link: '/en/integrations/ui-clients' },
              { text: 'Frameworks & SDKs', link: '/en/integrations/frameworks' }
            ]
          },
          {
            // See the Russian sidebar: legal documents are served by the WebApp only. They exist
            // in Russian solely (RU jurisdiction), hence the same canonical links here.
            text: 'Legal',
            items: [
              { text: 'Privacy Policy', link: 'https://rawrter.com/en/legal/privacy' },
              { text: 'Public Offer', link: 'https://rawrter.com/en/legal/offer' }
            ]
          }
        ],
        outline: {
          label: 'On this page',
          level: [2, 3]
        },
        docFooter: {
          prev: 'Previous page',
          next: 'Next page'
        }
      }
    }
  },
  themeConfig: {
    logo: '/logo.svg',
    search: {
      provider: 'local',
      options: {
        locales: {
          root: {
            translations: {
              button: {
                buttonText: 'Поиск',
                buttonAriaLabel: 'Поиск'
              },
              modal: {
                noResultsText: 'Нет результатов по запросу',
                resetButtonTitle: 'Сбросить поиск',
                footer: {
                  selectText: 'выбрать',
                  navigateText: 'перейти',
                  closeText: 'закрыть'
                }
              }
            }
          }
        }
      }
    }
  }
})