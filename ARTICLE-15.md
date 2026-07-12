# Part 15 — Cloud-Native & DevOps: Aspire, Docker Compose, Terraform, and GHCR

> **Series:** Building an AI-Native Medical Clinic SaaS — Reference Implementation
> **Part 14 recap:** RFC 9457 ProblemDetails, API versioning, HybridCache on Valkey, idempotency middleware, JWT refresh tokens, impersonation, MinIO storage, MailKit email.

---

## What we're building

Our modular monolith is feature-complete for the core domain. In this part we make it *operationally complete*:

| Goal | Mechanism |
|---|---|
| One-command local stack | .NET Aspire 9 AppHost |
| Reproducible production deploy | Docker Compose |
| AWS cloud infrastructure | Terraform (modular) |
| Automated image publishing | GitHub Actions → GHCR |
| `dotnet new` module scaffolding | Template manifest |

By the end, a developer can clone the repo and run `dotnet run --project src/Host/MedClinic.AppHost` to spin up Postgres + pgAdmin, Valkey + RedisInsight, MinIO, run migrations, seed demo data, and start the API — zero manual setup.

---

## 1. .NET Aspire local orchestration

### Why Aspire?

Before Aspire, every developer had a slightly different `docker-compose.yml`, `.env` file, and startup order they'd cobbled together. Someone forgot to run migrations; someone else had the wrong Postgres version. Aspire solves this by making the *entire* startup sequence a C# program — typed, refactorable, and testable.

### Project setup (.NET 10 + NuGet-only Aspire)

.NET 10's SDK deprecated the Aspire workload. The new approach uses the `Aspire.AppHost.Sdk` directly in the project file:

```xml
<!-- src/Host/MedClinic.AppHost/MedClinic.AppHost.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <Sdk Name="Aspire.AppHost.Sdk" Version="9.3.0" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <UserSecretsId>mediclinic-apphost</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="9.3.0" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" Version="9.3.0" />
    <PackageReference Include="Aspire.Hosting.Redis" Version="9.3.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MedClinic.Api\MedClinic.Api.csproj" />
    <ProjectReference Include="..\MedClinic.DbMigrator\MedClinic.DbMigrator.csproj" />
    <ProjectReference Include="..\MedClinic.DemoSeeder\MedClinic.DemoSeeder.csproj" />
  </ItemGroup>

</Project>
```

> **Key difference from older docs:** `IsAspireHost=true` triggers a deprecation error in .NET 10 SDK (`NETSDK1228`). Use `<Sdk Name="Aspire.AppHost.Sdk" Version="9.3.0" />` instead — this is the workload-free, NuGet-only path.

### The orchestration program

```csharp
// src/Host/MedClinic.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var jwtSecret  = builder.AddParameter("jwt-secret",  secret: true);
var pgPassword = builder.AddParameter("pg-password",  secret: true);

// PostgreSQL 16 + pgAdmin on :5050
var postgres = builder
    .AddPostgres("postgres", password: pgPassword)
    .WithImage("postgres", "16-alpine")
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(5050))
    .WithDataVolume("mediclinic-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);

var db = postgres.AddDatabase("mediclinic");

// Valkey (Redis-compatible) + RedisInsight on :5540
var valkey = builder
    .AddRedis("valkey")
    .WithImage("valkey/valkey", "8-alpine")
    .WithRedisInsight(insight => insight.WithHostPort(5540))
    .WithDataVolume("mediclinic-valkey-data")
    .WithLifetime(ContainerLifetime.Persistent);

// MinIO object storage
var minio = builder
    .AddContainer("minio", "minio/minio", "latest")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithEnvironment("MINIO_ROOT_USER",     "minioadmin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin123")
    .WithBindMount("mediclinic-minio-data", "/data")
    .WithLifetime(ContainerLifetime.Persistent);

// One-shot migrator — runs before API starts
var migrator = builder
    .AddProject<Projects.MedClinic_DbMigrator>("migrator")
    .WithReference(db)
    .WithEnvironment("Jwt__Secret", jwtSecret)
    .WaitFor(db);

// Demo seeder — runs once after migrations
var seeder = builder
    .AddProject<Projects.MedClinic_DemoSeeder>("seeder")
    .WithReference(db)
    .WaitForCompletion(migrator);

// API — waits for migrations, references all dependencies
builder
    .AddProject<Projects.MedClinic_Api>("api")
    .WithReference(db)
    .WithReference(valkey)
    .WithEnvironment("Jwt__Secret",          jwtSecret)
    .WithEnvironment("Storage__Endpoint",    minio.GetEndpoint("api"))
    .WithEnvironment("Storage__AccessKey",   "minioadmin")
    .WithEnvironment("Storage__SecretKey",   "minioadmin123")
    .WithEnvironment("Storage__BucketName",  "mediclinic")
    .WithExternalHttpEndpoints()
    .WaitForCompletion(migrator);

builder.Build().Run();
```

