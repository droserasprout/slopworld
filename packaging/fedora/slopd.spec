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

Name: slopd
Version: %{slopworld_version}
Release: 1
Summary: Linux session daemon for SlopWorld coding agents
License: MIT
URL: https://github.com/droserasprout/slopworld
ExclusiveArch: x86_64 aarch64
Requires: bash, bat, bubblewrap, git, less, micro, passt, ripgrep, systemd, tmux >= 3.7, slopctl
Suggests: rtkit, highlight, moor, procps-ng, python3-pygments

%description
Linux session daemon for SlopWorld coding agents.
Does not require RimWorld or the SlopWorld game client.

%install
mkdir -p "%{buildroot}"
cp -a "%{_sourcedir}/payload/." "%{buildroot}/"

%files
%defattr(-,root,root,-)
/usr/bin/slopd
/usr/lib/systemd/user/slopd.service
%doc /usr/share/doc/slopd
%license /usr/share/licenses/slopd
