# Azure Storage Data Services — Blob, File, Queue, Table, Disk (+ Interview Questions)

One consolidated map of the five data services you'll be asked about, with plain-English
explanations and an interview question bank per service. The deep per-service docs are
cross-linked; this doc is the "see them all side by side + drill the questions" view.

> **Account vs. Disk — the framing that trips people up.** A **storage account** contains
> **four** services: **Blob**, **File**, **Queue**, **Table**. **Azure Disk (Managed Disks)**
> is a *separate* top-level resource used by VMs — it is **not** one of the four services in a
> storage account. Historically an unmanaged VM disk was literally a **page blob** in your own
> storage account; Managed Disks moved that under Azure's control, but "a disk is page-blob-shaped
> storage" is still the right mental model. See the Disk section below.

---

## 0. What is a BLOB? (Binary Large Object)

**BLOB = Binary Large Object** — an unstructured file. Azure doesn't know or care whether the
bytes are a JPEG, a PDF, a log file, or a virtual disk. What it *does* care about is **how you
intend to write to it**, and that is the entire reason there are **three blob types**, fixed at
creation and not convertible afterward:

| Blob type | Write pattern | Built for | Analogy | This repo |
|-----------|---------------|-----------|---------|-----------|
| **Block blob** | Write-once / replace whole; read whole or in ranges | Images, docs, backups — the 95% default | A photograph (replace the whole photo, don't edit one pixel in place) | `UploadBlockBlobAsync` |
| **Append blob** | Append to the **end only**; can't edit earlier bytes | Logs, audit trails (guarantee: nothing already written is silently altered) | A receipt printer (paper only comes out the end) | `AppendLineAsync` |
| **Page blob** | Random read/write at **512-byte-aligned** offsets; fixed size pre-allocated | VM disks (VHDs), sparse/random-access files | A hard-drive platter (seek to any sector, rewrite it) | `CreatePageBlobAsync` / `WritePageAsync` |

Full explanation + verified-working code: `docs/BLOB_STORAGE.md`.

---

## 1. Azure Blob Storage

**What it is:** massively scalable **object storage** for unstructured files, addressed by URL
(`https://<account>.blob.core.windows.net/<container>/<blob>`). Organized as *account → container →
blob*; the namespace is **flat** (folders are virtual, implied by `/` in blob names).

**When to use:** storing/serving files — user uploads, images, documents, backups, big data,
static website assets, data-lake storage.

**Key facts to know:** three blob types (above); access **tiers** (Hot / Cool / Cold / Archive);
**redundancy** (LRS / ZRS / GRS / RA-GRS); **SAS** for scoped temporary access; **lifecycle**
policies; **soft delete / versioning / snapshots**. Account & access details: `docs/AZURE_STORAGE.md`.

**Interview questions**
1. What does BLOB stand for, and what are the three blob types? *(Binary Large Object; block/append/page.)*
2. Can you convert a block blob to a page blob? *(No — type is fixed at creation.)*
3. For typical user uploads (PDFs/images), which type? *(Block blob.)*
4. There are no real folders — how do directories work? *(Virtual, via `/` in names + prefix/delimiter listing.)*
5. How do you give someone temporary read access to one private blob? *(A short-lived read SAS URL — don't make it public or share the key.)*
6. What are the access tiers and when use each? *(Hot/Cool/Cold/Archive by access frequency; Archive is offline and must be rehydrated.)*

---

## 2. Azure Files

**What it is:** a fully-managed **file share** you access over the standard **SMB** (and NFS)
protocol — so it looks like a normal network drive (`\\account.file.core.windows.net\share`) that
you can **mount** on Windows/Linux/macOS with a drive letter, or reach via REST/SDK. Deep dive:
`docs/AZURE_FILE_SHARE.md`.

**When to use:** **lift-and-shift** apps that expect a real filesystem / UNC path; **shared config
or content** across many servers; replacing an on-prem file server. The differentiator vs. Blob:
Azure Files is mountable and POSIX/SMB filesystem-shaped; Blob is object storage reached by URL/SDK.

**Key facts:** true directory hierarchy (unlike Blob's virtual folders); mountable by multiple VMs
at once; **Azure File Sync** caches a share on-prem and syncs to the cloud; auth via account
key/SAS or Entra ID for SMB.

**Interview questions**
7. Azure Files vs. Blob Storage — the core difference? *(Files = mountable SMB/NFS share with real folders; Blob = object storage by URL, virtual folders.)*
8. A scenario where Files beats Blob? *(A legacy app that reads/writes a UNC path or drive letter and can't be rewritten to call an SDK.)*
9. How do you access an Azure file share? *(Mount via SMB with drive letter, or SDK/REST.)*
10. What is Azure File Sync? *(Syncs an on-prem Windows Server file cache with a cloud share — cloud tiering + centralization.)*

---

## 3. Azure Queue Storage

**What it is:** a **simple, massive-scale message queue** inside the storage account — a producer
`PostMessage`s, a consumer reads and then deletes. Messages up to 64 KB, queue up to the account
limit (huge). Deep dive: `docs/AZURE_STORAGE_QUEUE.md`.

**When to use:** basic **decoupling / async work offload / fan-out** where you don't need advanced
messaging features. When you need sessions, topics/subscriptions, dead-letter queues, transactions,
duplicate detection, or FIFO — reach for **Service Bus** instead (`docs/MESSAGING_COMPARISON.md`).

**Key facts (the "gotcha" ones):**
- **Visibility timeout + PopReceipt:** reading a message hides it (doesn't delete it); the consumer
  must call delete with the **PopReceipt** to remove it. If the consumer crashes before deleting,
  the message *reappears* after the timeout — that's the at-least-once safety net.
- **No dead-letter queue** — you track poison messages yourself via **DequeueCount**.
- **At-least-once** delivery → consumers should be **idempotent**.

**Interview questions**
11. Storage Queue vs. Service Bus queue — when pick which? *(Storage Queue = simple/cheap/huge; Service Bus = topics, sessions, DLQ, transactions, ordering.)*
12. How does a Storage Queue avoid losing a message if the consumer crashes? *(Read hides via visibility timeout; message reappears if not deleted with its PopReceipt.)*
13. What is the PopReceipt for? *(Proof-of-read token required to delete the specific message you dequeued.)*
14. Do Storage Queues have a dead-letter queue? *(No — use DequeueCount to detect poison messages.)*

---

## 4. Azure Table Storage

**What it is:** a **NoSQL key-value / wide-column** store for massive amounts of **structured,
non-relational** data — schemaless rows (entities) with properties. Every entity is keyed by a
**PartitionKey + RowKey** pair, which together form the primary key and drive scalability.

**When to use:** cheap, huge-scale structured data with simple lookup patterns — device/IoT
telemetry, user profiles, metadata, address books. **No** joins, **no** complex queries, **no**
stored procedures. When you outgrow it or need richer querying/global distribution, the successor
is **Azure Cosmos DB (Table API)** — same programming model, more features (`docs/SQL_VS_COSMOS.md`).

**Key facts:**
- **PartitionKey** groups entities served together (the unit of scale/transactions); **RowKey** is
  unique within a partition. Choosing these well is the whole performance game.
- Fast queries = **point query** on PartitionKey + RowKey. Querying on other properties = a slow
  table scan.
- **Entity Group Transactions** are supported only *within a single PartitionKey*.
- Schemaless: different rows in the same table can have different columns.

**Interview questions**
15. What kind of database is Table Storage? *(NoSQL key-value / wide-column, schemaless.)*
16. What's the primary key made of? *(PartitionKey + RowKey.)*
17. Why does PartitionKey choice matter so much? *(It's the unit of partitioning/scale and the only scope for batch transactions; bad keys → hot partitions and table scans.)*
18. Table Storage vs. Cosmos DB Table API? *(Same model; Cosmos adds global distribution, guaranteed low latency, richer indexing/throughput — at higher cost.)*
19. Table Storage vs. Azure SQL? *(Table = NoSQL, no joins/relations/schema, cheap + huge scale; SQL = relational, joins, ACID, complex queries.)*

---

## 5. Azure Disk (Managed Disks)

**What it is:** **block-level storage volumes** attached to Azure **VMs** — the VM's OS disk and
data disks. "**Managed**" means Azure handles the underlying storage account/placement for you; you
just pick a size and performance tier. Under the covers this is **page-blob-shaped** random-access
storage (an unmanaged disk *was* literally a page blob in your own account).

**Note on framing:** unlike the four above, a Managed Disk is **not** a service *inside* a storage
account — it's its own resource type, tied to a VM. It shows up on this list because "disk =
page blob = the random-access member of the storage family" is a classic interview connection.

**When to use:** you need a real **block device** for a VM — OS disk, database data files, anything
that wants a mounted filesystem with low-latency random I/O. (For a *shared network* filesystem
across machines, that's **Azure Files**, not a disk.)

**Key facts:**
- **Disk types by performance:** **Ultra Disk** > **Premium SSD (v2)** > **Standard SSD** >
  **Standard HDD** — decreasing IOPS/throughput and cost.
- **Managed vs. unmanaged:** managed is the default and recommended (Azure owns the storage account);
  unmanaged = you manage a page blob in your own account (legacy).
- **Snapshots & images** for backup/cloning; disks are zone/region-redundant options too (e.g. ZRS disks).
- One disk attaches to **one VM** (except shared-disk configurations for clustering); it is *not* a
  general file-sharing mechanism.

**Interview questions**
20. What is an Azure Managed Disk and what's it for? *(Block storage volume for a VM's OS/data disks; Azure manages the backing storage.)*
21. How does a disk relate to blobs? *(A VM disk is page-blob-shaped random-access storage; unmanaged disks were literally page blobs.)*
22. Managed vs. unmanaged disks? *(Managed = Azure handles the storage account/placement, the default; unmanaged = you manage a page blob yourself, legacy.)*
23. Disk performance tiers? *(Ultra > Premium SSD > Standard SSD > Standard HDD, by IOPS/throughput/cost.)*
24. Disk vs. Azure Files — when which? *(Disk = single-VM block device, low-latency random I/O; Files = shared SMB/NFS share mountable by many machines.)*

---

## Side-by-side comparison

| Service | Data shape | Access | Primary use | Successor / sibling |
|---------|-----------|--------|-------------|--------------------|
| **Blob** | Unstructured files (objects) | URL / SDK / SAS | Store & serve files, backups, static sites | Data Lake Gen2 (hierarchical namespace) |
| **File** | Filesystem (real folders) | **SMB/NFS mount** or SDK | Lift-and-shift, shared network drive | Azure File Sync |
| **Queue** | Small messages (≤64 KB) | SDK / REST | Simple async decoupling, fan-out | **Service Bus** (advanced messaging) |
| **Table** | NoSQL key-value entities | SDK / REST (PartitionKey+RowKey) | Cheap huge-scale structured data | **Cosmos DB Table API** |
| **Disk** | Block device (sectors) | Attached to a **VM** | VM OS/data disks, low-latency random I/O | (page blob under the hood) |

**The one-liner map:** Blob = *files by URL* · File = *mountable share* · Queue = *simple messages* ·
Table = *NoSQL rows* · Disk = *a VM's hard drive*.

## Cross-links
- Blob types & verified code: `docs/BLOB_STORAGE.md`
- Account, access, SAS, Postman: `docs/AZURE_STORAGE.md`
- Queue deep dive: `docs/AZURE_STORAGE_QUEUE.md`
- Azure Files deep dive: `docs/AZURE_FILE_SHARE.md`
- Table vs. relational / Cosmos: `docs/SQL_VS_COSMOS.md`
- Queue vs. Service Bus messaging: `docs/MESSAGING_COMPARISON.md`
- Master index & full question bank: `docs/BLOB_STORAGE_STUDY_INDEX.md`
