# Blob Storage — Master Study Index & Interview Questions

One-stop index for **everything associated with Azure Blob/Storage** in this repo,
plus a complete self-test question bank. Use the checklist to see what's covered and
where; use the questions to drill. Playlist: `docs/STUDY_RESOURCES.md`.

## The full topic list (what "Blob Storage" encompasses)

### Core — blob types (`docs/BLOB_STORAGE.md`)
- [x] **Block blob** — general files (images, docs, backups); the 95% default
- [x] **Append blob** — append-only writes; logs/audit trails
- [x] **Page blob** — 512-byte-aligned random read/write; VM disks (VHDs)
- [x] Blob type is fixed at creation; the specialized clients (`BlockBlobClient` /
      `AppendBlobClient` / `PageBlobClient`)
- [x] Testing locally with **Azurite**

### Account & access (`docs/AZURE_STORAGE.md`)
- [x] Why Azure Storage (durability, decoupling, tiers, backbone of other services)
- [x] Storage **account** and its four services (Blob / Queue / Table / File)
- [x] **Private vs. anonymous** (public) container access
- [x] **Virtual directories** (flat namespace, `/` in names)
- [x] **Connecting**: connection string/key, SAS, Managed Identity, Storage Explorer
- [x] **Listing** blobs (flat + hierarchical by prefix/delimiter)
- [x] **SAS** (service / account / user-delegation)
- [x] **Accessing a blob from Postman** (SAS URL, or Entra ID bearer/JWT)

### Sibling storage services
- [x] **Storage Queues** — `docs/AZURE_STORAGE_QUEUE.md` (intro, advantages, fan-out,
      delete via PopReceipt, vs Service Bus)
- [x] **Azure Files** — `docs/AZURE_FILE_SHARE.md` (SMB share, mount, use cases)
- [ ] **Table Storage** — NoSQL key-value (mentioned; see also Cosmos in `docs/SQL_VS_COSMOS.md`)

### Blob-driven compute
- [x] **Blob trigger** & Event-Grid-based blob events — `docs/FUNCTION_APPS.md`

### Operational features (covered in the question bank below)
- [ ] Access **tiers** (Hot / Cool / Cold / Archive) & rehydration
- [ ] **Redundancy** (LRS / ZRS / GRS / RA-GRS)
- [ ] **Lifecycle management** policies
- [ ] **Soft delete**, **versioning**, **snapshots**
- [ ] **Leases** & optimistic concurrency (ETag / If-Match)
- [ ] **AzCopy**, static website hosting, CORS, custom metadata

