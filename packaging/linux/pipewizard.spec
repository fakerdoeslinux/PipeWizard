Name:           PipeWizard
Version:        %{pipewizard_version}
Release:        1%{?dist}
Summary:        Launcher and mod manager for the NSMBW native PC port
License:        GPL-3.0-only
BuildArch:      x86_64
Requires:       glibc
Requires:       libX11
Requires:       libXcursor
Requires:       libXrandr
Requires:       libXi
Requires:       libXext

%description
PipeWizard launches the New Super Mario Bros. Wii native PC port and manages
GameBanana mods.

%prep

%build

%install
mkdir -p %{buildroot}/opt/pipewizard
cp -a %{_sourcedir}/publish/. %{buildroot}/opt/pipewizard/
mkdir -p %{buildroot}%{_bindir}
install -m 0755 %{_sourcedir}/pipewizard-launcher %{buildroot}%{_bindir}/pipewizard
mkdir -p %{buildroot}%{_datadir}/applications
install -m 0644 %{_sourcedir}/pipewizard.desktop %{buildroot}%{_datadir}/applications/pipewizard.desktop
mkdir -p %{buildroot}%{_datadir}/icons/hicolor/256x256/apps
install -m 0644 %{_sourcedir}/pipewizard.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/pipewizard.png

%files
/opt/pipewizard
%{_bindir}/pipewizard
%{_datadir}/applications/pipewizard.desktop
%{_datadir}/icons/hicolor/256x256/apps/pipewizard.png

%changelog
* Mon Nov 02 2026 PipeWizard maintainers - 1.0.2-1
- Add Linux RPM package
