# Zeiss Work Platform — Cloud-Native Service on Azure

A cloud-native work processing platform built for the **Carl Zeiss Vision SRE Assessment**. The system exposes a REST API that enqueues work items to Azure Service Bus, processes them asynchronously via a background worker, and returns results — all running on Azure Container Apps with zero-secret infrastructure.

---

## Architecture

```
                                ┌──────────────────────────────────────────────────────┐
                                │              Azure Resource Group                     │
                                │                                                      │
┌──────────┐  POST /api/work    │  ┌─────────────────────────────────┐                 │
│          │ ──────────────────► │  │   Azure Container Apps          │                 │
│  Client  │                    │  │   (.NET 8 Minimal API)           │                 │
│          │ ◄────────────────── │  │                                 │                 │
└──────────┘  GET /api/work     │  │   ┌───────────┐ ┌────────────┐  │                 │
                                │  │   │ API Layer │ │ Background │  │                 │
                                │  │   │           │ │ Worker     │  │                 │
                                │  │   └─────┬─────┘ └─────┬──────┘  │                 │
                                │  │         │             │          │                 │
                                │  │   System-Assigned Managed Identity                │
                                │  └─────────┼─────────────┼──────────┘                │
                                │            │             │                            │
                                │     ┌──────▼──────┐ ┌────▼────────┐                  │
                                │     │ Service Bus │ │  Key Vault  │                  │
                                │     │ (Basic)     │ │  (secrets)  │                  │
                                │     │             │ └─────────────┘                  │
                                │     │ work-queue  │                                  │
                                │     └─────────────┘  ┌──────────────────┐            │
                                │                      │ Application      │            │
                                │                      │ Insights         │            │
                                │                      │ + Log Analytics  │            │
                                │                      └──────────────────┘            │
                                └──────────────────────────────────────────────────────┘

                                ┌──────────────────────────────────────────────────────┐
                                │              GitHub                                   │
                                │  ┌──────────────┐    ┌──────────────────┐             │
                                │  │ GitHub       │    │ GitHub Container │             │
                                │  │ Actions      │───►│ Registry (ghcr)  │             │
                                │  │ (CI/CD)      │    │ (free)           │             │
                                │  └──────────────┘    └──────────────────┘             │
                                └──────────────────────────────────────────────────────┘
```

### Request Flow

1. **POST /api/work** → API creates a `WorkItem`, stores it in-memory, enqueues to Service Bus
2. **Service Bus** → Message sits in `work-queue` until consumed
3. **Background Worker** (hosted service in same container) → Dequeues, simulates processing, marks item as `Processed`
4. **GET /api/work** → Returns all work items with their current status

---

## Azure Services Used (6 Services — $0/month)

| Service | SKU | Purpose | Cost |
|---------|-----|---------|------|
| **Azure Container Apps** | Consumption | Hosts API + worker | Free (180K vCPU-sec/mo free grant) |
| **Azure Service Bus** | Basic | Async message queue | ~$0.05/million ops |
| **Azure Key Vault** | Standard | Stores Service Bus connection string | ~$0.03/10K ops |
| **Application Insights** | Pay-as-you-go | Distributed tracing, metrics | Free (5GB/mo) |
| **Log Analytics Workspace** | Pay-as-you-go | Log aggregation for Container Apps | Free (5GB/mo) |
| **Managed Identity** | System-assigned | Passwordless auth to Service Bus & Key Vault | Free |

> **Container Registry**: Using **GitHub Container Registry (ghcr.io)** — free and natively integrated with GitHub Actions. The assessment permits "Docker Registry" as an alternative to ACR.

---

## Project Structure

```
├── README.md                        ← You are here (architecture & deployment)
├── USER-GUIDE.md                    ← Reviewer guide (how to access & test the API)
├── src/
│   └── WorkApi/
│       ├── WorkApi.csproj           ← .NET 8 project
│       ├── Program.cs               ← API endpoints + DI setup
│       ├── Dockerfile               ← Multi-stage Alpine build
│       ├── appsettings.json
│       ├── Models/
│       │   └── WorkItem.cs          ← Domain model
│       └── Services/
│           ├── WorkItemStore.cs     ← Thread-safe in-memory store
│           ├── ServiceBusPublisher.cs  ← Enqueue messages
│           ├── ServiceBusWorker.cs     ← Background processor
│           └── ServiceBusHealthCheck.cs ← Readiness probe
├── infra/
│   ├── main.bicep                   ← Orchestrator module
│   ├── modules/
│   │   ├── container-apps.bicep     ← Container Apps Environment + App
│   │   ├── service-bus.bicep        ← Namespace + Queue
│   │   ├── key-vault.bicep          ← Vault + secrets
│   │   ├── monitoring.bicep         ← Log Analytics + App Insights
│   │   └── rbac.bicep               ← Role assignments for Managed Identity
│   └── parameters/
│       ├── dev.bicepparam
│       └── prod.bicepparam
└── .github/
    └── workflows/
        └── ci-cd.yml                ← Build → Push → Deploy (dev/prod)
```

