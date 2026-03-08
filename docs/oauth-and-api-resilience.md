# OAuth and API Resilience Design

This document defines authentication and resiliency behavior for Genesys API integration.

Audience: backend/infrastructure contributors.

Use this guide when changing token management, HTTP policies, or permission assumptions.

## Authentication Model

Primary model: OAuth client credentials.

Why:

- Supports unattended runner execution.
- Avoids user-interactive login flows in scheduled tasks.
- Aligns with service-style access control.

Alternative model (authorization code + PKCE) is intentionally out of scope for current implementation.

## Token Lifecycle Rules

Required behavior:

1. Acquire token from region-specific login endpoint.
2. Cache token until near expiry.
3. On `401`, force refresh once and retry once.
4. On repeated auth failure, surface clear error.

`403` is treated as a permissions problem, not a retryable auth-refresh event.

## Required Access

Minimum endpoint access required:

- `GET /api/v2/users`
- `GET /api/v2/telephony/providers/edges/extensions`

Additional audit paths require additional read permissions (groups, queues, flows, DIDs, audit logs, operational events, outbound events).

Exact role/scope labels vary by tenant and must be validated in the target org.

## Retry and Rate-Limit Policy

Retryable conditions:

- `429`
- `408`
- transient `5xx`
- transient network failures

Non-retryable conditions:

- `400`, `404`
- repeated `401` after forced refresh
- `403`

Backoff guidance:

- Respect `Retry-After` when present.
- Otherwise use bounded exponential backoff with jitter.

## Region and Endpoint Construction

- Auth base: `https://login.<region>`
- API base: `https://api.<region>`

`region` comes from configuration and must match the target Genesys org.

## Observability Expectations

Log these for each request:

- endpoint path
- status code
- elapsed time
- retry count (when applicable)

Do not log secrets or bearer tokens.

## Related Documents

- [setup and operations guide](setup-and-operations.md)
- [architecture guide](application-architecture.md)
- [QA matrix](detailed-qa-matrix.md)

