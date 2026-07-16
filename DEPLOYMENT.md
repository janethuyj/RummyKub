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

> ⚠️ **Do not build on a 1 GB shape.** `VM.Standard.E2.1.Micro` has 954 MB of
> usable RAM and no swap by default. `dotnet publish` needs more than that, so
> the build does not merely run slowly — it thrashes the page cache until the box
> stops accepting new SSH connections, and you have to reboot from the OCI
> console. Use [§3e](#3e-build-on-a-workstation-and-ship-the-image-recommended)
> instead. Building on the VM is only reasonable on an A1.Flex with ≥4 GB.

If your VM has the memory for it:
```bash
sudo docker compose -f deploy/docker-compose.yml up -d --build
```

Building on the VM produces the right CPU architecture automatically (arm64 on
A1, x86 on E2). Make sure the `rummykub` A record already points at the VM, then
visit **https://rummykub.helleon.com** — Caddy fetches a Let’s Encrypt
certificate on first request, and SignalR runs over `wss://`.

### 3e. Build on a workstation and ship the image (recommended)

Your laptop has more CPU and RAM than an Always Free VM and keeps the layer cache
warm between deploys. The VM only runs `docker load`, which costs it nothing.

First check the VM's architecture — SSH in and run `uname -m`. It gives `x86_64`
(E2 shapes) or `aarch64` (A1 shapes). If it matches your workstation, a plain
`docker build` works. If you are on x86 and the VM is arm64, cross-build with
`docker buildx build --platform linux/arm64 -t rummykub:latest --load .`
(needs Docker Desktop's containerd image store enabled).

The commands below are **PowerShell** (the default Windows shell). Run them from
the repo root.

```powershell
# 1. Build the image locally, stamping the git commit into it so /health can
#    report which version is deployed (see the verify step below).
docker build --build-arg GIT_SHA=$(git rev-parse --short HEAD) -t rummykub:latest .

# 2. Save it to a tarball. Use -o, NOT `| gzip` — PowerShell has no gzip and its
#    pipeline corrupts binary data. -o has docker write the file directly.
docker save rummykub:latest -o rummykub.tar          # ~220 MB, uncompressed

# 3. Copy the tarball AND the VM compose overlay to the VM. The overlay is a new
#    file; without it the compose command below fails with "no such file".
scp -i $key rummykub.tar                    ubuntu@<vm-ip>:~
scp -i $key deploy\docker-compose.vm.yml    ubuntu@<vm-ip>:~/RummyKub/deploy/
```

> **SSH key setup (first time).** `$key` is the path to your OCI private key,
> e.g. `$key = "C:\Users\<you>\.ssh\your-oracle-key"`. If `scp`/`ssh` reports
> *"Unprotected private key file"*, Windows permissions on the key are too open —
> lock it to just you, once:
> ```powershell
> icacls $key /inheritance:r
> icacls $key /grant:r "$($env:USERNAME):F"
> ```
> The key's passphrase is prompted separately. To avoid retyping it every command:
> `Start-Service ssh-agent; ssh-add $key` (then you can drop `-i $key`).

Then SSH in and load the image and start the stack. Docker on the VM needs root,
so use `sudo` (or add yourself to the `docker` group — see below):

```bash
ssh -i <key> ubuntu@<vm-ip>
# on the VM, from ~/RummyKub:
sudo docker load -i ~/rummykub.tar          # -> "Loaded image: rummykub:latest"
sudo docker images | grep rummykub          # confirm it's there before continuing
rm ~/rummykub.tar                           # cleanup (optional)

sudo docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.vm.yml up -d
```

`deploy/docker-compose.vm.yml` replaces the `build:` block with
`image: rummykub:latest`, so this starts from the loaded image with **no** build.
Keep passing both `-f` flags on every later compose command.

> **Skip `sudo` on Docker:** run `sudo usermod -aG docker $USER` once, then
> `newgrp docker` (applies the group to your current shell without logging out).
> After that, `docker ...` works without `sudo`.

**Verify:**
```bash
sudo docker ps                              # app + caddy both Up
wget -qO- http://localhost:8080/health      # -> {"status":"ok","version":"<sha>"}
```
The `version` is the git commit the image was built from. Compare it to
`git rev-parse --short HEAD` on your workstation — if they match, the VM is
running exactly what you built, and any staleness is browser cache, not the
deploy. This endpoint is JSON (not the cached SPA), so it always reflects reality.

**Update later:** repeat build → `docker save -o` → `scp` the tarball → `docker
load` → the same `up -d`. You only re-copy the overlay if it changed. The
previous image stays behind as a rollback — `docker images` shows it.

### 3f. Add swap (do this regardless)

A 954 MB box running Docker, Caddy, and the app has under 500 MB of headroom.
Swap is what keeps sshd answering when something spikes, so you don't get locked
out of your own VM:
```bash
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

**Logs / health:** (drop `sudo` if you added yourself to the `docker` group;
the VM images ship `wget`, not `curl`)
```bash
sudo docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.vm.yml logs -f app
wget -qO- http://localhost:8080/health
```

### Optional: use a registry (OCIR) instead of copying tarballs
The tarball in §3e ships ~100 MB on every deploy. A registry only transfers
changed layers, which is worth the setup if you deploy often:
```bash
docker buildx build --platform linux/amd64 \
  -t <region-key>.ocir.io/<namespace>/rummykub:latest --push .
```
On the VM, `docker login <region-key>.ocir.io` (username `<namespace>/<user>`,
password = Auth Token), set the same image in `deploy/docker-compose.vm.yml`,
drop `pull_policy: never`, and `up -d` (no `--build`).

Building in **GitHub Actions** and pushing to OCIR from there removes the manual
step entirely — the repo already has CI in `.github/workflows/ci.yml`.

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
