# User Guide — Zeiss Work Platform API

Welcome! This guide explains how to access and interact with the Zeiss Work Platform API.

---

## Environments

| Environment | Base URL | Deployment |
|-------------|----------|------------|
| **Dev** | `https://ca-zeiss-work-dev.wittymeadow-e54dc6a8.eastus.azurecontainerapps.io` | Auto-deploys on every push to `main` |
| **Prod** | Deployed after manual approval in GitHub Actions | Requires reviewer sign-off |

---

## Your Access

| Detail | Value |
|--------|-------|
| **Your Account** | `yash042@gmail.com` |
| **Access Level** | API access (public HTTPS, no auth required) |

> The API is publicly accessible over HTTPS — no login required to call the endpoints. Anyone with the URL can read and submit work items.

---

## API Base URL (Dev — Live Now)

```
https://ca-zeiss-work-dev.wittymeadow-e54dc6a8.eastus.azurecontainerapps.io
```

---

## Endpoints

### 1. Health Check

```bash
BASE="https://ca-zeiss-work-dev.wittymeadow-e54dc6a8.eastus.azurecontainerapps.io"

# Liveness — is the app running?
curl $BASE/health/live

# Readiness — is Service Bus connected?
curl $BASE/health/ready
```

**Expected response**: `200 OK` with `Healthy`

---

### 2. Create a Work Item

```bash
BASE="https://ca-zeiss-work-dev.wittymeadow-e54dc6a8.eastus.azurecontainerapps.io"

curl -X POST $BASE/api/work \
  -H "Content-Type: application/json" \
  -d '{"description": "My first work item"}'
```

**Expected response** (`202 Accepted`):
```json
{
  "id": "e7767131",
  "description": "My first work item",
  "status": "Queued",
  "createdAt": "2026-08-12T14:38:47.4257988Z",
  "processedAt": null
}
```

**What happens behind the scenes**:
1. The API stores the work item in memory
2. A message is enqueued to Azure Service Bus (`work-queue`)
3. The background worker picks it up within ~1 second and processes it
4. The item's status changes from `Queued` → `Processed`

---

### 3. Get All Work Items

```bash
BASE="https://ca-zeiss-work-dev.wittymeadow-e54dc6a8.eastus.azurecontainerapps.io"

curl $BASE/api/work
```

**Expected response** (`200 OK`):
```json
[
  {
    "id": "e7767131",
    "description": "My first work item",
    "status": "Processed",
    "createdAt": "2026-08-12T14:38:47.4257988Z",
    "processedAt": "2026-08-12T14:38:48.1459568Z"
  }
]
```

---

### 4. Get a Single Work Item

```bash
BASE="https://ca-zeiss-work-dev.wittymeadow-e54dc6a8.eastus.azurecontainerapps.io"

curl $BASE/api/work/e7767131
```

**Expected response** (`200 OK`): The single work item JSON.

Returns `404 Not Found` if the ID doesn't exist.

> **Note:** The root path `/` returns `404` — this is expected. The app has no browser UI; use the endpoints above.

---

## What You Can View in Azure Portal

After signing in to [portal.azure.com](https://portal.azure.com):

### 1. Resource Group Overview
Navigate to resource group `rg-zeiss-work` to see all deployed Azure resources:
- Container Apps Environment + Container App (`ca-zeiss-work-dev`)
- Service Bus namespace `sb-hz4xcf2fyukki` + queue `work-queue`
- Key Vault (stores `ServiceBusConnectionString` secret)
- Application Insights + Log Analytics Workspace
- User-Assigned Managed Identity (`id-zeiss-work-dev`)

### 2. Application Insights
Go to **Application Insights** → **Live Metrics** to see real-time:
- Request rates and response times
- Dependency calls (Service Bus)
- Exceptions and failures

### 3. Container App Logs
Go to **Container App** → **Log stream** to see live application logs, or use **Log Analytics** to query:
```kusto
ContainerAppConsoleLogs_CL
| where ContainerAppName_s == "ca-zeiss-work-dev"
| order by TimeGenerated desc
| take 50
```

### 4. Service Bus Queue Metrics
Go to **Service Bus** → **Queues** → `work-queue` → **Metrics** to see:
- Active message count
- Incoming / outgoing messages
- Dead-letter queue depth

---

## Quick Test Script

Run all endpoints in sequence:

```bash
BASE="https://ca-zeiss-work-dev.wittymeadow-e54dc6a8.eastus.azurecontainerapps.io"

echo "--- Health Check ---"
curl -s $BASE/health/ready
echo

echo "--- Create Work Item 1 ---"
curl -s -X POST $BASE/api/work \
  -H "Content-Type: application/json" \
  -d '{"description": "Review infrastructure code"}' | python3 -m json.tool
echo

echo "--- Create Work Item 2 ---"
curl -s -X POST $BASE/api/work \
  -H "Content-Type: application/json" \
  -d '{"description": "Check observability setup"}' | python3 -m json.tool
echo

echo "--- Wait for processing ---"
sleep 3

echo "--- Get All Items ---"
curl -s $BASE/api/work | python3 -m json.tool
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| First request is slow (~5-10s) | Container scales to zero when idle (`minReplicas=0`). Wait for cold start and retry |
| Items stay `Queued` | Background worker may be restarting. Check Container App logs |
| `404` on root `/` | Expected — the app has no root route. Use `/api/work` or `/health/live` |
| `404` on `/api/work/{id}` | The ID doesn't exist, or the container restarted (items are in-memory only) |
| `503` or timeout | Container App is scaling up. Retry after ~10 seconds |
