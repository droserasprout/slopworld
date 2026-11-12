// Pre-runtime counterpart of Patch_LoadingTips/LoadingTipSelection. Quotes are
// the authored shared file; the deterministic colony random stream is untouched.
export function loadingQuotes(source,columns,visibleLines,uiScale,grandma=false,random=Math.random) {
  // StreamReader consumes a UTF-8 BOM and ReadLine accepts CR, LF and CRLF.
  const pool=source.replace(/^\uFEFF/,'').split(/\r\n|\r|\n/).filter(line=>line.trim() && !line.startsWith('#'))
    .filter(line=>!grandma || !line.endsWith(' (')).map(line=>line.endsWith(' (')?line.slice(0,-2):line);
  const recent=[],lines=[];
  let tokens=[],at=0,last=null,budget=0,scrollAt=null,scrolls=0;
  const capacityRatio=(2/Math.max(.5,uiScale))**2;
  const limit=Math.max(1,Math.ceil(48*capacityRatio));
  const pick=()=>{
    // LoadingTipSelection and LoadTip both fall back rather than stop the stream.
    if(!pool.length) return ['Loading.'];
    const occupied=lines.at(-1)?.length??0;
    const capacity=occupied>0 && occupied<columns ? columns-occupied-1 : columns;
    let best=[];
    do {
      let gap=Infinity;
      for(const tip of pool) {
        if(recent.includes(tip)) continue;
        let used=0;
        for(const word of tip.split('\n')[0].split(' ').filter(Boolean)) {
          const next=used+(used?1:0)+word.length;
          if(next>capacity) break;
          used=next;
        }
        const remaining=capacity-used;
        if(remaining>gap) continue;
        if(remaining<gap) {best=[];gap=remaining;}
        best.push(tip);
      }
      if(!best.length) recent.shift();
    } while(!best.length);
    const selected=best[Math.floor(random()*best.length)];
    recent.push(selected);if(recent.length>12)recent.shift();
    const words=selected.split(' ').filter(Boolean);
    return words.length?words:['Loading.'];
  };
  return {
    lines,
    get scrolls(){return scrolls;},
    get halfStep(){return scrollAt!==null;},
    advance(now) {
      if(last===null) budget=limit;
      else budget+=Math.max(0,now-last)*48*capacityRatio;
      last=now;budget=Math.min(budget,limit);
      if(scrollAt!==null) {
        if(now-scrollAt<.06)return false;
        lines.shift();scrollAt=null;scrolls++;return true;
      }
      const count=Math.min(limit,Math.floor(budget));
      if(!count)return false;
      for(let i=0;i<count;i++) {
        if(at>=tokens.length) {tokens=pick();at=0;}
        const word=tokens[at++];
        if(!lines.length)lines.push('');
        const current=lines.at(-1);
        if(!current)lines[lines.length-1]=word;
        else if(current.length+1+word.length<=columns)lines[lines.length-1]+=' '+word;
        else {
          if(lines.length>=visibleLines)scrollAt=now;
          lines.push(word);
        }
        budget--;
        if(scrollAt!==null)break;
      }
      return true;
    },
  };
}
