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
const diagnosticSchema = JSON.parse(fs.readFileSync(
  path.join(root, 'docs', 'ligase-host', 'device-presence-diagnostics-v1.schema.json'), 'utf8'));
const diagnosticVectors = JSON.parse(fs.readFileSync(
  path.join(root, 'tests', 'fixtures', 'device-presence-diagnostics-v1-vectors.json'), 'utf8'));
const ajv = new Ajv2020({ allErrors: true, strict: true });
ajv.addSchema(schema);
ajv.addSchema(diagnosticSchema);
const request = ajv.compile({ $ref: `${schema.$id}#/$defs/heartbeatRequest` });
const result = ajv.compile({ $ref: `${schema.$id}#/$defs/heartbeatResult` });
const projection = ajv.compile({ $ref: `${schema.$id}#/$defs/deviceProjection` });
const exchange = ajv.compile({ $ref: `${schema.$id}#/$defs/httpExchangeEvidence` });
const lifecycle = ajv.compile({ $ref: `${schema.$id}#/$defs/clientLifecycleCase` });
const diagnostics = ajv.compile({ $ref: diagnosticSchema.$id });

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

assertAll(diagnosticVectors.valid, diagnostics, true, 'validDiagnostics');
assertAll(diagnosticVectors.invalid, diagnostics, false, 'invalidDiagnostics');
for (const value of diagnosticVectors.valid) {
  const counters = value.counters;
  if (counters.handlerEntered !== counters.rejected415 + counters.rejected400 +
      counters.rejected401 + counters.accepted200) {
    throw new Error('diagnosticCounterPartition');
  }
  const serialized = JSON.stringify(value);
  for (const forbidden of ['deviceUuid', 'certificate', 'clientAddress', 'requestBody', 'lastSeen']) {
    if (serialized.includes(forbidden)) throw new Error(`diagnosticSecret:${forbidden}`);
  }
}
for (const value of diagnosticVectors.semanticInvalid) {
  if (!diagnostics(value)) throw new Error('semanticInvalidShape');
  const counters = value.counters;
  const partition = counters.handlerEntered === counters.rejected415 +
    counters.rejected400 + counters.rejected401 + counters.accepted200;
  const projections = value.devices.every(device =>
    device.currentProjection === 'online'
      ? device.acceptedCount > 0 && device.lastReceiptAgeMs !== null &&
        device.lastReceiptAgeMs < 15000
      : device.currentProjection === 'unknown'
        ? device.lastReceiptAgeMs === null && value.coreUptimeMs < 15000
        : device.lastReceiptAgeMs === null || device.lastReceiptAgeMs >= 15000);
  if (partition && projections) throw new Error('semanticInvalidAccepted');
}

const source = fs.readFileSync(path.join(root, 'src', 'nvhttp.cpp'), 'utf8');
for (const required of [
  '^/ligase/v1/device-presence/diagnostics$',
  'ligase_device_presence_diagnostics_local',
  'tls_route_auth_rejected',
  'diagnostic_max_response_bytes',
  'ligase_request_is_loopback'
]) {
  if (!source.includes(required)) throw new Error(`diagnosticSourceMissing:${required}`);
}
for (const forbidden of [
  'devicePresenceDiagnostics.log',
  'lastReceiptUtc',
  'requestBodyDiagnostic',
  'clientAddressDiagnostic'
]) {
  if (source.includes(forbidden)) throw new Error(`diagnosticSourceForbidden:${forbidden}`);
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
  deadlineCases: vectors.deadlineCases.length,
  diagnosticValid: diagnosticVectors.valid.length,
  diagnosticInvalid: diagnosticVectors.invalid.length,
  diagnosticSemanticInvalid: diagnosticVectors.semanticInvalid.length
}));
