# VillageOS managed-microservice example — Rust + Axum.
FROM rust:1-slim AS build
WORKDIR /src
COPY Cargo.toml ./
COPY src ./src
RUN cargo build --release

FROM debian:stable-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /src/target/release/vos-managedmicroservice-rust /app
# Mycelium launches handlers as local daemons; this binds 127.0.0.1 like the
# .NET examples. For containerized runs bind 0.0.0.0 and use host networking.
ENTRYPOINT ["/app"]
