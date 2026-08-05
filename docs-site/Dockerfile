# Build stage — VitePress статика. base='/docs/' см. ./.vitepress/config.mts.
FROM node:20-alpine AS builder
WORKDIR /app
COPY package.json ./
# На node:20-alpine + npm@10 симлинки в node_modules/.bin иногда не получают exec-бит
# (.EXIT 126 «vitepress: Permission denied»). Запускаем vitepress напрямую через node,
# минуя npm-bin shim — это надёжнее, чем chmod/glob по симлинкам.
RUN npm install --unsafe-perm
COPY . .
RUN node node_modules/vitepress/bin/vitepress.js build

# Serve stage — nginx отдаёт собранный сайт под /docs/ (см. nginx.conf).
FROM nginx:alpine
COPY --from=builder /app/.vitepress/dist /usr/share/nginx/html/docs
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]