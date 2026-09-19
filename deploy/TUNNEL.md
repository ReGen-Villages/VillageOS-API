# The Cloudflare account's part of a tunnelled deployment

**Authorise the serving machine once in the browser; it creates its own tunnel and hostnames. Then
host the public pages, and put a sign-in gate in front of the main host.** Nothing else in the account
changes; a website already on the domain is untouched.

This is written for whoever signs into the Cloudflare account that holds the domain, and assumes no
prior Cloudflare knowledge. `app.example.org`, `intake.example.org` and `submit.example.org` stand for
the deployment's own hostnames.

## What this is

- The **broker** is the VillageOS server; the **intake service** takes land submissions from the
  public; both run on a machine that has no public address.
- A **Cloudflare Tunnel** is a small program on that machine that keeps an outgoing connection open
  to Cloudflare. When somebody visits `app.example.org`, Cloudflare sends the request down that
  connection. Nothing on the machine's network is opened to the internet, its address stays hidden,
  and Cloudflare provides the HTTPS certificate.
- The **public pages** — the submission form and the pages a submitter reads — are plain files, hosted
  by Cloudflare itself.

## What you need

- Sign-in to the Cloudflare account that holds the domain, with the **Super Administrator** role,
  or **Administrator** plus **Cloudflare Zero Trust**.
- About fifteen minutes, part of it beside the person running the machine.
- No cost. Tunnels, static hosting and the sign-in gate are on the free plan (up to fifty users).
  Cloudflare may ask for a payment method when Zero Trust is switched on for the first time; the free
  plan is not charged.

## 1. Authorise the machine

The person running the machine starts its tunnel script. It opens a browser page on Cloudflare
asking which domain the machine may manage. Sign in there, pick the domain, click **Authorize**.

That is the whole hand-over: the machine now creates the tunnel, its hostnames and their DNS records
by itself. No token changes hands.

**Check:** **Zero Trust → Networks → Tunnels** shows a tunnel named after the machine, **Healthy**,
once the machine side has started it; **DNS → Records** shows `app` and `intake` as CNAMEs to
`<id>.cfargotunnel.com`, proxied (orange cloud).

## 2. Host the public pages

The machine side hands you a folder of files (the form, findings and explore pages).

1. **Workers & Pages → Create → Pages → Upload assets**; name the project; upload the folder.
2. **Custom domains → Set up a custom domain →** `submit.example.org`. Cloudflare creates the
   record and the certificate.

Every later update of the pages is the same upload again: the project → **Create new deployment →
Upload**.

## 3. Recommended: a sign-in gate in front of the main host

The server has its own login, but this keeps strangers from reaching that login at all.

1. **Zero Trust → Access → Applications → Add an application → Self-hosted**.
2. Give it a name; application domain `app.example.org`.
3. Add a policy: action **Allow**, include **Emails**, and list the addresses that may open it.
4. Save.

Visitors now enter their email, receive a one-time code from Cloudflare, and only then see the
server. Adding a person later is adding one email to this list.

**Not on `intake.` or `submit.`** — the form is for people without an account, and it posts to the
intake service; a gate on either breaks every submission.

## What not to touch

- Any website already on the domain and its records.
- The nameservers.
- The zone's SSL/TLS settings — the tunnel does not depend on them.

## If something is wrong

| Symptom | Meaning | Who fixes it |
|---|---|---|
| Tunnel shows **Inactive** or **Down** | The connector on the machine is not running | The machine side |
| Browser shows Cloudflare **error 1033** | Same — the tunnel is not connected | The machine side |
| Browser shows **502 Bad Gateway** | Connector is running but nothing answers on the port the hostname points at | The machine side |
| The form says it cannot send, or a submission is refused | The pages were built against another intake address, or the intake service was not told the pages' site | The machine side |
| You cannot see Zero Trust | Your role lacks the permission | The account owner adds the **Cloudflare Zero Trust** role |

## Later

Moving the deployment to a machine with a public address: authorise that machine the same way; it
creates its own tunnel, and the old one is deleted from the Tunnels page. Nothing else changes.
