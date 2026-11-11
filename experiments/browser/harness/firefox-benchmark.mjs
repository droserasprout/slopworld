// Own a fresh Firefox/WebDriver, loopback bridge and disposable workload sessions.
// WebDriver HTTP is dependency-free; browser scripts never receive daemon credentials.
import {spawn, execFileSync} from 'node:child_process';
import {readFile, readdir, mkdir, writeFile} from 'node:fs/promises';
import path from 'node:path';
import {setTimeout as pause} from 'node:timers/promises';
import {serve} from './server.mjs';
import {cpuQuota, summarizeProcesses} from './browser-process-cpu.mjs';
import {jiterpreterSnapshot} from './browser-jiterpreter.mjs';

const [directory, output, phaseList='idle,output,typing,scrolling,switching',page='/workspace.html'] = process.argv.slice(2);
const phases=phaseList.split(',');
if(!directory || !output || phases.some(p=>!['idle','output','typing','cyrillic','scrolling','switching'].includes(p)))
  throw Error('Usage: firefox-benchmark.mjs RUNTIME OUTPUT [PHASES] [PAGE]');
if(!page.startsWith('/') || page.startsWith('//') || /token|credential|authorization/i.test(page))
  throw Error('PAGE must be a local path without credentials');
const report={version:1,backend:'Firefox WebDriver',phases:[],cleanup:false,browserCleanup:false};
const ticks=Number(execFileSync('getconf',['CLK_TCK'],{encoding:'utf8'}));
const pageSize=Number(execFileSync('getconf',['PAGESIZE'],{encoding:'utf8'}));
const distribution=values=>{
  const sorted=values.filter(Number.isFinite).toSorted((a,b)=>a-b);
  const at=p=>sorted.length?sorted[Math.floor(p*(sorted.length-1))]:null;
  return {count:sorted.length,p50:at(.5),p95:at(.95),p99:at(.99),max:sorted.at(-1)??null};
};
// Firefox's root PID comes from its returned capabilities. Include descendants
// by parent identity, without recording command lines or other sessions.
async function sample(root) {
  const rows=(await Promise.all((await readdir('/proc')).filter(p=>/^\d+$/.test(p)).map(async pid=>{
    try {
      const stat=await readFile(`/proc/${pid}/stat`,'utf8');
      const f=stat.slice(stat.lastIndexOf(')')+2).trim().split(/\s+/);
      return {pid:Number(pid),parent:Number(f[1]),start:f[19],ticks:Number(f[11])+Number(f[12]),rss:Number(f[21]),role:Number(pid)===root?'browser':'child'};
    } catch(error) {if(['ENOENT','ESRCH','EACCES'].includes(error.code))return null;throw error;}
  }))).filter(Boolean);
  const included=new Set([root]);
  for(let changed=true;changed;) {
    changed=false;
    for(const row of rows)if(included.has(row.parent)&&!included.has(row.pid)){included.add(row.pid);changed=true;}
  }
  return {time:performance.now(),processes:rows.filter(row=>included.has(row.pid)).map(({parent,...row})=>row)};
}
let driver, session, base, server, installed=false;
let stopping=false;
const interrupt=()=>{stopping=true;};
process.on('SIGTERM',interrupt);process.on('SIGINT',interrupt);
async function request(method,route,body,timeout=180000) {
  const response=await fetch(base+route,{method,headers:{'Content-Type':'application/json'},
    ...(body===undefined?{}:{body:JSON.stringify(body)}),signal:AbortSignal.timeout(timeout)});
  const {value}=await response.json();
  if(!response.ok || value?.error)throw Error(`WebDriver ${route}: ${value?.error??response.status}: ${value?.message??''}`);
  return value;
}
const command=(route,body)=>request('POST',`/session/${session}${route}`,body);
// Firefox WebDriver evaluates in a sandbox with a separate module map. Run the
// workload in the document realm so gui.js shares the live renderer's image bank.
// A temporary DOM node transfers JSON without exposing globals or credentials.
const evaluate=expression=>command('/execute/async',{script:`
  const done=arguments[arguments.length-1], expression=arguments[0];
  const node=document.createElement('span'),script=document.createElement('script');
  node.hidden=true;node.id='firefox-probe-'+Date.now()+'-'+Math.random().toString(16).slice(2);
  document.documentElement.append(node);
  node.addEventListener('complete',()=>{
    const result=JSON.parse(node.getAttribute('data-result'));
    node.remove();script.remove();done(result);
  },{once:true});
  script.type='module';
  script.textContent='const node=document.getElementById('+JSON.stringify(node.id)+');'+
    'try { const value=await eval('+JSON.stringify(expression)+');'+
    'node.setAttribute("data-result",JSON.stringify({value})); }'+
    'catch(error){node.setAttribute("data-result",JSON.stringify({error:String(error)}));}'+
    'node.dispatchEvent(new Event("complete"));';
  document.documentElement.append(script);`,args:[expression]}).then(result=>{
  if(result.error)throw Error(result.error);return result.value;
});
const started=performance.now();
try {
  const serving=await serve(directory);server=serving.server;
  driver=spawn(process.env.GECKODRIVER??'geckodriver',['--host','127.0.0.1','--port','0','--allow-system-access'],{
    detached:true,stdio:['ignore','pipe','pipe'],env:{...process.env,DBUS_SESSION_BUS_ADDRESS:'unix:path=/dev/null'},
  });
  let log='',launchError;
  const collect=data=>{log=(log+data).slice(-8000);};
  driver.stdout.on('data',collect);driver.stderr.on('data',collect);
  driver.on('error',error=>{launchError=error;});
  for(let i=0;i<150;i++) {
    const match=/Listening on (127\.0\.0\.1:\d+)/.exec(log);
    if(match){base='http://'+match[1];break;}
    if(launchError || driver.exitCode!==null)throw Error('Geckodriver failed to start');
    await pause(100);
  }
  if(!base)throw Error('Geckodriver did not become ready');
  const launched=await request('POST','/session',{capabilities:{alwaysMatch:{browserName:'firefox',
    'moz:firefoxOptions':{binary:process.env.FIREFOX??'/usr/bin/firefox',args:process.env.PROBE_HEADLESS==='0'?[]:['-headless'],prefs:{
      'layout.css.devPixelsPerPx':process.env.PROBE_DPR??'1.666666667',
      'browser.shell.checkDefaultBrowser':false,
    }}}}});
  session=launched.sessionId;
  const caps=launched.capabilities,root=caps['moz:processID'];
  report.browser={name:caps.browserName,version:caps.browserVersion,headless:caps['moz:headless']};
  report.startup={browserReadyMs:performance.now()-started};
  report.cpuQuota=await cpuQuota(root);
  const bound=Number(process.env.PROBE_CPU_LIMIT_CORES??2);
  if(!report.cpuQuota.available || report.cpuQuota.cores===null || report.cpuQuota.cores>bound)
    throw Error('Firefox escaped required CPU quota');
  await command('/timeouts',{script:180000,pageLoad:180000});
  const width=Number(process.env.PROBE_WIDTH??1536),height=Number(process.env.PROBE_HEIGHT??864);
  await command('/window/rect',{width,height});
  // Fractional desktop scaling can round a decoration adjustment by one CSS
  // pixel. Correct the observed inner size before the workspace allocates targets.
  for(let attempt=0;attempt<4;attempt++) {
    const size=await evaluate('({width:innerWidth,height:innerHeight,outerWidth,outerHeight})');
    if(size.width===width && size.height===height)break;
    await command('/window/rect',{width:size.outerWidth+width-size.width,height:size.outerHeight+height-size.height});
  }
  const navigating=performance.now();
  await command('/url',{url:serving.url+page});
  await evaluate(`(async()=>{const end=performance.now()+170000;
    while(window.__workspace?.status!=='running'){
      if(window.__workspace?.status==='fail')throw Error(window.__workspace.error);
      if(performance.now()>end)throw Error('Workspace startup timed out');
      await new Promise(r=>setTimeout(r,200));
    }
  })()`);
  report.startup.workspaceReadyMs=performance.now()-navigating;
  await evaluate('window.__managedProbe.WorkspaceQuadCache(true)');
  report.configuration=await evaluate(`(()=>{const c=document.querySelector('canvas');return {
    viewport:[innerWidth,innerHeight],canvas:[c.width,c.height],dpr:devicePixelRatio,
    canvasContext:c.getContext('2d').getContextAttributes(),runtime:window.__workspace.status,
    userAgent:navigator.userAgent,quadCache:'on',
    webglRenderer:(()=>{const g=document.createElement('canvas').getContext('webgl');
      const e=g?.getExtension('WEBGL_debug_renderer_info');return e?g.getParameter(e.UNMASKED_RENDERER_WEBGL):null;})()
  };})()`);
  await evaluate(await readFile(new URL('./benchmark-workload.js',import.meta.url),'utf8'));installed=true;
  const fixture=path.resolve(new URL('./benchmark-fixture.py',import.meta.url).pathname);
  const fixtureCommand="python3 -u '"+fixture.replaceAll("'","'\\''")+"'";
  report.fixture=await evaluate(`window.__benchmark.prepare(${JSON.stringify(fixtureCommand)})`);
  console.log('Firefox workspace and benchmark fixtures ready');
  for(const phase of phases) {
    if(stopping)throw Error('Interrupted');
    const jiterpreterBefore=await evaluate(`(${jiterpreterSnapshot.toString()})()`);
    const before=await sample(root);
    const result=await evaluate(`window.__benchmark.run(${JSON.stringify(phase)})`);
    const after=await sample(root);
    const stageMeans=Object.fromEntries(Object.keys(result.stages[0]??{}).map(key=>
      [key,result.stages.reduce((sum,s)=>sum+s[key],0)/result.stages.length]));
    const targetRepaints=result.stages.filter(stage=>stage.targetWrites>0);
    const summary={phase,durationMs:result.durationMs,frameMs:distribution(result.intervals),
      drawFrameMs:distribution(result.drawIntervals),latencyMs:distribution(result.latencyMs),stageMeans,
      targetRepaintMs:Object.fromEntries(['managedMs','repaintMs','serializationMs','canvasMs'].map(key=>[key,distribution(targetRepaints.map(s=>s[key]))])),
      latencyDefinition:['typing','cyrillic'].includes(phase)?'DOM key dispatch to matching text in native receive buffer'
        :phase==='switching'?'Native Open to selected pane draw submission':null,
      browserProcesses:summarizeProcesses(before,after,ticks,pageSize)};
    report.phases.push({summary,result,jiterpreterBefore,jiterpreterAfter:await evaluate(`(${jiterpreterSnapshot.toString()})()`)});
    console.log(JSON.stringify(summary));
    if(process.env.PROBE_SCREENSHOTS==='1') {
      const png=await request('GET',`/session/${session}/screenshot`);
      await mkdir(path.dirname(output),{recursive:true});
      await writeFile(output+'.'+phase+'.png',Buffer.from(png,'base64'));
    }
  }
  // The invocation-owned driver permits about:support navigation. Read only
  // allowlisted graphics rows; never persist the full support page.
  let current,tab;
  try {
    current=await request('GET',`/session/${session}/window`);
    tab=await command('/window/new',{type:'tab'});
    await command('/window',{handle:tab.handle});
    await command('/url',{url:'about:support'});
    await pause(1000);
    report.graphics=await command('/execute/sync',{script:`return [...document.querySelectorAll('tr')]
      .map(row=>[...row.querySelectorAll('th,td')].map(cell=>cell.textContent.trim()))
      .filter(cells=>/^(Compositing|AzureCanvasBackend|AzureContentBackend|AzureFallbackCanvasBackend|WebGL [12] Driver Renderer)$/.test(cells[0]));`,args:[]});
  } catch(error) {report.graphicsUnavailable=String(error);}
  finally {
    if(tab)await request('DELETE',`/session/${session}/window`).catch(()=>{});
    if(current)await command('/window',{handle:current});
  }
} catch(error) {report.error=String(error);process.exitCode=1;}
finally {
  if(installed) {
    report.ownedSessions=report.fixture?.created??[];
    try {report.ownedSessions=await evaluate('[...window.__benchmark.created]');}catch{}
    try {await evaluate('window.__benchmark.cleanup()');report.cleanup=true;}
    catch(error){report.cleanupError=String(error);process.exitCode=1;}
  }
  if(!report.cleanup && report.ownedSessions?.length && server) {
    try {
      const origin='http://127.0.0.1:'+server.address().port;
      for(const name of report.ownedSessions) {
        const target=origin+'/host/api/sessions/'+encodeURIComponent(name);
        const headers={origin,'sec-fetch-site':'same-origin'};
        const removed=await fetch(target,{method:'DELETE',headers,signal:AbortSignal.timeout(10000)});
        await removed.arrayBuffer();
        const check=await fetch(target,{headers,signal:AbortSignal.timeout(10000)});await check.arrayBuffer();
        if(check.status!==404)throw Error('Owned fixture remains: '+name);
      }
      report.fixtureFallbackCleanup=true;
    } catch(error){report.fixtureFallbackCleanupError=String(error);process.exitCode=1;}
  }
  if(session) {
    try {await request('DELETE',`/session/${session}`,undefined,20000);report.browserCleanup=true;}
    catch(error){report.browserCleanupError=String(error);process.exitCode=1;}
  }
  if(driver?.pid) {
    // The detached process group belongs only to this invocation. Kill stragglers
    // even if session creation failed before returning the Firefox capability PID.
    try {process.kill(-driver.pid,'SIGTERM');}catch(error){if(error.code!=='ESRCH')throw error;}
    await pause(250);
    try {process.kill(-driver.pid,'SIGKILL');}catch(error){if(error.code!=='ESRCH')throw error;}
  }
  if(server){server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
  // No driver logs, environment, capabilities/profile paths or URL query strings
  // are emitted. Error text is scrubbed against host secrets before persistence.
  let json=JSON.stringify(report,null,2)+'\n';
  for(const secret of [process.env.SLOPD_TOKEN,process.env.SLOPD_URL])if(secret)json=json.replaceAll(secret,'[redacted]');
  await mkdir(path.dirname(output),{recursive:true});await writeFile(output,json);
  console.log(JSON.stringify({output,error:report.error?'See report':undefined,cleanup:report.cleanup,browserCleanup:report.browserCleanup}));
}
