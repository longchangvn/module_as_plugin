# syntax=docker/dockerfile:1
FROM node:20-alpine AS build
WORKDIR /app
COPY apps/shell/package.json apps/shell/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY apps/shell/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
# Reverse-proxy /api/v1 to the gateway (same-origin shell, no CORS).
COPY apps/shell/nginx.conf /etc/nginx/conf.d/default.conf
