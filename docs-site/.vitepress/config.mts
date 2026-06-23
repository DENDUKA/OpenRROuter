import { defineConfig } from 'vitepress'

export default defineConfig({
  title: "OpenRussiaRouter Docs",
  description: "Официальная документация платформы OpenRussiaRouter",
  lang: 'ru-RU',
  cleanUrls: true,
  appearance: 'dark', // по умолчанию темная тема как в aitunnel
  themeConfig: {
    logo: '/logo.svg',
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
    },
    search: {
      provider: 'local',
      options: {
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
})