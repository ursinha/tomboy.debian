Name:           tomboy
Version:        1.11.5
Release:        1
Epoch:          0
Summary:        Tomboy is a desktop note-taking application for Linux and Unix. 

Group:          Office
License:        GPL
URL:            http://www.beatniksoftware.com/tomboy/
Source0:        %{name}-%{version}.tar.gz
BuildRoot:      %{_tmppath}/%{name}-%{version}-%{release}-root-%(%{__id_u} -n)


BuildRequires:  gtk2-devel >= 2.2.3
BuildRequires:  atk-devel >= 1.2.4
BuildRequires:  gtkspell-devel
BuildRequires:  gtk-sharp2
%{?_with_dbus:BuildRequires:  dbus-glib}
Requires:       gtk2-devel >= 2.2.3 
Requires:       atk-devel >= 1.2.4
Requires:       gtkspell
Requires:       gtk-sharp2
Requires:       libpanel-applet-2.so.0
%{?_with_dbus:Requires:  dbus-glib}

%description
Tomboy is a desktop note-taking application for Linux and Unix. Simple and easy
to use, but with potential to help you organize the ideas and information you
deal with every day.  The key to Tomboy's usefulness lies in the ability to
relate notes and ideas together.  Using a WikiWiki-like linking system,
organizing ideas is as simple as typing a name.  Branching an idea off is easy
as pressing the Link button. And links between your ideas won't break, even when
renaming and reorganizing them.

Available rpmbuild rebuild options :
--with : dbus

%prep
%setup -q


%build
%configure %{!?_with_dbus: --enable-dbus=no}
%{__make} %{?_smp_mflags}


%install
%{__rm} -rf ${RPM_BUILD_ROOT}
export GCONF_DISABLE_MAKEFILE_SCHEMA_INSTALL=1
%makeinstall
unset GCONF_DISABLE_MAKEFILE_SCHEMA_INSTALL

%find_lang %{name}


%post
export GCONF_CONFIG_SOURCE=`gconftool-2 --get-default-source`
gconftool-2 --makefile-install-rule \
    %{_sysconfdir}/gconf/schemas/tomboy.schemas > /dev/null


%preun
export GCONF_CONFIG_SOURCE=`gconftool-2 --get-default-source`
gconftool-2 --makefile-uninstall-rule \
    %{_sysconfdir}/gconf/schemas/tomboy.schemas >/dev/null;


%clean
%{__rm} -rf ${RPM_BUILD_ROOT}


%files -f %{name}.lang
%defattr(-,root,root,-)
%doc AUTHORS ChangeLog COPYING NEWS README
%dir %{_libdir}/%{name}
%dir %{_libdir}/%{name}/Plugins
%{_bindir}/%{name}
%{_libdir}/%{name}/*
%{_libdir}/%{name}/Plugins/*
%{_libdir}/bonobo/servers/GNOME_TomboyApplet.server
%{_libdir}/pkgconfig/*.pc
%{?_with_dbus:${datarootdir}/dbus-1/services/org.gnome.Tomboy.service}
%{_mandir}/man1/%{name}.1.gz
%{_datadir}/applications/tomboy.desktop
%{_datadir}/pixmaps/tintin.png
%{_sysconfdir}/gconf/schemas/tomboy.schemas

%changelog
* Fri Oct 08 2004 Alex Graveley  <alex@beatniksoftware.com> - 0.2
- Update to add .schemas, .desktop, .service, %find_lang, and %doc files.

* Sun Sep 18 2004 Ricardo Veguilla <veguilla@hpcf.upr.edu> - 0.1
- Initial package.

