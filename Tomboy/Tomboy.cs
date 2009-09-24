
using System;
using System.IO;
using System.Xml;
using Mono.Unix;

#if FIXED_PANELAPPLET
using Gnome;
#elif !WIN32 && !MAC
using _Gnome;
#endif

using Tomboy.Sync;

namespace Tomboy
{
	public class Tomboy : Application
	{
		static bool debugging;
		static NoteManager manager;
		static TomboyTrayIcon tray_icon;
		static TomboyTray tray = null;
		static bool tray_icon_showing = false;
		static bool is_panel_applet = false;
		static PreferencesDialog prefs_dlg;
		static SyncDialog sync_dlg;
#if ENABLE_DBUS || WIN32 || MAC
		static RemoteControl remote_control;
#endif
		static Gtk.IconTheme icon_theme = null;

		public static void Main (string [] args)
		{
			// TODO: Extract to a PreInit in Application, or something
#if WIN32
			string tomboy_path =
				Environment.GetEnvironmentVariable ("TOMBOY_PATH_PREFIX");
			string tomboy_gtk_basepath =
				Environment.GetEnvironmentVariable ("TOMBOY_GTK_BASEPATH");
			Environment.SetEnvironmentVariable ("GTK_BASEPATH",
				tomboy_gtk_basepath ?? string.Empty);
			if (string.IsNullOrEmpty (tomboy_path)) {
				string gtk_lib_path = null;
				try {
					gtk_lib_path = (string)
						Microsoft.Win32.Registry.GetValue (@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\.NETFramework\AssemblyFolders\GtkSharp",
						                                   string.Empty,
						                                   string.Empty);
				} catch (Exception e) {
					Console.WriteLine ("Exception while trying to get GTK# install path: " +
					                   e.ToString ());
				}
				if (!string.IsNullOrEmpty (gtk_lib_path))
					tomboy_path =
						gtk_lib_path.Replace ("lib\\gtk-sharp-2.0", "bin");
			}
			if (!string.IsNullOrEmpty (tomboy_path))
				Environment.SetEnvironmentVariable ("PATH",
				                                    tomboy_path +
				                                    Path.PathSeparator +
				                                    Environment.GetEnvironmentVariable ("PATH"));
#endif
			// Initialize GETTEXT
			Catalog.Init ("tomboy", Defines.GNOME_LOCALE_DIR);

			TomboyCommandLine cmd_line = new TomboyCommandLine (args);
			debugging = cmd_line.Debug;
			Logger.LogLevel = debugging ? Level.DEBUG : Level.INFO;
			is_panel_applet = cmd_line.UsePanelApplet;

#if ENABLE_DBUS || WIN32 || MAC // Run command-line earlier with DBus enabled
			if (cmd_line.NeedsExecute) {
				// Execute args at an existing tomboy instance...
				cmd_line.Execute ();
				return;
			}
#endif // ENABLE_DBUS || WIN32

			// NOTE: It is important not to use the Preferences
			//       class before this call.
			Initialize ("tomboy", "tomboy", "tomboy", args);

			// Add private icon dir to search path
			icon_theme = Gtk.IconTheme.Default;
			icon_theme.AppendSearchPath (Path.Combine (Path.Combine (Defines.DATADIR, "tomboy"), "icons"));

//   PluginManager.CheckPluginUnloading = cmd_line.CheckPluginUnloading;

			// Create the default note manager instance.
			string note_path = GetNotePath (cmd_line.NotePath);
			manager = new NoteManager (note_path);

			SyncManager.Initialize ();

			// Register the manager to handle remote requests.
			RegisterRemoteControl (manager);

			SetupGlobalActions ();
			ActionManager am = Tomboy.ActionManager;

			ApplicationAddin [] addins =
			        manager.AddinManager.GetApplicationAddins ();
			foreach (ApplicationAddin addin in addins) {
				addin.Initialize ();
			}

#if !ENABLE_DBUS && !WIN32 && !MAC
			if (cmd_line.NeedsExecute) {
				cmd_line.Execute ();
			}
#endif

			if (is_panel_applet) {
				tray_icon_showing = true;

				// Show the Close item and hide the Quit item
				am ["CloseWindowAction"].Visible = true;
				am ["QuitTomboyAction"].Visible = false;

				RegisterPanelAppletFactory ();
				Logger.Log ("All done.  Ciao!");
				Exit (0);
			} else {
				RegisterSessionManagerRestart (
				        Environment.GetEnvironmentVariable ("TOMBOY_WRAPPER_PATH"),
				        args,
				        new string [] { "TOMBOY_PATH=" + note_path  }); // TODO: Pass along XDG_*?
				StartTrayIcon ();
			}

			Logger.Log ("All done.  Ciao!");
		}

