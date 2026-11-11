(()=>{
  const probe=window.__managedProbe;
  const state=JSON.parse(probe.WorkspaceState());
  const observations=[];
  try {
    for(const scale of [1,2,5/3,1.9,1]) {
      probe.ConfigureWorkspaceViewport(state.screenWidth,state.screenHeight,scale);
      const snapshot=JSON.parse(probe.WorkspaceFontSnapshot());
      // Includes every prepared style's family, line box and command-path checks.
      probe.WorkspaceNativeTextProbe();
      // On-demand faces initially contain only their requested density. Select
      // the nearest available variant until a resize installs another one.
      if(snapshot.faces.length<4 || snapshot.faces.some(face=>{
        const nearest=face.available.reduce((best,next)=>Math.abs(next-scale)<Math.abs(best-scale)?next:best);
        return Math.abs(face.density-nearest)>1e-5;
      }))
        throw new Error('Wrong native font density: '+JSON.stringify(snapshot));
      if(observations.length && snapshot.atlasVersion<=observations.at(-1).atlasVersion)
        throw new Error('Viewport change did not invalidate the font atlas');
      observations.push(snapshot);
    }
    const first=observations[0].faces, last=observations.at(-1).faces;
    if(first.some((face,index)=>face.texture!==last[index].texture))
      throw new Error('Returning to a prepared density allocated another atlas');
    return {status:'ok',observations};
  } finally {
    probe.ConfigureWorkspaceViewport(state.screenWidth,state.screenHeight,state.uiScale);
  }
})()
