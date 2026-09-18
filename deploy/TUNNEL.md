# Setting up the tunnel in the Cloudflare account

**Create one tunnel, give it the deployment's hostname, and hand the tunnel's token to the person
running the machine. Nothing else in the account changes; a website already on the domain is
untouched.**

This is written for whoever signs into the Cloudflare account that holds the domain, and assumes no
prior Cloudflare knowledge. `app.example.org` stands for the deployment's own hostname throughout.

## What this is

- The **broker** is the VillageOS server. For now it runs on a machine that has no public address.
- A **Cloudflare Tunnel** is a small program on that machine that keeps an outgoing connection open
  to Cloudflare. When somebody visits `app.example.org`, Cloudflare sends the request down that
  connection. Nothing on the machine's network is opened to the internet, its address stays hidden,
  and Cloudflare provides the HTTPS certificate.

## What you need

- Sign-in to the Cloudflare account that holds the domain, with the **Super Administrator** role,
  or **Administrator** plus **Cloudflare Zero Trust**.
- About fifteen minutes.
- No cost. Tunnels and the sign-in gate in step 5 are on the free plan (up to fifty users).
  Cloudflare may ask for a payment method when Zero Trust is switched on for the first time; the free
  plan is not charged.

## Steps

### 1. Open Zero Trust

1. Go to <https://dash.cloudflare.com> and pick the account.
2. In the left menu choose **Zero Trust**.
3. If this is the first time: choose a team name and the **Free** plan.

### 2. Create the tunnel

1. **Networks → Tunnels → Create a tunnel**.
2. Choose the **Cloudflared** type.
3. Name it after the machine that will run it, and save.

### 3. Copy the token for the person running the machine

The next screen, *Install and run a connector*, shows install commands. Each contains one long token
starting with `eyJ`.

1. Copy **only the token**.
2. Send it over a private channel — a password-manager share, or a message you both delete. The
   token lets any machine act as this tunnel, so treat it like a password.
3. Do not install anything yourself. Click **Next**.

### 4. Give the tunnel its public hostname

On the *Route tunnel* screen, under **Public hostname**:

| Field | Value |
|---|---|
| Subdomain | `app` |
| Domain | `example.org` |
| Path | leave empty |
| Service type | `HTTPS` |
| URL | `localhost:7243` |

Then open **Additional application settings → TLS** and switch **No TLS Verify** to **on**. The
server presents a certificate it signed itself; visitors still get Cloudflare's real certificate.

Save the tunnel. Cloudflare creates the DNS record for you.

**Check:** **DNS → Records** now shows `app` as a CNAME to `<id>.cfargotunnel.com`, proxied
(orange cloud).

If the deployment also serves the intake host, add a second public hostname the same way:
subdomain `intake`, service type `HTTP`, URL `localhost:80` — the machine's reverse proxy splits the
two hostnames from there, and the first hostname then points at the proxy too rather than at the
server directly. The person running the machine will say which shape they run.

### 5. Recommended: put a sign-in gate in front of it

The server has its own login, but this keeps strangers from reaching that login at all.

1. **Zero Trust → Access → Applications → Add an application → Self-hosted**.
2. Give it a name; application domain `app.example.org`.
3. Add a policy: action **Allow**, include **Emails**, and list the addresses that may open it.
4. Save.

Visitors now enter their email, receive a one-time code from Cloudflare, and only then see the
server. Adding a person later is adding one email to this list. Do not put the gate on the intake
host: the public form must reach it without signing in.

### 6. Tell the person running the machine

Send them: the token (step 3), confirmation that the hostname is set (step 4), and whether the gate
is on and which emails are on it (step 5).

## Checks once the machine side is running

| Where | What you should see |
|---|---|
| **Zero Trust → Networks → Tunnels** | The tunnel shows **Healthy** with one connector |
| `https://app.example.org` | The server's sign-in page (after the email code, if step 5 is on) |

## What not to touch

- Any website already on the domain and its records.
- The nameservers.
- The zone's SSL/TLS settings — the tunnel does not depend on them.

## If something is wrong

| Symptom | Meaning | Who fixes it |
|---|---|---|
| Tunnel shows **Inactive** or **Down** | The connector on the machine is not running | The machine side |
| Browser shows Cloudflare **error 1033** | Same — the tunnel is not connected | The machine side |
| Browser shows **502 Bad Gateway** | Connector is running but nothing answers on the port the hostname points at, or **No TLS Verify** is off | The machine side checks the server; you check step 4 |
| You cannot see Zero Trust | Your role lacks the permission | The account owner adds the **Cloudflare Zero Trust** role |

## Alternative: let the machine side do steps 2–5

If handing over a token is unwelcome, invite that person instead: **Manage Account → Members →
Invite**, with the roles **Cloudflare Zero Trust** and **DNS**. They then create the tunnel and
hostname themselves and no token changes hands.

## Later

Moving the deployment to a machine with a public address: install the connector there with the same
token. Nothing in Cloudflare changes.
