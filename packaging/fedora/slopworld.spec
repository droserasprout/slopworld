# Private mod assemblies must not create system dependencies or providers.
%global __requires_exclude_from ^/usr/share/slopworld/.*$
%global __provides_exclude_from ^/usr/share/slopworld/.*$
%global debug_package %{nil}
# Release binaries are stripped; omit build-id links.
%global _build_id_links none
# The packager supplies the source commit timestamp instead of an RPM changelog.
%global source_date_epoch_from_changelog 0
%global use_source_date_epoch_as_buildtime 1
%global clamp_mtime_to_source_date_epoch 1

Name: slopworld
Version: %{slopworld_version}
Release: 1
Summary: RimWorld colony interface for live AI coding agents
License: MIT
URL: https://github.com/droserasprout/slopworld
ExclusiveArch: x86_64 aarch64
Requires: fontconfig, xclip
Suggests: glib2, gtk3, python3-gobject, xdg-desktop-portal, xsel

%description
Includes the game launcher and RimWorld 1.7 mod with assets and OST.
RimWorld must be installed separately. See /usr/share/doc/slopworld/README.Fedora
for mod attachment. Install slopd separately for a local server.

%install
mkdir -p "%{buildroot}"
cp -a "%{_sourcedir}/payload/." "%{buildroot}/"

%files
%defattr(-,root,root,-)
/usr/bin/slopworld
/usr/share/applications/slopworld.desktop
/usr/share/icons/hicolor/128x128/apps/slopworld.png
/usr/share/slopworld
/usr/share/bash-completion/completions/slopworld
/usr/share/zsh/site-functions/_slopworld
/usr/share/fish/vendor_completions.d/slopworld.fish
%doc /usr/share/doc/slopworld
%license /usr/share/licenses/slopworld