Three design decisions worth explaining:

**`WaitFor` vs `WaitForCompletion`.**
`WaitFor(db)` means "start when the container is healthy." `WaitForCompletion(migrator)` means "start when the process exited with code 0." The migrator and seeder are one-shot executables — they run, do their work, and exit. Aspire tracks the exit code and unblocks dependent resources.

**`ContainerLifetime.Persistent`.**
By default Aspire tears down containers when the AppHost exits. `Persistent` keeps Postgres, Valkey, and MinIO running across restarts. This means your data survives a code change → restart cycle, which is the right default for a database-backed app in development.

**Secrets via parameters.**
`builder.AddParameter("jwt-secret", secret: true)` reads from user-secrets in development. In CI or production you'd set `Parameters__jwt-secret` as an environment variable. No secrets in source control.

### Service defaults

The `MedClinic.ServiceDefaults` project centralises OTel, health checks, and service discovery. Every service calls `builder.AddServiceDefaults()` to get the same baseline:

```csharp
public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
{
    builder.ConfigureOpenTelemetry();
    builder.AddDefaultHealthChecks();
    builder.Services.AddServiceDiscovery();
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
    });
    return builder;
}
```

And `MapDefaultEndpoints()` exposes `/health/live` and `/health/ready` consistently across all services.

### Demo seeder

We need a reproducible demo environment. The seeder creates four users in an idempotent way — safe to run repeatedly:

```csharp
// Seeds admin, doctor, pharmacist, receptionist
// Uses StaticTenantContext to inject a fixed clinic ID
// FindByEmailAsync check makes every seed operation idempotent
```

The `StaticTenantContext` is an inner class that implements `ITenantContext` with a hardcoded `DemoClinicId = aaaaaaaa-0000-0000-0000-000000000001`. This sidesteps the HTTP-based tenant resolution that the normal API uses — the seeder has no web context.

---

## 2. Docker Compose production stack

The `deploy/docker/docker-compose.yml` mirrors the Aspire graph in YAML, with production hardening:

```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB:       mediclinic
      POSTGRES_USER:     mediclinic
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U mediclinic"]
      interval: 10s
      retries: 5

  migrator:
    image: ghcr.io/${GITHUB_REPOSITORY}/migrator:${IMAGE_TAG}
    depends_on:
      postgres: { condition: service_healthy }
    restart: "no"          # one-shot — must not restart on exit 0

  api:
    image: ghcr.io/${GITHUB_REPOSITORY}/api:${IMAGE_TAG}
    depends_on:
      migrator: { condition: service_completed_successfully }
    environment:
      Jwt__Secret:         ${JWT_SECRET}
      ConnectionStrings__DefaultConnection: >-
        Host=postgres;Database=mediclinic;Username=mediclinic;Password=${DB_PASSWORD}
```

Key points:

- **`restart: "no"` on migrator** — the migrator must run exactly once per deploy. Docker Compose's `service_completed_successfully` condition correctly chains the API to wait for a `0` exit, not just any termination.
- **Images from GHCR** — `${GITHUB_REPOSITORY}` and `${IMAGE_TAG}` come from the `.env` file, which is `.gitignore`d. Operators set these per environment.
- **Separate networks** — `backend` and `frontend` networks prevent direct database access from the public-facing tier.

---

## 3. Terraform — AWS infrastructure

The Terraform layout follows the standard module pattern:

```
deploy/terraform/
├── main.tf          # Root — wires modules together
├── variables.tf     # All inputs with defaults
├── outputs.tf       # ALB URL, RDS endpoint, etc.
└── modules/
    ├── vpc/         # VPC, subnets, IGW, NAT gateway
    ├── rds/         # RDS PostgreSQL 16, encrypted, deletion-protected
    ├── elasticache/ # ElastiCache Valkey cluster
    ├── s3/          # Encrypted, versioned, public-access-blocked bucket
    └── ecs/         # Fargate cluster, ALB, task definitions, IAM
```

### VPC layout

Two public + two private subnets across two AZs. The API runs in private subnets; the ALB sits in public subnets. A NAT gateway lets private resources reach the internet (for ECR pulls, OTel exports) without being directly reachable.

### RDS

```hcl
resource "aws_db_instance" "postgres" {
  engine_version          = "16"
  storage_encrypted       = true
  backup_retention_period = 7
  deletion_protection     = true
  skip_final_snapshot     = false
}
```

`deletion_protection = true` means Terraform cannot accidentally destroy the database. You must disable it explicitly before any `terraform destroy`. `skip_final_snapshot = false` ensures a snapshot is taken before deletion — the last-resort recovery point.

### ECS Fargate + ALB

