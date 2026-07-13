# OMEN live GCP dashboard

This loopback-only proxy lets OMEN view the live GCP P7 dashboards without adding
public HTTPS or exposing the Valheim control endpoints. It forwards only the HTML
surfaces and `GET /api/v0/telemetry/*`.

From this directory:

```powershell
docker compose up -d
Start-Process http://127.0.0.1:8080/community
```

Other local surfaces:

```text
http://127.0.0.1:8080/networksense
http://127.0.0.1:8080/events
http://127.0.0.1:8080/testing
```

The proxy binds only to `127.0.0.1`. `/valheim/*`, Operator API routes, WebSocket
control routes, and non-GET telemetry requests are deliberately unavailable through it.
To point at another GCP gateway, edit the `upstream` address in `nginx.conf`.
