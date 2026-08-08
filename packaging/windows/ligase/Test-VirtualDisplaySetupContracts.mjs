import fs from 'node:fs';
import {createRequire} from 'node:module';
import path from 'node:path';
const repo=path.resolve(process.argv[2] ?? '.');
const require=createRequire(path.join(repo,'package.json'));
const Ajv2020=require('ajv/dist/2020').default;
const schema=JSON.parse(fs.readFileSync(path.join(repo,'docs','ligase-host','virtual-display-setup-result-v1.schema.json'),'utf8'));
const requestSchema=JSON.parse(fs.readFileSync(path.join(repo,'docs','ligase-host','virtual-display-setup-request-v1.schema.json'),'utf8'));
const ajv=new Ajv2020({strictSchema:true,strictTypes:false,strictTuples:false,allErrors:true,validateFormats:false});
const validate=ajv.compile(schema);
const validateRequest=ajv.compile(requestSchema);
const z='e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';
const h='1'.repeat(64), op='2'.repeat(64), old='5'.repeat(64);
const hashes={
  'addedByLigase,addedByLigase':'8be5778520c95d2f5ebd20ff6bc54f76b2ade2178fd26b2cfcb9c21b18c57ea0',
  'addedByLigase,notOwned':'97f266505608d0ce1c392e675f3a10a758090c3e14b0aaf28b27a57b70252b70',
  'addedByLigase,legacyOwned':'1870df34b6a5ce2fd8c441c4a96990f6d4d74e1d04459fe9ab124d982de7a1e5',
  'notOwned,addedByLigase':'5130122c4d1d15d17785ca0e8e2ae6278c0c402a3290d1623346ac8cc2c35a03',
  'notOwned,notOwned':'689455d737a8a106f8aec0d4d099d573dfe48d5e9d28714c796fabd222d30d1f',
  'notOwned,legacyOwned':'06568eddac20032fbe52856184076a35bbcce9ee9072063ce6428de7edb97b3c',
  'legacyOwned,addedByLigase':'8823972cf9c2465d76f9d5231717be4d383b93e1a32392be252481035dd889af',
  'legacyOwned,notOwned':'c870751255f5a30f8b8959a3ad81949a430d29c688f26d9a86362557a1ddb72e',
  'legacyOwned,legacyOwned':'ff95f9b6c83abf1aea2a8a28457e34673d79c37ec3fa112fa898d4c9196b90c7'};
const clone=v=>JSON.parse(JSON.stringify(v));
const request=operation=>({schemaVersion:1,operation,operationIdSha256:op,
  createdUtc:'2026-08-08T00:00:00Z',sourceHead:'a'.repeat(40),helperSha256:h,
  packageSha256:h,hardwareId:'ROOT\\SUDOMAKER\\SUDOVDA',hardCapMilliseconds:120000,
  settleMilliseconds:500,trustSelected:operation==='provision',
  packageSelected:operation==='provision',createSelected:operation==='provision',
  legacyMarkerPolicy:'v1CertificateOnly',packageOwnershipPolicy:'provisionOperationProvenanceOnly',
  transport:{mode:'inheritedHandles',operationDirectoryAcl:'systemAdministratorsCallerOnly',
    operationDirectoryNonReparse:true,requestCreateNew:true,requestFlushCompleted:true,
    requestWriteHandlesClosed:true,requestReadOnlyHandle:true,requestWriteSharing:false,
    resultCreateNew:true,resultExclusiveHandle:true,requestFileIdentitySha256:h,
    resultFileIdentitySha256:h}});
