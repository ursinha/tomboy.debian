
using System;
using System.IO;
using System.Collections.Generic;
using Mono.Unix;

namespace Tomboy
{
	public delegate void NotesChangedHandler (object sender, Note changed);

	public class NoteManager
	{
		string notes_dir;
		string backup_dir;
		List<Note> notes;
		AddinManager addin_mgr;
		TrieController trie_controller;

		public static string NoteTemplateTitle = Catalog.GetString ("New Note Template");

		static string start_note_uri = String.Empty;

		static NoteManager ()
		{
			// Watch the START_NOTE_URI setting and update it so that the
			// StartNoteUri property doesn't generate a call to
			// Preferences.Get () each time it's accessed.
			start_note_uri =
			        Preferences.Get (Preferences.START_NOTE_URI) as string;
			Preferences.SettingChanged += OnSettingChanged;
		}

		static void OnSettingChanged (object sender, NotifyEventArgs args)
		{
			switch (args.Key) {
			case Preferences.START_NOTE_URI:
				start_note_uri = args.Value as string;
				break;
			}
		}

public NoteManager (string directory) :
		this (directory, Path.Combine (directory, "Backup"))
		{
		}

		public NoteManager (string directory, string backup_directory)
		{
			Logger.Log ("NoteManager created with note path \"{0}\".", directory);

			notes_dir = directory;
			backup_dir = backup_directory;
			notes = new List<Note> ();

			string conf_dir = Services.NativeApplication.ConfigurationDirectory;

			string old_notes_dir = null;
			bool migration_needed = false;

			bool first_run = FirstRun ();
			if (first_run) {
				old_notes_dir = Services.NativeApplication.PreOneDotZeroNoteDirectory;
				migration_needed = DirectoryExists (old_notes_dir);
			}
			CreateNotesDir ();
			if (!Directory.Exists (conf_dir))
				Directory.CreateDirectory (conf_dir);

			if (migration_needed) {
				// Copy notes
				foreach (string noteFile in Directory.GetFiles (old_notes_dir, "*.note"))
					File.Copy (noteFile, Path.Combine (notes_dir, Path.GetFileName (noteFile)));

				// Copy deleted notes
				string old_backup = Path.Combine (old_notes_dir, "Backup");
				if (DirectoryExists (old_backup)) {
					Directory.CreateDirectory (backup_dir);
					foreach (string noteFile in Directory.GetFiles (old_backup, "*.note"))
						File.Copy (noteFile, Path.Combine (backup_dir, Path.GetFileName (noteFile)));
				}

				// Copy configuration data
				// NOTE: Add-in configuration data copied by AddinManager
				string sync_manifest_name = "manifest.xml";
				string old_sync_manifest_path =
					Path.Combine (old_notes_dir, sync_manifest_name);
				if (File.Exists (old_sync_manifest_path))
					File.Copy (old_sync_manifest_path,
					           Path.Combine (conf_dir, sync_manifest_name));


				// NOTE: Not copying cached data

				first_run = false;
			}

			trie_controller = CreateTrieController ();
			addin_mgr = new AddinManager (conf_dir,
			                              migration_needed ? old_notes_dir : null);

			if (first_run) {
				// First run. Create "Start Here" notes.
				CreateStartNotes ();
			} else {
				LoadNotes ();
			}

			if (migration_needed) {
				// Create migration notification note
				// Translators: The title of the data migration note
				string base_migration_note_title = Catalog.GetString ("Your Notes Have Moved!");
				string migration_note_title = base_migration_note_title;

				// NOTE: Uncomment to generate a title suitable for
				//       putting in the note collection
//				int count = 1;
//				while (Find (migration_note_title) != null) {
//					migration_note_title = base_migration_note_title +
//						string.Format (" ({0})", ++count);
//				}

				string migration_note_content_template = Catalog.GetString (
// Translators: The contents (not including the title) of the data migration note. {0}, {1}, {2}, {3}, and {4} are replaced by directory paths and should not be changed
@"In the latest version of Tomboy, your note files have moved.  You have probably never cared where your notes are stored, and if you still don't care, please go ahead and <bold>delete this note</bold>.  :-)

Your old note directory is still safe and sound at <link:url>{0}</link:url> . If you go back to an older version of Tomboy, it will look for notes there.

But we've copied your notes and configuration info into new directories, which will be used from now on:

<list><list-item dir=""ltr"">Notes can now be found at <link:url>{1}</link:url>
</list-item><list-item dir=""ltr"">Configuration is at <link:url>{2}</link:url>
</list-item><list-item dir=""ltr"">You can install add-ins at <link:url>{3}</link:url>
</list-item><list-item dir=""ltr"">Log files can be found at <link:url>{4}</link:url></list-item></list>

Ciao!");
				string migration_note_content =
					"<note-content version=\"1.0\">" +
					migration_note_title + "\n\n" +
					string.Format (migration_note_content_template,
					               old_notes_dir + Path.DirectorySeparatorChar,
					               notes_dir + Path.DirectorySeparatorChar,
					               conf_dir + Path.DirectorySeparatorChar,
					               Path.Combine (conf_dir, "addins") + Path.DirectorySeparatorChar,
					               Services.NativeApplication.LogDirectory + Path.DirectorySeparatorChar) +
					"</note-content>";

				// NOTE: Uncomment to create a Tomboy note and
				//       show it to the user
//				Note migration_note = Create (migration_note_title,
//				                              migration_note_content);
//				migration_note.QueueSave (ChangeType.ContentChanged);
//				migration_note.Window.Show ();

				// Place file announcing migration in old note directory
				try {
					using (var writer = File.CreateText (Path.Combine (old_notes_dir,
					                                                   migration_note_title.ToUpper ().Replace (" ", "_"))))
						writer.Write (migration_note_content);
				} catch {}
			}

			Tomboy.ExitingEvent += OnExitingEvent;
		}
		
		
		// Create the TrieController. For overriding in test methods.
		protected virtual TrieController CreateTrieController ()
		{
			return new TrieController (this);
		}

		// For overriding in test methods.
		protected virtual bool DirectoryExists (string directory)
		{
			return Directory.Exists (directory);
		}

		// For overriding in test methods.
		protected virtual DirectoryInfo CreateDirectory (string directory)
		{
			return Directory.CreateDirectory (directory);
		}

		protected virtual bool FirstRun ()
		{
			return !DirectoryExists (notes_dir);
		}

		// Create the notes directory if it doesn't exist yet.
		void CreateNotesDir ()
		{
			if (!DirectoryExists (notes_dir)) {
				// First run. Create storage directory.
				CreateDirectory (notes_dir);
			}
		}

		void OnNoteRename (Note note, string old_title)
		{
			if (NoteRenamed != null)
				NoteRenamed (note, old_title);
			this.notes.Sort (new CompareDates ());
		}

		void OnNoteSave (Note note)
		{
			if (NoteSaved != null)
				NoteSaved (note);
			this.notes.Sort (new CompareDates ());
		}

		protected virtual void CreateStartNotes ()
		{
			// FIXME: Delay the creation of the start notes so the panel/tray
			// icon has enough time to appear so that Tomboy.TrayIconShowing
			// is valid.  Then, we'll be able to instruct the user where to
			// find the Tomboy icon.
			//string icon_str = Tomboy.TrayIconShowing ?
			//     Catalog.GetString ("System Tray Icon area") :
			//     Catalog.GetString ("GNOME Panel");
			string start_note_content =
			        Catalog.GetString ("<note-content>" +
			                           "Start Here\n\n" +
			                           "<bold>Welcome to Tomboy!</bold>\n\n" +
			                           "Use this \"Start Here\" note to begin organizing " +
			                           "your ideas and thoughts.\n\n" +
			                           "You can create new notes to hold your ideas by " +
			                           "selecting the \"Create New Note\" item from the " +
			                           "Tomboy Notes menu in your GNOME Panel. " +
			                           "Your note will be saved automatically.\n\n" +
			                           "Then organize the notes you create by linking " +
			                           "related notes and ideas together!\n\n" +
			                           "We've created a note called " +
			                           "<link:internal>Using Links in Tomboy</link:internal>.  " +
			                           "Notice how each time we type <link:internal>Using " +
			                           "Links in Tomboy</link:internal> it automatically " +
			                           "gets underlined?  Click on the link to open the note." +
			                           "</note-content>");

			string links_note_content =
			        Catalog.GetString ("<note-content>" +
			                           "Using Links in Tomboy\n\n" +
			                           "Notes in Tomboy can be linked together by " +
			                           "highlighting text in the current note and clicking" +
			                           " the <bold>Link</bold> button above in the toolbar.  " +
			                           "Doing so will create a new note and also underline " +
			                           "the note's title in the current note.\n\n" +
			                           "Changing the title of a note will update links " +
			                           "present in other notes.  This prevents broken links " +
			                           "from occurring when a note is renamed.\n\n" +
			                           "Also, if you type the name of another note in your " +
			                           "current note, it will automatically be linked for you." +
			                           "</note-content>");

			try {
				Note start_note = Create (Catalog.GetString ("Start Here"),
				                          start_note_content);
				start_note.QueueSave (ChangeType.ContentChanged);
				Preferences.Set (Preferences.START_NOTE_URI, start_note.Uri);

				Note links_note = Create (Catalog.GetString ("Using Links in Tomboy"),
				                          links_note_content);
				links_note.QueueSave (ChangeType.ContentChanged);

				if (!Tomboy.IsPanelApplet)
					start_note.Window.Show ();
			} catch (Exception e) {
				Logger.Warn ("Error creating start notes: {0}\n{1}",
				             e.Message, e.StackTrace);
			}
		}

		protected virtual void LoadNotes ()
		{
			string [] files = Directory.GetFiles (notes_dir, "*.note");

			foreach (string file_path in files) {
				try {
					Note note = Note.Load (file_path, this);
					if (note != null) {
						note.Renamed += OnNoteRename;
						note.Saved += OnNoteSave;
						notes.Add (note);
					}
				} catch (System.Xml.XmlException e) {
					Logger.Log ("Error parsing note XML, skipping \"{0}\": {1}",
					            file_path,
					            e.Message);
				}
			}
			
			notes.Sort (new CompareDates ());

			// Update the trie so addins can access it, if they want.
			trie_controller.Update ();

			bool startup_notes_enabled = (bool)
			                             Preferences.Get (Preferences.ENABLE_STARTUP_NOTES);

			// Load all the addins for our notes.
			// Iterating through copy of notes list, because list may be
			// changed when loading addins.
			List<Note> notesCopy = new List<Note> (notes);
			foreach (Note note in notesCopy) {
				addin_mgr.LoadAddinsForNote (note);

				// Show all notes that were visible when tomboy was shut down
				if (note.IsOpenOnStartup) {
					if (startup_notes_enabled)
						note.Window.Show ();

					note.IsOpenOnStartup = false;
					note.QueueSave (ChangeType.NoChange);
				}
			}

			// Make sure that a Start Note Uri is set in the preferences, and
			// make sure that the Uri is valid to prevent bug #508982. This
			// has to be done here for long-time Tomboy users who won't go
			// through the CreateStartNotes () process.
			if (StartNoteUri == String.Empty ||
			    FindByUri(StartNoteUri) == null) {
				// Attempt to find an existing Start Here note
				Note start_note = Find (Catalog.GetString ("Start Here"));
				if (start_note != null)
					Preferences.Set (Preferences.START_NOTE_URI, start_note.Uri);
			}
		}

		void OnExitingEvent (object sender, EventArgs args)
		{
			// Call ApplicationAddin.Shutdown () on all the known ApplicationAddins
			foreach (ApplicationAddin addin in addin_mgr.GetApplicationAddins ()) {
				try {
					addin.Shutdown ();
				} catch (Exception e) {
					Logger.Warn ("Error calling {0}.Shutdown (): {1}",
					             addin.GetType ().ToString (), e.Message);
				}
			}

			Logger.Log ("Saving unsaved notes...");
			
			// Use a copy of the notes to prevent bug #510442 (crash on exit
			// when iterating the notes to save them.
			List<Note> notesCopy = new List<Note> (notes);
			foreach (Note note in notesCopy) {
				// If the note is visible, it will be shown automatically on
				// next startup
				if (note.HasWindow && note.Window.Visible)
					note.IsOpenOnStartup = true;

				note.Save ();
			}
		}

		public void Delete (Note note)
		{
			if (File.Exists (note.FilePath)) {
				if (backup_dir != null) {
					if (!Directory.Exists (backup_dir))
						Directory.CreateDirectory (backup_dir);

					string backup_path =
					        Path.Combine (backup_dir,
					                      Path.GetFileName (note.FilePath));
					if (File.Exists (backup_path))
						File.Delete (backup_path);

					File.Move (note.FilePath, backup_path);
				} else
					File.Delete (note.FilePath);
			}

			notes.Remove (note);
			note.Delete ();

			Logger.Log ("Deleting note '{0}'.", note.Title);

			if (NoteDeleted != null)
				NoteDeleted (this, note);
		}

		string MakeNewFileName ()
		{
			return MakeNewFileName (Guid.NewGuid ().ToString ());
		}

		string MakeNewFileName (string guid)
		{
			return Path.Combine (notes_dir, guid + ".note");
		}

		// Create a new note with a generated title
		public Note Create ()
		{
			int new_num = notes.Count;
			string temp_title;

			while (true) {
				temp_title = String.Format (Catalog.GetString ("New Note {0}"),
				                            ++new_num);
				if (Find (temp_title) == null)
					break;
			}

			return Create (temp_title);
		}

		public static string SplitTitleFromContent (string title, out string body)
		{
			body = null;

			if (title == null)
				return null;

			title = title.Trim();
			if (title == string.Empty)
				return null;

			string[] lines = title.Split (new char[] { '\n', '\r' }, 2);
			if (lines.Length > 0) {
				title = lines [0];
				title = title.Trim ();
				title = title.TrimEnd ('.', ',', ';');
				if (title == string.Empty)
					return null;
			}

			if (lines.Length > 1)
				body = lines [1];

			return title;
		}

		public Note Create (string title)
		{
			return CreateNewNote (title, null);
		}

		public Note Create (string title, string xml_content)
		{
			return CreateNewNote (title, xml_content, null);
		}

		public Note CreateWithGuid (string title, string guid)
		{
			return CreateNewNote (title, guid);
		}

		// Create a new note with the specified title, and a simple
		// "Describe..." body or the body from the "New Note Template"
		// note if it exists.  If the "New Note Template" body is found
		// the text will not automatically be highlighted.
		private Note CreateNewNote (string title, string guid)
		{
			string body = null;

			title = SplitTitleFromContent (title, out body);
			if (title == null)
				return null;
			
			Note note_template = Find (NoteTemplateTitle);
			if (note_template != null) {
				// Use the body from the "New Note Template" note
				string xml_content =
					note_template.XmlContent.Replace (NoteTemplateTitle,
					                                  XmlEncoder.Encode (title));
				return CreateNewNote (title, xml_content, guid);
			}
			
			// Use a simple "Describe..." body and highlight
			// it so it can be easily overwritten
			body = Catalog.GetString ("Describe your new note here.");

			string header = title + "\n\n";
			string content =
			        String.Format ("<note-content>{0}{1}</note-content>",
			                       XmlEncoder.Encode (header),
			                       XmlEncoder.Encode (body));

			Note new_note = CreateNewNote (title, content, guid);

			// Select the inital
			// "Describe..." text so typing will overwrite the body text,
			NoteBuffer buffer = new_note.Buffer;
			Gtk.TextIter iter = buffer.GetIterAtOffset (header.Length);
			buffer.MoveMark (buffer.SelectionBound, iter);
			buffer.MoveMark (buffer.InsertMark, buffer.EndIter);

			return new_note;
		}

		// Create a new note with the specified Xml content
		private Note CreateNewNote (string title, string xml_content, string guid)
		{
			if (title == null || title == string.Empty)
				throw new Exception ("Invalid title");

			if (Find (title) != null)
				throw new Exception ("A note with this title already exists: " + title);

			string filename;
			if (guid != null)
				filename = MakeNewFileName (guid);
			else
				filename = MakeNewFileName ();

			Note new_note = Note.CreateNewNote (title, filename, this);
			new_note.XmlContent = xml_content;
			new_note.Renamed += OnNoteRename;
			new_note.Saved += OnNoteSave;

			notes.Add (new_note);

			// Load all the addins for the new note
			addin_mgr.LoadAddinsForNote (new_note);

			if (NoteAdded != null)
				NoteAdded (this, new_note);

			return new_note;
		}
		
		/// <summary>
		/// Get the existing template note or create a new one
		/// if it doesn't already exist.
		/// </summary>
		/// <returns>
		/// A <see cref="Note"/>
		/// </returns>
		public Note GetOrCreateTemplateNote ()
		{
			Note template_note = Find (NoteTemplateTitle);
			if (template_note == null) {
				template_note =
					Create (NoteTemplateTitle,
							GetNoteTemplateContent (NoteTemplateTitle));
					
				// Select the initial text
				NoteBuffer buffer = template_note.Buffer;
				Gtk.TextIter iter = buffer.GetIterAtLineOffset (2, 0);
				buffer.MoveMark (buffer.SelectionBound, iter);
				buffer.MoveMark (buffer.InsertMark, buffer.EndIter);

				// Flag this as a template note
				Tag tag = TagManager.GetOrCreateSystemTag (TagManager.TemplateNoteSystemTag);
				template_note.AddTag (tag);

				template_note.QueueSave (ChangeType.ContentChanged);
			}
			
			return template_note;
		}
		
		public static string GetNoteTemplateContent (string title)
		{
			const string base_xml =
			        "<note-content>" +
			        "<note-title>{0}</note-title>\n\n" +
			        "{1}" +
			        "</note-content>";

			return string.Format (base_xml,
			                      XmlEncoder.Encode (title),
			                      Catalog.GetString ("Describe your new note here."));
		}

		public Note Find (string linked_title)
		{
			foreach (Note note in notes) {
				if (note.Title.ToLower () == linked_title.ToLower ())
					return note;
			}
			return null;
		}

		public Note FindByUri (string uri)
		{
			foreach (Note note in notes) {
				if (note.Uri == uri)
					return note;
			}
			return null;
		}

		class CompareDates : IComparer<Note>
		{
			public int Compare (Note a, Note b)
			{
				// Sort in reverse chrono order...
				if (a == null || b == null)
					return -1;
				else
					return DateTime.Compare (b.ChangeDate,
					                         a.ChangeDate);
			}
		}

		public static string StartNoteUri
		{
			get {
				return start_note_uri;
			}
		}

		public List<Note> Notes
		{
			get {
				// FIXME: Only sort on change by listening to
				//        Note.Saved or Note.Buffer.Changed
				//notes.Sort (new CompareDates ());
				return notes;
			}
		}

		public TrieTree TitleTrie
		{
			get {
				return trie_controller.TitleTrie;
			}
		}

		public AddinManager AddinManager
		{
			get {
				return addin_mgr;
			}
		}

		public string NoteDirectoryPath
		{
			get {
				return notes_dir;
			}
		}

		public event NotesChangedHandler NoteDeleted;
		public event NotesChangedHandler NoteAdded;
		public event NoteRenameHandler NoteRenamed;
		public event NoteSavedHandler NoteSaved;
	}

	public class TrieController
	{
		TrieTree title_trie;
		NoteManager manager;

		public TrieController (NoteManager manager)
		{
			this.manager = manager;
			manager.NoteDeleted += OnNoteDeleted;
			manager.NoteAdded += OnNoteAdded;
			manager.NoteRenamed += OnNoteRenamed;

			Update ();
		}

		void OnNoteAdded (object sender, Note added)
		{
			Update ();
		}

		void OnNoteDeleted (object sender, Note deleted)
		{
			Update ();
		}

		void OnNoteRenamed (Note renamed, string old_title)
		{
			Update ();
		}

		public void Update ()
		{
			title_trie = new TrieTree (false /* !case_sensitive */);

			foreach (Note note in manager.Notes) {
				title_trie.AddKeyword (note.Title, note);
			}
		}

		public TrieTree TitleTrie
		{
			get {
				return title_trie;
			}
		}
	}
}
