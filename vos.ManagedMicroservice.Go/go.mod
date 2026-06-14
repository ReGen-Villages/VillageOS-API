module villageos.example/managedmicroservice-go

// Dependency-free on purpose: the whole VillageOS managed-microservice contract
// (register over HTTP, validate an HS256 JWT, serve /handle /health /stats
// /shutdown) is expressible with the Go standard library alone. See README.md.
go 1.26
