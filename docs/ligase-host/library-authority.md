# Ligase Host library authority

Status: implemented in the Host P1 authority boundary.

## Ownership

The desktop product library has one writer: the .NET
`LibraryMutationCoordinator`. The C++ streaming core consumes the generated
`ligase-sync.json` and `apps.json` projections. The C++ core does not expose a
library mutation API.

The desktop may write only when all of the following identify the one core it
started:

- the original process handle is alive and its creation time is unchanged;
- the expected base port has exactly one Ligase core;
- loopback `serverinfo` returns the expected Host UUID;
- a per-start nonce, authority token, and normalized-root fingerprint match the
  values consumed by that core.

An external core, multiple cores, an exited core, or any identity mismatch makes
the library read-only. The UI must explain how to recover and its command
handlers must repeat the authority check.

## Windows shortcut import

The non-Steam add page may accept exactly one local Windows `.lnk` through
drag-and-drop or the keyboard-reachable file picker. Dropping or choosing a
file only creates a preview; it never mutates the Host library. The preview
shows the resolved executable, arguments, working directory and icon source,
and a separate explicit confirmation is required before the existing
`LibraryMutationCoordinator` writes and verifies the Core projection.

`WindowsShortcutResolver` is the single parser. It rejects non-shortcuts,
multiple inputs, missing or network targets, scripts, command interpreters,
installers, unsupported URI/UWP targets, and any shortcut/target/working path
that traverses a reparse point. It does not execute the target or invoke the
shell. Preview authority binds the shortcut bytes, executable bytes, canonical
target and arguments, working directory and icon. Confirmation resolves and
rehashes the source again; drift cancels the preview and produces zero library
writes. Duplicate executable additions are still rejected by the canonical
Host library repository. Cancel always discards the preview without changing
the library.

Layout binding is another library mutation under this same authority. The Host
uses only `(hostUniqueId, appUuid)` and an exact canonical
`(layoutId, revision)`; it never matches by game name or numeric launch appid.
The library item, H1 catalog projection, Sync fields and managed-Core readback
form one rollback boundary. New Steam items receive `portableIdentity` from the
already verified local Steam manifest App ID. Executable and system items do
not gain a guessed portable identity.

A newly started managed Core may own a live process before its loopback
`serverinfo` route is ready. The desktop owns one cancellable readiness loop
that probes every 400 ms for at most 15 seconds and presents a zero-result state
as **starting**, not as multiple cores. When exactly one matching Core becomes
readable, the loop revalidates nonce, token, root fingerprint, port and Host
UUID, updates the shell through its UI dispatcher, refreshes the visible game
library and removes the temporary read-only state. Only a result greater than
one is labelled multiple cores. Closing the application cancels the loop;
duplicate triggers share the existing run instead of starting concurrent
probes. A 15-second zero result becomes an explicit unavailable/timeout state
with a read-only **refresh status** action and never remains indefinitely in
the starting state.

## Explicit product data root

Managed structured deployments place `ligase-bootstrap.json` at the
installation root and resolve it through the typed `InstallationLayout`
authority; the Desktop executable lives under `Desktop/`. The bootstrap
contains `{ "dataRoot": "<absolute-directory>" }` and binds the installation
to one explicit product root without relying on the process working directory.
Automation may instead use `--data-root <absolute-directory>` or the
child-process environment variable `LIGASE_DATA_ROOT`. Command line wins over
environment, which wins over the typed bootstrap path. This is the product data
boundary used by the desktop, its managed core, and every generated projection.
A relative, missing, or duplicate command-line value is rejected.

The Settings page exposes the resolved DataRoot, its canonical instance UUID
when it is a `%ProgramData%\Ligase Host\Instances\<UUID>` root, and whether the
installed bootstrap resolves to the same directory. This is read-only operator
evidence. It does not expose the per-start authority token, nonce, root
fingerprint, paired-device material, or other secrets, and it does not infer a
historical migration source from the current path alone.

An ordinary unpackaged first run without this option still uses
`%LOCALAPPDATA%\Ligase Host`. Deployment and acceptance automation must pass an
explicit root; it must not infer authority from the process package context or
from a redirected `LocalAppData` directory.

## Projection transaction

Collection writes are serialized. Before mutation, the coordinator snapshots
only:

- `library.json`;
- `ligase-sync.json`;
- `apollo/apps.json`.

The repository writes all projections using their existing same-directory
temporary-file replacement. Only after all files are complete does the
coordinator request a core reload. Success requires core read-back of:

- the unchanged Host UUID;
- the canonical library UUID and publication/store metadata;
- the loaded applist UUID and numeric launch ID.

Failure restores the exact three snapshots, reloads the core again, and compares
the core's restored consumption view with its pre-write view.

