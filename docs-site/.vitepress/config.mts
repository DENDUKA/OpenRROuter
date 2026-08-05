import { defineConfig } from 'vitepress'

export default defineConfig({
  title: "OpenRussiaRouter Docs",
  description: "Официальная документация платформы OpenRussiaRouter",
  // Сайт смонтирован под https://openrussiarouter.com/docs/ — все ассеты и навигация
  // VitePress префиксятся base, иначе внутренние ссылки уводят на апекс-домен.
  base: '/docs/',
  cleanUrls: true,
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