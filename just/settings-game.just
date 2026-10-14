# Game settings, imported only by the root justfile.
export GOGDL := env("GOGDL", 'gogdl')
config_home := if env("XDG_CONFIG_HOME", "") != '' { env("XDG_CONFIG_HOME") } else { env("HOME") + '/.config' }
export GOGDL_AUTH := env("GOGDL_AUTH", config_home + '/heroic/gog_store/auth.json')
export GOGDL_ID := env("GOGDL_ID", '1094900565')
export GOGDL_PATH := env("GOGDL_PATH", env("HOME") + '/GOG Games')
export GOGDL_LOGIN_URL := env("GOGDL_LOGIN_URL", 'https://auth.gog.com/auth?client_id=46899977096215655&redirect_uri=https%3A%2F%2Fembed.gog.com%2Fon_login_success%3Forigin%3Dclient&response_type=code&layout=client2')
