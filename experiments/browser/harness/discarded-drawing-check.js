(()=>{
  if(window.__workspace?.status!=='running')throw new Error('Workspace is not running');
  const result=window.__managedProbe.WorkspaceDiscardedDrawingProbe();
  if(result!=='ok')throw new Error(result);
  return {status:'ok',nativePasses:true,repaintCommandsIdentical:true,exceptionRestoresDrawing:true};
})()
