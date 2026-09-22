# VillageOS managed-microservice example — Go.
# Multi-stage build producing a tiny static binary on distroless.
FROM golang:1.26-alpine AS build
WORKDIR /src
COPY go.mod main.go ./
RUN CGO_ENABLED=0 go build -trimpath -o /app .

FROM gcr.io/distroless/static-debian12
COPY --from=build /app /app
# Mycelium launches handlers as local daemons; they bind localhost like the
# .NET examples. To run containerized, bind 0.0.0.0 (edit main.go) and use
# host networking so Mycelium can reach the service.
ENTRYPOINT ["/app"]
