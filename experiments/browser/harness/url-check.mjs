const base = process.argv[2];
const checks=[];
for (const path of ['/','/workspace.html','/bootstrap.js','/loading.js','/mod/LoadingFont.png','/main.js','/assets/fonts/manifest.json','/assets/reference-appearance.json','/host/health']) {
  const response=await fetch(new URL(path,base),{headers:{'Sec-Fetch-Site':'same-origin',Origin:new URL(base).origin}});
  checks.push({path,status:response.status});
  if(path==='/' && response.redirected)throw new Error('Workspace root redirected');
  await response.body?.cancel();
  if (!response.ok) process.exitCode=1;
}
console.log(JSON.stringify({url:new URL('/',base).href,checks}));
