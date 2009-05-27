
using System;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using Mono.Unix;

using Tomboy;

namespace Tomboy.ExportToHtml
{
	public class ExportToHtmlNoteAddin : NoteAddin
	{
		const string stylesheet_name = "ExportToHtml.xsl";
		static XslTransform xsl;

		Gtk.ImageMenuItem item;

		static XslTransform NoteXsl
		{
			get {
				if (xsl == null) {
					Assembly asm = Assembly.GetExecutingAssembly ();
					string asm_dir = System.IO.Path.GetDirectoryName (asm.Location);
					string stylesheet_file = Path.Combine (asm_dir, stylesheet_name);
		
					xsl = new XslTransform ();
		
					if (File.Exists (stylesheet_file)) {
						Logger.Log ("ExportToHTML: Using user-custom {0} file.",
						            stylesheet_name);
						xsl.Load (stylesheet_file);
					} else {
						Stream resource = asm.GetManifestResourceStream (stylesheet_name);
						if (resource != null) {
							XmlTextReader reader = new XmlTextReader (resource);
							xsl.Load (reader, null, null);
							resource.Close ();
						} else {
							Logger.Log ("Unable to find HTML export template '{0}'.",
							            stylesheet_name);
						}
					}
				}
				return xsl;
			}
		}

		public override void Initialize ()
		{
		}

		public override void Shutdown ()
		{
			// Disconnect the event handlers so
			// there aren't any memory leaks.
			if (item != null)
				item.Activated -= ExportButtonClicked;
		}

		public override void OnNoteOpened ()
		{
			item =
			        new Gtk.ImageMenuItem (Catalog.GetString ("Export to HTML"));
			item.Image = new Gtk.Image (Gtk.Stock.Save, Gtk.IconSize.Menu);
			item.Activated += ExportButtonClicked;
			item.Show ();
			AddPluginMenuItem (item);
		}

		void ExportButtonClicked (object sender, EventArgs args)
		{
			ExportToHtmlDialog dialog = new ExportToHtmlDialog (Note.Title + ".html");
			int response = dialog.Run();
			string output_path = dialog.Filename;

			if (response != (int) Gtk.ResponseType.Ok) {
				dialog.Destroy ();
				return;
			}

			Logger.Log ("Exporting Note '{0}' to '{1}'...", Note.Title, output_path);

			StreamWriter writer = null;
			string error_message = null;

			try {
				try {
					// FIXME: Warn about file existing.  Allow overwrite.
					File.Delete (output_path);
				} catch {
			}

			writer = new StreamWriter (output_path);
				WriteHTMLForNote (writer, Note, dialog.ExportLinked, dialog.ExportLinkedAll);

				// Save the dialog preferences now that the note has
				// successfully been exported
				dialog.SavePreferences ();
				dialog.Destroy ();
				dialog = null;

				try {
					Uri output_uri = new Uri (output_path);
					Services.NativeApplication.OpenUrl (output_uri.AbsoluteUri);
				} catch (Exception ex) {
					Logger.Log ("Could not open exported note in a web browser: {0}",
					            ex);

					string detail = String.Format (
					                        Catalog.GetString ("Your note was exported to \"{0}\"."),
					                        output_path);

					// Let the user know the note was saved successfully
					// even though showing the note in a web browser failed.
					HIGMessageDialog msg_dialog =
					        new HIGMessageDialog (
					        Window,
					        Gtk.DialogFlags.DestroyWithParent,
					        Gtk.MessageType.Info,
					        Gtk.ButtonsType.Ok,
					        Catalog.GetString ("Note exported successfully"),
					        detail);
					msg_dialog.Run ();
					msg_dialog.Destroy ();
				}
			} catch (UnauthorizedAccessException) {
				error_message = Catalog.GetString ("Access denied.");
			} catch (DirectoryNotFoundException) {
				error_message = Catalog.GetString ("Folder does not exist.");
			} catch (Exception e) {
				Logger.Log ("Could not export: {0}", e);

				error_message = e.Message;
			} finally {
				if (writer != null)
					writer.Close ();
			}

			if (error_message != null)
			{
				Logger.Log ("Could not export: {0}", error_message);

				string msg = String.Format (
				                     Catalog.GetString ("Could not save the file \"{0}\""),
				                     output_path);

				HIGMessageDialog msg_dialog =
				        new HIGMessageDialog (
				        dialog,
				        Gtk.DialogFlags.DestroyWithParent,
				        Gtk.MessageType.Error,
				        Gtk.ButtonsType.Ok,
				        msg,
				        error_message);
				msg_dialog.Run ();
				msg_dialog.Destroy ();
			}

			if (dialog != null)
				dialog.Destroy ();
		}

		public void WriteHTMLForNote (TextWriter writer,
		                              Note note,
		                              bool export_linked,
		                              bool export_linked_all)
		{
			// NOTE: Don't use the XmlDocument version, which strips
			// whitespace between elements for some reason.  Also,
			// XPathDocument is faster.
			StringWriter s_writer = new StringWriter ();
			NoteArchiver.Write (s_writer, note.Data);
			StringReader reader = new StringReader (s_writer.ToString ());
			s_writer.Close ();
			XPathDocument doc = new XPathDocument (reader);
			reader.Close ();

			XsltArgumentList args = new XsltArgumentList ();
			args.AddParam ("export-linked", "", export_linked);
			args.AddParam ("export-linked-all", "", export_linked_all);
			args.AddParam ("root-note", "", note.Title);
			args.AddExtensionObject ("http://beatniksoftware.com/tomboy",
				new TransformExtension ());

			if ((bool) Preferences.Get (Preferences.ENABLE_CUSTOM_FONT)) {
				string font_face = (string) Preferences.Get (Preferences.CUSTOM_FONT_FACE);
				Pango.FontDescription font_desc =
				        Pango.FontDescription.FromString (font_face);
				string font = String.Format ("font-family:'{0}';", font_desc.Family);

				args.AddParam ("font", "", font);
			}

			NoteNameResolver resolver = new NoteNameResolver (note.Manager, note);
			NoteXsl.Transform (doc, args, writer, resolver);
		}
	}

	/// <summary>
	/// Makes <see cref="System.String.ToLower"/> available in the
	/// XSL stylesheet.
	/// </summary>
	public class TransformExtension
	{
		public String ToLower (string s)	
		{
			return s.ToLower ();
		}
	}
}