for(const operation of ['provision','uninstall']) {
  const value=request(operation);
  if(!validateRequest(value)) throw new Error(`request-${operation}: ${ajv.errorsText(validateRequest.errors)}`);
}
for(const mutation of [
  v=>{v.transport.requestWriteSharing=true},
  v=>{v.transport.requestWriteHandlesClosed=false},
  v=>{v.operation='uninstall'},
  v=>{v.transport.mode='path'}]) {
  const value=request('provision'); mutation(value);
  if(validateRequest(value)) throw new Error('invalid request accepted');
}
const component=(state='notAttempted',code='none')=>({state,code});
const entry=(store,ownership,historical=false,cleanupState='notAttempted')=>{
  if(historical) return {store,ownership,authoritySource:'historicalMarker',preState:'historicalMarker',mutation:'none',readback:'markerExact',cleanupState};
  if(ownership==='addedByLigase') return {store,ownership,authoritySource:'currentOperation',preState:'absent',mutation:'addedByThisOperation',readback:'present',cleanupState};
  return {store,ownership,authoritySource:'currentObservation',preState:'present',mutation:'none',readback:'present',cleanupState};
};
const stores=(root,publisher,{historical=false,cleanup}={})=>{
  const clean=(ownership)=>typeof cleanup==='function'?cleanup(ownership):(cleanup ?? 'notAttempted');
  const entries=[entry('LocalMachine\\Root',root,historical,clean(root)),entry('LocalMachine\\TrustedPublisher',publisher,historical,clean(publisher))];
  return {state:'verified',entries,ownedCount:[root,publisher].filter(x=>x!=='notOwned').length,ownershipSetSha256:hashes[`${root},${publisher}`]};
};
const installed=(root='addedByLigase',publisher='addedByLigase')=>{
  const anyAdded=[root,publisher].includes('addedByLigase');
  return {schemaVersion:1,operation:'provision',operationIdsSha256:[op],sourceHead:'3'.repeat(40),helperSha256:h,packageSha256:'4'.repeat(64),state:'completed',code:'installed',stage:'completed',firstFailureFrozen:false,writtenUtc:'2026-08-08T00:00:00Z',migration:{state:'v2Committed',source:'none',packageOwnership:'addedByLigase',certificateOwnership:anyAdded?'owned':'notOwned'},ownershipAcquisition:{state:'proven',source:'currentOperation',acquisitionOperationIndex:0,preState:'absent',mutation:'publishedByLigaseProvision',readback:'presentPinned'},certificateStores:stores(root,publisher),inventory:{state:'proven',nodes:[{present:true,bound:true}],uniqueSetSha256:h,residual:'exactOneBound'},removal:{state:'notRequired',removed:[],terminal:'none',nativeCode:-1,rebootRequired:false},zeroProof:{state:'completed',samples:[{zero:true},{zero:true},{zero:true}],windowMilliseconds:3,epochStable:true},trust:anyAdded?component('completed','added'):component('verified','alreadyOwned'),package:component('completed','installed'),create:component('completed','created'),marker:component('completed','committed'),compensation:{state:'notRequired',code:'none'},execution:{elapsedMilliseconds:10,hardCapMilliseconds:120000,resultFileState:'verified'}};
};
const historicalAlreadyInstalled=(source,root,publisher,packageOwnership='legacyUnknown')=>{
  const v=installed(root,publisher); v.code='alreadyInstalled'; v.zeroProof={state:'notAttempted',samples:[],windowMilliseconds:0,epochStable:false}; v.removal={state:'notRequired',removed:[],terminal:'none',nativeCode:-1,rebootRequired:false}; v.trust=component('verified','alreadyOwned'); v.package=component('verified','alreadyOwned'); v.create=component('notRequired','none'); v.marker=component('verified','alreadyOwned'); v.migration={state:source==='v1'?'v1Read':'v2Read',source,packageOwnership,certificateOwnership:v.certificateStores.ownedCount?'owned':'notOwned'}; v.ownershipAcquisition={state:'none',source:'none'}; v.certificateStores=stores(root,publisher,{historical:true}); if(source==='v1'){v.certificateStores.entries=v.certificateStores.entries.map(e=>e.ownership==='notOwned'?entry(e.store,'notOwned',false):e);} return v;
};
const markerCommitFailed=(root,publisher,source='none')=>{
  const v=installed(root,publisher); v.state='failed';v.code='markerCommitFailed';v.stage='commitMarker';v.firstFailureFrozen=true;v.migration.state=source==='v1'?'v1UpgradeFailed':'v2CommitFailed';v.migration.source=source;v.marker=component('failed','readbackFailure');v.compensation={state:'completed',code:'marker'};return v;
};
const v1CommitFailedPartial=()=>{
  const v=markerCommitFailed('addedByLigase','addedByLigase','v1');
  v.certificateStores=stores('legacyOwned','addedByLigase');
  v.certificateStores.entries[0]=entry('LocalMachine\\Root','legacyOwned',true);
  v.certificateStores.ownedCount=2;v.certificateStores.ownershipSetSha256=hashes['legacyOwned,addedByLigase'];
  return v;
};
const uninstallShared=(root='addedByLigase',publisher='notOwned')=>{
  const cleanup=ownership=>ownership==='notOwned'?'retained':'removed';
  const hasOwned=[root,publisher].some(x=>x!=='notOwned');
  return {schemaVersion:1,operation:'uninstall',operationIdsSha256:[op],sourceHead:'3'.repeat(40),helperSha256:h,packageSha256:'4'.repeat(64),state:'completed',code:'uninstalledSharedPackageRetained',stage:'completed',firstFailureFrozen:false,writtenUtc:'2026-08-08T00:00:00Z',migration:{state:'v2Read',source:'v2',packageOwnership:'notOwned',certificateOwnership:hasOwned?'owned':'notOwned'},ownershipAcquisition:{state:'none',source:'none'},certificateStores:stores(root,publisher,{historical:true,cleanup}),inventory:{state:'proven',nodes:[],uniqueSetSha256:z,residual:'zero'},removal:{state:'completed',removed:[{nativeCode:0,rebootRequired:false,strictDecrease:true}],terminal:'completed',nativeCode:0,rebootRequired:false},zeroProof:{state:'completed',samples:[{zero:true},{zero:true},{zero:true}],windowMilliseconds:3,epochStable:true},trust:hasOwned?{state:'completed',code:'removed',ownership:'owned'}:{state:'verified',code:'absentNotOwned',ownership:'notOwned'},package:{state:'verified',code:'retainedNotOwned',ownership:'notOwned'},create:component('notRequired','none'),marker:{state:'completed',code:'removed',ownership:'owned'},compensation:{state:'notRequired',code:'none'},execution:{elapsedMilliseconds:10,hardCapMilliseconds:120000,resultFileState:'verified'}};
};
const uninstallAllNotOwnedPresent=()=>{const v=uninstallShared('notOwned','notOwned');v.trust={state:'verified',code:'retainedNotOwned',ownership:'notOwned'};return v;};
const v1EmptyProvision=()=>{
  const v=installed('notOwned','notOwned');
  v.migration={state:'v1Upgraded',source:'v1',packageOwnership:'addedByLigase',certificateOwnership:'notOwned'};
  return v;
};
const v1EmptyUninstall=(packagePresent=false,certificatePresent=true)=>{
  const v=uninstallShared('notOwned','notOwned');
  v.code='uninstalledLegacyPackageRetained';
  v.migration={state:'v1Read',source:'v1',packageOwnership:'legacyUnknown',certificateOwnership:'notOwned'};
  v.certificateStores=stores('notOwned','notOwned',{cleanup:'retained'});
  if(!certificatePresent) for(const value of v.certificateStores.entries){value.preState='absent';value.readback='absent';}
  v.package=packagePresent?{state:'verified',code:'retainedLegacyUnknown',ownership:'legacyUnknown'}:
    {state:'verified',code:'absentNotOwned',ownership:'notOwned'};
  v.trust={state:'verified',code:'retainedNotOwned',ownership:'notOwned'};
  return v;
};
const ownershipReadFailed=(operation='provision',source='unknown',reason='unreadable')=>{
  const uninstall=operation==='uninstall';
  return {schemaVersion:1,operation,operationIdsSha256:[op],sourceHead:'3'.repeat(40),helperSha256:h,packageSha256:'4'.repeat(64),state:'failed',code:'ownershipReadFailed',stage:uninstall?'cleanupOwnership':'readOwnership',firstFailureFrozen:true,writtenUtc:'2026-08-08T00:00:00Z',migration:{state:'failed',source,packageOwnership:'unknown',certificateOwnership:'unknown'},ownershipAcquisition:{state:'none',source:'none'},ownershipReadFailure:{state:'failed',reason,count:1,source,resourceMutationCount:0},certificateStores:{state:'unavailable',entries:[],ownedCount:-1,ownershipSetSha256:z},inventory:uninstall?{state:'proven',nodes:[],uniqueSetSha256:z,residual:'zero'}:{state:'proven',nodes:[{present:true,bound:false}],uniqueSetSha256:h,residual:'exactOneUnbound'},removal:{state:'notRequired',removed:[],terminal:'none',nativeCode:-1,rebootRequired:false},zeroProof:uninstall?{state:'completed',samples:[{zero:true},{zero:true},{zero:true}],windowMilliseconds:3,epochStable:true}:{state:'notAttempted',samples:[],windowMilliseconds:0,epochStable:false},trust:component(),package:component(),create:component(uninstall?'notRequired':'notAttempted','none'),marker:{state:'failed',code:reason==='conflict'?'conflict':'readbackFailure',ownership:'unknown'},compensation:{state:'notRequired',code:'none'},execution:{elapsedMilliseconds:10,hardCapMilliseconds:120000,resultFileState:'verified'}};
};
const pass=(name,v)=>{if(!validate(v)) throw new Error(`${name}: ${ajv.errorsText(validate.errors,{separator:' | '})}`);};
const reject=(name,v)=>{if(validate(v)) throw new Error(`${name}: unexpectedly accepted`);};
let positive=0,negative=0;
for(const [name,v] of [
  ['both-added',installed()],['both-preexisting',installed('notOwned','notOwned')],
  ['partial-root-added',installed('addedByLigase','notOwned')],['partial-publisher-added',installed('notOwned','addedByLigase')],
  ['v1-historical',historicalAlreadyInstalled('v1','legacyOwned','notOwned')],
  ['v2-historical',historicalAlreadyInstalled('v2','addedByLigase','notOwned','notOwned')],
  ['empty-owned',installed('notOwned','notOwned')],
  ['commit-failure-current',markerCommitFailed('addedByLigase','notOwned')],
  ['v1-commit-failure-partial-new',v1CommitFailedPartial()],
  ['uninstall-partial-owned',uninstallShared('addedByLigase','notOwned')],
  ['uninstall-all-notOwned-retained',uninstallShared('notOwned','notOwned')],
  ['uninstall-all-notOwned-present',uninstallAllNotOwnedPresent()],
  ['v1-empty-provision',v1EmptyProvision()],
  ['v1-empty-uninstall-package-absent',v1EmptyUninstall()],
  ['v1-empty-uninstall-package-retained',v1EmptyUninstall(true)],
  ['v1-empty-uninstall-cert-and-package-absent',v1EmptyUninstall(false,false)],
  ['provision-ownership-read-failed',ownershipReadFailed()],
  ['uninstall-ownership-read-failed',ownershipReadFailed('uninstall')]]){pass(name,v);positive++;}
{
  const base=installed('addedByLigase','notOwned');
  const cases=[];
  let v=clone(base);v.trust=component('verified','alreadyOwned');cases.push(['alreadyOwned-to-owned',v]);
  v=clone(base);v.certificateStores.entries[0]=entry('LocalMachine\\Root','notOwned');v.certificateStores.ownedCount=0;v.certificateStores.ownershipSetSha256=hashes['notOwned,notOwned'];v.migration.certificateOwnership='notOwned';cases.push(['added-to-notOwned',v]);
  v=clone(base);v.certificateStores.entries[0].authoritySource='currentObservation';v.certificateStores.entries[0].preState='present';v.certificateStores.entries[0].mutation='none';cases.push(['preexisting-included-as-owned',v]);
  v=clone(base);v.certificateStores.entries.reverse();cases.push(['store-order-drift',v]);
  v=clone(base);v.certificateStores.ownedCount=0;cases.push(['owned-count-drift',v]);
  v=clone(base);v.certificateStores.ownershipSetSha256=z;cases.push(['ownership-hash-drift',v]);
  v=clone(base);v.certificateStores.entries[1].ownership='legacyOwned';cases.push(['current-legacy-splice',v]);
  v=clone(base);v.certificateStores.entries[0].authoritySource='historicalMarker';v.certificateStores.entries[0].preState='historicalMarker';v.certificateStores.entries[0].mutation='none';v.certificateStores.entries[0].readback='markerExact';cases.push(['current-historical-splice',v]);
  v=uninstallShared();v.certificateStores.entries[1].cleanupState='removed';cases.push(['uninstall-notOwned-delete',v]);
  v=uninstallShared();v.certificateStores.entries[0].cleanupState='retained';cases.push(['uninstall-owned-retain',v]);
  v=uninstallShared('notOwned','notOwned');v.trust={state:'completed',code:'removed',ownership:'owned'};cases.push(['all-notOwned-trust-owned',v]);
  v=uninstallShared();v.trust={state:'verified',code:'absentNotOwned',ownership:'notOwned'};cases.push(['owned-stores-trust-notOwned',v]);
  v=markerCommitFailed('addedByLigase','notOwned');v.certificateStores.entries[0].authoritySource='historicalMarker';v.certificateStores.entries[0].preState='historicalMarker';v.certificateStores.entries[0].mutation='none';v.certificateStores.entries[0].readback='markerExact';cases.push(['commit-failure-history-splice',v]);
  v=historicalAlreadyInstalled('v2','addedByLigase','notOwned','notOwned');v.certificateStores.entries[0]=entry('LocalMachine\\Root','addedByLigase');cases.push(['v2-history-current-splice',v]);
  v=v1EmptyUninstall();v.certificateStores.entries[0]=entry('LocalMachine\\Root','legacyOwned',true,'removed');v.certificateStores.ownedCount=1;v.certificateStores.ownershipSetSha256=hashes['legacyOwned,notOwned'];v.migration.certificateOwnership='owned';cases.push(['v1-empty-promoted-to-legacyOwned',v]);
  v=v1EmptyUninstall();v.certificateStores.entries[0].ownership='legacyOwned';v.certificateStores.entries[0].cleanupState='removed';v.certificateStores.ownedCount=1;v.certificateStores.ownershipSetSha256=hashes['legacyOwned,notOwned'];v.migration.certificateOwnership='owned';cases.push(['v1-empty-present-cert-promoted-owned',v]);
  v=v1EmptyUninstall();v.migration.packageOwnership='addedByLigase';cases.push(['v1-empty-package-promoted-added',v]);
  v=v1EmptyUninstall();v.certificateStores.ownedCount=1;cases.push(['v1-empty-count-drift',v]);
  v=v1EmptyUninstall();v.certificateStores.ownershipSetSha256=z;cases.push(['v1-empty-hash-drift',v]);
  v=v1EmptyUninstall();v.migration.source='v2';cases.push(['v1-empty-source-drift',v]);
  for(const [name,value] of cases){reject(name,value);negative++;}
}
if(process.argv[3]==='--emit-consumer-fixtures'){
  const output=path.resolve(process.argv[4]);fs.mkdirSync(output,{recursive:true});
  const valid=installed('addedByLigase','notOwned');
  const missing=clone(valid);delete missing.inventory;
  const contradictory=clone(valid);contradictory.zeroProof.state='notAttempted';
  fs.writeFileSync(path.join(output,'valid.json'),JSON.stringify(valid));
  fs.writeFileSync(path.join(output,'missing-branch.json'),JSON.stringify(missing));
  fs.writeFileSync(path.join(output,'contradictory-branch.json'),JSON.stringify(contradictory));
}
console.log(JSON.stringify({code:'legacyV1EmptyStoresAddendumPassed',topLevelBranches:schema.oneOf.length,positive,negative,ownedHashes:Object.keys(hashes).length}));
