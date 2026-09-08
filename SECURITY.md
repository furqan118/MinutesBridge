# Security policy

MinutesBridge handles employee meeting notes that may contain confidential
information. Do not include real meeting content, OAuth tokens, cookies,
credentials, Confluence identifiers, or tenant URLs in issues, screenshots,
test fixtures, logs, or crash reports.

Report suspected vulnerabilities through the organization's private security
reporting channel. Do not open a public issue containing exploit details or
sensitive data.

## Locked controls

- No Atlassian client secret, API token, or employee password in the desktop app.
- No telemetry or meeting-content logging.
- Human review before publishing.
- The app runs without elevation.
- Broker and Atlassian calls use HTTPS, explicit timeouts, bounded responses,
  and validated identifiers.
- The access token is stored as a session-persistent Windows Generic Credential
  and is rejected shortly before expiry. The broker retains refresh tokens.
- Live publishing remains disabled until its separate restricted-draft change
  passes security review and CI.
