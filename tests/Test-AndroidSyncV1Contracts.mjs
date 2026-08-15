import crypto from 'node:crypto';
import fs from 'node:fs';
import {createRequire} from 'node:module';
import path from 'node:path';

const repo=path.resolve(process.argv[2] ?? '.');
const dependencyRoot=path.resolve(process.env.LIGASE_AJV_ROOT ?? repo);
const require=createRequire(path.join(dependencyRoot,'package.json'));
const Ajv2020=require('ajv/dist/2020').default;
const schema=JSON.parse(fs.readFileSync(path.join(repo,'docs','ligase-host','android-sync-v1.schema.json'),'utf8'));
const vectors=JSON.parse(fs.readFileSync(path.join(repo,'tests','fixtures','android-sync-v1-vectors.json'),'utf8'));
const ajv=new Ajv2020({strictSchema:true,strictTypes:true,allErrors:true,validateFormats:false});
const validate=ajv.compile(schema);
const clone=value=>JSON.parse(JSON.stringify(value));

if(!validate(vectors.validSync)) throw new Error(`validSyncRejected:${ajv.errorsText(validate.errors)}`);

const pointer=(root,value)=>{
  const parts=value.split('/').slice(1).map(part=>part.replaceAll('~1','/').replaceAll('~0','~'));
  let parent=root;
  for(const part of parts.slice(0,-1)) parent=parent[part];
  return [parent,parts.at(-1)];
};
for(const test of vectors.invalidSync){
  const value=clone(vectors.validSync);
  const [parent,key]=pointer(value,test.pointer);
  if(test.remove) delete parent[key]; else parent[key]=test.value;
  if(validate(value)) throw new Error(`invalidSyncAccepted:${test.name}`);
}

const semantic=value=>{
  const ids=new Set();
  for(const item of value.library.items){
    if(ids.has(item.id)) return false;
    ids.add(item.id);
    if(item.kind==='steam' &&
       (String(item.steamAppId)!==item.portableIdentity?.id ||
        item.steamAppId<1 || item.steamAppId>4294967295)) return false;
    if(item.coverSourceKind==='steamClientLibraryCache' && String(item.steamAppId)!==item.coverSourceId) return false;
  }
  return true;
};
if(!semantic(vectors.validSync)) throw new Error('validSyncSemanticRejected');
for(const test of vectors.semanticInvalid){
  const value=clone(vectors.validSync);
  if(test.duplicateFirstItem) value.library.items.push(clone(value.library.items[0]));
  else { const [parent,key]=pointer(value,test.pointer); parent[key]=test.value; }
  if(!validate(value)) throw new Error(`semanticVectorNotSchemaValid:${test.name}`);
  if(semantic(value)) throw new Error(`semanticInvalidAccepted:${test.name}`);
}

const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const sha=/^[0-9a-f]{64}$/;
const evaluateAsset=test=>{
  const readable=test.fileReadable!==false;
  const actual=test.bodyBase64
    ? crypto.createHash('sha256').update(Buffer.from(test.bodyBase64,'base64')).digest('hex')
    : test.actualSha256;
  if(!uuid.test(test.appUuid)) return [409,'assetIdentityMismatch'];
  if(!test.syncCoverSha256 || !readable) return [404,'assetUnavailable'];
  if(test.bodyLength===0 || test.bodyLength>8388608) return [413,'assetTooLarge'];
  if(!sha.test(test.syncCoverSha256) || !sha.test(actual??'') || actual!==test.syncCoverSha256)
    return [409,'assetCorrelationMismatch'];
  return [200,'ok'];
};
for(const test of vectors.appAssetCases){
  const [status,error]=evaluateAsset(test);
  if(status!==test.expectedStatus || (test.error && error!==test.error))
    throw new Error(`appAssetVectorMismatch:${test.name}:${status}:${error}`);
  if(status===200){
    const body=Buffer.from(test.bodyBase64,'base64');
    if(body.length!==test.bodyLength) throw new Error('appAssetLengthMismatch');
    if(test.expectedHeaders['Content-Length']!==String(body.length) ||
       test.expectedHeaders['X-Ligase-App-Uuid']!==test.appUuid ||
       test.expectedHeaders['X-Ligase-Cover-Sha256']!==test.syncCoverSha256 ||
       test.expectedHeaders['Content-Type']!=='image/png') throw new Error('appAssetHeaderMismatch');
  }
}
for(const outcome of vectors.layoutBindingOutcomes){
  if(outcome.fallbackAllowed!==false || !['noExplicitBinding','bindingNotFound','bindingRetired','bindingDraftNotInstalled'].includes(outcome.code))
    throw new Error(`layoutBindingOutcomeInvalid:${outcome.state}`);
}

const native=fs.readFileSync(path.join(repo,'src','nvhttp.cpp'),'utf8');
for(const token of ['Content-Length','X-Ligase-App-Uuid','X-Ligase-Cover-Sha256','assetAuthorityUnavailable'])
  if(!native.includes(token)) throw new Error(`nativeProjectionMissing:${token}`);
console.log(JSON.stringify({result:'androidSyncV1ContractsPassed',schemaSelected:1,invalidSync:vectors.invalidSync.length,semanticInvalid:vectors.semanticInvalid.length,appAssetCases:vectors.appAssetCases.length,layoutOutcomes:vectors.layoutBindingOutcomes.length}));
