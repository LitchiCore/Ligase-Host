import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';

const root = process.env.LIGASE_SOURCE_ROOT || path.resolve(import.meta.dirname, '..');
const dependencyRoot = process.env.LIGASE_NODE_DEPENDENCY_ROOT || root;
const require = createRequire(path.join(dependencyRoot, 'package.json'));
const Ajv2020 = require('ajv/dist/2020').default;
const schema = JSON.parse(fs.readFileSync(path.join(
  root, 'docs', 'ligase-host', 'library-mutation-outcome-v1.schema.json'), 'utf8'));
const validate = new Ajv2020({ strict: true, strictTypes: false, allErrors: true, validateFormats: false }).compile(schema);

const attempt = (resultCode, stage, reasonCode, httpStatusCode, elapsedMs) => ({
  resultCode, stage, reasonCode, httpStatusCode, elapsedMs
});
const base = {
  schemaVersion: 1,
  operation: 'addSteam',
  state: 'committed',
  primary: attempt('completed', 'reload.readback', 'none', 200, 12),
  rollback: attempt('notAttempted', 'none', 'none', null, null),
  writtenAtUtc: '2026-08-21T04:30:00+08:00'
};
const valid = [
  base,
  { ...base, state: 'rolledBack', primary: attempt('timeout', 'reload.transport', 'deadlineExceeded', null, 3000), rollback: attempt('completed', 'reload.readback', 'none', 200, 7) },
  { ...base, state: 'rollbackUnproven', primary: attempt('rejected', 'reload.precondition', 'sessionActive', 409, 2), rollback: attempt('failed', 'rollback.mutation', 'mutationFailed', null, null) },
  { ...base, state: 'rolledBack', primary: attempt('failed', 'addSteam.mutation', 'mutationFailed', null, null), rollback: attempt('completed', 'reload.readback', 'none', 200, 7) },
  { ...base, state: 'rollbackUnproven', primary: attempt('failed', 'reload.appCatalogReload', 'reloadFailed', 500, 3), rollback: attempt('projectionMismatch', 'rollback.verify', 'projectionMismatch', null, 4) }
  ,{ ...base, state: 'rolledBack', primary: attempt('invalidResponse', 'reload.response', 'unexpectedHttpStatus', 201, 2), rollback: attempt('completed', 'reload.readback', 'none', 200, 7) }
  ,{ ...base, state: 'rolledBack', primary: attempt('invalidResponse', 'reload.response', 'unexpectedHttpStatus', 204, 2), rollback: attempt('completed', 'reload.readback', 'none', 200, 7) }
  ,{ ...base, state: 'rolledBack', primary: attempt('invalidResponse', 'reload.response', 'unexpectedHttpStatus', 418, 2), rollback: attempt('completed', 'reload.readback', 'none', 200, 7) }
  ,{ ...base, state: 'rolledBack', primary: attempt('invalidResponse', 'reload.response', 'unexpectedHttpStatus', 502, 2), rollback: attempt('completed', 'reload.readback', 'none', 200, 7) }
  ,{ ...base, state: 'rolledBack', primary: attempt('transportFailed', 'reload.transport', 'httpStatusFailure', 502, 2), rollback: attempt('completed', 'reload.readback', 'none', 200, 7) }
];
const invalid = [
  { ...base, rawPath: 'D:/secret' },
  { ...base, schemaVersion: 2 },
  { ...base, operation: 'unknown' },
  { ...base, state: 'committed', primary: attempt('timeout', 'reload.transport', 'deadlineExceeded', null, 3000) },
  { ...base, state: 'committed', rollback: attempt('completed', 'reload.readback', 'none', 200, 1) },
  { ...base, state: 'rolledBack', rollback: attempt('timeout', 'reload.transport', 'deadlineExceeded', null, 3000) },
  { ...base, state: 'rollbackUnproven', rollback: attempt('completed', 'reload.readback', 'none', 200, 1) },
  { ...base, primary: { ...base.primary, elapsedMs: -1 } },
  { ...base, primary: { ...base.primary, httpStatusCode: '200' } },
  { ...base, primary: { ...base.primary, token: 'secret' } },
  { ...base, primary: attempt('failed', 'evil.stage', 'reloadFailed', 500, 1) },
  { ...base, primary: attempt('failed', 'reload.appCatalogReload', 'evilReason', 500, 1) },
  { ...base, primary: attempt('failed', 'reload.appCatalogReload', 'reloadFailed', 599, 1) },
  { ...base, state: 'rolledBack', primary: attempt('completed', 'reload.readback', 'none', 200, 1), rollback: attempt('completed', 'reload.readback', 'none', 200, 1) },
  { ...base, state: 'rollbackUnproven', primary: attempt('timeout', 'reload.transport', 'deadlineExceeded', null, 3000), rollback: attempt('notAttempted', 'none', 'none', null, null) },
  { ...base, operation: 'remove', state: 'rolledBack', primary: attempt('failed', 'addSteam.mutation', 'mutationFailed', null, null), rollback: attempt('completed', 'reload.readback', 'none', 200, 1) }
  ,{ ...base, primary: attempt('completed', 'reload.readback', 'none', 201, 1) }
  ,{ ...base, primary: attempt('completed', 'reload.readback', 'none', 204, 1) }
  ,{ ...base, state: 'rolledBack', primary: attempt('invalidResponse', 'reload.response', 'unexpectedHttpStatus', 99, 1), rollback: attempt('completed', 'reload.readback', 'none', 200, 1) }
  ,{ ...base, state: 'rolledBack', primary: attempt('invalidResponse', 'reload.response', 'unexpectedHttpStatus', 600, 1), rollback: attempt('completed', 'reload.readback', 'none', 200, 1) }
  ,{ ...base, state: 'rolledBack', primary: attempt('failed', 'reload.appCatalogReload', 'reloadFailed', 418, 1), rollback: attempt('completed', 'reload.readback', 'none', 200, 1) }
];

for (const [index, value] of valid.entries()) {
  if (!validate(value)) throw new Error(`valid[${index}] rejected: ${JSON.stringify(validate.errors)}`);
}
for (const [index, value] of invalid.entries()) {
  if (validate(value)) throw new Error(`invalid[${index}] accepted`);
}
console.log(JSON.stringify({ result: 'libraryMutationOutcomeV1ContractsPassed', valid: valid.length, invalid: invalid.length }));
