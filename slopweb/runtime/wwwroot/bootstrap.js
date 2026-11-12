// Keep runtime module fetching/compilation out of the first loading-screen paint.
import {startLoading} from './loading.js';
import {workspaceParameters} from './workspace-viewport.js';
const parameters=workspaceParameters();
try {
  if (parameters.has('workspace') && parameters.has('app'))
    window.__loading=await startLoading(parameters);
  await import('./main.js');
} catch(error) {
  window.__loading?.finish();
  window.__probeResult={error:String(error),stack:error.stack};
  document.getElementById('result').textContent='Workspace could not start.\n'+error;
}
