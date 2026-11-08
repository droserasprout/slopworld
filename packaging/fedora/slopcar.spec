%global debug_package %{nil}
%global _build_id_links none
%global source_date_epoch_from_changelog 0
%global use_source_date_epoch_as_buildtime 1
%global clamp_mtime_to_source_date_epoch 1

Name: slopcar
Version: %{slopworld_version}
Release: 1
Summary: Host launcher for the SlopWorld Linux sidecar
License: MIT
URL: https://github.com/droserasprout/slopworld
ExclusiveArch: x86_64 aarch64
Requires: (moby-engine or docker-ce-cli)

%description
Manages the container, workspace and credential mounts, and persistent state.
Requires access to a running local Docker engine. Does not install the native
SlopWorld daemon, game mod, or agent CLIs.

%install
mkdir -p "%{buildroot}"
cp -a "%{_sourcedir}/payload/." "%{buildroot}/"

%files
%defattr(-,root,root,-)
/usr/bin/slopcar
/usr/share/bash-completion/completions/slopcar
/usr/share/zsh/site-functions/_slopcar
/usr/share/fish/vendor_completions.d/slopcar.fish
%doc /usr/share/doc/slopcar
%license /usr/share/licenses/slopcar
