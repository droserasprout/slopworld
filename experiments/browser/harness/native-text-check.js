(async()=>{
  if(window.__workspace?.status!=='running')throw new Error('Workspace is not running');
  const {renderGui}=await import('/gui.js');
  const packet=JSON.parse(window.__managedProbe.WorkspaceNativeTextProbe());
  const canvas=document.createElement('canvas');canvas.width=400;canvas.height=300;
  renderGui(canvas,packet.commands);
  const pixels=canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
  let red=0,white=0;
  for(let i=0;i<pixels.length;i+=4)if(pixels[i+3]>64) {
    if(pixels[i]>100&&pixels[i+1]<10&&pixels[i+2]<10)red++;
    if(pixels[i]>100&&pixels[i+1]>100&&pixels[i+2]>100)white++;
  }
  if(!red||!white)throw new Error('Native rich text did not produce both span colors');
  const game=JSON.parse(window.__managedProbe.GameSnapshot());
  if(game.hasGame||game.hasWorld||game.maps!==null||game.ticks!==0)throw new Error('Text check created a colony');
  return {status:'ok',lines:packet.lines,redPixels:red,whitePixels:white,genericText:false,colony:false};
})()
