# MinutesBridge

MinutesBridge is a Windows desktop app that converts copied Microsoft Teams
Facilitator notes into the BHITS Confluence meeting-notes format. It does not
record meetings, retrieve transcripts, or send notes to another AI service.

## Current milestone (v0.2)

This starter implements:

- paste/import of plain-text Facilitator notes;
- meeting metadata, attendees, and regrets;
- topic-and-bullet parsing;
- a reviewable page preview;
- safe Confluence storage-format rendering;
- local HTML draft export;
- a Confluence Cloud REST API v2 publisher boundary;
- tests for output encoding, title format, and note parsing.

The secure connection foundation now also implements:

- an HTTPS OAuth broker client with bounded responses and polling secrets kept
  out of URLs;
- browser-based employee authorization without an embedded client secret;
- session-only token protection through Windows Credential Manager;
- loading up to 100 spaces and root pages the signed-in employee may access;
- discovery and linking of the newest correctly dated BHITS meeting page;
- strict validation for broker URLs, tenant/site identifiers, pasted note size,
  people lists, tokens, and remote response sizes;
- Windows CI for formatting, unit tests, application builds, and CodeQL.

Live publishing is intentionally disabled in the desktop UI until OAuth is
connected. No Atlassian client secret or employee password belongs in the app.

## Build on Windows

Requirements: Windows 10/11 and the .NET 10 SDK.

```powershell
dotnet restore
dotnet test
dotnet run --project src/MinutesBridge.App
```

## Confluence contract

The publisher targets:

`POST https://api.atlassian.com/ex/confluence/{cloudId}/wiki/api/v2/pages`

Required OAuth scopes are `read:space:confluence`, `read:page:confluence`, and
`write:page:confluence`; the broker also requests `offline_access` when it must
renew a session. The employee must also have
permission to create pages in the chosen space.

Atlassian Cloud 3LO currently requires a confidential client secret and does
not support PKCE for a public desktop client. Production authentication should
therefore use an organization-controlled OAuth broker. API tokens and client
secrets must never be embedded in the installer.

The administrator supplies the broker origin without a path:

```powershell
[Environment]::SetEnvironmentVariable(
  "MINUTESBRIDGE_BROKER_BASE_URI",
  "https://minutesbridge-broker.example/",
  "User")
```

The desktop-to-broker messages and server-side security requirements are in
[`docs/oauth-broker-contract.md`](docs/oauth-broker-contract.md). The broker
deployment itself needs the organization's chosen hosting platform, managed
secret store, redirect URI, and identity controls.

## Next milestone

1. Add editable agenda rows and rich clipboard import.
2. Publish as a restricted draft, then open the created page for review.
3. Add sign-out/revocation and broker session renewal.
4. Generate and commit the NuGet lock file on Windows.
5. Package and sign an MSIX installer.

## Privacy defaults

- Human review is mandatory before publishing.
- Notes stay in memory unless the employee explicitly saves a draft.
- HTML is encoded before it enters the Confluence storage body.
- No telemetry or content logging is enabled.
- The application runs as the signed-in user and does not request elevation.