The same coordinator is the only desktop entry point for adding, deleting,
publishing, and sorting library items. The game library exposes deletion through
each non-system item's **Manage** action, with an explicit confirmation that the
item disappears from every client while its executable remains untouched.
Desktop and virtual-desktop system entries cannot be deleted. A successful UI
result is shown only after reload and core read-back succeed.

## Loopback core contract

The HTTP base port exposes two loopback-only `POST` routes:

- `/ligase/v1/authority/readback`
- `/ligase/v1/authority/reload`

Both require JSON `{ "token": "<per-process authority token>" }`. They reject
non-loopback callers and token mismatches. Reload also rejects an active game
session. Read-back returns the current Host UUID, start nonce, root fingerprint,
Sync library projection, and the actual `proc::proc.get_apps()` UUID/numeric-ID
view. It never returns the filesystem path and is not a second mutation API.

Reload responses also carry a closed `reload` projection with `schemaVersion`,
`resultCode`, `stage`, `reasonCode`, and monotonic `elapsedMs`. In particular,
an active session is `rejected/precondition/sessionActive`; strict catalog
failures distinguish `failed/appCatalogParse/catalogMalformed`,
`failed/appCatalogLoad/catalogUnreadable|catalogLoadFailed`, and
`failed/appCatalogSwap/catalogSwapFailed`; success is `completed/readback/none`.
Desktop retains the HTTP status and these typed fields
instead of collapsing every failure into a generic read-back error. Its existing
three-second deadline is unchanged. The reload path now reparses only the
application catalog; virtual-display initialization remains part of normal Core
startup/refresh and is not performed inside this bounded library transaction.

Only exact HTTP `200` can carry `completed/readback/none`. Other successful-class
statuses, including `201` and `204`, and unexpected `4xx`/`5xx` responses are
closed as `invalidResponse/reload.response/unexpectedHttpStatus` with the bounded
numeric status retained. An `HttpRequestException` that carries a valid HTTP
status is closed separately as
`transportFailed/reload.transport/httpStatusFailure`; one without a status remains
`transportFailed/reload.transport/httpRequestFailed`. Neither branch can create
an ad-hoc result, stage, reason, or out-of-range status. The production semantic
validator used by the HTTP producer is also run before outcome persistence and
again on the committed read-back.

Every mutation that reaches the projection-write stage attempts to atomically replace
`library-mutation-outcome-v1.json` under the owned DataRoot. The safe record
contains only the operation, `committed`/`rolledBack`/`rollbackUnproven` state,
typed primary and rollback status/reason/elapsed fields, and write time. It does
not contain tokens, executable paths, cover paths, item names, or raw HTTP
content. Its exact machine shape and state cross-checks are owned by
[`library-mutation-outcome-v1.schema.json`](library-mutation-outcome-v1.schema.json).
UI success is still emitted only after the post-reload projection read-back;
rollback success likewise requires a fresh restored read-back. Outcome persistence
is an injectable atomic write-and-readback boundary. If it is unavailable after a
committed projection, the mutation remains committed and the UI reports the
diagnostic persistence warning instead of rolling data back. During mutation
failure, rollback and outcome persistence failures are attached as secondary
facts; the original typed exception and stack remain the caller-visible primary.
An unavailable outcome file therefore means `persistenceUnavailable/state unknown`,
not that a previously read-back projection failed.

The outcome writer creates a unique temporary file in the destination directory
with `CreateNew`, denies sharing while writing, uses `WriteThrough` plus
`Flush(true)`, and hashes bytes read back through that same temporary handle.
Existing records are replaced with the platform's same-volume atomic replace;
the first publication uses a same-directory atomic rename. The committed record
is then opened once, fully read and hashed through that handle, deserialized, and
validated before persistence is reported complete. Every failure attempts to
remove only its unique temporary file, and a cleanup failure remains a typed
`persistenceUnavailable/state unknown` fact. This contract proves process-visible
atomic publication and handle-bound byte read-back on the supported Windows/NTFS
deployment. It does **not** claim power-loss durability for directory metadata,
because .NET does not expose a portable directory-handle flush for this sequence.

Run the isolated route smoke test with:

```powershell
.\scripts\ligase\test-authority-readback.ps1 -Binary <built-sunshine.exe>
```

The test uses an isolated data root and port, proves malformed, unreadable, and
semantically unloadable catalog inputs each return typed `500` while the prior
in-memory catalog remains unchanged, then proves a valid parse-and-swap. It stops
only its own process.

## P1 cross-device evidence boundary

Android verified that the same Host UUID and pinned certificate changed from
three to four visible items after the fixture was added, using the fixture's
canonical UUID plus its numeric launch mapping. The Host then removed that
fixture through `LibraryMutationCoordinator`; library, Sync, applist, and core
read-back no longer contained the UUID, the library revision advanced to 6,
the streaming revision remained 6, and the original three entries remained.

