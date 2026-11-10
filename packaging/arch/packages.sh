# Shared runtime metadata and payload ownership for native source/release packages.
_slopworld_desc="RimWorld interface for SlopWorld coding agents"
_slopworld_depends=('gcc-libs' 'glibc' 'fontconfig' 'xclip')
_slopworld_optional=('glib2: desktop associations and portal access' 'gtk3: Open With chooser'
  'python-gobject: Open With chooser' 'xdg-desktop-portal: Open With fallback'
  'xsel: clipboard fallback' 'songrec: song recognition')
_slopd_desc="SlopWorld session daemon"
_slopd_depends=('gcc-libs' 'glibc' 'alsa-lib' 'bash' 'bat' 'bubblewrap' 'dbus' 'git'
  'less' 'micro' 'passt' 'ripgrep' 'systemd' 'tmux>=3.7')
_slopd_optional=('rtkit: audio scheduling' 'highlight: syntax highlighter' 'moor: pager'
  'ncspot: Spotify playback' 'procps-ng: process inspection' 'python-pygments: highlighter')
_slopctl_desc="Command-line client for local or remote SlopWorld daemons"
_slopctl_depends=('gcc-libs' 'glibc')
_slopctl_optional=('systemd: read local daemon journal')

# Staged releases select one product; source package functions declare the same
# assignments directly so makepkg can extract split-package .SRCINFO metadata.
_package_metadata() {
  local product="$1"
  local -n description="_${product}_desc" required="_${product}_depends" optional="_${product}_optional"
  pkgdesc="$description"
  depends=("${required[@]}")
  optdepends=("${optional[@]}")
  provides=() conflicts=("$product-git")
  unset install
  if [[ "$product" = slopd ]]; then depends+=('slopctl'); fi
  if [[ "$product" = slopworld ]]; then install=slopworld.install; fi
}

_package_native() {
  local product="$1"
  cd "$srcdir/$pkgbase" || return
  install -Dm755 "target/release/$product" "$pkgdir/usr/bin/$product"
  if [[ "$product" = slopd ]]; then
    install -Dm644 slopd/slopd.service "$pkgdir/usr/lib/systemd/user/slopd.service"
    sed -i 's|%h/.local/bin/slopd|/usr/bin/slopd|' "$pkgdir/usr/lib/systemd/user/slopd.service"
  else
    install -Dm644 "packaging/completions/bash-completion/completions/$product" "$pkgdir/usr/share/bash-completion/completions/$product"
    install -Dm644 "packaging/completions/zsh/site-functions/_$product" "$pkgdir/usr/share/zsh/site-functions/_$product"
    install -Dm644 "packaging/completions/fish/vendor_completions.d/$product.fish" "$pkgdir/usr/share/fish/vendor_completions.d/$product.fish"
  fi
  if [[ "$product" = slopworld ]]; then
    local mod="$pkgdir/usr/share/slopworld/SlopWorld"
    UV_OFFLINE=true just package-stage-mod "$mod"
    find "$mod" -type d -exec chmod 755 {} +
    find "$mod" -type f -exec chmod 644 {} +
    install -Dm644 packaging/slopworld.desktop "$pkgdir/usr/share/applications/slopworld.desktop"
    install -Dm644 mod/Textures/SlopWorld/SlopWorld_icon.png "$pkgdir/usr/share/icons/hicolor/128x128/apps/slopworld.png"
  fi
  install -Dm644 README.md "$pkgdir/usr/share/doc/$product/README.md"
  install -Dm644 LICENSE "$pkgdir/usr/share/licenses/$product/LICENSE"
  cp -r licenses "$pkgdir/usr/share/licenses/$product/third-party"
}
