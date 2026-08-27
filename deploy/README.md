# Deployment routing

**The intake service answers on its own hostname; the API and the GUI stay on the main one. The
split lives in the reverse proxy and nowhere else.**

## What the Caddyfile does

- `app.example.org` — `/api/*` goes to the broker on `localhost:7243`; every other path serves the
  built GUI (`vos.Trellis`'s build output).
- `intake.example.org` — everything goes to the intake service on `localhost:7300`.
- TLS terminates at the proxy for both hosts. Caddy provisions and renews the certificates itself.

Replace the hostnames and the intake port with the deployment's own, put the GUI's build output
where `root` points, and run `caddy run --config deploy/Caddyfile`.

## What the services must do

- **Bind loopback.** Every service listens on this machine only and is unreachable except through
  the proxy. Write the binding whichever way the language allows — `localhost`, `127.0.0.1` or a
  Kestrel listen call are all read the same.
  `Tests/vos.ContinuousIntegration.Tests/ServicesBindLoopbackTests` pins it: no service in any
  language may name an address reaching past this machine, and every managed service must state a
  binding, all of which must be loopback.
- **The broker stays unaware of hostnames.** Nothing in it reads the request's host name, and that
  is deliberate — see the "On subdomain" note in [`../docs/LAND_INTAKE.md`](../docs/LAND_INTAKE.md).
- **The intake service names the form's origin.** The public form is served from the main host and
  posts across origins, so start the intake service with
  `--publicFormOrigin=https://app.example.org` (comma-separate several). With none configured the
  service refuses every cross-origin caller.
- **The intake service sends mail, and will not start without somewhere to send it.** A submission is
  accepted only from somebody who answered a code sent to the address on it, so the service is
  launched with `--mailHost=<host> --mailFrom=<address>`, optionally `--mailPort` (587 by default)
  and `--mailUser`. The password is `MailPassword` in configuration or the environment and is never
  a command-line argument, because the command line is visible to every process on the host. With
  none of this configured the service prints what is missing and stops, rather than starting and
  refusing every submission.
- **The intake service holds an API key** (`ApiKey` in configuration or the environment) created
  against the intake model, so its credential does not expire and reaches no project model — see
  "Giving a service an API key" in [`../docs/SERVICES.md`](../docs/SERVICES.md).
- **The proxy passes the caller's address on.** The submission route is anonymous and rate limited per
  source, and every caller reaches the service from loopback, so the address the limit partitions on is
  the one in `X-Forwarded-For`. Caddy's `reverse_proxy` sets it; a different proxy has to be configured
  to. Without it every submitter on the internet shares one budget. The service reads the header only
  from loopback, which is the only place it can be reached from.
