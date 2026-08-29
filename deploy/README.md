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
- **The intake service names the form's origin.** The public form is served from a public site of its
  own — not from the main host and not by this service — so it posts across origins. Start the intake
  service with `--publicFormOrigin=https://villageos.ai,https://regenvillages.com`, naming every site
  the form is served from. With none configured the service refuses every cross-origin caller.
- **The intake service sends mail, and will not start without somewhere to send it.** A submission is
  accepted only from somebody who answered a code sent to the address on it, so the service is
  launched with `--mailHost=<host> --mailFrom=<address>`, optionally `--mailPort` (587 by default)
  and `--mailUser`. The password is `MailPassword` in configuration or the environment and is never
  a command-line argument, because the command line is visible to every process on the host. With
  none of this configured the service prints what is missing and stops, rather than starting and
  refusing every submission.
- **`--mailDelivery=console` is not for a deployment.** It writes each code to the log instead of
  sending it, so a developer with no relay to hand can run the whole exchange. Nobody has to receive
  a code that way, so no address is verified and a submission may name any address at all — which is
  why the service refuses to start with it anywhere but Development, and says so loudly in the log on
  the startup it does allow. The default, `--mailDelivery=server`, is the behaviour above.
- **The intake service holds an API key** (`ApiKey` in configuration or the environment) created
  against the intake model, so its credential does not expire and reaches no project model — see
  "Giving a service an API key" in [`../docs/SERVICES.md`](../docs/SERVICES.md).
- **The proxy passes the caller's address on.** The submission route is anonymous and rate limited per
  source, and every caller reaches the service from loopback, so the address the limit partitions on is
  the one in `X-Forwarded-For`. Caddy's `reverse_proxy` sets it; a different proxy has to be configured
  to. Without it every submitter on the internet shares one budget. The service reads the header only
  from loopback, which is the only place it can be reached from.

## The public submission form

**The form is a build of its own, served from a public site, and its only correspondent is the intake
service.** It carries no sign-in, no broker client and no part of the signed-in application;
`vos.Trellis/src/publicForm/noSignedInCode.test.ts` fails if an import ever leads back to one.

```
cd vos.Trellis
VITE_INTAKE_URL=https://intake.example.org npm run build:public-form
```

`dist-public-form/` is the whole deliverable: an `index.html` and its assets, addressed relatively, so
the directory can be placed at any path on the site. `VITE_INTAKE_URL` is read at build time, not at run
time — a form built without it collects answers it cannot post, and says so instead of offering the
button.

Two things have to agree for the form to work:

| What | Where it is set |
|---|---|
| The address the form posts to | `VITE_INTAKE_URL` when the form is built |
| The origins the service answers | `--publicFormOrigin` when the service is started |

**What the form is drawn with comes from the model, over `GET /submissions/form`.** The page holds no
credential, so it cannot read the model; the service reads it under its own and answers with the
programme categories a submission may name and the basemap sources a map may draw on, and nothing else.

**A basemap address the model holds is published by this.** The form is open to anybody, so a tile or
style address carrying a key in it is readable by anybody who opens the page. That was already true of
every browser signed in to the GUI; what changes is who can open it. A deployment whose imagery is
behind a key should put a proxy in front of the provider rather than a key in the model.
