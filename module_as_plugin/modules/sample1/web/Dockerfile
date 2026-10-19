# syntax=docker/dockerfile:1
# Module Federation remote (:5176) — serves remoteEntry.js + assets.
FROM node:20-alpine AS build
WORKDIR /app
COPY modules/sample1/web/package.json modules/sample1/web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY modules/sample1/web/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
