FROM node:26.12.0-alpine3.24 AS build
WORKDIR /repo/src/FieldService.Notifications
COPY src/FieldService.Notifications/package.json src/FieldService.Notifications/package-lock.json src/FieldService.Notifications/.npmrc ./
RUN npm ci --ignore-scripts
COPY src/FieldService.Notifications/ ./
RUN npm run build

FROM node:26.12.0-alpine3.24 AS runtime
ENV NODE_ENV=production
WORKDIR /app
COPY src/FieldService.Notifications/package.json src/FieldService.Notifications/package-lock.json src/FieldService.Notifications/.npmrc ./
RUN npm ci --omit=dev --ignore-scripts && rm -rf /root/.npm /tmp/node-compile-cache && mkdir -p /app/logs && chown node:node /app/logs
COPY --from=build /repo/src/FieldService.Notifications/dist ./dist
USER node
EXPOSE 5090
CMD ["node", "dist/main.js"]
