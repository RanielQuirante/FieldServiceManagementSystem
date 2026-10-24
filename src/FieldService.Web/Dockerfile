FROM node:26.12.0-alpine3.24 AS build
WORKDIR /repo/src/FieldService.Web
COPY src/FieldService.Web/package.json src/FieldService.Web/package-lock.json src/FieldService.Web/.npmrc ./
RUN npm ci --ignore-scripts
COPY docs/contracts/openapi.v1.json /repo/docs/contracts/openapi.v1.json
COPY src/FieldService.Web/ ./
RUN npm run generate:api && npm run build-only

FROM nginxinc/nginx-unprivileged:1.30.6-alpine3.24 AS runtime
COPY src/FieldService.Web/nginx/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /repo/src/FieldService.Web/dist /usr/share/nginx/html
EXPOSE 8080
