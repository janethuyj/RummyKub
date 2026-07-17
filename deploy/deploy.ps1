# RummyKub deployment — build the image locally, ship the tarball,
# docker load + compose up on the VM (per DEPLOYMENT.md §3e; the 1 GB VM
# never builds).
#
# Usage (from the repo root, Docker Desktop running):
#   .\deploy\deploy.ps1 -VmHost ubuntu@<VM_IP> -KeyPath C:\path\to\ssh_key
#
# NOTE: the VM stack also runs MahjongHu (majiang.helleon.com) via
# deploy/docker-compose.majiang.yml, shipped there by the MahjongHu repo's
# deploy script. This script includes that overlay automatically when it is
# present, so a rummykub deploy never removes the majiang service.

param(
    [Parameter(Mandatory = $true)] [string]$VmHost,
    [Parameter(Mandatory = $true)] [string]$KeyPath,
    # E2 shapes are x86_64 (linux/amd64). Pass linux/arm64 for A1 shapes.
    [string]$Platform = 'linux/amd64'
)

$ErrorActionPreference = 'Stop'
$sha = git rev-parse --short HEAD

Write-Host "== 1/4 Building image rummykub:latest ($sha, $Platform) ==" -ForegroundColor Cyan
docker build --platform $Platform --build-arg GIT_SHA=$sha -t rummykub:latest .
if ($LASTEXITCODE -ne 0) { throw "docker build failed" }

Write-Host "== 2/4 Saving image tarball ==" -ForegroundColor Cyan
docker save rummykub:latest -o rummykub.tar
if ($LASTEXITCODE -ne 0) { throw "docker save failed" }

Write-Host "== 3/4 Uploading tarball and compose files ==" -ForegroundColor Cyan
scp -i $KeyPath rummykub.tar "${VmHost}:~/"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }
scp -i $KeyPath deploy/docker-compose.yml deploy/docker-compose.vm.yml deploy/Caddyfile "${VmHost}:~/RummyKub/deploy/"
if ($LASTEXITCODE -ne 0) { throw "scp compose files failed" }

Write-Host "== 4/4 Loading image and restarting the stack on the VM ==" -ForegroundColor Cyan
ssh -i $KeyPath $VmHost 'sudo docker load -i ~/rummykub.tar && rm ~/rummykub.tar && cd ~/RummyKub && FLAGS="-f deploy/docker-compose.yml -f deploy/docker-compose.vm.yml" && if [ -f deploy/docker-compose.majiang.yml ]; then FLAGS="$FLAGS -f deploy/docker-compose.majiang.yml"; fi && sudo docker compose $FLAGS up -d && sudo docker exec deploy-caddy-1 caddy reload --config /etc/caddy/Caddyfile'
if ($LASTEXITCODE -ne 0) { throw "remote deploy failed" }

Write-Host "== Verifying ==" -ForegroundColor Cyan
ssh -i $KeyPath $VmHost "sudo docker exec deploy-caddy-1 wget -qO- http://app:8080/health"
Write-Host "`nDeployed $sha. Visit https://rummykub.helleon.com" -ForegroundColor Green
