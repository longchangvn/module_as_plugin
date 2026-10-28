# syntax=docker/dockerfile:1
# Module Federation remote (:5179) — serves remoteEntry.js + assets.
FROM node:20-alpine AS build
WORKDIR /app
COPY modules/inventory/web/package.json modules/inventory/web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY modules/inventory/web/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
