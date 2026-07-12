// MediClinic — .NET Aspire local orchestrator
// Run with: dotnet run --project src/Host/MedClinic.AppHost
// Dashboard:  https://localhost:15888
// API:        http://localhost:5000
// pgAdmin:    http://localhost:5050
// RedisInsight: http://localhost:5540
// MinIO UI:   http://localhost:9001

var builder = DistributedApplication.CreateBuilder(args);

// ── Secrets (override with user-secrets or environment variables) ─────────────
var jwtSecret  = builder.AddParameter("jwt-secret",  secret: true);
var pgPassword = builder.AddParameter("pg-password",  secret: true);

// ── PostgreSQL 16 + pgAdmin ───────────────────────────────────────────────────
var postgres = builder
    .AddPostgres("postgres", password: pgPassword)
    .WithImage("postgres", "16-alpine")
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(5050))
    .WithDataVolume("mediclinic-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);

var db = postgres.AddDatabase("mediclinic");

// ── Valkey (Redis-compatible) + RedisInsight ──────────────────────────────────
var valkey = builder
    .AddRedis("valkey")        // Aspire.Hosting.Redis — works with Valkey image
    .WithImage("valkey/valkey", "8-alpine")
    .WithRedisInsight(insight => insight.WithHostPort(5540))
    .WithDataVolume("mediclinic-valkey-data")
    .WithLifetime(ContainerLifetime.Persistent);

// ── MinIO (S3-compatible object storage) ─────────────────────────────────────
var minio = builder
    .AddContainer("minio", "minio/minio", "latest")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin123")
    .WithBindMount("mediclinic-minio-data", "/data")
    .WithLifetime(ContainerLifetime.Persistent);

// ── DbMigrator — one-shot, runs before API starts ────────────────────────────
var migrator = builder
    .AddProject<Projects.MedClinic_DbMigrator>("migrator")
    .WithReference(db)
    .WithEnvironment("Jwt__Secret", jwtSecret)
    .WaitFor(db);

// ── DemoSeeder — one-shot, runs after migrations ──────────────────────────────
var seeder = builder
    .AddProject<Projects.MedClinic_DemoSeeder>("seeder")
    .WithReference(db)
    .WaitForCompletion(migrator);

// ── API ───────────────────────────────────────────────────────────────────────
builder
    .AddProject<Projects.MedClinic_Api>("api")
    .WithReference(db)
    .WithReference(valkey)
    .WithEnvironment("Jwt__Secret", jwtSecret)
    .WithEnvironment("Storage__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("Storage__AccessKey", "minioadmin")
    .WithEnvironment("Storage__SecretKey", "minioadmin123")
    .WithEnvironment("Storage__BucketName", "mediclinic")
    .WithExternalHttpEndpoints()
    .WaitForCompletion(migrator);

// ── React apps (placeholders — will be wired once implemented) ────────────────
// builder.AddNpmApp("portal-doctor",  "../../apps/portal-doctor",  "dev")
//     .WithReference(api).WaitFor(api);
// builder.AddNpmApp("portal-patient", "../../apps/portal-patient", "dev")
//     .WithReference(api).WaitFor(api);

builder.Build().Run();