The ECS module provisions:
1. **ALB** — HTTP listener on port 80, health-checking `/health/live`
2. **Cluster** with Container Insights enabled
3. **Two task definitions** — migrator (256 CPU / 512 MiB) and API (configurable)
4. **IAM roles** — execution role (ECR pull, CloudWatch logs) and task role (S3 access only)
5. **ECS Service** for the API — runs in private subnets, registers with the ALB target group

The migrator task definition is included but *not* run automatically by Terraform — you trigger it as a one-off task via `aws ecs run-task` during deployment. This keeps Terraform as pure infrastructure and separates the data migration concern from the compute provisioning concern.

### Sensitive variables

```hcl
variable "db_password" {
  sensitive = true    # never echoed in plan output
}

variable "jwt_secret" {
  sensitive = true
}
```

These come from environment variables (`TF_VAR_db_password`, `TF_VAR_jwt_secret`) or from a Terraform Cloud workspace. They are never committed to source control.

---

## 4. GitHub Actions — publish to GHCR

The workflow triggers on any `v*` tag push:

```yaml
on:
  push:
    tags: ["v*"]
```

Both the API and migrator images are built with BuildKit layer caching (`cache-from: type=gha`) and pushed with semantic version tags:

```
ghcr.io/org/repo/api:1.2.3
ghcr.io/org/repo/api:1.2
ghcr.io/org/repo/api:sha-abc1234
```

The `sha-` tag is what you pass to Terraform (`image_tag = "sha-abc1234"`) for a reproducible, rollback-friendly deploy.

**No secrets to configure.** `GITHUB_TOKEN` is automatically provided with `packages: write` permission — no manual setup needed.

---

## 5. `dotnet new` module template

For teams adding new modules, we ship a `dotnet new` template:

```bash
dotnet new install ./template
dotnet new mediclinic-module --ModuleName Inventory --IncludeTests true
```

The `template.json` manifest:
- Names the template `mediclinic-module` / `MediClinic Module`
- Replaces `Acme` everywhere (namespace, file names, project names) with the chosen `ModuleName`
- Optional `IncludeContracts` and `IncludeTests` booleans to exclude unused stubs
- Runs `dotnet restore` as a post-action

The template content folder (not shown — you'd add it) mirrors the Patients module as the canonical reference: `.Contracts` project, `Domain/`, `Features/`, `Persistence/`, and one integration test project.

---

## 6. Deployment workflow

Here's the full deploy sequence for a new environment:

```bash
# 1. Provision infrastructure
cd deploy/terraform
terraform init
terraform apply -var="github_repository=org/mediclinic" \
                -var="image_tag=sha-abc1234"

# 2. Run migrations (one-off ECS task)
aws ecs run-task \
  --cluster mediclinic-production \
  --task-definition mediclinic-migrator-production \
  --launch-type FARGATE \
  --network-configuration "..."

# 3. Deploy new API version
terraform apply -var="image_tag=sha-newsha"
# ECS replaces tasks with rolling update
```

For day-2 operations, only step 3 is needed — infrastructure is stable, migrations are idempotent.

---

## What we have

At the end of Part 15:

| Concern | Solution |
|---|---|
| Local dev setup | `dotnet run --project src/Host/MedClinic.AppHost` |
| Container images | Multi-stage Dockerfiles, published to GHCR on tag push |
| Production stack | Docker Compose or AWS ECS Fargate |
| Cloud infrastructure | Terraform modules (VPC, RDS, ElastiCache, S3, ECS) |
| DB migrations | One-shot migrator, never runs at API startup |
| Demo data | Idempotent DemoSeeder, tenant-isolated |
| Module scaffolding | `dotnet new mediclinic-module` |

The repository is now a complete, deployable SaaS. The next parts will build the frontend portals (doctor and patient) and extend the domain with the remaining modules.

---

## Running it yourself

```bash
git clone https://github.com/your-org/mediclinic
cd mediclinic

# Set secrets (one-time)
dotnet user-secrets set "Parameters:jwt-secret" "your-32-char-secret" \
  --project src/Host/MedClinic.AppHost
dotnet user-secrets set "Parameters:pg-password" "yourpassword" \
  --project src/Host/MedClinic.AppHost

# Start everything
dotnet run --project src/Host/MedClinic.AppHost

# Aspire dashboard: https://localhost:15888
# API:             http://localhost:5000/scalar
# pgAdmin:         http://localhost:5050
# RedisInsight:    http://localhost:5540
# MinIO:           http://localhost:9001  (minioadmin / minioadmin123)
```

Login with the seeded accounts:

| Email | Password | Role |
|---|---|---|
| admin@demo.clinic | Demo1234! | Admin |
| doctor@demo.clinic | Demo1234! | Doctor |
| pharmacist@demo.clinic | Demo1234! | Pharmacist |
| reception@demo.clinic | Demo1234! | Receptionist |
