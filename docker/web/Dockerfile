FROM node:22-alpine AS build
WORKDIR /app

COPY web/package.json web/package-lock.json ./
RUN npm install --prefer-offline

COPY web/ .
# API URL is injected at build time so the SPA can reach the backend.
ARG VITE_API_BASE_URL=http://localhost:8080
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
# Optional dev-only admin JWT for the manual test stack (empty in production).
ARG VITE_DEV_ADMIN_TOKEN=""
ENV VITE_DEV_ADMIN_TOKEN=$VITE_DEV_ADMIN_TOKEN
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY docker/web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
