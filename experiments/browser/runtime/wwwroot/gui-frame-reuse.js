// Reuse only an identical, screen-only packet against unchanged Canvas resources.
const screenCommands=new Set(['fill','text','imageTexture','imageBatch','targetTexture','matrix','group','clipScreen','clip','pop']);
export class GuiFrameReuse {
  previous=null;
  invalidate(){this.previous=null;}
  prepare(commands,width,height,revision,enabled) {
    if(!enabled||commands.some(command=>!screenCommands.has(command.kind))) {
      this.invalidate();return {skip:false,key:null};
    }
    const key=JSON.stringify(commands);
    if(key.length>2*1024*1024){this.invalidate();return {skip:false,key:null};}
    const previous=this.previous;
    return {key,skip:!!previous&&previous.key===key&&previous.width===width&&previous.height===height&&previous.revision===revision};
  }
  commit(key,width,height,revision) {
    this.previous=key===null?null:{key,width,height,revision};
  }
}
