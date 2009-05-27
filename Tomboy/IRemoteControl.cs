﻿using System;

namespace Tomboy
{
	public interface IRemoteControl
	{
		bool AddTagToNote (string uri, string tag_name);
		string CreateNamedNote (string linked_title);
		string CreateNote ();
		bool DeleteNote (string uri);
		bool DisplayNote (string uri);
		bool DisplayNoteWithSearch (string uri, string search);
		void DisplaySearch ();
		void DisplaySearchWithText (string search_text);
		string FindNote (string linked_title);
		string FindStartHereNote ();
		string [] GetAllNotesWithTag (string tag_name);
		long GetNoteChangeDate (string uri);
		string GetNoteCompleteXml (string uri);
		string GetNoteContents (string uri);
		string GetNoteContentsXml (string uri);
		long GetNoteCreateDate (string uri);
		string GetNoteTitle (string uri);
		string [] GetTagsForNote (string uri);
		bool HideNote (string uri);
		string [] ListAllNotes ();
		event RemoteAddedHandler NoteAdded;
		event RemoteDeletedHandler NoteDeleted;
		bool NoteExists (string uri);
		event RemoteSavedHandler NoteSaved;
		bool RemoveTagFromNote (string uri, string tag_name);
		string [] SearchNotes (string query, bool case_sensitive);
		bool SetNoteCompleteXml (string uri, string xml_contents);
		bool SetNoteContents (string uri, string text_contents);
		bool SetNoteContentsXml (string uri, string xml_contents);
		string Version ();
	}
}
