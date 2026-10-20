# syntax=docker/dockerfile:1
# Module Federation remote (:__WEB_PORT__) — serves remoteEntry.js + assets.
FROM node:20-alpine AS build
WORKDIR /app
COPY modules/__MODULE_NAME__/web/package.json modules/__MODULE_NAME__/web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY modules/__MODULE_NAME__/web/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