		public static bool Debugging
		{
			get { return debugging; }
		}

		static string GetNotePath (string override_path)
		{
			// Default note location, as specified in --note-path or $TOMBOY_PATH
			string note_path = (override_path != null) ?
			        override_path :
			        Environment.GetEnvironmentVariable ("TOMBOY_PATH");
			if (note_path == null)
				note_path = Services.NativeApplication.DataDirectory;

			// Tilde expand
			return note_path.Replace ("~", Environment.GetEnvironmentVariable ("HOME")); // TODO: Wasted work
		}

		static void RegisterPanelAppletFactory ()
		{
			// This will block if there is no existing instance running
#if !WIN32 && !MAC
			PanelAppletFactory.Register (typeof (TomboyApplet));
#endif
		}

		static void StartTrayIcon ()
		{
			// Create the tray icon and run the main loop
			tray_icon = new TomboyTrayIcon (manager);
			tray = tray_icon.Tray;

			// Give the TrayIcon 2 seconds to appear.  If it
			// doesn't by then, open the SearchAllNotes window.
			tray_icon_showing = tray_icon.IsEmbedded && tray_icon.Visible;
			if (!tray_icon_showing)
				GLib.Timeout.Add (2000, CheckTrayIconShowing);

			StartMainLoop ();
		}

		static bool CheckTrayIconShowing ()
		{
			tray_icon_showing = tray_icon.IsEmbedded && tray_icon.Visible;
			
			// Check to make sure the tray icon is showing.  If it's not,
			// it's likely that the Notification Area isn't available.  So
			// instead, launch the Search All Notes window so the user can
			// can still use Tomboy.
#if !MAC
			if (tray_icon_showing == false)
				ActionManager ["ShowSearchAllNotesAction"].Activate ();
#endif
			
			return false; // prevent GLib.Timeout from calling this method again
		}

		static void RegisterRemoteControl (NoteManager manager)
		{
#if ENABLE_DBUS || WIN32 || MAC
			try {
				remote_control = RemoteControlProxy.Register (manager);
				if (remote_control != null) {
					Logger.Log ("Tomboy remote control active.");
				} else {
					// If Tomboy is already running, open the search window
					// so the user gets some sort of feedback when they
					// attempt to run Tomboy again.
					IRemoteControl remote = null;
					try {
						remote = RemoteControlProxy.GetInstance ();
						remote.DisplaySearch ();
					} catch {}

					Logger.Log ("Tomboy is already running.  Exiting...");
					System.Environment.Exit (-1);
				}
			} catch (Exception e) {
				Logger.Log ("Tomboy remote control disabled (DBus exception): {0}",
				            e.Message);
			}
#endif
		}

		// These actions can be called from anywhere in Tomboy
		static void SetupGlobalActions ()
		{
			ActionManager am = Tomboy.ActionManager;
			am ["NewNoteAction"].Activated += OnNewNoteAction;
			am ["QuitTomboyAction"].Activated += OnQuitTomboyAction;
			am ["ShowPreferencesAction"].Activated += OnShowPreferencesAction;
			am ["ShowHelpAction"].Activated += OnShowHelpAction;
			am ["ShowAboutAction"].Activated += OnShowAboutAction;
			am ["TrayNewNoteAction"].Activated += OnNewNoteAction;
			am ["ShowSearchAllNotesAction"].Activated += OpenSearchAll;
			am ["NoteSynchronizationAction"].Activated += OpenNoteSyncWindow;
		}

