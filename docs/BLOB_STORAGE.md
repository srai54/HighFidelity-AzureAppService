# Azure Blob Storage — Block, Append, and Page Blobs

## The plain-English version

"Blob" just means "an unstructured file" — Azure doesn't know or care if it's a JPEG, a PDF, a log file, or a virtual disk. What Azure *does* care about is **how you intend to write to it**, and that's the entire reason there are three distinct blob types instead of one. This is the single most commonly misunderstood part of Blob Storage: people assume "blob" is one thing with optional settings, when it's actually three genuinely different underlying storage structures, chosen at creation time and not changeable afterward.

- **Block blob** — write it once (or replace it entirely), read it whole or in ranges. This is 95% of what you'll ever use: images, documents, backups, any "just store this file" scenario.
- **Append blob** — write is restricted to *adding to the end only*. You cannot edit byte 500 of an append blob; you can only add new bytes after whatever's already there. Built for one job: log files and audit trails, where the guarantee "nothing already written can be silently altered" is the actual feature.
- **Page blob** — random read/write access at 512-byte-aligned offsets, pre-allocated to a fixed size up front. This is what Azure VM disks (VHDs) are built on under the hood — a page blob is, functionally, a virtual hard disk. Almost nobody reaches for this directly in typical app code; it exists because *something* needs to back "a disk you can seek to any offset in and rewrite."

## The analogy that makes it click

- A **block blob** is a photograph — you take it, you can replace the whole thing with a new photo later, but you don't "edit pixel 400 of the existing photo in place."
- An **append blob** is a receipt printer — paper only ever comes out the end, you can tear off and read what's printed so far, but you can never go back and change a line that already printed.
- A **page blob** is a hard drive platter — you can jump to any specific spot and read or write exactly that spot, as long as you address it in fixed-size chunks (512 bytes), which is exactly how a real disk works at the sector level.

## How this repo implements it — and actually verified working

**`src/WebApp/Services/BlobStorageService.cs`** exposes all three through distinct methods rather than one generic "upload" method, specifically so the difference stays visible in the API surface instead of being hidden behind a single method with a type parameter:

```csharp
Task UploadBlockBlobAsync(...)   // BlockBlobClient
Task AppendLineAsync(...)        // AppendBlobClient
Task CreatePageBlobAsync(...)    // PageBlobClient
Task WritePageAsync(...)         // PageBlobClient
```

All three were run for real against **Azurite** (Microsoft's official local Storage emulator — installed here via `npm install -g azurite`, not a real Azure Storage account) and confirmed working end-to-end:

```
POST /api/blobs/block/hello3.txt   -> {"blobName":"hello3.txt","type":"Block","sizeBytes":22}
POST /api/blobs/append/audit.log   -> {"blobName":"audit.log","type":"Append","appended":"line one"}
                                    -> {"blobName":"audit.log","type":"Append","appended":"line two"}
POST /api/blobs/page/disk.vhd?sizeInBytes=1024 -> {"blobName":"disk.vhd","type":"Page","sizeBytes":1024}

GET /api/blobs           -> ["audit.log","disk.vhd","hello3.txt"]
GET /api/blobs/hello3.txt -> "hello from block blob"
GET /api/blobs/audit.log  -> "line one\nline two"
```
The append blob's two lines both landed, in order, in a single blob — proving the append-only write actually accumulated rather than overwriting. This is genuinely tested, not just written to look plausible.

## The 512-byte rule — the specific gotcha worth remembering

Page blobs are pre-allocated in fixed pages, and both the blob's total size and every write's offset/length **must be a multiple of 512 bytes**. This repo's `WritePageAsync` throws an `ArgumentException` up front if you violate that, rather than letting a confusing Azure error surface later:
```csharp
if (offset % 512 != 0 || data.Length % 512 != 0)
    throw new ArgumentException("Page blob writes must start at a 512-byte-aligned offset and be a multiple of 512 bytes long.");
```
This single constraint is *why* page blobs aren't used for general file storage — nobody wants to think about 512-byte alignment to store a PDF. It only makes sense when the thing you're storing is itself naturally block-addressed, like a virtual disk.

## What's real vs. reference-only in this repo

Fully real — this is the one topic in this repo that was tested end-to-end, not just written to compile. See `docs/ARCHITECTURE.md` for the full picture of what was and wasn't verified across all five topics.

---

## Interview Questions

**Q: What are the three Azure blob types and what's each one actually for?**
Block blobs — general file storage, upload as a whole or in committed blocks, the default choice for almost everything (images, documents, backups). Append blobs — append-only writes, built for logs/audit trails where you need a guarantee that existing content can't be silently rewritten. Page blobs — random 512-byte-aligned read/write access, pre-allocated to a fixed size; this is what Azure VM disks (VHDs) are built on.

**Q: Can you convert a block blob into a page blob, or vice versa?**
No. The blob type is chosen at creation and is fixed — it's not a setting you toggle, it's a fundamentally different underlying storage structure. If you need different access semantics, you create a new blob of the right type and migrate the data.

**Q: Why can't you edit the middle of an append blob?**
Because that's the entire feature, not a limitation to work around. Append-only means anything already written is guaranteed never to change — exactly the property you want from an audit log or telemetry stream, where "can this log entry have been silently altered after the fact" needs to be a hard no.

**Q: Why does a page blob require 512-byte-aligned offsets and lengths?**
Because it mirrors how physical disks address storage — in fixed-size sectors. A page blob functions as a virtual hard disk (this is literally the storage layer underneath an Azure VM's VHD), so it inherits a disk's addressing constraints. Any write must specify a page-aligned offset and a page-aligned length; Azure will reject anything else.

**Q: If you're just storing user-uploaded files (PDFs, images) for a typical web app, which blob type do you use?**
Block blob, essentially always. Append and page blobs solve specific problems (append-only logs; disk-like random access) that general file storage doesn't have.

**Q: How would you actually test Blob Storage code locally without a real Azure Storage account?**
Azurite — Microsoft's official local Storage emulator, which speaks the same REST API surface as real Azure Storage for Blob/Queue/Table. Point `BlobServiceClient` at the well-known connection string `"UseDevelopmentStorage=true"` and the exact same SDK code runs against Azurite instead of a real account — no code difference, no test doubles, no mocking, the real client hitting a real (local) server.

**Q: What actually happens when you call BlobContainerClient.GetBlockBlobClient / GetAppendBlobClient / GetPageBlobClient?**
Each returns a specialized client scoped to that specific blob type's operations — `BlockBlobClient` exposes block-commit APIs, `AppendBlobClient` exposes `AppendBlockAsync`, `PageBlobClient` exposes `UploadPagesAsync`/`CreateAsync(size)`. You pick which specialized client to ask for based on what you're storing; the container itself doesn't enforce or care which blob types live inside it.
