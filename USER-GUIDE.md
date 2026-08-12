# User Guide — Zeiss Work Platform API

Welcome! This guide explains how to access and interact with the Zeiss Work Platform API.

---

## Your Access

| Detail | Value |
|--------|-------|
| **Your Account** | `yash042@gmail.com` |
| **Access Level** | **Reader** on Azure Resource Group + API access |
| **IAM Role** | Azure RBAC `Reader` — view all resources, logs, and metrics |

> Your Gmail account has been invited as a **Guest User** in Azure Active Directory with **Reader** access to the resource group. You can view infrastructure, logs, and Application Insights — but cannot modify resources.

---

## API Base URL

```
https://ca-zeiss-work-dev.<region>.azurecontainerapps.io
```

> The exact URL will be shared separately after deployment. Replace `<region>` with the deployed region (e.g., `westeurope`).

---

## Endpoints

### 1. Health Check

```bash
# Liveness — is the app running?
curl https://<APP_URL>/health/live

# Readiness — is Service Bus connected?
curl https://<APP_URL>/health/ready
```

**Expected response**: `200 OK` with `Healthy`

---

### 2. Create a Work Item

```bash
curl -X POST https://<APP_URL>/api/work \
  -H "Content-Type: application/json" \
  -d '{"description": "My first work item"}'
```

**Expected response** (`202 Accepted`):
```json
{
  "id": "a1b2c3d4",
  "description": "My first work item",
  "status": "Queued",
  "createdAt": "2026-08-12T10:00:00Z",
  "processedAt": null
}
```

**What happens behind the scenes**:
1. The API stores the work item in memory
2. A message is enqueued to Azure Service Bus (`work-queue`)
3. The background worker picks it up and processes it asynchronously
4. The item's status changes from `Queued` → `Processed`

---

### 3. Get All Work Items

```bash
curl https://<APP_URL>/api/work
```

**Expected response** (`200 OK`):
```json
[
  {
    "id": "a1b2c3d4",
    "description": "My first work item",
    "status": "Processed",
    "createdAt": "2026-08-12T10:00:00Z",
    "processedAt": "2026-08-12T10:00:01Z"
  }
]
```

---

### 4. Get a Single Work Item

```bash
curl https://<APP_URL>/api/work/a1b2c3d4
```

**Expected response** (`200 OK`): The single work item JSON.

Returns `404 Not Found` if the ID doesn't exist.

---

## What You Can View in Azure Portal

After signing in to [portal.azure.com](https://portal.azure.com) with `yash042@gmail.com`:

### 1. Resource Group Overview
Navigate to the resource group to see all deployed Azure resources:
- Container App (running the API)
- Service Bus namespace + queue
- Key Vault (secrets)
- Application Insights
- Log Analytics Workspace

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
APP_URL="https://ca-zeiss-work-dev.<region>.azurecontainerapps.io"

echo "--- Health Check ---"
curl -s $APP_URL/health/live
echo

echo "--- Create Work Item 1 ---"
curl -s -X POST $APP_URL/api/work \
  -H "Content-Type: application/json" \
  -d '{"description": "Review infrastructure code"}'
echo

echo "--- Create Work Item 2 ---"
curl -s -X POST $APP_URL/api/work \
  -H "Content-Type: application/json" \
  -d '{"description": "Check observability setup"}'
echo

echo "--- Wait for processing ---"
sleep 3

echo "--- Get All Items ---"
curl -s $APP_URL/api/work | python3 -m json.tool
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| `502 Bad Gateway` | Container is starting up (cold start ~5-10s). Wait and retry |
| Items stay `Queued` | Background worker may be restarting. Check Container App logs |
| Can't access Azure Portal | Ensure you accepted the Azure AD guest invitation sent to `yash042@gmail.com` |
| `404` on `/api/work/{id}` | The ID doesn't exist. Items are in-memory and reset on container restart |