		static void OnNewNoteAction (object sender, EventArgs args)
		{
			try {
				Note new_note = manager.Create ();
				new_note.Window.Show ();
			} catch (Exception e) {
				HIGMessageDialog dialog =
				        new HIGMessageDialog (
				        null,
				        0,
				        Gtk.MessageType.Error,
				        Gtk.ButtonsType.Ok,
				        Catalog.GetString ("Cannot create new note"),
				        e.Message);
				dialog.Run ();
				dialog.Destroy ();
			}
		}

		static void OpenNoteSyncWindow (object sender, EventArgs args)
		{
			if (sync_dlg == null) {
				sync_dlg = new SyncDialog ();
				sync_dlg.Response += OnSyncDialogResponse;
			}

			sync_dlg.Present ();
		}

		static void OnSyncDialogResponse (object sender, Gtk.ResponseArgs args)
		{
			((Gtk.Widget) sender).Destroy ();
			sync_dlg = null;
		}

		static void OnQuitTomboyAction (object sender, EventArgs args)
		{
			if (Tomboy.IsPanelApplet)
				return; // Ignore the quit action

			Logger.Log ("Quitting Tomboy.  Ciao!");
			Exit (0);
		}

		static void OnShowPreferencesAction (object sender, EventArgs args)
		{
			if (prefs_dlg == null) {
				prefs_dlg = new PreferencesDialog (manager.AddinManager);
				prefs_dlg.Response += OnPreferencesResponse;
			}
			prefs_dlg.Present ();
		}

		static void OnPreferencesResponse (object sender, Gtk.ResponseArgs args)
		{
			((Gtk.Widget) sender).Destroy ();
			prefs_dlg = null;
		}

		static void OnShowHelpAction (object sender, EventArgs args)
		{
			Gdk.Screen screen = null;
			if (tray_icon != null) {
#if WIN32 || MAC
				screen = tray_icon.Tray.TomboyTrayMenu.Screen;
#else
				Gdk.Rectangle area;
				Gtk.Orientation orientation;
				tray_icon.GetGeometry (out screen, out area, out orientation);
#endif
			}
			GuiUtils.ShowHelp ("ghelp:tomboy", screen, null);

		}