The Android refresh after deletion was not executed because that device had no
certificate for the managed instance and the current first-run flow requires a
separately initialized management account. This is tracked as
`BLOCKED_BY_HOST_FIRST_RUN_PAIRING`; it is not represented as a successful
client-side deletion refresh and no hidden account or compatibility route was
created to bypass it.
## Steam 游戏封面 authority

Steam 游戏封面不按名称查询或模糊关联。唯一身份链是：

1. `SteamLibraryService` 从本机 `appmanifest_<appid>.acf` 读取十进制 App ID；
2. `CoverArtService.FindSteamAsync` 再次读取同一 manifest，并要求文件名与内容里的
   `appid` 都等于 `SteamGame.AppId`；
3. 只接受同一 Steam 安装根下两种精确 client-cache 形状：旧式
   `appcache/librarycache/<appid>/library_600x900.jpg`，或当前客户端的
   `appcache/librarycache/<appid>/<40位小写十六进制缓存键>/library_capsule_<manifest-language>.jpg`；
   后者的语言必须来自同一 app manifest，缓存键、文件名、路径层级均不做模糊搜索，
   路径链和文件均不得是 reparse point；
4. retained read 期间禁止写入或替换，JPEG 必须能由 Windows Imaging Component
   解码且尺寸恰为 Steam Library Capsule 的 600×900，或 Steam 自动生成的
   300×450 半尺寸版本；
5. 转码后只把经过 PNG signature、decoder、pixel-count 和 12 MiB 上限验证的 PNG
   原子写到权威 DataRoot 的 `covers/`。

Steam 官方把 [600×900 Library Capsule](https://partner.steamgames.com/doc/store/assets/libraryassets)
定义为 Steam Library 的主要竖版封面素材，并明确会自动生成 300×450 半尺寸版本。
Steam 没有把客户端 `librarycache` 的内部目录布局发布为稳定 API；上述 hashed-child
形状因此只是本机客户端字节的严格、fail-closed 消费规则，不是对未来缓存布局的猜测。
Ligase 只读取用户本机 Steam client cache，不随安装包分发、上传或声明拥有这些第三方
图片。`cover-cache-authority-v1.json` 记录相对缓存路径、内容 SHA-256、Steam App ID、
来源和 `thirdPartyArtworkLocalUseOnlyNoRedistribution` 使用边界；machine schema 是
[`cover-cache-authority-v1.schema.json`](cover-cache-authority-v1.schema.json)。

`library.json` 与 `ligase-sync.json` 保存同一 `coverSha256/sourceKind/sourceId/
usageRights`。`ApolloAppsWriter` 仅把已缓存 PNG 的绝对 `image-path` 交给 Apollo；
`/appasset?appid=<apollo-app-id>` 只有在 app UUID、Sync `coverSha256` 与实际 PNG bytes
三方一致时才返回 `200`，并附带 exact UUID/SHA/content-type/content-length header；
缺失或不匹配按 [`android-sync-contract.md`](android-sync-contract.md) 的 closed status
失败，8 MiB 以上拒绝；
路径验证和 PNG fallback 沿用
[Sunshine upstream `process.cpp`](https://github.com/LizardByte/Sunshine/blob/master/src/process.cpp)，
Android 可把 `applist` 的 UUID、Sync 的 `coverSha256` 与 appasset bytes 三方关联。
Host source/tests 只能证明该接口可验证；真正 Host→Android 图片显示仍是跨端真实验收门。

现有 Steam 项目的“选择封面”使用唯一 `UpdateExistingSteamCoverAsync` transaction。
前门同时锁 canonical library item UUID、当前 `{ provider: "steam", id: "<appid>" }`
portable identity，以及候选的 App ID、source kind、source ID 和 usage rights。转码后的
PNG 先取得 DataRoot `covers/` owned receipt；随后只替换同一 `LibraryItem` 的
`coverImagePath/contentSha256/sourceKind/sourceId/usageRights/updatedAt`，UUID、portable
identity、启动信息、布局绑定、发布状态及其他字段保持不变。事务在返回成功前必须完成
`library.json`、Apollo `apps.json`、`ligase-sync.json` 的发布及 managed Core fresh
readback。任一写入、发布或 readback 失败会恢复事务前的投影，并只清理本次创建且仍与
receipt 内容匹配的新缓存文件；不会删除来源不明或被替换的文件。相同封面是无 revision
写入的幂等结果。旧封面仅在新事务已提交且不再被任何 library item 引用后按 cache
authority 清理；清理暂时失败不会伪称已回滚已成功 readback 的新封面，而会在 UI 中
明确保留待清理状态。

删除游戏成功且核心 readback 已确认后，Host 仅根据缓存 authority 清理不再被任何
library item 引用的 `covers/` owned 文件；外部文件、reparse、未知记录均不删除。
离线、cache 缺失或无合格图片时保持稳定占位图，不回退到名称模糊匹配。
