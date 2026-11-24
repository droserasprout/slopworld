// Keep runtime module fetching/compilation out of the first loading-screen paint.
import {startLoading} from './loading.js';
import {workspaceParameters} from './workspace-viewport.js';
const parameters=workspaceParameters();
try {
  window.__loading=await startLoading(parameters);
  const [{startRuntime}, {dotnet}] = await Promise.all([import('./main.js'), import('./_framework/dotnet.js')]);
  await startRuntime(dotnet, parameters);
} catch(error) {
  window.__loading?.finish();
  document.body.classList.remove('loading');
  window.__probeResult={error:String(error),stack:error.stack};
  document.getElementById('result').textContent='Workspace could not start.\n'+error;
}
