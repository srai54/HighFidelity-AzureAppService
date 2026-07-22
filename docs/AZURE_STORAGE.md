# Azure Storage — Account, Access, SAS & Connecting

The account-level concepts behind Blob Storage. Blob *types* (Block/Append/Page) are
in `docs/BLOB_STORAGE.md`; this doc covers the storage **account**, access control,
SAS, virtual directories, listing, and how to reach a blob from tools like Postman.

## Why use Azure Storage at all?

Because files/data don't belong on your app server's local disk:
- **Durability & scale** — data is replicated (LRS/ZRS/GRS) and effectively
  unlimited; you don't manage disks. An App Service instance can be recycled or
  scaled out at any time, taking its local disk with it — Storage persists
  independently.
- **Decoupling** — many app instances (and Functions, and other services) share the
  same storage; nothing is pinned to one machine.
- **Cheap & tiered** — pay for what you use, with Hot/Cool/Cold/Archive tiers to
  trade access speed for lower cost on rarely-touched data.
- **It's the backbone of other Azure features** — Function Apps require a storage
  account, diagnostic logs land in it, and it underpins VM disks (page blobs).

A **storage account** is the top-level resource, and it contains four services:
**Blob** (files/objects), **Queue** (simple messaging — see
`docs/AZURE_STORAGE_QUEUE.md`), **Table** (NoSQL key-value), and **File** (SMB file
shares — see `docs/AZURE_FILE_SHARE.md`).

## Private vs. anonymous (public) access

A blob **container's** access level controls who can read it *without credentials*:
- **Private (no anonymous access)** — the default and the safe choice. Every request
  must be authorized (account key, SAS, or Entra ID/Managed Identity). Nothing is
  readable by an anonymous URL.
- **Blob (anonymous read for blobs)** — anyone with the blob's URL can read that
  blob, but can't list the container.
- **Container (anonymous read + list)** — anyone can read blobs *and* enumerate the
  container's contents.

