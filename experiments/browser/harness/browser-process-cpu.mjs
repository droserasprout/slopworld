// Host-only accounting for the diagnostic browser, including software GPU work.
// No command lines, environment variables or unrelated processes enter reports.
import {readFile, readdir} from 'node:fs/promises';
import {execFileSync} from 'node:child_process';
import path from 'node:path';

// Parent slices can impose a tighter limit than the browser's transient unit.
export async function cpuQuota(pid, read=readFile) {
  const membership=await read(`/proc/${pid}/cgroup`,'utf8');
  const group=membership.split('\n').find(line=>line.startsWith('0::'))?.slice(3);
  if(group===undefined)return {available:false,reason:'No unified cgroup membership'};
  const root='/sys/fs/cgroup';
  // Root membership must compare equal to root (without a trailing slash),
  // otherwise walking parents reaches / and never terminates in containers.
  let directory=path.resolve(root,'.'+group), cores=null;
  if(directory!==root&&!directory.startsWith(root+'/'))
    return {available:false,reason:'Cgroup membership escapes mounted hierarchy'};
  for(;;) {
    try {
      const [quota,period]=(await read(path.join(directory,'cpu.max'),'utf8')).trim().split(/\s+/);
      if(quota!=='max') {
        const limit=Number(quota)/Number(period);
        if(!Number.isFinite(limit)||limit<=0)throw new Error('Invalid cpu.max');
        cores=cores===null?limit:Math.min(cores,limit);
      }
    } catch(error) {if(error.code!=='ENOENT')throw error;}
    if(directory===root)break;
    directory=path.dirname(directory);
  }
  return {available:true,cores};
}

export function identifyBrowserCommand(cmd,profile) {
  // Chromium may replace argv with one space-separated process title.
  const title=' '+cmd.replaceAll('\0',' ')+' ';
  return {own:title.includes(` --user-data-dir=${profile} `),
    role:/(?:^|[\s\0])--type=([^\s\0]+)/.exec(cmd)?.[1]??'browser'};
}

export function summarizeProcesses(before, after, ticksPerSecond, pageSize) {
  const previous = new Map(before.processes.map(p=>[`${p.pid}:${p.start}`,p]));
  let complete = before.processes.length === after.processes.length;
  const processes = after.processes.map(p=>{
    const old = previous.get(`${p.pid}:${p.start}`);
    if (!old) complete=false;
    return {pid:p.pid, role:p.role, rssBytes:p.rss*pageSize,
      cpuPercent:old ? 100*((p.ticks-old.ticks)/ticksPerSecond)/((after.time-before.time)/1000) : null};
  });
  return {complete, durationMs:after.time-before.time,
    cpuPercent:processes.reduce((sum,p)=>sum+(p.cpuPercent??0),0),
    rssBytes:processes.reduce((sum,p)=>sum+p.rssBytes,0), processes};
}

export function processSampler(profile) {
  const ticks = Number(execFileSync('getconf',['CLK_TCK'],{encoding:'utf8'}));
  const pages = Number(execFileSync('getconf',['PAGESIZE'],{encoding:'utf8'}));
  return {
    async quota() {
      const snapshot=await this.sample();
      const browser=snapshot.processes.find(p=>p.role==='browser');
      if(!browser)throw new Error('Diagnostic browser root is absent');
      return cpuQuota(browser.pid);
    },
    async sample() {
      const rows = await Promise.all((await readdir('/proc')).filter(p=>/^\d+$/.test(p)).map(async pid=>{
        try {
          const [stat,cmd] = await Promise.all([readFile(`/proc/${pid}/stat`,'utf8'),readFile(`/proc/${pid}/cmdline`,'utf8')]);
          const fields=stat.slice(stat.lastIndexOf(')')+2).trim().split(/\s+/);
          return {pid:Number(pid),parent:Number(fields[1]),start:fields[19],
            ticks:Number(fields[11])+Number(fields[12]),rss:Number(fields[21]),
            ...identifyBrowserCommand(cmd,profile)};
        } catch(error) {
          if (['ENOENT','ESRCH','EACCES'].includes(error.code)) return null;
          throw error;
        }
      }));
      const live=rows.filter(Boolean), included=new Set(live.filter(p=>p.own).map(p=>p.pid));
      let changed=true;
      while(changed) {
        changed=false;
        for(const p of live) if(included.has(p.parent)&&!included.has(p.pid)) {included.add(p.pid);changed=true;}
      }
      if(!included.size)throw new Error('Diagnostic browser process tree is absent');
      return {time:performance.now(),processes:live.filter(p=>included.has(p.pid)).map(({own,parent,...p})=>p)};
    },
    summarize:(before,after)=>summarizeProcesses(before,after,ticks,pages),
  };
}
