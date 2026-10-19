# syntax=docker/dockerfile:1
# Module Federation remote (:5177) — serves remoteEntry.js + assets.
FROM node:20-alpine AS build
WORKDIR /app
COPY modules/sample2/web/package.json modules/sample2/web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY modules/sample2/web/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
