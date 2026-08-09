import { defineConfig } from 'vitepress'

// Канонический адрес для каждой страницы /docs/ строится от сырого пути
// (guide/models.md -> https://openrussiarouter.com/docs/guide/models).
// og-image тот же бренд-баннер, что на основном сайте.
function canonicalUrl(page: string): string {
  const isIndex = page.replace(/\.md$/, '').match(/(^|\/)index$/) !== null
  const rel = page.replace(/\.md$/, '').replace(/\/?index$/, '')
  const path = rel === '' ? '' : `/${rel}`
  return `https://openrussiarouter.com/docs${path}${isIndex ? '/' : ''}`
}

export default defineConfig({
  title: "OpenRussiaRouter Docs",
  description: "Официальная документация платформы OpenRussiaRouter",
  // Сайт смонтирован под https://openrussiarouter.com/docs/ — все ассеты и навигация
  // VitePress префиксятся base, иначе внутренние ссылки уводят на апекс-домен.
  base: '/docs/',
  cleanUrls: true,
  // Per-page SEO: canonical + Open Graph/Twitter. Возвращаемые HeadConfig[] mergeHead'ом
  // добавляются к уже собранному head (не перезаписывают его).
  transformHead({ page, title, description }) {
    const canonical = canonicalUrl(page)
    return [
      ['link', { rel: 'canonical', href: canonical }],
      ['meta', { property: 'og:url', content: canonical }],
      ['meta', { property: 'og:site_name', content: 'OpenRussiaRouter' }],
      ['meta', { property: 'og:type', content: 'website' }],
      ['meta', { property: 'og:title', content: title }],
      ['meta', { property: 'og:description', content: description }],
      ['meta', { property: 'og:image', content: 'https://openrussiarouter.com/og-image.png' }],
      ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
      ['meta', { name: 'twitter:title', content: title }],
      ['meta', { name: 'twitter:description', content: description }],
      ['meta', { name: 'twitter:image', content: 'https://openrussiarouter.com/og-image.png' }]
    ]
  },
  appearance: 'dark', // по умолчанию темная тема как в aitunnel
  locales: {
    root: {
      label: 'Русский',
      lang: 'ru-RU',
      themeConfig: {
        nav: [
          { text: 'В панель', link: 'https://openrussiarouter.com' },
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
            text: 'Документы',
            items: [
              { text: 'Политика конфиденциальности', link: '/legal/privacy-policy' },
              { text: 'Публичная оферта', link: '/legal/public-offer' }
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
          { text: 'Dashboard', link: 'https://openrussiarouter.com' },
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
            text: 'Legal',
            items: [
              { text: 'Privacy Policy', link: '/en/legal/privacy-policy' },
              { text: 'Public Offer', link: '/en/legal/public-offer' }
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