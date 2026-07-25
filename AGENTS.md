# Ligase Host repository rules

- Work in the current checkout. Before every phase, read this file and inspect
  `git status`, the complete diff, and untracked files. This repository may be
  shared by concurrent Host tasks; preserve changes outside the assigned
  allowlist.
- The existing DirectX shader deletions under
  `src_assets/windows/assets/shaders/directx/` are out of scope. Do not restore,
  stage, reformat, or otherwise absorb them into another change.
- Put build outputs, temporary files, source snapshots, and caches under the
  approved D-drive development root. Do not recreate physical build or cache
  state on the system drive.
- The installed product uses the structured installer layout. The stable root
  launcher is the product entry; Desktop, Core, tools, and deployment assets
  retain their owned subdirectories. Do not use a repository binary, Debug
  output, a changed working directory, or a hand-copied dependency to hide an
  installed-product defect.
- Starting or replacing an installed product, invoking an installer or UAC,
  changing firewall rules, installing or removing a driver, and changing
  certificates require explicit authorization. Read-only inspection does not
  grant mutation authority.
- Installer automation must use the repository's typed invocation seam and
  native-argument harness. Do not pass paths with spaces through a flattened
  `Start-Process -ArgumentList` array or reconstruct installer argv by string
  concatenation. Interactive uninstall/install steps are handed to the user;
  agents perform preflight and readback unless explicitly directed otherwise.
- Link to the canonical documents under `docs/ligase-host/` for installer,
  lifecycle, protocol, firewall, and cross-client contracts. Do not duplicate
  scripts, wire schemas, route matrices, secrets, or security rules in a
  secondary document.
- When present, the coordination-root `AUTHORITY.md` is the owner and
  field-location index for Android, Host, and Layouts Web. Read it before
  cross-repository work and report stale paths or ownership conflicts to the
  coordinator. The coordinator maintains that index; repository agents do not
  edit it concurrently. It never replaces canonical Host documents, machine
  JSON, route implementations, tests, or typed models.
- The domain owner prepares a review snapshot with an exact allowlist, stable
  byte sizes and SHA-256 values, machine validation, and explicit exclusions.
  Review assets remain uncommitted and must be labelled `REVIEW`; issuing a
  revision invalidates all older hashes.
- Every affected consumer performs an independent, read-only review and returns
  `ACCEPT` or `NEEDS_REVISION` against the exact snapshot. Silence, an earlier
  acceptance, build success, or a coordinator summary is not acceptance.
- `NEEDS_REVISION` returns the snapshot to its owner. The owner changes only
  review assets, publishes new hashes, and repeats every required review.
  Production implementation, staging, candidate packaging, and compatibility
  fallbacks are prohibited while any required reviewer has not accepted.
- After all required reviews accept, wait for explicit coordinator
  authorization before staging the machine authority. Commit and push that
  authority as an exact change and report the remote SHA. Begin production work
  only under a separately stated implementation scope, in compile-safe
  dependency order.
- Frontend and backend owners communicate typed seams and blockers directly,
  but both report review verdicts, authorization needs, commit SHAs, runtime
  mutations, and final evidence to the coordinator. A peer notification does
  not itself authorize a broader action.
- In a shared checkout, never stage, revert, format, or repair peer WIP. Reserve
  build, product, installer, and runtime windows explicitly and release them
  promptly. Cross-repository changes remain separate commits in their owning
  repositories.
- Reports must distinguish `REVIEW`, `FROZEN`, `TRANSITIONAL`, implemented,
  packaged, installed, runtime-tested, and blocked states. Provider authority
  and typed API commits precede dependent consumer implementation.
- Every task final report and commit message must declare
  `DOC_IMPACT=UPDATED|NONE` with a reason. Changes to user behavior, UI flow,
  typed owners or dependencies, installation layout/launcher/bootstrap,
  protocol/security/error/session semantics, permission/UAC/firewall/driver
  boundaries, or real acceptance steps update their owner document in the same
  commit. Purely mechanical or test-only changes may use `NONE`.
- Link machine JSON, schemas, and protocol authorities instead of copying
  them. Do not describe planned or `REAL_UI_PENDING` work as implemented or
  passed, and do not put transient commit SHAs or test counts in stable
  documentation.
- Stage exact paths only. Never use `git add .`, never stage unrelated work,
  and verify the staged diff before committing.
- Keep verification layers distinct: source/unit/build checks, packaged-payload
  checks, installed-product UI checks, and real pairing/stream-session checks
  are separate evidence. An earlier layer must not be reported as proof of a
  later one.
- Report each phase structurally: conclusion, evidence, exact scope, remaining
  blockers, required peer or user action, commit and remote readback when
  applicable, `DOC_IMPACT` with its reason, and the runtime state that was or
  was not changed.
