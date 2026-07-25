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
- Link to the canonical documents under `docs/ligase-host/` for installer,
  lifecycle, protocol, firewall, and cross-client contracts. Do not duplicate
  scripts, wire schemas, route matrices, secrets, or security rules in a
  secondary document.
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
