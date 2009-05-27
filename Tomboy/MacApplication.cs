// Permission is hereby granted, free of charge, to any person obtaining 
// a copy of this software and associated documentation files (the 
// "Software"), to deal in the Software without restriction, including 
// without limitation the rights to use, copy, modify, merge, publish, 
// distribute, sublicense, and/or sell copies of the Software, and to 
// permit persons to whom the Software is furnished to do so, subject to 
// the following conditions: 
//  
// The above copyright notice and this permission notice shall be 
// included in all copies or substantial portions of the Software. 
//  
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND 
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE 
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION 
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION 
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
// 
// Copyright (c) 2008 Novell, Inc. (http://www.novell.com) 
// 
// Authors: 
//      Sandy Armstrong <sanfordarmstrong@gmail.com>
// 

using System;
using System.Runtime.InteropServices;
using Mono.Unix;

using Gtk;
using IgeMacIntegration;

using System.Collections.Generic;

namespace Tomboy
{
	public class WindowMenuManager
	{
		static List<Note> openNotes = new List<Note> ();
		static bool initialized;
		
		public static void WatchNote (Note note)
		{
			if (!openNotes.Contains (note)) {
				openNotes.Add (note);
				UpdateWindowMenu ();
			}
		}
		
		public static void UnwatchNote (Note note)
		{
			openNotes.Remove (note);
			UpdateWindowMenu ();
		}
		
		public static void UpdateWindowMenu ()
		{
			MenuItem searchItem =
				Tomboy.ActionManager.UI.GetWidget (
				                                   "/MainWindowMenubar/MainWindowMenuPlaceholder/WindowMenu/ShowSearchAllNotesAction") as MenuItem;
			
			if (searchItem == null) {
				return;
			}
			Menu windowMenu = searchItem.Parent as Menu;

			if (windowMenu == null) {
				return;
			}
			windowMenu.HideAll ();
			foreach (MenuItem child in windowMenu.Children) {
				if (child is OpenNoteMenuItem) {
					windowMenu.Remove (child);
					child.Destroy (); // TODO: Necessary?
				}
			};
				
			foreach (Note note in openNotes) {
				MenuItem noteItem = new OpenNoteMenuItem (note);
				windowMenu.Add (noteItem);
			}
			
			windowMenu.ShowAll ();
		}
	}
	
	public class OpenNoteMenuItem : MenuItem
	{
		private Note note;
		
		public OpenNoteMenuItem (Note note) : base (note.Title)
		{
			this.note = note;
		}
		
		protected override void OnActivated ()
		{
			note.Window.Present ();
			base.OnActivated ();
		}
	}

	public class OpenNoteWatcher : NoteAddin
	{
		public override void OnNoteOpened ()
		{
			WindowMenuManager.WatchNote (Note);
			Note.Window.Hidden += OnHidden;
			Note.Window.Shown += OnShown;
		}
		
		public override void Shutdown ()
		{
			WindowMenuManager.UnwatchNote (Note);
			if (Note.HasWindow) {
				Note.Window.Hidden -= OnHidden;
				Note.Window.Shown -= OnShown;
			}
		}

		public override void Initialize ()
		{
		}

		private void OnHidden (object sender, EventArgs args)
		{
			WindowMenuManager.UnwatchNote (Note);
		}
		
		private void OnShown (object sender, EventArgs args)
		{
			WindowMenuManager.WatchNote (Note);
		}

	}
	
	public class MacApplication : WindowsApplication
	{
		private const string osxMenuXml =@"
<ui>
  <menubar name=""MainWindowMenubar"">
    <placeholder name=""MainWindowMenuPlaceholder"">
      <menu name=""WindowMenu"" action=""WindowMenuAction"">
        <menuitem action=""ShowSearchAllNotesAction""/>
	<separator />
      </menu>
    </placeholder>
  </menubar>
</ui>
";
		public override void StartMainLoop ()
		{
			Gtk.UIManager uiManager = Tomboy.ActionManager.UI;
			
			ActionGroup mainMenuActionGroup = new ActionGroup ("Mac");
			mainMenuActionGroup.Add (new ActionEntry [] {
				new ActionEntry ("WindowMenuAction",
				                 null,
				                 // Translators: This is the name of "Window" menu in the Mac menubar
				                 Catalog.GetString ("_Window"),
				                 null,
				                 null,
				                 null)
			});
			
			uiManager.AddUiFromString (osxMenuXml);
			uiManager.InsertActionGroup (mainMenuActionGroup, 1);
			
			Gtk.MenuShell mainMenu = uiManager.GetWidget ("/MainWindowMenubar") as Gtk.MenuShell;
			mainMenu.Show ();
			IgeMacMenu.MenuBar = mainMenu;
			WindowMenuManager.UpdateWindowMenu ();

			Gtk.MenuItem about_item = uiManager.GetWidget ("/MainWindowMenubar/HelpMenu/ShowAbout") as Gtk.MenuItem;
			Gtk.MenuItem prefs_item = uiManager.GetWidget ("/MainWindowMenubar/EditMenu/ShowPreferences") as Gtk.MenuItem;
			Gtk.MenuItem quit_item  = uiManager.GetWidget ("/MainWindowMenubar/FileMenu/QuitTomboy") as Gtk.MenuItem;
			
			IgeMacMenuGroup about_group = IgeMacMenu.AddAppMenuGroup ();
			IgeMacMenuGroup prefs_group = IgeMacMenu.AddAppMenuGroup ();

			about_group.AddMenuItem (about_item, null);
			prefs_group.AddMenuItem (prefs_item, null);
			
			IgeMacMenu.QuitMenuItem = quit_item;
			
			IgeMacDock dock = new IgeMacDock();
			dock.Clicked += delegate (object sender, EventArgs args) {
				if (Tomboy.Tray.TomboyTrayMenu.Visible)
					Tomboy.Tray.TomboyTrayMenu.Hide ();
				else
					Tomboy.Tray.Tray.ShowMenu (false);
			};
			dock.QuitActivate += delegate (object sender, EventArgs args) { Exit (0); };
			
			Tomboy.ActionManager ["CloseWindowAction"].Visible = false;
			
			base.StartMainLoop ();
		}
			
			[DllImport ("libc", EntryPoint="system")]
			public static extern int system (string command);
			
			public override void OpenUrl (string url)
			{
				system ("open \"" + url + "\"");
			}


	}
}
