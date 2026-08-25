# Public delivery tracking deployment

The Razor page calls the API server-to-server at
`/v1/public/deliveries/{publicToken}`. Override the path only with a relative
path containing exactly one `{publicToken}` placeholder.

When trusted client identity forwarding is enabled, configure the same secret
in both services:

```text
Razor/Fly: DeliveryTracking__ProxySecret
API:      ClientIdentity__ProxySecret
```

The Razor client forwards only a syntactically valid `Fly-Client-IP` header as
`X-Morita-Client-IP`, together with `X-Morita-Proxy-Secret`. It never forwards
an arbitrary browser-supplied IP or exposes the secret to page scripts.

Production must also set `DeliveryTracking__GoogleReviewUrl` to a non-empty
Google review URL.
