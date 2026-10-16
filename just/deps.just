# Update dependency locks and notices

# Fetch and verify the latest Harmony assembly
[group('deps')]
deps-harmony destination='mod/Assemblies':
    uv run --locked python -m tools.dependencies.fetch_harmony {{ quote(destination) }}
