// Read-only diagnostics for the pinned .NET 10 runtime. Counter indices match
// jiterpreter_dump_stats in its shipped runtime JS; no tuning or tracing enabled.
export function jiterpreterSnapshot() {
  try {
    const runtime=globalThis.getDotnetRuntime?.(0);
    if(!runtime?.INTERNAL?.jiterpreter_get_options)return {available:false};
    const counter=runtime.Module?._mono_jiterp_get_counter;
    if(typeof counter!=='function')return {available:false};
    return {available:true,options:{...runtime.INTERNAL.jiterpreter_get_options()},
      compiledTraces:counter(1),compiledBytes:counter(6),
      generationMs:counter(11),compilationMs:counter(12)};
  } catch(error) {
    // A runtime upgrade may remove these internal diagnostics; that must not
    // prevent the actual workload and its ordinary measurements from running.
    return {available:false,error:String(error)};
  }
}
