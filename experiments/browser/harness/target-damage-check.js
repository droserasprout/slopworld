// Differential real-Canvas replay. The driver appends a read-only target accessor
// to its temporary renderer copy; production exports contain no pixel readback.
const synthetic=async()=>{
  const reference=await import('./gui.js?damageReference='+Date.now());
  const candidate=await import('./gui.js?damageCandidate='+Date.now());
  reference.configureGuiTargetDamage(false);
  candidate.configureGuiTargetDamage(true);
  const a=document.createElement('canvas'),b=document.createElement('canvas');
  a.width=b.width=80;a.height=b.height=64;
  const image=(id,pixels)=>({kind:'image',value:{id,width:2,height:2,pixels:btoa(String.fromCharCode(...pixels))}});
  const white=image(1,Array(16).fill(255));
  const glyph=image(2,[255,20,80,255,20,255,80,127,20,80,255,64,255,255,255,0]);
  const draw=(id,rect,color=[1,1,1,1])=>({kind:'imageBatch',value:{id,color,quads:[...rect,0,0,1,1]}});
  const row=(y,x=4)=>[{kind:'clipScreen',value:[2.25,y,74.5,12.5]},
    {kind:'matrix',value:[1,0,0,1,0,0]},draw(2,[x,y+.25,24,12]),{kind:'pop',value:0}];
  const packet=({x=4,remove=false,alpha=1,shift=0,width=80,clip=0,scale=1}={})=>[
    {kind:'matrix',value:[scale,0,0,scale,0,0]},
    {kind:'clipScreen',value:[clip,0,80-clip,64]},
    {kind:'target',value:{id:7,width,height:64}},
    draw(1,[1.25,1.25,76.5,60.5],[.1,.2,.3,alpha]),
    ...row(8+shift),...(remove?[]:row(26+shift,x)),
    {kind:'target',value:{id:0}}, {kind:'pop',value:0},
    {kind:'matrix',value:[1,0,0,1,0,0]},
    {kind:'targetTexture',value:{id:7,rect:[0,0,width,64],uv:[0,0,1,1],color:[1,1,1,1]}}
  ];
  const cases=[
    ['initial',[white,glyph,...packet()]],['changed-row',packet({x:8})],
    ['identical-fringe',packet({x:8})],['erase-row',packet({remove:true})],
    ['restore-row',packet()],['scroll-rows',packet({shift:3})],
    ['transparent-reset',packet({alpha:.5})],['opaque-after-transparent',packet()],
    ['image-replacement',[image(2,[1,255,9,128,255,1,9,255,9,1,255,32,0,0,0,0]),...packet()]],
    ['changed-clip',packet({clip:3.25})],['changed-scale',packet({scale:1.1})],
    ['resize',packet({width:79})],['release-reuse',[{kind:'releaseTarget',value:7},...packet()]],
    ['repeat-after-reuse',packet()]
  ];
  const mismatches=[];
  for(const [name,commands]of cases){
    reference.renderGui(a,structuredClone(commands));candidate.renderGui(b,structuredClone(commands));
    for(const [label,left,right]of [['screen',a.getContext('2d').getImageData(0,0,80,64).data,b.getContext('2d').getImageData(0,0,80,64).data],
      ['target',reference.auditTarget(7),candidate.auditTarget(7)]]){
      let count=0;for(let i=0;i<left.length;i++)if(left[i]!==right[i])count++;
      if(count)mismatches.push({name,label,count});
    }
  }
  // Native terminal-sized targets exercise accelerated clipping paths that a
  // tiny Canvas does not. Keep the fractional reset and 5/3 display density.
  a.width=b.width=2560;a.height=b.height=1440;
  const scale=[5/3,0,0,5/3,0,0];
  const largeRow=(y,x=220)=>[{kind:'clipScreen',value:[359,y*5/3,2200,26.5]},
    {kind:'matrix',value:scale},draw(2,[x,y,24,12]),{kind:'pop',value:0}];
  const largePacket=x=>[{kind:'matrix',value:scale},
    {kind:'clipScreen',value:[0,0,2560,1440]},
    {kind:'target',value:{id:8,width:2560,height:1440}},
    draw(1,[215.26923,13,1320.7307,851],[.078431375,.07058824,.05490196,1]),
    ...largeRow(32),...largeRow(64,x),
    {kind:'target',value:{id:0}},{kind:'pop',value:0},{kind:'matrix',value:[1,0,0,1,0,0]},
    {kind:'targetTexture',value:{id:8,rect:[0,0,2560,1440],uv:[0,0,1,1],color:[1,1,1,1]}}];
  const largeCases=[['large-initial',[white,glyph,...largePacket(220)]],
    ['large-edit',largePacket(224)],['large-repeat',largePacket(224)],['large-edit-again',largePacket(228)]];
  for(const [name,commands]of largeCases) {
    reference.renderGui(a,structuredClone(commands));candidate.renderGui(b,structuredClone(commands));
    for(const [label,left,right]of [['screen',a.getContext('2d').getImageData(0,0,a.width,a.height).data,b.getContext('2d').getImageData(0,0,b.width,b.height).data],
      ['target',reference.auditTarget(8),candidate.auditTarget(8)]]) {
      let count=0;for(let i=0;i<left.length;i++)if(left[i]!==right[i])count++;
      if(count)mismatches.push({name,label,count});
    }
  }
  const stats=candidate.guiTargetDamageStats();
  for(const [renderer,canvas] of [[reference,a],[candidate,b]]) {
    renderer.renderGui(canvas,[{kind:'releaseTarget',value:7},{kind:'releaseTarget',value:8},{kind:'releaseImage',value:1},{kind:'releaseImage',value:2}]);
    if(renderer.guiTargetCount()||renderer.guiImageCount())throw Error('Resource cleanup failed');
  }
  return {cases:cases.length+largeCases.length,mismatches,stats};
};
try {
  const result=await synthetic();
  window.__probeResult={status:result.mismatches.length||!result.stats.planned||!result.stats.skippedDraws?'fail':'ok',...result};
} catch(error){window.__probeResult={status:'fail',error:String(error)};}
