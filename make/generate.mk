.PHONY: api-contract api-docs reference bake-loading-font

## Generated files

api-contract: shared/protocol.yaml shared/slopworld.proto tools/wire_contract.py ## Generate stable shared protocol bindings
	@mkdir -p mod/Source/SlopWorld/Client/Generated
	@protoc -I shared --csharp_out=mod/Source/SlopWorld/Client/Generated shared/slopworld.proto
	@$(PYTHON) tools/wire_contract.py
	@$(PYTHON) tools/protobuf_http.py

api-docs: api-contract ## Generate the mdBook API route inventory
	@$(PYTHON) tools/api_docs.py

reference:         ## Generate the environment/API/CLI reference
	@$(PYTHON) tools/reference.py

bake-loading-font: ## Bake the loading screen font atlas from the bundled source font
	@$(PYTHON) tools/loading_font_atlas.py --font "$(FONT_SOURCE)"

.PHONY: check-generated
check-generated: api-contract ## Reject uncommitted generated protocol changes
	@git diff --exit-code -- shared slopd/src/shared mod/Source/SlopWorld/Client/Generated