*(The `[ ]` items are explained in the question bank below rather than in a dedicated
doc — they're covered here so this index is complete for interview prep.)*

---

## Interview question bank

### A. Blob types (answers in `docs/BLOB_STORAGE.md`)
1. What are the three Azure blob types and what's each one for?
2. Can you convert a block blob into a page blob, or vice versa?
3. Why can't you edit the middle of an append blob?
4. Why does a page blob require 512-byte-aligned offsets and lengths?
5. For typical user-uploaded files (PDFs, images), which blob type?
6. How do you test Blob Storage code locally without a real account? (Azurite)
7. What does `GetBlockBlobClient`/`GetAppendBlobClient`/`GetPageBlobClient` return?

### B. Account & access (answers in `docs/AZURE_STORAGE.md`)
8. Why put files in Azure Storage instead of the app server's disk?
9. Private vs. anonymous container access — what's safe?
10. There are no real folders — how do "directories" work?
11. What is a SAS and why use it over the account key?
12. How would you let someone download one private blob temporarily?
13. How do you access a blob from Postman? (SAS URL, or Entra ID bearer + `x-ms-version`)
14. Most secure way for an app in Azure to reach storage? (Managed Identity + RBAC)

### C. Storage Queues (answers in `docs/AZURE_STORAGE_QUEUE.md`)
15. Storage Queue vs. Service Bus queue — when pick which?
16. How does a Storage Queue avoid losing a message if the consumer crashes?
17. What's the PopReceipt for?
18. How do Storage Queues support a fan-out pattern?
19. Do Storage Queues have a dead-letter queue? (No — use DequeueCount)

### D. Azure Files (answers in `docs/AZURE_FILE_SHARE.md`)
20. What is Azure Files and how is it different from Blob Storage?
21. A scenario where Azure Files beats Blob?
22. How do you access an Azure file share? (SMB mount / SDK)
23. What is Azure File Sync?

### E. Operational features (answers below — not in a dedicated doc)

**Q24: What are the blob access tiers and when do you use each?**
Hot (frequent access, highest storage cost / lowest access cost), Cool (infrequent,
≥30 days), Cold (rarely, ≥90 days), Archive (offline, cheapest storage, ≥180 days but
must be **rehydrated** — hours — before reading). Set per-blob or as the account
default; move data down-tier as it ages to cut cost. Archive can't be read directly.

**Q25: What are the redundancy options?**
LRS (3 copies in one datacenter — cheapest, protects against disk/rack failure), ZRS
(3 copies across availability zones in one region), GRS (LRS + async copy to a paired
region — regional-disaster protection), RA-GRS (GRS where the secondary region is also
readable). Choose by how much geographic/zone failure you must survive vs. cost.

**Q26: What is lifecycle management?**
Rule-based automation on a storage account that transitions blobs between tiers or
deletes them based on age/last-modified/last-access — e.g. "move to Cool after 30
days, Archive after 90, delete after 365." Automates cost optimization without code.

**Q27: What's the difference between soft delete, versioning, and snapshots?**
Soft delete keeps deleted blobs recoverable for a retention window instead of removing
them immediately (protects against accidental delete/overwrite). Versioning
automatically keeps a new version of a blob on every write, so you can restore prior
content. A snapshot is a manual, read-only point-in-time copy of a blob you create
explicitly. Versioning is automatic-per-write; snapshots are on-demand.

**Q28: How do you handle concurrent writes to the same blob (optimistic concurrency)?**
Each blob has an **ETag** that changes on every write. Read it, then write with an
`If-Match: <etag>` condition — if someone else changed the blob in between, the ETag
won't match and your write fails (412 Precondition Failed) instead of silently
clobbering theirs. For exclusive access you can also take a **lease** (a lock with a
duration) so only the lease holder can write.

**Q29: What is a blob lease?**
A short-term or infinite lock on a blob (or container). While held, only the lease
holder can write/delete; others get 409/412. Used to coordinate exclusive access — a
classic example is a single-instance "leader election" where whoever holds the lease
is the active worker.

**Q30: What is AzCopy?**
A command-line tool for high-performance, parallel copy/sync of blobs (and files) —
between local ↔ storage, or storage ↔ storage across accounts/regions. The go-to for
bulk data movement and migrations (faster and more resilient than the SDK for large
transfers).

**Q31: Can Blob Storage host a static website?**
Yes — enable the account's **static website** feature and it serves content from a
special `$web` container over an HTTPS endpoint (with index/error document support).
Cheap hosting for SPAs/static sites, usually fronted by Azure CDN or Front Door.

**Q32: What is CORS on a storage account, and why would you need it?**
Cross-Origin Resource Sharing rules that let a browser on one origin call the storage
REST endpoint on another (e.g. a SPA uploading directly to blob via a SAS). Without a
CORS rule allowing the origin/method, the browser blocks the request.

**Q33: How do you attach custom metadata to a blob, and how is it different from properties?**
Blobs carry system **properties** (content type, length, etc.) and user-defined
**metadata** — arbitrary `x-ms-meta-*` key/value pairs you set (`SetMetadataAsync`).
Metadata is your app's tags on the blob; system properties are broker/HTTP-level
attributes.

**Q34: What's the difference between the storage account kinds / performance tiers?**
General-purpose v2 (standard, the default — all services, all tiers) vs. Premium
(SSD-backed, low latency — Premium block blob, page blob, or file share variants).
Choose Premium for high-transaction/low-latency workloads, GPv2 standard for everything
else.

---

## How to use this

- **Learning:** read the linked topic doc for each section (they have the plain-English
  explanations + code + repo references).
- **Drilling:** cover the answers and self-test with the numbered questions above —
  A–D map to the four storage docs; E is answered inline here.
- **Honesty note:** what was actually *run* (Blob types via Azurite) vs. reference-only
  is tracked in `docs/ARCHITECTURE.md`.
