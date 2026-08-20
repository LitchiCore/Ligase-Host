import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dependencyRoot = path.resolve(process.env.LIGASE_AJV_ROOT ?? root);
const require = createRequire(path.join(dependencyRoot, 'package.json'));
const Ajv2020 = require('ajv/dist/2020').default;
const schema = JSON.parse(fs.readFileSync(
  path.join(root, 'docs', 'ligase-host', 'device-presence-v1.schema.json'), 'utf8'));
const vectors = JSON.parse(fs.readFileSync(
  path.join(root, 'tests', 'fixtures', 'device-presence-v1-vectors.json'), 'utf8'));
const ajv = new Ajv2020({ allErrors: true, strict: true });
ajv.addSchema(schema);
const request = ajv.compile({ $ref: `${schema.$id}#/$defs/heartbeatRequest` });
const result = ajv.compile({ $ref: `${schema.$id}#/$defs/heartbeatResult` });
const projection = ajv.compile({ $ref: `${schema.$id}#/$defs/deviceProjection` });
const exchange = ajv.compile({ $ref: `${schema.$id}#/$defs/httpExchangeEvidence` });
const lifecycle = ajv.compile({ $ref: `${schema.$id}#/$defs/clientLifecycleCase` });

function assertAll(values, validator, expected, label) {
  values.forEach((value, index) => {
    if (validator(value) !== expected) {
      throw new Error(`${label}[${index}] unexpected=${JSON.stringify(validator.errors)}`);
    }
  });
}

assertAll(vectors.validHeartbeatRequests, request, true, 'validHeartbeatRequests');
assertAll(vectors.invalidHeartbeatRequests, request, false, 'invalidHeartbeatRequests');
assertAll(vectors.validHeartbeatResults, result, true, 'validHeartbeatResults');
assertAll(vectors.invalidHeartbeatResults, result, false, 'invalidHeartbeatResults');
assertAll(vectors.validDeviceProjections, projection, true, 'validDeviceProjections');
assertAll(vectors.invalidDeviceProjections, projection, false, 'invalidDeviceProjections');

for (const test of vectors.httpExchangeCases) {
  if (!exchange(test.value)) throw new Error(`exchangeShape:${test.name}`);
  const accepted = test.value.status === 200 &&
    test.value.responseContentType === 'application/json' &&
    test.value.bodyObjectCount === 1 && test.value.bodyBytes > 0 &&
    test.value.bodyBytes <= test.value.maxResponseBytes &&
    test.value.bodyValid && test.value.generationCurrent;
  if (accepted !== test.accepted) throw new Error(`exchangeSemantic:${test.name}`);
}

for (const test of vectors.clientLifecycleCases) {
  if (!lifecycle(test)) throw new Error(`lifecycleShape:${JSON.stringify(lifecycle.errors)}`);
  const eligible = test.generationCurrent && !test.requestInFlight;
  const candidate = test.generationCurrent
    ? (test.activeStreamHostId ?? (test.foreground ? test.selectedHostId : null))
    : null;
  const actual = candidate !== null && test.authenticatedHostIds.includes(candidate)
    ? candidate : null;
  const actualSend = eligible && actual !== null;
  if (actual !== test.expectedTargetHostId || actualSend !== test.expectedSend)
    throw new Error(`lifecycleSemantic:${JSON.stringify(test)}`);
}

for (const test of vectors.deadlineCases) {
  const actual = test.hasReceipt
    ? (test.ageMs < 15000 ? 'online' : 'offline')
    : (test.coreUptimeMs < 15000 ? 'unknown' : 'offline');
  if (actual !== test.expectedPresence) throw new Error(`deadline:${JSON.stringify(test)}`);
}

console.log(JSON.stringify({
  result: 'devicePresenceV1ContractPassed',
  schemaSelected: 1,
  valid: vectors.validHeartbeatRequests.length +
    vectors.validHeartbeatResults.length + vectors.validDeviceProjections.length,
  invalid: vectors.invalidHeartbeatRequests.length +
    vectors.invalidHeartbeatResults.length + vectors.invalidDeviceProjections.length,
  httpCases: vectors.httpExchangeCases.length,
  lifecycleCases: vectors.clientLifecycleCases.length,
  deadlineCases: vectors.deadlineCases.length
}));
