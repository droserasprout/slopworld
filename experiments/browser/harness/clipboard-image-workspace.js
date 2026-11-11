// Attach one PNG to a fresh Codex composer; never submit a prompt or type in the source agent.
(async()=>{
  const b=window.__benchmark, m=window.__managedProbe;
  const report=window.__imageClipboardReport={stage:'prepare'};
  let saved;
  const pause=ms=>new Promise(r=>setTimeout(r,ms));
  try {
    const original=await navigator.clipboard.read();
    saved=await Promise.all(original.filter(i=>i.types.length).map(async i=>new ClipboardItem(
      Object.fromEntries(await Promise.all(i.types.map(async t=>[t,await i.getType(t)]))))));
    const session=await b.prepareImage();report.session=session;report.stage='paste';
    // A tiny, valid PNG fixture; clipboard decoding uses the browser encoder.
    const canvas=document.createElement('canvas');canvas.width=canvas.height=2;
    const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));
    await navigator.clipboard.write([new ClipboardItem({'image/png':blob})]);
    await window.__trustedPaste();report.stage='attachment';
    const end=performance.now()+30000;
    while(!m.WorkspaceTerminalContains(session,'Image #1')) {
      if(performance.now()>end){report.errors=JSON.parse(m.WorkspaceState()).errors.slice(-6);throw Error('Codex did not show the image attachment');}
      await pause(100);
    }
    return {pngPaste:true,agentAttachment:true,promptSubmitted:false,session};
  } finally {
    try { await b.cleanup(); }
    finally { if(saved?.length)await navigator.clipboard.write(saved);else if(saved)await navigator.clipboard.writeText(''); }
  }
})()