		static void OnShowAboutAction (object sender, EventArgs args)
		{
			string [] authors = new string [] {
				Catalog.GetString ("Primary Development:"),
				"\tAlex Graveley (original author)",
				"\tBoyd Timothy (retired maintainer)",
				"\tSandy Armstrong (maintainer)",
				"\t\t<sanfordarmstrong@gmail.com>",
				"",
				Catalog.GetString ("Contributors:"),
				"\tAaron Bockover",
				"\tAlexey Nedilko",
				"\tAlex Kloss",
				"\tAnders Petersson",
				"\tAndrew Fister",
				"\tBenjamin Podszun",
				"\tBuchner Johannes",
				"\tChris Scobell",
				"\tDave Foster",
				"\tDavid Trowbridge",
				"\tDoug Johnston",
				"\tEveraldo Canuto",
				"\tFrederic Crozat",
				"\tGabriel de Perthuis",
				"\tJakub Steiner",
				"\tJames Westby",
				"\tJamin Philip Gray",
				"\tJan Rüegg",
				"\tJay R. Wren",
				"\tJeffrey Stedfast",
				"\tJeff Tickle",
				"\tJerome Haltom",
				"\tJoe Shaw",
				"\tJohn Anderson",
				"\tJohn Carr",
				"\tJon Lund Steffensen",
				"\tJP Rosevear",
				"\tKevin Kubasik",
				"\tLaurent Bedubourg",
				"\tŁukasz Jernaś",
				"\tMark Wakim",
				"\tMathias Hasselmann",
				"\tMatt Johnston",
				"\tMike Mazur",
				"\tNathaniel Smith",
				"\tPrzemysław Grzegorczyk",
				"\tRobert Buchholz",
				"\tRobin Sonefors",
				"\tRodrigo Moya",
				"\tRomain Tartiere",
				"\tRyan Lortie",
				"\tSebastian Dröge",
				"\tSebastian Rittau",
				"\tStefan Cosma",
				"\tStefan Schweizer",
				"\tTommi Asiala",
				"\tWouter Bolsterlee",
				"\tYonatan Oren"
			};

			string [] documenters = new string [] {
				"Alex Graveley <alex@beatniksoftware.com>",
				"Boyd Timothy <btimothy@gmail.com>",
				"Brent Smith <gnome@nextreality.net>",
				"Paul Cutler <pcutler@foresightlinux.org>",
				"Sandy Armstrong <sanfordarmstrong@gmail.com>"
			};

			string translators = Catalog.GetString ("translator-credits");
			if (translators == "translator-credits")
				translators = null;

			Gtk.AboutDialog about = new Gtk.AboutDialog ();
			about.Name = "Tomboy";
			about.Version = Defines.VERSION;
			about.Logo = GuiUtils.GetIcon ("tomboy", 48);
			about.Copyright =
			        Catalog.GetString ("Copyright \xa9 2004-2007 Alex Graveley\n" +
				                   "Copyright \xa9 2004-2009 Others\n");
			about.Comments = Catalog.GetString ("A simple and easy to use desktop " +
			                                    "note-taking application.");
			Gtk.AboutDialog.SetUrlHook (delegate (Gtk.AboutDialog dialog, string link) {
				try {
					Services.NativeApplication.OpenUrl (link, null);
				} catch (Exception e) {
					GuiUtils.ShowOpeningLocationError (dialog, link, e.Message);
				}
			}); 
			about.Website = Defines.TOMBOY_WEBSITE;
			about.WebsiteLabel = Catalog.GetString("Homepage");
			about.Authors = authors;
			about.Documenters = documenters;
			about.TranslatorCredits = translators;
			about.IconName = "tomboy";
			about.Response += delegate {
				about.Destroy ();
			};
			about.Present ();
		}

		static void OpenSearchAll (object sender, EventArgs args)
		{
			NoteRecentChanges.GetInstance (manager).Present ();
		}

		public static NoteManager DefaultNoteManager
		{
			get {
				return manager;
			}
		}

		public static bool TrayIconShowing
		{
			get {
				tray_icon_showing = !is_panel_applet && tray_icon != null &&
					tray_icon.IsEmbedded && tray_icon.Visible;
				return tray_icon_showing;
			}
		}

		public static bool IsPanelApplet
		{
			get {
				return is_panel_applet;
			}
		}

		public static TomboyTray Tray
		{
			get {
				return tray;
			} set {
				tray = value;
			}
		}

		public static SyncDialog SyncDialog
		{
			get {
				return sync_dlg;
			}
		}
	}

	public class TomboyCommandLine
	{
		bool debug;
		bool new_note;
		bool panel_applet;
		string new_note_name;
		bool open_start_here;
		string open_note_uri;
		string open_note_name;
		string open_external_note_path;
		string highlight_search;
		string note_path;
		string search_text;
		bool open_search;
//  bool check_plugin_unloading;

		public TomboyCommandLine (string [] args)
		{
			Parse (args);
		}

		// TODO: Document this option
		public bool Debug
		{
			get { return debug; }
		}

		public bool UsePanelApplet
		{
			get {
				return panel_applet;
			}
		}

		public bool NeedsExecute
		{
			get {
				return new_note ||
				open_note_name != null ||
				open_note_uri != null ||
				open_search ||
				open_start_here ||
				open_external_note_path != null;
			}
		}

		public string NotePath
		{
			get {
				return note_path;
			}
		}

//  public bool CheckPluginUnloading
//  {
//   get { return check_plugin_unloading; }
//  }

		public static void PrintAbout ()
		{
			string about =
			        Catalog.GetString (
			                "Tomboy: A simple, easy to use desktop note-taking " +
			                "application.\n" +
			                "Copyright (C) 2004-2006 Alex Graveley " +
			                "<alex@beatniksoftware.com>\n\n");

			Console.Write (about);
		}