Interview point: **default to Private.** Anonymous access is convenient for genuinely
public assets (a public website's images), but it's a common security misconfiguration
— a "Container" public setting leaks your whole file list. When you need to give
*specific, temporary* access to a private blob, you don't make it public — you issue
a **SAS** (below). Note: many accounts now disable anonymous access at the account
level entirely (`allowBlobPublicAccess = false`) as a guardrail.

## Virtual directories

Blob Storage is **flat** — a container holds blobs, with no real folders. But blob
*names* can contain `/`, and tools (Portal, Storage Explorer, the SDK) interpret those
slashes as a **virtual directory** hierarchy. So a blob named
`invoices/2026/07/inv-1001.pdf` *looks* like it's in folders, but there is no
`invoices` folder object — it's all in the blob's name. Consequences:
- You "list a directory" by listing blobs **with a prefix** (`invoices/2026/`) and a
  delimiter (`/`) — the SDK returns the matching blobs plus the virtual sub-prefixes.
- There are no empty folders — a "folder" exists only as long as some blob's name
  starts with that prefix.

## Connecting to a storage account

Four common ways, roughly least → most secure:
1. **Connection string / account key** — `DefaultEndpointsProtocol=...;AccountName=...;AccountKey=...`.
   Simple, but the account key is a god-key (full access to everything) — guard it,
   rotate it, don't commit it. Local dev uses Azurite's well-known
   `"UseDevelopmentStorage=true"` (see `docs/BLOB_STORAGE.md`).
2. **SAS token** — a scoped, time-limited, signed URL/token (below). Hand *this* out,
   not the account key.
3. **Entra ID + Managed Identity / `DefaultAzureCredential`** — no key at all; grant
   the identity a role like `Storage Blob Data Contributor`. Preferred in Azure
   (same pattern as Key Vault — see `docs/MANAGED_IDENTITY_ENTRA_ID.md`):
   ```csharp
   var client = new BlobServiceClient(
       new Uri("https://<account>.blob.core.windows.net"), new DefaultAzureCredential());
   ```
4. **Azure Storage Explorer** (desktop app) or the **Portal** — connect with your
   login, a connection string, or a SAS, to browse/upload/download by hand — great
   for inspecting what your code wrote.

## Listing blobs

You enumerate a container rather than "opening a folder":
```csharp
BlobContainerClient container = blobServiceClient.GetBlobContainerClient("app-blobs");

// flat list of everything in the container:
await foreach (BlobItem blob in container.GetBlobsAsync())
    Console.WriteLine(blob.Name);   // e.g. "invoices/2026/07/inv-1001.pdf"

// "directory" listing — by prefix + delimiter (virtual folders):
await foreach (var item in container.GetBlobsByHierarchyAsync(prefix: "invoices/2026/", delimiter: "/"))
    Console.WriteLine(item.IsPrefix ? $"[dir] {item.Prefix}" : $"      {item.Blob.Name}");
```
This repo lists via `BlobStorageService.ListBlobsAsync` (`GET /api/blobs`) — see
`docs/BLOB_STORAGE.md`.

## Shared Access Signature (SAS)

A **SAS** is a signed token that grants **scoped, time-limited** access to storage
*without* handing over the account key. It encodes: what resource, which permissions
(read/write/list/delete…), a start/expiry time, allowed IPs/protocols — all signed so
it can't be tampered with. Three kinds:
- **Service SAS** — access to one service's resource (a specific blob or container),
  signed with the account key.
- **Account SAS** — broader; spans services/resource types on the account.
- **User-delegation SAS** — signed with an **Entra ID** credential (Managed Identity)
  instead of the account key — the **most secure**, because no account key is
  involved at all. Preferred when you can.

```csharp
// Service SAS for one blob, read-only, valid 1 hour:
BlobClient blob = container.GetBlobClient("invoices/2026/07/inv-1001.pdf");
if (blob.CanGenerateSasUri)
{
    var sas = new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1))
    {
        BlobContainerName = container.Name,
        BlobName = blob.Name
    };
    Uri sasUri = blob.GenerateSasUri(sas);   // a full https URL with a ?sv=...&sig=... token
}
```
The result is a normal HTTPS URL with the SAS as query string — anyone with that URL
can do exactly what the SAS allows, until it expires. Least-privilege + short expiry
is the rule (a leaked SAS is only useful until it lapses). SAS is exactly how you give
temporary read access to a *private* blob instead of making it public.

## Accessing a blob from Postman

Because a SAS URL is just an authorized HTTPS endpoint, you can hit a private blob
from Postman (or curl, or a browser) without any SDK:
- **With a SAS token (simplest):** generate a read SAS URL (above), then in Postman do
  a **GET** on that full URL (`https://<acct>.blob.core.windows.net/<container>/<blob>?sv=...&sig=...`).
  No auth header needed — the signature *is* the auth. This is the usual "download a
  private blob from a tool" path.
- **With an Entra ID bearer (JWT) token:** blobs also support OAuth2. Get a token for
  the storage resource, e.g. `az account get-access-token --resource https://storage.azure.com/`,
  then in Postman set headers:
  - `Authorization: Bearer <token>`
  - `x-ms-version: 2021-08-06` (a recent API version — **required** for the
    OAuth path)
  and GET `https://<acct>.blob.core.windows.net/<container>/<blob>`. The identity
  behind the token needs a data-plane role (e.g. `Storage Blob Data Reader`).

Rule of thumb: **SAS** for handing a specific blob to a client/tool temporarily;
**bearer/JWT** when the caller is an Entra ID identity that already has an RBAC role.

## What's real vs. reference-only in this repo

Blob operations (upload/list/download for all three types) were genuinely tested
against **Azurite** (see `docs/BLOB_STORAGE.md` / `docs/ARCHITECTURE.md`). The SAS,
Entra-ID/Postman, and access-level specifics here are accurate to the real APIs but
weren't exercised against a real cloud account in this environment.

---

## Interview Questions

**Q: Why put files in Azure Storage instead of the app server's disk?**
Durability (replicated, not tied to one VM), shared access across many app/Function
instances, effectively unlimited scale with cost tiers, and independence from the
app's lifecycle (an App Service instance's local disk vanishes when it's recycled or
scaled). Storage persists regardless.

**Q: Private vs. anonymous container access — what's safe?**
Private (default) requires authorization for every request — the safe choice. "Blob"
allows anonymous read of individual blobs by URL; "Container" also allows anonymous
listing (leaks your file inventory). Default to Private and grant temporary access via
SAS rather than making things public; many accounts disable public access at the
account level entirely.

**Q: There are no real folders in Blob Storage — how do "directories" work?**
The namespace is flat; folders are *virtual*, implied by `/` in blob names. You "list a
folder" by listing blobs with a prefix + delimiter. There are no empty folders — a
prefix exists only while some blob's name uses it.

**Q: What is a SAS and why use it over the account key?**
A Shared Access Signature is a signed token granting scoped, time-limited, specific
permissions to storage without exposing the account key (which grants everything). You
hand out a SAS (ideally short-lived, least-privilege), not the key. A user-delegation
SAS (signed by Entra ID, no account key at all) is the most secure variant.

**Q: How would you let someone download one private blob temporarily?**
Generate a read-only Service (or user-delegation) SAS URL for that blob with a short
expiry and give them the URL — they GET it directly (browser/Postman/curl), no
credentials needed, and access lapses automatically. You never make the blob public or
share the account key.

**Q: How do you access a blob from Postman?**
Either GET a SAS URL (the signature is the auth — no header needed), or use OAuth2:
send `Authorization: Bearer <token>` (from `az account get-access-token --resource
https://storage.azure.com/`) plus an `x-ms-version` header, with the token's identity
holding a Storage Blob Data role.

**Q: What's the most secure way for an app in Azure to reach storage?**
Managed Identity + `DefaultAzureCredential` with an RBAC data role (e.g. Storage Blob
Data Contributor) — no connection string or account key stored anywhere, same benefit
as with Key Vault.
