# Trace settings, imported only by the root justfile.
export TRACE_LOG := env("TRACE_LOG", '')
export TRACE_SECONDS := env("TRACE_SECONDS", '30')
export TRACE_LABEL := env("TRACE_LABEL", 'current')
export TRACE_OUT := env("TRACE_OUT", 'bench/results/' + (if BENCH_RUN != '' { BENCH_RUN } else { 'current' }) + '/raw/trace/' + TRACE_LABEL + '.log')
export TRACE_FILES := env("TRACE_FILES", 'bench/results/*/raw/trace/*.log')
