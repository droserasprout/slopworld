// Synthetic transport workload, not a whole-frame or browser latency measurement.
import assert from 'node:assert/strict';
import {performance} from 'node:perf_hooks';
import {GuiPacketDecoder,GuiFrameReuse} from '../../../runtime/wwwroot/gui-frame-reuse.js';

const commands=[];
for(let i=0;i<650;i++)commands.push({kind:'fill',value:{rect:[i%80*9.1,i/80*17.3,8.1,16.7],color:[1,.1,.5,1],rounding:0}});
for(let batch=0;batch<200;batch++)commands.push({kind:'imageBatch',value:{id:1,color:[1,1,1,1],
  quads:Array.from({length:14},(_,i)=>[batch*7.3+i*8.1,batch*1.1,8.1,16.7,.1,.2,.01,.02]).flat()}});
const packets=Array.from({length:32},(_,i)=>{
  commands[0].value.rect[0]=i;
  return JSON.stringify({commands});
});
const repeated=packets.map(()=>(' '+packets[0]).slice(1));
function run(values,optimized,count) {
  const decoder=new GuiPacketDecoder(),reuse=new GuiFrameReuse();
  let skipped=0;
  const started=performance.now();
  for(let i=0;i<count;i++) {
    const raw=values[i%values.length];
    const result=optimized?decoder.decode(raw):JSON.parse(raw);
    const replay=reuse.prepare(result.commands,1536,864,1,true,optimized?raw:null);
    skipped+=Number(replay.skip);
    if(!replay.skip)reuse.commit(replay.key,1536,864,1);
  }
  assert.equal(skipped,values===repeated?count-1:0);
  return (performance.now()-started)/count;
}
for(const [workload,values] of [['repeated',repeated],['changing',packets]]) {
  run(values,false,100);run(values,true,100);
  const baseline=[],optimized=[];
  for(let round=0;round<8;round++)for(const enabled of round%2?[true,false]:[false,true])
    (enabled?optimized:baseline).push(run(values,enabled,200));
  const median=values=>values.sort((a,b)=>a-b)[values.length>>1];
  const before=median(baseline),after=median(optimized);
  console.log(JSON.stringify({workload,commands:commands.length,packetChars:values[0].length,
    baselineMs:before,optimizedMs:after,speedup:before/after}));
}
