# ClassifiedAds OpenIddict Identity Server

## Overview

This project implements an **OpenID Connect / OAuth 2.0** authorization server using [OpenIddict](https://github.com/openiddict/openiddict-core) (v7.x) on top of ASP.NET Core Identity.

---

## OAuth Version: OAuth 2.0 (with OAuth 2.1-aligned improvements)

The server is currently configured as **OAuth 2.0** because it enables the following grant types:

| Grant Type | RFC | OAuth 2.0 | OAuth 2.1 | Status in this project |
|---|---|---|---|---|
| Authorization Code | RFC 6749 §4.1 | ✅ | ✅ (required) | **Enabled** |
| Client Credentials | RFC 6749 §4.4 | ✅ | ✅ | **Enabled** |
| Refresh Token | RFC 6749 §6 | ✅ | ✅ | **Enabled** |
| Resource Owner Password (ROPC) | RFC 6749 §4.3 | ✅ | ❌ Removed | **Enabled** ⚠️ |
| Hybrid Flow (OIDC) | OpenID Connect | ✅ | ❌ Not specified | **Enabled** ⚠️ |
| Implicit Flow | RFC 6749 §4.2 | ✅ | ❌ Removed | Not used ✅ |

> **Note — OAuth 2.1 Compliance Gap**: OAuth 2.1 ([draft-ietf-oauth-v2-1](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-v2-1)) removes the **Password Grant (ROPC)** and the **Implicit Flow**, and mandates **PKCE for all Authorization Code flows**. The current configuration still enables the Password Grant (`AllowPasswordFlow()`) and the Hybrid Flow (`AllowHybridFlow()`), meaning the server is **OAuth 2.0**, not OAuth 2.1.
>
> It is strongly recommended to migrate away from the Password Grant by replacing it with the Authorization Code flow + PKCE. See [PKCE section](#pkce-proof-key-for-code-exchange) below.

---

## PKCE (Proof Key for Code Exchange)

**PKCE is enabled and enforced for all registered clients.**

PKCE ([RFC 7636](https://datatracker.ietf.org/doc/html/rfc7636)) mitigates authorization code interception attacks. Every client application registered in `SeedDataHostedService.cs` carries:

```csharp
Requirements =
{
    OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
}
```

This makes `code_challenge` and `code_challenge_method` **mandatory** in every authorization request for those clients, regardless of client type (public or confidential).

### Clients and their PKCE status

| Client ID | Client Type | PKCE Required | Allowed Grant Types |
|---|---|---|---|
| `Swagger` | Confidential | ✅ Required | AuthorizationCode, ClientCredentials, RefreshToken |
| `ReverseProxy.Yarp` | Confidential | ✅ Required | AuthorizationCode, ClientCredentials, **Password** ⚠️, RefreshToken |
| `ClassifiedAds.WebMVC` | Confidential | ✅ Required | AuthorizationCode, ClientCredentials, **Password** ⚠️, RefreshToken |
| `ClassifiedAds.BlazorServerSide` | Confidential | ✅ Required | AuthorizationCode, ClientCredentials, RefreshToken |
| `ClassifiedAds.BlazorWebAssembly` | **Public** | ✅ Required | AuthorizationCode, ClientCredentials, RefreshToken |
| `ClassifiedAds.Angular` | **Public** | ✅ Required | AuthorizationCode, ClientCredentials, RefreshToken |
| `ClassifiedAds.React` | **Public** | ✅ Required | AuthorizationCode, ClientCredentials, RefreshToken |
| `ClassifiedAds.Vue` | **Public** | ✅ Required | AuthorizationCode, ClientCredentials, RefreshToken |

> **Best Practice**: PKCE is especially critical for **Public clients** (SPAs, mobile apps) that cannot safely store a `client_secret`. All public clients (`BlazorWebAssembly`, `Angular`, `React`, `Vue`) correctly use `ClientType = OpenIddictConstants.ClientTypes.Public` and enforce PKCE.

---

## Server Endpoints

| Endpoint | URI |
|---|---|
| Token | `POST /connect/token` |
| Authorization | `GET /connect/authorize` |
| End Session (Logout) | `GET /connect/logout` |
| UserInfo | `GET /connect/userinfo` |

---

## Token Security

- **Signing** — tokens are signed with an X.509 certificate (`IdentityServer:SigningCertificate` in `appsettings.json`).
- **Encryption** — token encryption with an X.509 certificate is configured but disabled at runtime via `options.DisableAccessTokenEncryption()` to allow resource servers to validate JWTs without sharing the encryption key.
- **Key storage** — ASP.NET Core Data Protection keys are persisted to the database (`PersistKeysToDbContext<AdsDbContext>`).

---

## Recommendations for OAuth 2.1 Migration

To fully align with OAuth 2.1 (and current security best practices), consider the following:

1. **Remove the Password Grant** — Replace ROPC flows in `ReverseProxy.Yarp` and `ClassifiedAds.WebMVC` with the Authorization Code + PKCE flow. Remove `AllowPasswordFlow()` from `Startup.cs`.
2. **Remove the Hybrid Flow** — The Hybrid Flow is not part of OAuth 2.1. Use Authorization Code + PKCE instead. Remove `AllowHybridFlow()` from `Startup.cs`.
3. **Enforce Access Token Encryption** — Consider re-enabling `AddEncryptionCertificate` and removing `DisableAccessTokenEncryption()` if all resource servers can be updated to handle encrypted tokens.
4. **Short-lived tokens** — Configure short-lived access tokens and use refresh token rotation to limit the blast radius of token theft.

---

## Project Structure

```
ClassifiedAds.IdentityServer/
├── Controllers/
│   ├── AuthorizationController.cs   ← handles /connect/authorize, /connect/token, /connect/logout
│   └── UserInfoController.cs        ← handles /connect/userinfo
├── HostedServices/
│   └── SeedDataHostedService.cs     ← seeds OpenIddict clients and scopes on startup
├── ConfigurationOptions/
│   └── IdentityServerOptions.cs     ← certificate configuration
└── Startup.cs                       ← OpenIddict server registration and flow configuration
```