---

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| **.NET 8 Minimal API** | Assessment prefers .NET; minimal API is concise and fast |
| **Single container (API + Worker)** | Cost-efficient for demo; worker runs as `BackgroundService` in the same process |
| **ghcr.io over ACR** | Zero cost; ACR Basic = $5/mo. Assessment allows "Docker Registry" |
| **Bicep over Terraform** | Native Azure tooling, no state file management, first-class VS Code support |
| **Service Bus Basic** | Cheapest tier that supports queues; sufficient for demo workload |
| **In-memory store** | Simple, no database cost. For production, swap to Cosmos DB or SQL |
| **Managed Identity** | Zero secrets in code/config — passwordless auth to Service Bus & Key Vault |
| **minReplicas: 0** | Scale to zero when idle — no cost during inactivity |
| **Alpine Docker image** | Smallest .NET base image (~100MB vs 210MB for Debian) |
| **GitHub Actions** | Free for public repos, native OIDC federation with Azure |

---

## Deployment Steps

### Prerequisites

- Azure subscription with **Contributor** access
- GitHub repository (public or private)
- Azure CLI (`az`) installed locally

### 1. Create Azure Resources for CI/CD

```bash
# Set variables
SUBSCRIPTION_ID="<your-subscription-id>"
RESOURCE_GROUP="rg-zeiss-work-dev"
LOCATION="westeurope"

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create Azure AD app registration for GitHub Actions OIDC
az ad app create --display-name "github-zeiss-workapi"
APP_ID=$(az ad app list --display-name "github-zeiss-workapi" --query "[0].appId" -o tsv)

# Create service principal
az ad sp create --id $APP_ID
SP_OBJECT_ID=$(az ad sp show --id $APP_ID --query "id" -o tsv)

# Assign Contributor on resource group
az role assignment create \
  --assignee-object-id $SP_OBJECT_ID \
  --assignee-principal-type ServicePrincipal \
  --role Contributor \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"

# Create federated credential for GitHub Actions
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<GITHUB_ORG>/<REPO_NAME>:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

### 2. Configure GitHub Secrets

| Secret | Value |
|--------|-------|
| `AZURE_CLIENT_ID` | App registration Application (client) ID |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `GHCR_PAT` | GitHub PAT with `read:packages` scope |

| Variable | Value |
|----------|-------|
| `AZURE_RESOURCE_GROUP` | `rg-zeiss-work-dev` (dev) / `rg-zeiss-work-prod` (prod) |

### 3. Push & Deploy

```bash
git push origin main
# GitHub Actions will: build → push image to ghcr.io → deploy infra → deploy app
```

### 4. Verify

```bash
# Get the Container App URL from pipeline output or:
APP_URL=$(az containerapp show -n ca-zeiss-work-dev -g $RESOURCE_GROUP --query "properties.configuration.ingress.fqdn" -o tsv)

# Test endpoints
curl https://$APP_URL/health/live
curl -X POST https://$APP_URL/api/work -H "Content-Type: application/json" -d '{"description":"Test item"}'
curl https://$APP_URL/api/work
```

---

## CI/CD Pipeline

```
┌─────────┐     ┌──────────────────┐     ┌──────────────────┐
│  Push   │────►│   Build Stage    │────►│   Deploy Dev     │
│  main   │     │                  │     │                  │
└─────────┘     │  • dotnet build  │     │  • Bicep deploy  │
                │  • Docker build  │     │  • Container App │
                │  • Push to ghcr  │     │    update        │
                └──────────────────┘     └────────┬─────────┘
                                                  │
                                         ┌────────▼─────────┐
                                         │   Deploy Prod    │
                                         │  (env approval)  │
                                         │                  │
                                         │  • Bicep deploy  │
                                         │  • Container App │
                                         │    update        │
                                         └──────────────────┘
```

- **Build**: Runs on every push & PR — restore, build, Docker build+push
- **Deploy Dev**: Automatic on `main` push
- **Deploy Prod**: Requires manual approval via GitHub Environment protection rules

---

## Security

- **No secrets in code or YAML** — all authentication uses Managed Identity or GitHub OIDC
- **Key Vault** stores Service Bus connection string (accessed via Managed Identity)
- **RBAC authorization** on Key Vault (no access policies)
- **Container runs as non-root** user (`appuser` in Dockerfile)
- **GitHub OIDC** federation — no long-lived Azure credentials stored in GitHub

---

## Observability

- **Application Insights** — request traces, dependency tracking, exceptions
- **Log Analytics** — container logs, Service Bus diagnostics
- **Health probes**:
  - `GET /health/live` — liveness (always 200)
  - `GET /health/ready` — readiness (validates Service Bus connectivity)
- **Structured logging** via `ILogger` with correlation IDs

---

## Known Limitations

1. **In-memory store** — work items are lost on container restart. For production, add Azure Cosmos DB (free tier: 1000 RU/s) or Azure SQL
2. **Single container** — API and worker share the same process. For high throughput, split into separate Container Apps with KEDA scaling on Service Bus queue depth
3. **Service Bus Basic** — no topics/subscriptions, no sessions. Upgrade to Standard if needed
4. **No authentication on API** — for production, add Azure AD / API key validation
5. **Scale-to-zero** — first request after idle has ~5-10s cold start

---

## Extending This Solution

| Enhancement | How |
|-------------|-----|
| Persistent storage | Add Cosmos DB free tier (1000 RU/s, 25GB) |
| API authentication | Azure AD B2C or API Management |
| Separate worker scaling | Deploy worker as separate Container App with KEDA Service Bus scaler |
| Multi-region | Add Traffic Manager + geo-replicated Service Bus Premium |
| Cost alerting | Azure Budget alerts on resource group |