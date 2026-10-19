# syntax=docker/dockerfile:1
# Module Federation remote (:5174) — serves remoteEntry.js + assets.
FROM node:20-alpine AS build
WORKDIR /app
COPY modules/loyalty/web/package.json modules/loyalty/web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY modules/loyalty/web/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
