import { defineConfig } from 'vitepress'

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
          { text: 'В панель', link: 'https://rawrter.com' },
          { text: 'Модели', link: '/guide/models' }
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
              { text: 'Обработка ошибок', link: '/api/errors' }
            ]
          },
          {
            // Юридические документы живут только в WebApp (/legal/*): там их рендерит
            // LegalDocumentService из docs/mandatory/*.md с подстановкой плейсхолдеров RWT_*.
            // Копии в docs-site отставали по содержанию (не было пунктов про «Яндекс Пэй»)
            // и показывали сырые {{RWT_SERVICE_NAME}} — заводить их здесь заново нельзя.
            text: 'Документы',
            items: [
              { text: 'Политика конфиденциальности', link: 'https://rawrter.com/legal/privacy' },
              { text: 'Публичная оферта', link: 'https://rawrter.com/legal/offer' }
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
          { text: 'Dashboard', link: 'https://rawrter.com' },
          { text: 'Models', link: '/en/guide/models' }
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
              { text: 'Error Handling', link: '/en/api/errors' }
            ]
          },
          {
            // See the Russian sidebar: legal documents are served by the WebApp only. They exist
            // in Russian solely (RU jurisdiction), hence the same canonical links here.
            text: 'Legal',
            items: [
              { text: 'Privacy Policy', link: 'https://rawrter.com/legal/privacy' },
              { text: 'Public Offer', link: 'https://rawrter.com/legal/offer' }
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