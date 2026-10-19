# syntax=docker/dockerfile:1
FROM node:20-alpine AS build
WORKDIR /app
COPY apps/tenant-admin/package.json apps/tenant-admin/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY apps/tenant-admin/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
# Reverse-proxy /api/v1 to the gateway (same-origin SPA, no CORS).
COPY apps/tenant-admin/nginx.conf /etc/nginx/conf.d/default.conf
