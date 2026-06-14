# VillageOS managed-microservice example — Node.js + TypeScript.
# Zero runtime dependencies, so the final image carries only the compiled JS.
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json tsconfig.json ./
RUN npm install --no-audit --no-fund
COPY src ./src
RUN npm run build

FROM node:22-alpine
WORKDIR /app
COPY --from=build /app/dist ./dist
# Mycelium launches handlers as local daemons; this binds localhost like the
# .NET examples. For containerized runs, bind 0.0.0.0 and use host networking.
ENTRYPOINT ["node", "dist/index.js"]
