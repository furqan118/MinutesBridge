# OAuth broker contract

MinutesBridge is a public Windows client. It must never contain an Atlassian
client secret. An organization-controlled HTTPS broker owns the confidential
OAuth 2.0 authorization-code exchange and refresh token.

## Required scopes

- `read:space:confluence`
- `read:page:confluence`
- `write:page:confluence` (reserved for the reviewed publishing milestone)
- `offline_access` (broker only, for rotating refresh tokens)

Confluence permissions still apply. The broker must not elevate an employee
beyond the spaces and pages their Atlassian account can access.

## Desktop endpoints

### `POST /v1/oauth/atlassian/start`

Request:

```json
{"client":"MinutesBridge","version":"0.2"}
```

Response:

```json
{
  "requestId":"opaque, single-use value",
  "pollingSecret":"high-entropy secret",
  "authorizationUrl":"https://broker.example/authorize/...",
  "expiresAtUtc":"2026-09-08T16:30:00Z",
  "pollIntervalSeconds":2
}
```

### `POST /v1/oauth/atlassian/poll`

Request secrets belong in the JSON body, never the URL:

```json
{"requestId":"opaque value","pollingSecret":"high-entropy secret"}
```

Pending response:

```json
{"status":"pending"}
```

Completed response:

```json
{
  "status":"authorized",
  "accessToken":"short-lived Atlassian access token",
  "cloudId":"00000000-0000-0000-0000-000000000000",
  "siteUrl":"https://example.atlassian.net/",
  "accountDisplayName":"Employee display name",
  "expiresAtUtc":"2026-09-08T17:00:00Z"
}
```

Other terminal statuses are `declined` and `expired`.

## Broker security requirements

- TLS only, with HSTS and an organization-managed certificate.
- OAuth client secret and rotating refresh tokens in a managed secret store;
  each refresh-token exchange atomically replaces the prior token.
- Authorization request state is high entropy, single use, short lived, and
  bound to the initiating polling secret.
- Poll requests are rate limited; start requests have per-user and per-device
  limits; both have bounded JSON bodies.
- Browser callback uses exact redirect URI matching and rejects replay.
- Access tokens returned to the desktop are short lived. Refresh tokens never
  leave the broker.
- Logs redact tokens, authorization codes, polling secrets, cookies, and
  authorization headers. Meeting content never passes through this broker.
- CORS is disabled unless a separately reviewed browser client requires it.
- Production deployment uses workload identity where possible, dependency and
  secret scanning, audit events for authorization lifecycle changes, and a
  tested token-revocation path.

The broker implementation and deployment are intentionally not included in
the desktop repository because the hosting platform, organization identity
provider, redirect URI, and managed-secret service have not yet been selected.
