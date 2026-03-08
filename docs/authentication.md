# Authentication

## OAuth Flow

GenesysCloudAuditor authenticates to the Genesys Cloud API using the **Client Credentials** grant type. No user login is required — the application authenticates as a service using a Client ID and Client Secret.

```
App                          Genesys Login
─────────────────            ────────────────────────────────
POST /oauth/token
  client_id=...
  client_secret=...
  grant_type=client_credentials
                          ← { access_token, expires_in }

(token cached until expiry - safety window)

GET /api/v2/users           Genesys Cloud API
  Authorization: Bearer <token>
                          ← { entities: [...], pageCount, total }
```

---

## Setting Up OAuth Credentials

1. In **Genesys Cloud Admin → Integrations → OAuth**, create a new OAuth client.
2. Set **Grant Type** to **Client Credentials**.
3. Assign the appropriate roles to the OAuth client (see [Required Permissions](#required-permissions) below).
4. Record the **Client ID** and **Client Secret**.

---

## Required Permissions

The OAuth client must have read access to the following APIs:

| Permission | Endpoint |
|---|---|
| User read | `GET /api/v2/users` |
| Telephony / edge extension read | `GET /api/v2/telephony/providers/edges/extensions` |

Permissions are granted through Genesys Cloud **Roles** assigned to the OAuth client. The exact permission names vary by org configuration, but typically include `user:view` and `telephony:plugin:all` (or a read-scoped variant).

> If you receive a **403 Forbidden** response, verify that the OAuth client's roles include the necessary permissions and that the client is not division-scoped in a way that excludes the target users.

---

## Token Caching and Refresh

`TokenProvider` manages the token lifecycle:

- A token is requested once and cached in memory.
- Subsequent requests reuse the cached token until it is within a configurable **safety window** (default: 60 seconds) of expiration.
- On a **401 Unauthorized** response, the token is force-refreshed once and the request is retried automatically.
- Tokens are never written to disk.

---

## Rate Limiting

Genesys Cloud enforces per-client rate limits. The application handles this at two levels:

### Proactive throttling

`RateLimitHandler` uses a token-bucket algorithm to limit outbound request rate. Configure `Genesys:MaxRequestsPerSecond` in `appsettings.json` (default: 3).

### Reactive retry on 429

When a `429 Too Many Requests` response is received:

1. The `Retry-After` response header is parsed (supports both delta-seconds and HTTP-date formats).
2. The application waits for the specified duration before retrying.
3. If no `Retry-After` header is present, exponential backoff with jitter is applied.
4. Retries are bounded (maximum 6 attempts, capped at 30 seconds per wait).
5. All retry waits respect `CancellationToken`, so a user cancel will interrupt a retry wait immediately.

---

## Configuration

Credentials are never committed to source control. Use one of the following methods:

### Option 1 — .NET User Secrets (local development)

```powershell
cd src\GenesysExtensionAudit.App
dotnet user-secrets set "GenesysOAuth:ClientId"     "YOUR_CLIENT_ID"
dotnet user-secrets set "GenesysOAuth:ClientSecret" "YOUR_CLIENT_SECRET"
```

User secrets are loaded automatically when `DOTNET_ENVIRONMENT=Development`.

### Option 2 — Environment Variables (CI / packaging / production)

Use `__` as the section separator:

```powershell
setx GenesysOAuth__ClientId     "YOUR_CLIENT_ID"
setx GenesysOAuth__ClientSecret "YOUR_CLIENT_SECRET"
setx Genesys__Region            "mypurecloud.com"
```

### Option 3 — appsettings.json (non-secret settings only)

`src/GenesysExtensionAudit.App/appsettings.json` stores non-sensitive configuration:

```json
{
  "Genesys": {
    "Region": "mypurecloud.com",
    "PageSize": 100,
    "IncludeInactive": false,
    "MaxRequestsPerSecond": 3
  },
  "GenesysOAuth": {
    "ClientId": "",
    "ClientSecret": ""
  }
}
```

> **Never commit `ClientId` or `ClientSecret` to source control.** Leave them blank in `appsettings.json` and supply them via user secrets or environment variables.

---

## Regional Endpoints

The `Genesys:Region` setting controls which Genesys Cloud environment is targeted:

| Region | Value |
|---|---|
| US East (Commercial) | `mypurecloud.com` |
| US West | `usw2.pure.cloud` |
| EU West | `euw2.pure.cloud` |
| EU Frankfurt | `euc1.pure.cloud` |
| AP Southeast | `apse2.pure.cloud` |
| Canada | `cac1.pure.cloud` |

Token requests go to `https://login.{Region}/oauth/token`. API calls go to `https://api.{Region}/api/v2/...`.

---

## See Also

- [Architecture](architecture.md) — HTTP handler pipeline details
- [Deployment](deployment.md) — Environment variable configuration in CI
