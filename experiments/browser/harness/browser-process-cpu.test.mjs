import {test} from 'node:test';
import assert from 'node:assert/strict';
import {summarizeProcesses,processSampler,identifyBrowserCommand,cpuQuota} from './browser-process-cpu.mjs';
import {spawn} from 'node:child_process';
import {once} from 'node:events';

const sampleProcess=(pid,role,ticks,start='100')=>({pid,role,ticks,start,rss:10});
test('CPU quota includes tighter parent limits and distinguishes unlimited from unavailable',async()=>{
  const values=new Map([
    ['/proc/42/cgroup','0::/slice/browser'],
    ['/sys/fs/cgroup/slice/browser/cpu.max','200000 100000'],
    ['/sys/fs/cgroup/slice/cpu.max','50000 100000'],
  ]);
  const read=async file=>{if(values.has(file))return values.get(file);throw Object.assign(new Error('absent'),{code:'ENOENT'});};
  assert.deepEqual(await cpuQuota(42,read),{available:true,cores:.5});
  values.set('/sys/fs/cgroup/slice/cpu.max','max 100000');
  assert.deepEqual(await cpuQuota(42,read),{available:true,cores:2});
  values.set('/sys/fs/cgroup/slice/browser/cpu.max','max 100000');
  assert.deepEqual(await cpuQuota(42,read),{available:true,cores:null});
  values.set('/proc/42/cgroup','1:cpu:/legacy');
  assert.equal((await cpuQuota(42,read)).available,false);
});
test('root cgroup membership terminates at the mount without walking filesystem parents',async()=>{
  const reads=[];
  const read=async file=>{
    reads.push(file);
    if(file==='/proc/42/cgroup')return '0::/\n';
    assert.equal(file,'/sys/fs/cgroup/cpu.max');
    return 'max 100000';
  };
  assert.deepEqual(await cpuQuota(42,read),{available:true,cores:null});
  assert.equal(reads.length,2);
});
test('Chrome process titles and ordinary argv identify the exact profile',()=>{
  for(const separator of ['\0',' ']) {
    const cmd=['chromium','--type=gpu-process','--user-data-dir=/tmp/fixture','--headless'].join(separator);
    assert.deepEqual(identifyBrowserCommand(cmd,'/tmp/fixture'),{own:true,role:'gpu-process'});
    assert.equal(identifyBrowserCommand(cmd,'/tmp/fix').own,false);
  }
});
test('CPU includes renderer and multicore software GPU instead of capping at one core',()=>{
  const before={time:0,processes:[sampleProcess(1,'renderer',10),sampleProcess(2,'gpu-process',20)]};
  const after={time:1000,processes:[sampleProcess(1,'renderer',110),sampleProcess(2,'gpu-process',920)]};
  const result=summarizeProcesses(before,after,100,4096);
  assert.equal(result.cpuPercent,1000);
  assert.equal(result.complete,true);
  assert.equal(result.rssBytes,81920);
});
test('process exits and PID reuse mark CPU accounting incomplete',()=>{
  const before={time:0,processes:[sampleProcess(1,'renderer',100),sampleProcess(2,'gpu-process',200)]};
  const after={time:1000,processes:[sampleProcess(1,'renderer',110,'200')]};
  const result=summarizeProcesses(before,after,100,4096);
  assert.equal(result.complete,false);
  assert.equal(result.processes[0].cpuPercent,null);
  assert.equal(result.cpuPercent,0);
});

test('Linux sampler includes only its exact profile and reads real process counters',async()=>{
  const profile=`/tmp/cpu-fixture-${crypto.randomUUID()}`;
  const child=spawn(process.execPath,['-e','setTimeout(()=>{},10000)','--',`--user-data-dir=${profile}`],{stdio:'ignore'});
  const exited=once(child,'exit');
  try {
    await once(child,'spawn');
    const sampler=processSampler(profile);
    const before=await sampler.sample(),after=await sampler.sample();
    assert.deepEqual(after.processes.map(p=>p.pid),[child.pid]);
    const result=sampler.summarize(before,after);
    assert.equal(result.complete,true);
    assert.ok(result.rssBytes>0);
    assert.ok(Number.isFinite(result.cpuPercent));
  } finally {child.kill();await exited;}
});
