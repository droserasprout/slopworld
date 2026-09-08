.PHONY: gogdl-login gogdl-install gogdl-update

##
##-> RimWorld
##

gogdl-login:       ## Log into GOG interactively and save the gogdl token
	@mkdir -p "$(dir $(GOGDL_AUTH))"; \
	if command -v xdg-open >/dev/null 2>&1; then \
		xdg-open "$(GOGDL_LOGIN_URL)" >/dev/null 2>&1 || true; \
	fi; \
	echo "Log into GOG in your browser, then paste the authorization code here."; \
	echo "If the browser did not open, visit: $(GOGDL_LOGIN_URL)"; \
	printf "Authorization code: " >&2; \
	read -r code; \
	test -n "$$code" || { echo "authorization code is empty" >&2; exit 1; }; \
	$(GOGDL) --auth-config-path "$(GOGDL_AUTH)" auth --code "$$code"; \
	test -s "$(GOGDL_AUTH)" || { echo "gogdl did not save credentials to $(GOGDL_AUTH)" >&2; exit 1; }

gogdl-install:     ## Install RimWorld's native Linux build with gogdl
	@test -s "$(GOGDL_AUTH)" || { echo "missing gogdl login at $(GOGDL_AUTH); run make gogdl-login" >&2; exit 1; }
	@mkdir -p "$(GOGDL_PATH)" "$(dir $(GOGDL_AUTH))"
	$(GOGDL) --auth-config-path "$(GOGDL_AUTH)" download "$(GOGDL_ID)" \
		--path "$(GOGDL_PATH)" --platform linux --with-dlcs

gogdl-update:      ## Update the local RimWorld copy with gogdl
	@test -x "$(RIMWORLD)/RimWorldLinux" || { echo "missing RimWorld at $(RIMWORLD); run make gogdl-install or set RIMWORLD" >&2; exit 1; }
	@test -s "$(GOGDL_AUTH)" || { echo "missing gogdl login at $(GOGDL_AUTH); run make gogdl-login" >&2; exit 1; }
	@mkdir -p "$(dir $(GOGDL_AUTH))"
	$(GOGDL) --auth-config-path "$(GOGDL_AUTH)" update "$(GOGDL_ID)" \
		--path "$(RIMWORLD)" --platform linux --with-dlcs
