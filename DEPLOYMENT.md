# Deployment Guide

RummyKub is **one container**: an ASP.NET Core app that runs the SignalR hub and
serves the built React client from `wwwroot`. Game state is **in memory** — no
database — so you run a **single instance**. It needs WebSocket support and TLS
in front (browsers require `wss://`).

---

## 1. Verify locally in Docker (do this first)

Requires **Docker Desktop running**.

```bash
# From the repo root
docker build -t rummykub .
docker run --rm -p 8080:8080 rummykub
```

Then check it:

```bash
curl http://localhost:8080/health        # -> {"status":"ok"}
```

Open **http://localhost:8080** in a browser. To exercise multiplayer:

- **Offline vs AI:** create a room → **Add AI player** → **Start game**.
- **Two humans:** open a second browser tab/window, create a room in one, and
  **Join** by the 4-letter code in the other.

Play a full round — drag tiles to the board, **Play** a 30-point meld, watch the
AI take its turn, try the timer/undo/hint. When done, `Ctrl+C` stops the
container. This is the exact image you deploy, so if it works here it works on
the VM.

> Single-container note: the client talks to `/hub/game` on the **same origin**,
> so there is no CORS to configure — the server serves both halves.

---

## 2. What to create on Oracle Cloud (OCI)

**Recommended shape:** one **Always Free Compute VM** running Docker, with Caddy
in front for automatic HTTPS. Free, simplest, and a perfect fit for a
single-instance app. (An alternative using OCI Container Instances + Load
Balancer is sketched at the bottom.)

Create these resources in the OCI console:

| # | Resource | Notes |
|---|----------|-------|
| 1 | **VCN + public subnet** | Use *Networking → VCN Wizard → “VCN with Internet Connectivity.”* Creates the VCN, internet gateway, and route tables for you. |
| 2 | **Ingress rules** (Security List or NSG on the public subnet) | Allow TCP **22** (SSH — ideally your IP only), **80**, and **443** from `0.0.0.0/0`. |
| 3 | **Compute Instance** | Shape **VM.Standard.A1.Flex** (Ampere ARM, Always Free: up to 4 OCPU / 24 GB) *or* **VM.Standard.E2.1.Micro** (AMD x86, Always Free, 1 GB). Image: **Ubuntu 22.04**. Place in the public subnet, assign a public IP, add your SSH public key. |
| 4 | **Public IP** | Ephemeral is fine; reserve a static one if you want it to survive stop/start. |
| 5 | **DNS A record** | In your `helleon.com` DNS, add **A record: `rummykub` → `<VM public IP>`** (host `rummykub`, giving `rummykub.helleon.com`). Required for Let’s Encrypt to issue the certificate. |
| 6 | *(optional)* **OCIR** — Container Registry | Only if you want to build the image elsewhere and pull it. You’ll also generate an **Auth Token** (used as the docker login password). Skip this if you build on the VM. |

> ⚠️ **OCI gotcha — two firewalls.** Opening ports in the OCI Security
> List/NSG is not enough; the VM image also has a **host firewall**. On Ubuntu,
> Oracle preloads iptables rules that block everything but SSH. Open 80/443 on
> the host too (commands below).

---

## 3. Deploy onto the VM

SSH in (`ssh ubuntu@<public-ip>`), then:

### 3a. Open the host firewall (Ubuntu images)
```bash
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save
```

### 3b. Install Docker (and the Compose plugin)
```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER   # log out/in so 'docker' works without sudo

# Confirm Docker Compose v2 is present; if not, install the plugin:
docker compose version || sudo apt-get update && sudo apt-get install -y docker-compose-plugin
```
> If `docker compose` (with a space) prints “not a docker command”, the v2 plugin
> is missing — the line above installs it. If your VM only has the older
> standalone tool, use `docker-compose` (with a hyphen) in every command below.

### 3c. Get the code
```bash
git clone https://github.com/janethuyj/RummyKub.git
cd RummyKub
```
The Caddyfile is already set to `rummykub.helleon.com`. (To use a different host,
edit `deploy/Caddyfile` before the next step.)

### 3d. Build and run (Caddy auto-provisions HTTPS)
```bash
docker compose -f deploy/docker-compose.yml up -d --build
```

Building on the VM produces the right CPU architecture automatically (arm64 on
A1, x86 on E2). Make sure the `rummykub` A record already points at the VM, then
visit **https://rummykub.helleon.com** — Caddy fetches a Let’s Encrypt
certificate on first request, and SignalR runs over `wss://`.

**Update later:**
```bash
git pull && docker compose -f deploy/docker-compose.yml up -d --build
```

**Logs / health:**
```bash
docker compose -f deploy/docker-compose.yml logs -f app
curl -k https://rummykub.helleon.com/health
```

### Optional: build elsewhere and push to OCIR instead of building on the VM
If you build on an **x86 machine** for an **A1 (arm64) VM**, cross-build:
```bash
docker buildx build --platform linux/arm64 \
  -t <region-key>.ocir.io/<namespace>/rummykub:latest --push .
```
Then on the VM, set `image:` in `deploy/docker-compose.yml`, `docker login
<region-key>.ocir.io` (username `<namespace>/<user>`, password = Auth Token),
and `docker compose ... up -d` (no `--build`).

---

## Alternative: OCI Container Instances (no VM to manage)
1. Push the image to **OCIR** (as above).
2. Create a **Container Instance** from that image, port 8080.
3. Front it with a **Flexible Load Balancer** (supports WebSockets) with an HTTPS
   listener and a certificate (OCI Certificates or an uploaded Let’s Encrypt one),
   backend = the container instance on 8080.

This removes VM upkeep but the Load Balancer is **not** in the Always Free tier,
and it’s more moving parts than the VM + Caddy path above.

---

## Scaling beyond one instance (later)
In-memory state pins each room to one process, and WebSockets pin a client to one
server, so don’t run 2+ app replicas as-is. When you outgrow one VM: add the
**Redis backplane** (or Azure SignalR-style service), enable sticky sessions on a
load balancer, and move room state into Redis behind the existing `IRoomStore`
interface. See the build plan for details.
