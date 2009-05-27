
using System;
using System.Collections.Generic;
#if ENABLE_DBUS
using NDesk.DBus;
using org.freedesktop.DBus;
#endif

namespace Tomboy
{
	public delegate void RemoteDeletedHandler (string uri, string title);
	public delegate void RemoteAddedHandler (string uri);
	public delegate void RemoteSavedHandler (string uri);
#if ENABLE_DBUS
	[Interface ("org.gnome.Tomboy.RemoteControl")]
#endif
	public class RemoteControl : MarshalByRefObject, IRemoteControl
	{
		private NoteManager note_manager;

		public RemoteControl (NoteManager mgr)
		{
			note_manager = mgr;
			note_manager.NoteDeleted += OnNoteDeleted;
			note_manager.NoteAdded += OnNoteAdded;
			note_manager.NoteSaved += OnNoteSaved;
		}

		//Convert System.DateTime to unix timestamp
		private static long UnixDateTime(DateTime d)
		{
			long epoch_ticks = new DateTime (1970,1,1).Ticks;
			//Ticks is in 100s of nanoseconds, unix time is in seconds
			return (d.ToUniversalTime ().Ticks - epoch_ticks) / 10000000;
		}

		public string Version ()
		{
			return Defines.VERSION;
		}

		public bool DisplayNote (string uri)
		{
			Note note;

			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;

			note.Window.Present ();
			return true;
		}

		public bool HideNote (string uri)
		{
			Note note;

			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;

			note.Window.Hide ();
			return true;
		}

		public bool DisplayNoteWithSearch (string uri, string search)
		{
			Note note;

			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;

			note.Window.Present ();

			// Pop open the find-bar
			NoteFindBar find = note.Window.Find;
			find.ShowAll ();
			find.Visible = true;
			find.SearchText = search;

			return true;
		}

		public string FindNote (string linked_title)
		{
			Note note = note_manager.Find (linked_title);
			return (note == null) ? String.Empty : note.Uri;
		}

		public string FindStartHereNote ()
		{
			Note note = note_manager.FindByUri (NoteManager.StartNoteUri);
			return (note == null) ? String.Empty : note.Uri;
		}

		public string CreateNote ()
		{
			try {
				Note note = note_manager.Create ();
				return note.Uri;
			} catch {
			return  "";
		}
	}

	public string CreateNamedNote (string linked_title)
		{
			Note note;

			note = note_manager.Find (linked_title);
			if (note != null)
				return "";

			try {
				note = note_manager.Create (linked_title);
				return note.Uri;
			} catch {
			return "";
		}
	}

	public bool DeleteNote (string uri)
		{
			Note note;

			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;

			note_manager.Delete (note);
			return true;
		}

		public void DisplaySearch ()
		{
			NoteRecentChanges.GetInstance (note_manager).Present ();
		}

		public void DisplaySearchWithText (string search_text)
		{
			NoteRecentChanges recent_changes =
			        NoteRecentChanges.GetInstance (note_manager);
			if (recent_changes == null)
				return;

			recent_changes.SearchText = search_text;
			recent_changes.Present ();
		}

		public bool NoteExists (string uri)
		{
			Note note = note_manager.FindByUri (uri);
			return note != null;
		}

		public string[] ListAllNotes ()
		{
			List<string> uris = new List<string> ();
			foreach (Note note in note_manager.Notes) {
				uris.Add (note.Uri);
			}
			return uris.ToArray ();
		}

		public string GetNoteContents (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return "";
			return note.TextContent;
		}

		public string GetNoteTitle (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return "";
			return note.Title;
		}

		public long GetNoteCreateDate (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return -1;
			return UnixDateTime (note.CreateDate);
		}

		public long GetNoteChangeDate (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return -1;
			return UnixDateTime (note.MetadataChangeDate);
		}

		public string GetNoteContentsXml (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return "";
			return note.XmlContent;
		}

		public string GetNoteCompleteXml (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return "";
			return note.GetCompleteNoteXml () ?? string.Empty;
		}

		public bool SetNoteContents (string uri, string text_contents)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;
			note.TextContent = text_contents;
			return true;
		}

		public bool SetNoteContentsXml (string uri, string xml_contents)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;
			note.XmlContent = xml_contents;
			return true;
		}

		/// <summary>
		/// Reset the entire XML data for the given note.
		/// NOTE: Throws exception if xml_contents is invalid.
		/// </summary>
		public bool SetNoteCompleteXml (string uri, string xml_contents)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;
			note.LoadForeignNoteXml (xml_contents, ChangeType.ContentChanged);
			return true;
		}

		public string[] GetTagsForNote (string uri)
		{
			Note note = note_manager.FindByUri (uri);
			if (note == null)
				return new string [0];
			string [] tags = new string [note.Tags.Count];
			for (int i = 0; i < tags.Length; i++)
				tags [i] = note.Tags [i].NormalizedName;
			return tags;
		}

		public bool AddTagToNote (string uri, string tag_name)
		{
			Note note = note_manager.FindByUri (uri);
			if (note == null)
				return false;
			Tag tag = TagManager.GetOrCreateTag (tag_name);
			note.AddTag (tag);
			return true;
		}

		public bool RemoveTagFromNote (string uri, string tag_name)
		{
			Note note = note_manager.FindByUri (uri);
			if (note == null)
				return false;
			Tag tag = TagManager.GetTag (tag_name);
			if (tag != null)
				note.RemoveTag (tag);
			return true;
		}

		public string[] GetAllNotesWithTag (string tag_name)
		{
			Tag tag = TagManager.GetTag (tag_name);
			if (tag == null)
				return new string [0];
			string [] tagged_note_uris = new string [tag.Notes.Count];
			for (int i = 0; i < tagged_note_uris.Length; i++)
				tagged_note_uris [i] = tag.Notes [i].Uri;
			return tagged_note_uris;
		}

		private void OnNoteDeleted (object sender, Note note)
		{
			if (NoteDeleted != null)
				NoteDeleted (note.Uri, note.Title);
		}

		private void OnNoteAdded (object sender, Note note)
		{
			if (NoteAdded != null)
				NoteAdded (note.Uri);
		}

		private void OnNoteSaved (Note note)
		{
			if (NoteSaved != null)
				NoteSaved (note.Uri);
		}

		public string[] SearchNotes (string query, bool case_sensitive)
		{
			if (query == null)
				return null;

			Search search =  new Search(note_manager);
			List<string> list = new List<string>();
			IDictionary<Note,int> results =
				search.SearchNotes(query, case_sensitive, null);
			foreach (Note note in results.Keys) {
				list.Add (note.Uri);
			}
			return list.ToArray ();
		}

		public event RemoteDeletedHandler NoteDeleted;
		public event RemoteAddedHandler NoteAdded;
		public event RemoteSavedHandler NoteSaved;
	}
}
