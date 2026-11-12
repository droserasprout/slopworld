%global debug_package %{nil}
%global _build_id_links none
%global source_date_epoch_from_changelog 0
%global use_source_date_epoch_as_buildtime 1
%global clamp_mtime_to_source_date_epoch 1

Name: slopweb
Version: %{slopworld_version}
Release: 1
Summary: Local browser Eco workspace for SlopWorld coding agents
License: MIT
URL: https://github.com/droserasprout/slopworld
ExclusiveArch: x86_64 aarch64
Requires: nodejs >= 22
Requires: python3 >= 3.11
Requires: dotnet-sdk-10.0
Requires: fontconfig
Suggests: slopd

%description
Runs the actual mod and game code locally in WebAssembly. Requires a local
Waylith/RimWorld 1.7 installation; game data is prepared privately at launch
and is not included in this package. Connects to a local or remote slopd.

%install
mkdir -p "%{buildroot}"
cp -a "%{_sourcedir}/payload/." "%{buildroot}/"

%files
%defattr(-,root,root,-)
/usr/bin/slopweb
/usr/share/slopweb
/usr/share/bash-completion/completions/slopweb
/usr/share/zsh/site-functions/_slopweb
/usr/share/fish/vendor_completions.d/slopweb.fish
%doc /usr/share/doc/slopweb
%license /usr/share/licenses/slopweb
