// Reuse only an identical, screen-only packet against unchanged Canvas resources.
const screenCommands=new Set(['fill','text','imageTexture','imageBatch','targetTexture','matrix','group','clipScreen','clip','pop']);
// Keep only the last bounded packet. Consumers must not mutate its commands;
// decoded geometry has its own cache and Canvas validity has its own revision.
export class GuiPacketDecoder {
  previous=null;
  get packetId() { return this.previous?.result.packet ?? 0; }
  decode(raw) {
    if(this.previous?.raw===raw)return this.previous.result;
    const previous = this.previous;
    this.previous = null;
    const result = JSON.parse(raw);
    if ('repeat' in result) {
      if (!Number.isSafeInteger(result.repeat) || result.repeat <= 0 || result.repeat !== previous?.result.packet)
        throw new Error('GUI repeat does not match the retained packet');
      // Keep the full packet for the next acknowledgement, but use this frame's timings.
      this.previous = previous;
      return {...result, commands:previous.result.commands};
    }
    if(raw.length<=2*1024*1024)this.previous={raw,result};
    return result;
  }
}
export class GuiFrameReuse {
  previous=null;
  invalidate(){this.previous=null;}
  prepare(commands,width,height,revision,enabled,packet=null) {
    const previous = this.previous;
    const sameCommands = previous?.commands === commands;
    if(!enabled||(!sameCommands && commands.some(command=>!screenCommands.has(command.kind)))) {
      this.invalidate();return {skip:false,key:null};
    }
    const key=packet??(sameCommands ? previous.key : JSON.stringify(commands));
    if(key.length>2*1024*1024){this.invalidate();return {skip:false,key:null};}
    return {key,skip:!!previous&&previous.key===key&&previous.width===width&&previous.height===height&&previous.revision===revision};
  }
  commit(key,width,height,revision,commands) {
    this.previous=key===null?null:{key,width,height,revision,commands};
  }
}
