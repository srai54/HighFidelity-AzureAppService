# Azure Files (File Share)

The fourth service in a storage account (with Blob/Queue/Table). Where Blob Storage is
object storage accessed over its own REST API, **Azure Files is a real network file
share** you can mount like a drive.

## The concept

**Azure Files** gives you a fully-managed file share in the cloud that speaks the
**standard SMB** protocol (and NFS on Premium) — the exact same protocol Windows/Linux
use for network drives on-prem. So you can **mount it as a drive letter** (`Z:\`) or a
mount point (`/mnt/share`) on a VM, an on-prem machine, or an App Service, and read/write
files with ordinary file I/O (`System.IO.File`, `copy`, Explorer) — no special SDK
needed. It behaves like a shared folder that many machines can attach to at once.

Hierarchy: storage account → **file share** → real **directories** → files. Unlike Blob
Storage's *virtual* directories (`docs/AZURE_STORAGE.md`), Azure Files has **actual
directory objects**.

## Blob vs. Azure Files — the key distinction

| | Blob Storage | Azure Files |
|---|---|---|
| Access | REST API / SDK (`BlobClient`) | **SMB/NFS — mount as a drive**, plus REST |
| Looks like | Object store (containers + blobs) | A **file system** (shares + real folders) |
| Best for | App-managed objects: images, backups, big data, static assets | **Lift-and-shift** apps that expect a file path; shared config/files across machines |
| Folders | Virtual (implied by `/` in name) | Real directories |
| Code | You rewrite file access to use the Blob SDK | Existing `File.ReadAllText(@"Z:\config.json")` code works unchanged |

The mental rule: if your app (or a legacy app) already reads/writes **file paths** and
you don't want to rewrite it to call a storage SDK, **Azure Files** lets it keep using
paths against a mounted share. If you're writing new code that manages objects, **Blob**
is usually the better/cheaper fit.

## Use cases

- **Lift-and-shift / legacy apps** — an app hardcoded to read from `\\server\share\...`
  or `Z:\` keeps working by mounting an Azure file share at that path; no code change.
- **Shared configuration/content across instances** — several App Service or VM
  instances mounting the same share to read common files (templates, certs, shared
  assets).
- **Replacing an on-prem file server** — with **Azure File Sync**, an on-prem Windows
  Server caches a hot subset while the cloud share is the authoritative full copy.
- **Shared tools/dev artifacts** — a common drive for teams or build agents.
- **Container persistent volumes** — mounting a file share into containers (ACI/AKS)
  that need shared, persistent storage.

## Demo — create and mount

**Create a share (CLI):**
```bash
az storage share-rm create \
  --resource-group "$RG" --storage-account "$STORAGE" \
  --name "appshare" --quota 5      # 5 GiB
```

**Mount it on Windows** (the Portal's "Connect" button on a file share generates this
exact script, including the account key as the password):
```powershell
net use Z: \\<account>.file.core.windows.net\appshare /user:Azure\<account> <account-key>
# now Z:\ is the share — use it like any drive:
Set-Content Z:\hello.txt "written to an azure file share"
```

**Mount on Linux:**
```bash
sudo mount -t cifs //<account>.file.core.windows.net/appshare /mnt/appshare \
  -o username=<account>,password=<account-key>,serverino
```

**From .NET via the SDK** (when you can't mount, e.g. you want REST access):
```csharp
var share = new ShareClient(connectionString, "appshare");
await share.CreateIfNotExistsAsync();
ShareDirectoryClient dir = share.GetRootDirectoryClient();
ShareFileClient file = dir.GetFileClient("hello.txt");
// upload/download via file.UploadAsync / file.DownloadAsync
```
(Package: `Azure.Storage.Files.Shares`.)

## What's real vs. reference-only in this repo

This repo doesn't use Azure Files (it uses Blob Storage). This is a reference doc; the
CLI/mount/SDK commands are accurate to the real service.

---

## Interview Questions

**Q: What is Azure Files and how is it different from Blob Storage?**
Azure Files is a managed file share that speaks SMB (and NFS on Premium), so you can
mount it as a network drive and use ordinary file I/O and real directories. Blob
Storage is object storage accessed via its own REST API/SDK with only virtual folders.
Files is for apps that expect file paths; Blob is for app-managed objects.

**Q: Give a scenario where Azure Files is the right choice over Blob.**
Lift-and-shift of a legacy app that reads/writes a UNC path or drive letter — mount an
Azure file share at that path and the app works unchanged, versus rewriting all its
file access to the Blob SDK. Also: multiple instances needing to share the same files
via a mounted drive.

**Q: How do you access an Azure file share?**
Mount it over SMB (a `net use Z:` on Windows or `mount -t cifs` on Linux, authenticated
with the account key or Entra ID/Kerberos), or use the `Azure.Storage.Files.Shares` SDK
for REST access. The Portal's "Connect" button generates the mount script for you.

**Q: What is Azure File Sync?**
A service that syncs an on-prem Windows Server's folder with an Azure file share,
caching frequently-used files locally (cloud tiering) while the cloud share holds the
full authoritative copy — effectively turning the cloud share into the backing store
for an on-prem file server, with local speed for hot data.