		public static void PrintUsage ()
		{
			string usage =
			        Catalog.GetString (
			                "Usage:\n" +
			                "  --version\t\t\tPrint version information.\n" +
			                "  --help\t\t\tPrint this usage message.\n" +
			                "  --note-path [path]\t\tLoad/store note data in this " +
			                "directory.\n" +
			                "  --search [text]\t\tOpen the search all notes window with " +
			                "the search text.\n");

#if ENABLE_DBUS || WIN32 || MAC
			usage +=
			        Catalog.GetString (
			                "  --new-note\t\t\tCreate and display a new note.\n" +
			                "  --new-note [title]\t\tCreate and display a new note, " +
			                "with a title.\n" +
			                "  --open-note [title/url]\tDisplay the existing note " +
			                "matching title.\n" +
			                "  --start-here\t\t\tDisplay the 'Start Here' note.\n" +
			                "  --highlight-search [text]\tSearch and highlight text " +
			                "in the opened note.\n");
#endif

// TODO: Restore this functionality with addins
//   usage +=
//    Catalog.GetString (
//     "  --check-plugin-unloading\tCheck if plugins are " +
//     "unloaded properly.\n");

#if !ENABLE_DBUS && !WIN32 && !MAC
			usage += Catalog.GetString ("D-BUS remote control disabled.\n");
#endif

			Console.WriteLine (usage);
		}

		public static void PrintVersion()
		{
			Console.WriteLine (Catalog.GetString ("Version {0}"), Defines.VERSION);
		}

		public void Parse (string [] args)
		{
			for (int idx = 0; idx < args.Length; idx++) {
				bool quit = false;

				switch (args [idx]) {
				case "--debug":
					debug = true;
					break;
#if ENABLE_DBUS || WIN32 || MAC
				case "--new-note":
					// Get optional name for new note...
					if (idx + 1 < args.Length
					                && args [idx + 1] != null
					                && args [idx + 1] != String.Empty
					                && args [idx + 1][0] != '-') {
						new_note_name = args [++idx];
					}

					new_note = true;
					break;

				case "--open-note":
					// Get required name for note to open...
					if (idx + 1 >= args.Length ||
					                (args [idx + 1] != null
					                 && args [idx + 1] != String.Empty
					                 && args [idx + 1][0] == '-')) {
						PrintUsage ();
						quit = true;
					}

					++idx;

					// If the argument looks like a Uri, treat it like a Uri.
					if (args [idx].StartsWith ("note://tomboy/"))
						open_note_uri = args [idx];
					else if (File.Exists (args [idx])) {
						// This is potentially a note file
						open_external_note_path = args [idx];
					} else
						open_note_name = args [idx];

					break;

				case "--start-here":
					// Open the Start Here note
					open_start_here = true;
					break;

				case "--highlight-search":
					// Get required search string to highlight
					if (idx + 1 >= args.Length ||
					                (args [idx + 1] != null
					                 && args [idx + 1] != String.Empty
					                 && args [idx + 1][0] == '-')) {
						PrintUsage ();
						quit = true;
					}

					++idx;
					highlight_search = args [idx];
					break;
#else
				case "--new-note":
				case "--open-note":
				case "--start-here":
				case "--highlight-search":
					string unknown_opt =
					        Catalog.GetString (
					                "Tomboy: unsupported option '{0}'\n" +
					                "Try 'tomboy --help' for more " +
					                "information.\n" +
					                "D-BUS remote control disabled.");
					Console.WriteLine (unknown_opt, args [idx]);
					quit = true;
					break;
#endif // ENABLE_DBUS || WIN32

				case "--panel-applet":
					panel_applet = true;
					break;

				case "--note-path":
					if (idx + 1 >= args.Length ||
					                (args [idx + 1] != null
					                 && args [idx + 1] != String.Empty
					                 && args [idx + 1][0] == '-')) {
						PrintUsage ();
						quit = true;
					}

					note_path = args [++idx];

					if (!Directory.Exists (note_path)) {
						Console.WriteLine (
						        "Tomboy: Invalid note path: " +
						        "\"{0}\" does not exist.",
						        note_path);
						quit = true;
					}

					break;

				case "--search":
					// Get optional search text...
					if (idx + 1 < args.Length
					                && args [idx + 1] != null
					                && args [idx + 1] != String.Empty
					                && args [idx + 1][0] != '-') {
						search_text = args [++idx];
					}

					open_search = true;
					break;

//    case "--check-plugin-unloading":
//     check_plugin_unloading = true;
//     break;

				case "--version":
					PrintAbout ();
					PrintVersion();
					quit = true;
					break;

				case "--help":
				case "--usage":
					PrintAbout ();
					PrintUsage ();
					quit = true;
					break;

				default:
					break;
				}

				if (quit == true)
					System.Environment.Exit (1);
			}
		}

