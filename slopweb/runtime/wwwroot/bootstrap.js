// Keep runtime module fetching/compilation out of the first loading-screen paint.
import {startLoading} from './loading.js';
import {workspaceParameters} from './workspace-viewport.js';
const parameters=workspaceParameters();
let stage='Opening the loading screen';
try {
  window.__loading=await startLoading(parameters);
  stage='Loading workspace and .NET modules';
  const [{startRuntime}, {dotnet}] = await Promise.all([import('./main.js'), import('./_framework/dotnet.js')]);
  stage='Starting the workspace';
  await startRuntime(dotnet, parameters);
} catch(error) {
  window.__loading?.finish();
  document.body.classList.remove('loading');
  window.__probeResult={error:String(error),stage,stack:error?.stack};
  document.getElementById('result').textContent='Workspace could not start.\n'+stage+'\n'+error;
}
