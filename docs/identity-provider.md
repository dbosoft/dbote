# Identity provider requirements

What an identity provider has to do so that bote clients can sign in with it and the worker accepts
their tokens. The worker trusts the access token alone: tenant, client and whom a client may reach
come from it and from nothing the client sends. Whatever the identity provider puts into a token is
therefore what the gateway enforces.

`BasicIdentityProvider` in this repository does all of this for development, without the
safeguards a production issuer needs (see the end of this page).

## Overview

```
client                      identity provider                 worker
  | client assertion (ES256,  |                                  |
  | signed with client key)   |                                  |
  |-------------------------->| checks it against the            |
  |                           | registered public key            |
  |<--------------------------| access token (RS256/ES256):      |
  |   tid, sub, scope, bote_peers                                |
  |------------------------------------------------------------->| negotiate: validates against
  |                                                              | the JWKS, opens a SignalR
  |<-------------------------------------------------------------| connection for 15 minutes
```

The client asks for a new token whenever it connects, which is at start, after a dropped
connection and every 15 minutes, when the worker's SignalR token runs out. That is about four token
requests per client and hour.

## Client authentication

The client uses the OAuth 2.0 client credentials grant with a private key JWT
([RFC 7523](https://www.rfc-editor.org/rfc/rfc7523)). It posts a form to the token endpoint it is
configured with (`BoteCredentials.TokenEndpoint`):

| Field | Value |
| --- | --- |
| `grant_type` | `client_credentials` |
| `client_id` | the client id |
| `client_assertion_type` | `urn:ietf:params:oauth:client-assertion-type:jwt-bearer` |
| `client_assertion` | a JWT, see below |
| `scope` | `BoteCredentials.Scope`, e.g. `bote` |

The client assertion is signed with ES256 by the client's own ECDSA P-256 key and carries:

| | |
| --- | --- |
| `iss`, `sub` | the client id |
| `aud` | `BoteCredentials.Authority`, the issuer's identifier (not the token endpoint URL) |
| `jti` | a new GUID for every assertion |
| `nbf`, `exp` | now, and five minutes later |
| header `kid` | the key id of the client's signing key |
| header `typ` | `client-authentication+jwt` (explicit typing, RFC 8725; the identity provider requires it) |

The identity provider must:

- Know every client by tenant and client id together, with the public key registered for it.
  The client's private key never leaves the client.
- Verify the assertion's signature against that public key, `iss` and `sub` against the client id,
  `aud` against its own identifier, and `exp`.
- Refuse an assertion whose `jti` it has already seen before the assertion expired (replay).
- Accept the request as `application/x-www-form-urlencoded` and answer with JSON:
  `access_token`, `token_type` `Bearer`, `expires_in`.
- Take the client's tenant from the registration, never from the request. A token endpoint per
  tenant (`/{tenant}/token`) is fine as long as the client must be registered under that tenant.

Rotating a client's key: register the new public key next to the old one and tell them apart by
`kid`, then remove the old one when the client has switched.

## Access token

| | Requirement |
| --- | --- |
| Signature | RS256 (RSA) or ES256 (ECDSA P-256). The worker accepts these two asymmetric algorithms and refuses every other (no HMAC, no `none`). A production issuer such as OpenIddict signs RS256; the development BasicIdentityProvider signs ES256. |
| header `kid` | The id of the signing key in the JWKS. Without it rotation cannot work (see below). |
| `iss` | Exactly the worker's `OpenId:Authority`, character for character. |
| `aud` | Contains the worker's `OpenId:Audience`. |
| `exp` | Required. 15 minutes is enough: the token is only used to connect. The worker allows two minutes of clock skew. |
| `sub` | The client id. |
| `tid` | The tenant id. |
| `scope` | One string of space-separated scopes, containing the worker's `OpenId:RequiredScope` (e.g. `bote`). Not an array, and not `scp`. |
| `bote_peers` | Only for clients that send to other clients, together with the worker's `OpenId:PeerScope` in `scope`: the client ids of the same tenant the client may send to, as an array of strings (a single string for one). `*` stands for every client of the tenant. |

Grant scopes and peers from the client's registration, not from what the client asks for: the
worker takes them at face value.

### Ids

- `tid` and `sub` must not contain `/`, `\` or control characters, must not be `.` or `..`, and
  must not end in a dot; the worker refuses such tokens.
- The pair of tenant and client id names everything the worker keeps for the client: its queue,
  its SignalR user, its subscriptions. Never change a tenant's or a client's id, and never give an
  id that was in use to another client: the new one would get the old one's messages.
- Ids are compared as they are, case included.
- The tenant id is a folder in the attachment storage. Keep it short; a GUID is a good choice.
- `*` cannot be a client that others send to, since in `bote_peers` it means everyone.

### Peers

The worker lets a client send to another client only if its token carries the peer scope and
lists that client in `bote_peers`, and always within the sender's tenant. A reply is a message
of its own, so both sides need each other in their lists. With the scope but without the claim a
client reaches no other client.

For example, an application instance in the cloud asking agents on its customer's premises gets
`bote_peers: ["*"]`, each agent gets `bote_peers: ["instance"]`, and agents cannot talk to each
other.

The list is copied into the worker's SignalR token, so keep it short; use `*` for a client that
talks to many.

## Signing keys (JWKS)

- Publish the signing keys as a JSON Web Key Set at the worker's `OpenId:JwksUri`, over HTTPS and
  reachable from the worker: RSA keys (`kty` `RSA`, `n`, `e`, `kid`) or EC keys (`kty` `EC`,
  `crv` `P-256`, `x`, `y`, `kid`), matching the signing algorithm (RS256 / ES256).
- The worker caches the set for an hour. A token whose `kid` it does not know makes it load the
  set again, at most every 30 seconds, so a new key works within moments of being published.
- Rotation: publish the new key, then sign with it. Keep the old key published until the last
  token it signed has expired (token lifetime plus two minutes).
- If the identity provider cannot be reached, the worker keeps using the keys it has.

## Turning a client off

The worker does not ask the identity provider about a token after it has accepted it. When the
identity provider stops issuing tokens to a client:

- it cannot connect again; its current connection ends within 15 minutes,
- it can send nothing more once the connection has ended,
- it can still read its own queue for up to an hour with the SAS it already has.

## Off-the-shelf identity providers

Entra ID and Azure AD B2C cannot issue these tokens as they are: they put an application's
permissions in `roles` instead of `scope` and use `tid` for their own directory. An application
needs an issuer it controls — for example a token service of its own, OpenIddict or Duende
IdentityServer — configured for RS256 or ES256 and the claims above.

## Checklist

- [ ] Client credentials with private key JWT, assertion verified against the registered P-256 key
- [ ] `jti` replay refused
- [ ] Tenant taken from the registration
- [ ] Access token RS256 or ES256 with `kid`
- [ ] Client assertion explicitly typed `typ: client-authentication+jwt`
- [ ] `iss`, `aud`, `exp` as the worker is configured
- [ ] `sub`, `tid`, `scope` (a string), `bote_peers` for clients that talk to other clients
- [ ] Scopes and peers granted from the registration
- [ ] JWKS published, rotation with overlap
- [ ] Clients can be turned off

`BasicIdentityProvider` does not refuse a replayed `jti`, lets anyone register a client
(`POST /api/register-client`) and keeps one signing key from configuration. That is fine on a
developer's machine and in a test stack, and nowhere else.