		public void Execute ()
		{
#if ENABLE_DBUS || WIN32 || MAC
			IRemoteControl remote = null;
			try {
				remote = RemoteControlProxy.GetInstance ();
			} catch (Exception e) {
				Logger.Log ("Unable to connect to Tomboy remote control: {0}",
				            e.Message);
			}

			if (remote == null)
				return;

			if (new_note) {
				string new_uri;

				if (new_note_name != null) {
					new_uri = remote.FindNote (new_note_name);

					if (new_uri == null || new_uri == string.Empty)
						new_uri = remote.CreateNamedNote (new_note_name);
				} else
					new_uri = remote.CreateNote ();

				if (new_uri != null)
					remote.DisplayNote (new_uri);
			}

			if (open_start_here)
				open_note_uri = remote.FindStartHereNote ();

			if (open_note_name != null)
				open_note_uri = remote.FindNote (open_note_name);

			if (open_note_uri != null) {
				if (highlight_search != null)
					remote.DisplayNoteWithSearch (open_note_uri,
					                              highlight_search);
				else
					remote.DisplayNote (open_note_uri);
			}

			if (open_external_note_path != null) {
				string note_id = Path.GetFileNameWithoutExtension (open_external_note_path);
				if (note_id != null && note_id != string.Empty) {
					// Attempt to load the note, assuming it might already
					// be part of our notes list.
					if (remote.DisplayNote (
					                        string.Format ("note://tomboy/{0}", note_id)) == false) {

						StreamReader sr = File.OpenText (open_external_note_path);
						if (sr != null) {
							string noteTitle = null;
							string noteXml = sr.ReadToEnd ();

							// Make sure noteXml is parseable
							XmlDocument xmlDoc = new XmlDocument ();
							try {
								xmlDoc.LoadXml (noteXml);
							} catch {
							noteXml = null;
						}

						if (noteXml != null) {
								noteTitle = NoteArchiver.Instance.GetTitleFromNoteXml (noteXml);
								if (noteTitle != null) {
									// Check for conflicting titles
									string baseTitle = (string)noteTitle.Clone ();
									for (int i = 1; remote.FindNote (noteTitle) != string.Empty; i++)
										noteTitle = baseTitle + " (" + i.ToString() + ")";

									string note_uri = remote.CreateNamedNote (noteTitle);

									// Update title in the note XML
									noteXml = NoteArchiver.Instance.GetRenamedNoteXml (noteXml, baseTitle, noteTitle);

									if (note_uri != null) {
										// Load in the XML contents of the note file
										if (remote.SetNoteCompleteXml (note_uri, noteXml))
											remote.DisplayNote (note_uri);
									}
								}
							}
						}
					}
				}
			}

			if (open_search) {
				if (search_text != null)
					remote.DisplaySearchWithText (search_text);
				else
					remote.DisplaySearch ();
			}
#else
			if (open_search) {
				NoteRecentChanges recent_changes =
				        NoteRecentChanges.GetInstance (Tomboy.DefaultNoteManager);
				if (recent_changes == null)
					return;

				if (search_text != null)
					recent_changes.SearchText = search_text;

				recent_changes.Present ();
			}
#endif // ENABLE_DBUS || WIN32
		}
	}
}
