﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D.Text;
using Myra.Graphics2D.TextureAtlases;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.ColorPicker;
using Myra.Graphics2D.UI.File;
using Myra.Graphics2D.UI.Properties;
using Myra.Graphics2D.UI.Styles;
using Myra.MiniJSON;
using MyraPad.UI;
using Myra.Utility;
using Myra;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Input;
using System.Xml;
using System.Text;
using System.Threading;

namespace MyraPad
{
	public class Studio : Game
	{
		private static Studio _instance;

		private readonly List<WidgetInfo> _projectInfo = new List<WidgetInfo>();

		private readonly GraphicsDeviceManager _graphicsDeviceManager;
		private readonly State _state;
		private Desktop _desktop;
		private StudioWidget _ui;
		private PropertyGrid _propertyGrid;
		private Grid _statisticsGrid;
		private TextBlock _gcMemoryLabel;
		private TextBlock _fpsLabel;
		private TextBlock _widgetsCountLabel;
		private TextBlock _drawCallsLabel;
		//		private readonly FramesPerSecondCounter _fpsCounter = new FramesPerSecondCounter();
		private string _filePath;
		private string _lastFolder;
		private bool _isDirty;
		private Project _project;
		private int? _tagStart, _tagEnd;
		private bool _needsCloseTag;
		private int _line, _col, _indentLevel;
		private bool _applyAutoIndent = false;
		private bool _applyAutoClose = false;
		private Project _newProject;
		private object _newObject;
		private DateTime? _refreshInitiated;

		public static Studio Instance
		{
			get
			{
				return _instance;
			}
		}

		public string FilePath
		{
			get { return _filePath; }

			set
			{
				if (value == _filePath)
				{
					return;
				}

				_filePath = value;

				if (!string.IsNullOrEmpty(_filePath))
				{
					// Store last folder
					try
					{
						_lastFolder = Path.GetDirectoryName(_filePath);
					}
					catch (Exception)
					{
					}
				}

				UpdateTitle();
				UpdateMenuFile();
			}
		}

		public bool IsDirty
		{
			get { return _isDirty; }

			set
			{
				if (value == _isDirty)
				{
					return;
				}

				_isDirty = value;
				UpdateTitle();
			}
		}

		public Project Project
		{
			get { return _project; }

			set
			{
				if (value == _project)
				{
					return;
				}

				_project = value;

				_ui._projectHolder.Widgets.Clear();

				if (_project != null && _project.Root != null)
				{
					_ui._projectHolder.Widgets.Add(_project.Root);
				}

				_ui._menuFileReloadStylesheet.Enabled = _project != null && !string.IsNullOrEmpty(_project.StylesheetPath);
			}
		}

		public bool ShowDebugInfo
		{
			get
			{
				return _statisticsGrid.Visible;
			}

			set
			{
				_statisticsGrid.Visible = value;
			}
		}

		public Studio()
		{
			_instance = this;

			// Restore state
			_state = State.Load();

			_graphicsDeviceManager = new GraphicsDeviceManager(this);

			if (_state != null)
			{
				_graphicsDeviceManager.PreferredBackBufferWidth = _state.Size.X;
				_graphicsDeviceManager.PreferredBackBufferHeight = _state.Size.Y;

				if (_state.UserColors != null)
				{
					for(var i = 0; i < Math.Min(ColorPickerDialog.UserColors.Length, _state.UserColors.Length); ++i)
					{
						ColorPickerDialog.UserColors[i] = new Color(_state.UserColors[i]);
					}
				}

				_lastFolder = _state.LastFolder;
			}
			else
			{
				_graphicsDeviceManager.PreferredBackBufferWidth = 1280;
				_graphicsDeviceManager.PreferredBackBufferHeight = 800;
			}
		}

		protected override void Initialize()
		{
			base.Initialize();

			IsMouseVisible = true;
			Window.AllowUserResizing = true;
		}

		protected override void LoadContent()
		{
			base.LoadContent();

			MyraEnvironment.Game = this;

			BuildUI();

			if (_state != null && !string.IsNullOrEmpty(_state.EditedFile))
			{
				Load(_state.EditedFile);
			}
		}

		public void ClosingFunction(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (_isDirty)
			{
				OnExiting();
				e.Cancel = true;
			}
		}

		public void OnExiting()
		{
			var mb = Dialog.CreateMessageBox("Quit", "There are unsaved changes. Do you want to exit without saving?");

			mb.Closed += (o, args) =>
			{
				if (mb.Result)
				{
					Exit();
				}
			};

			mb.ShowModal(_desktop);
		}

		private void BuildUI()
		{
			_desktop = new Desktop();

			_ui = new StudioWidget();

			_ui._menuFileNew.Selected += NewItemOnClicked;
			_ui._menuFileOpen.Selected += OpenItemOnClicked;
			_ui._menuFileReload.Selected += OnMenuFileReloadSelected;
			_ui._menuFileSave.Selected += SaveItemOnClicked;
			_ui._menuFileSaveAs.Selected += SaveAsItemOnClicked;
			_ui._menuFileExportToCS.Selected += ExportCsItemOnSelected;
			_ui._menuFileReloadStylesheet.Selected += OnMenuFileReloadStylesheet;
			_ui._menuFileReloadStylesheet.Enabled = false;
			_ui._menuFileDebugOptions.Selected += DebugOptionsItemOnSelected;
			_ui._menuFileQuit.Selected += QuitItemOnDown;

			_ui._menuEditFormatSource.Selected += _menuEditFormatSource_Selected;

			_ui._menuHelpAbout.Selected += AboutItemOnClicked;

			_ui._textSource.CursorPositionChanged += _textSource_CursorPositionChanged;
			_ui._textSource.TextChanged += _textSource_TextChanged;
			_ui._textSource.KeyDown += _textSource_KeyDown;
			_ui._textSource.Char += _textSource_Char;

			_propertyGrid = new PropertyGrid();
			_propertyGrid.PropertyChanged += PropertyGridOnPropertyChanged;

			_ui._propertyGridPane.Content = _propertyGrid;

			_ui._topSplitPane.SetSplitterPosition(0, _state != null ? _state.TopSplitterPosition : 0.75f);
			_ui._leftSplitPane.SetSplitterPosition(0, _state != null ? _state.LeftSplitterPosition : 0.5f);

			_desktop.Widgets.Add(_ui);

			_statisticsGrid = new Grid
			{
				Visible = false
			};

			_statisticsGrid.RowsProportions.Add(new Grid.Proportion());
			_statisticsGrid.RowsProportions.Add(new Grid.Proportion());
			_statisticsGrid.RowsProportions.Add(new Grid.Proportion());

			_gcMemoryLabel = new TextBlock
			{
				Text = "GC Memory: ",
				Font = DefaultAssets.FontSmall
			};
			_statisticsGrid.Widgets.Add(_gcMemoryLabel);

			_fpsLabel = new TextBlock
			{
				Text = "FPS: ",
				Font = DefaultAssets.FontSmall,
				GridRow = 1
			};

			_statisticsGrid.Widgets.Add(_fpsLabel);

			_widgetsCountLabel = new TextBlock
			{
				Text = "Total Widgets: ",
				Font = DefaultAssets.FontSmall,
				GridRow = 2
			};

			_statisticsGrid.Widgets.Add(_widgetsCountLabel);

			_drawCallsLabel = new TextBlock
			{
				Text = "Draw Calls: ",
				Font = DefaultAssets.FontSmall,
				GridRow = 3
			};

			_statisticsGrid.Widgets.Add(_drawCallsLabel);

			_statisticsGrid.HorizontalAlignment = HorizontalAlignment.Left;
			_statisticsGrid.VerticalAlignment = VerticalAlignment.Bottom;
			_statisticsGrid.Left = 10;
			_statisticsGrid.Top = -10;
			_desktop.Widgets.Add(_statisticsGrid);

			UpdateMenuFile();

			try
			{
				UpdatePositions();
			}
			catch (Exception)
			{
			}
		}

		private void _menuEditFormatSource_Selected(object sender, EventArgs e)
		{
			try
			{
				var doc = new XmlDocument();
				doc.LoadXml(_ui._textSource.Text);

				StringBuilder sb = new StringBuilder();
				XmlWriterSettings settings = new XmlWriterSettings
				{
					Indent = _state.Options.AutoIndent,
					IndentChars = new string(' ', _state.Options.IndentSpacesSize),
					NewLineChars = "\n",
					NewLineHandling = NewLineHandling.Replace
				};
				using (XmlWriter writer = XmlWriter.Create(sb, settings))
				{
					doc.Save(writer);
				}

				_ui._textSource.Text = sb.ToString();
			}
			catch (Exception ex)
			{
				var messageBox = Dialog.CreateMessageBox("Error", ex.Message);
				messageBox.ShowModal(_desktop);
			}
		}

		private void _textSource_Char(object sender, GenericEventArgs<char> e)
		{
			_applyAutoClose = e.Data == '>';
		}

		private void _textSource_KeyDown(object sender, GenericEventArgs<Keys> e)
		{
			_applyAutoIndent = e.Data == Keys.Enter;
		}

		private void ApplyAutoIndent()
		{
			if (!_state.Options.AutoIndent || _state.Options.IndentSpacesSize <= 0 || !_applyAutoIndent)
			{
				return;
			}

			_applyAutoIndent = false;

			var text = _ui._textSource.Text;
			var pos = _ui._textSource.CursorPosition;

			if (string.IsNullOrEmpty(text) || pos == 0 || pos >= text.Length)
			{
				return;
			}

			var il = _indentLevel;
			if (pos < text.Length - 2 && text[pos] == '<' && text[pos + 1] == '/')
			{
				--il;
			}

			if (il <= 0)
			{
				return;
			}

			// Insert indent
			var indent = new string(' ', il * _state.Options.IndentSpacesSize);
			_ui._textSource.Text = text.Substring(0, pos) + indent + text.Substring(pos);

			// Move cursor
			_ui._textSource.CursorPosition += indent.Length;
		}

		private void ApplyAutoClose()
		{
			if (!_state.Options.AutoClose || !_applyAutoClose)
			{
				return;
			}

			_applyAutoClose = false;

			var text = _ui._textSource.Text;
			var pos = _ui._textSource.CursorPosition;

			if (_tagStart == null || _tagEnd == null || !_needsCloseTag)
			{
				return;
			}

			var tagName = text.Substring(_tagStart.Value + 1, _tagEnd.Value - _tagStart.Value - 1);
			var close = "</" + tagName + ">";
			_ui._textSource.Text = text.Substring(0, pos) + close + text.Substring(pos);
		}

		private void _textSource_TextChanged(object sender, ValueChangedEventArgs<string> e)
		{
			try
			{
				IsDirty = true;

				var newLength = string.IsNullOrEmpty(e.NewValue) ? 0 : e.NewValue.Length;
				var oldLength = string.IsNullOrEmpty(e.OldValue) ? 0 : e.OldValue.Length;
				if (Math.Abs(newLength - oldLength) > 1 || _applyAutoClose)
				{
					// Refresh now
					QueueRefreshProject();
				}
				else
				{
					// Refresh after delay
					_refreshInitiated = DateTime.Now;
				}
			}
			catch (Exception)
			{
			}
		}

		private void QueueRefreshProject()
		{
			_refreshInitiated = null;
			ThreadPool.QueueUserWorkItem(RefreshProjectAsync);
		}

		private void RefreshProjectAsync(object state)
		{
			try
			{
				_ui._textStatus.Text = "Reloading...";
				_newProject = Project.LoadFromXml(_ui._textSource.Text);
				_ui._textStatus.Text = string.Empty;
			}
			catch (Exception ex)
			{
				_ui._textStatus.Text = ex.Message;
			}
		}

		private void UpdatePositions()
		{
			_line = _col = _indentLevel = 0;

			if (string.IsNullOrEmpty(_ui._textSource.Text))
			{
				return;
			}

			var cursorPos = _ui._textSource.CursorPosition;
			var text = _ui._textSource.Text;

			for (var i = 0; i < Math.Min(text.Length, cursorPos); ++i)
			{
				++_col;

				var c = text[i];
				if (c == '\n')
				{
					++_line;
					_col = 0;
				}

				if (c == '>')
				{
					if (i > 0 && (text[i - 1] == '/' || text[i - 1] == '?'))
					{
						// Tag without closing
						continue;
					}

					for (var j = i; j >= 0; j--)
					{
						if (text[j] == '<')
						{
							if (text[j + 1] != '/')
							{
								++_indentLevel;
							}
							else
							{
								--_indentLevel;
							}

							break;
						}
					}
				}
			}

			_ui._textLocation.Text = string.Format("Line: {0}, Col: {1}, Indent: {2}", _line + 1, _col + 1, _indentLevel);
		}

		private void UpdateTag()
		{
			var lastStart = _tagStart;
			var lastEnd = _tagEnd;
			_tagStart = _tagEnd = null;
			_needsCloseTag = false;
			_propertyGrid.Object = null;

			if (string.IsNullOrEmpty(_ui._textSource.Text))
			{
				return;
			}

			var cursorPos = _ui._textSource.CursorPosition;
			var text = _ui._textSource.Text;

			var start = cursorPos;
			var end = start;

			if (start > 1 && (text[start - 1] == '>' || text[start] == '>'))
			{
				start -= 2;
				end -= 2;
			}

			// Find start
			while (start >= 0)
			{
				var c = text[start];
				if (c == '>' ||
					(start == 0 && c != '<'))
				{
					return;
				}

				if (c == '<')
				{
					break;
				}

				--start;
			}

			while (end < text.Length)
			{
				var c = text[end];
				if ((end > start && c == '<') ||
					(end == text.Length - 1 && c != '>'))
				{
					return;
				}

				if (c == '>')
				{
					break;
				}

				++end;
			}

			var xml = text.Substring(start, end - start + 1);

			if (xml[1] == '/')
			{
				// Close tag
				return;
			}

			_needsCloseTag = xml[xml.Length - 2] != '/';

			if (_needsCloseTag)
			{
				var tagName = Regex.Match(xml, "<([A-Za-z0-9]+)").Groups[1].Value;

				xml += "</" + tagName + ">";
			}

			if (start != lastStart || end != lastEnd)
			{
				ThreadPool.QueueUserWorkItem(LoadObjectAsync, xml);
			}

			_tagStart = start;
			_tagEnd = end;
		}

		private void LoadObjectAsync(object state)
		{
			try
			{
				var xml = (string)state;
				_newObject = Project.LoadObjectFromXml(xml);
			}
			catch (Exception)
			{
			}
		}

		private void UpdateCursor()
		{
			try
			{
				UpdatePositions();
				UpdateTag();
				ApplyAutoIndent();
				ApplyAutoClose();
			}
			catch (Exception)
			{
			}
		}

		private void _textSource_CursorPositionChanged(object sender, EventArgs e)
		{
			UpdateCursor();
		}

		private void OnMenuFileReloadSelected(object sender, EventArgs e)
		{
			Load(FilePath);
		}

		private static string BuildPath(string folder, string fileName)
		{
			if (Path.IsPathRooted(fileName))
			{
				return fileName;
			}

			return Path.Combine(folder, fileName);
		}

		private Stylesheet StylesheetFromFile(string path)
		{
			var data = File.ReadAllText(path);
			var root = (Dictionary<string, object>)Json.Deserialize(data);

			var folder = Path.GetDirectoryName(path);

			// Load texture atlases
			var textureAtlases = new Dictionary<string, TextureRegionAtlas>();
			Dictionary<string, object> textureAtlasesNode;
			if (root.GetStyle("textureAtlases", out textureAtlasesNode))
			{
				foreach (var pair in textureAtlasesNode)
				{
					var atlasPath = BuildPath(folder, pair.Key.ToString());
					var imagePath = BuildPath(folder, pair.Value.ToString());
					using (var stream = File.OpenRead(imagePath))
					{
						var texture = Texture2D.FromStream(GraphicsDevice, stream);

						var atlasData = File.ReadAllText(atlasPath);
						textureAtlases[pair.Key] = TextureRegionAtlas.FromJson(atlasData, texture);
					}
				}
			}

			// Load fonts
			var fonts = new Dictionary<string, SpriteFont>();
			Dictionary<string, object> fontsNode;
			if (root.GetStyle("fonts", out fontsNode))
			{
				foreach (var pair in fontsNode)
				{
					var fontPath = BuildPath(folder, pair.Value.ToString());

					var fontData = File.ReadAllText(fontPath);
					fonts[pair.Key] = SpriteFontHelper.LoadFromFnt(fontData,
						s =>
						{
							if (s.Contains("#"))
							{
								var parts = s.Split('#');

								return textureAtlases[parts[0]][parts[1]];
							}

							var imagePath = BuildPath(folder, s);
							using (var stream = File.OpenRead(imagePath))
							{
								var texture = Texture2D.FromStream(GraphicsDevice, stream);

								return new TextureRegion(texture);
							}
						});
				}
			}

			return Stylesheet.CreateFromSource(data,
				s =>
				{
					TextureRegion result;
					foreach (var pair in textureAtlases)
					{
						if (pair.Value.Regions.TryGetValue(s, out result))
						{
							return result;
						}
					}

					throw new Exception(string.Format("Could not find texture region '{0}'", s));
				},
				s =>
				{
					SpriteFont result;

					if (fonts.TryGetValue(s, out result))
					{
						return result;
					}

					throw new Exception(string.Format("Could not find font '{0}'", s));
				}
			);
		}

		private static void IterateWidget(Widget w, Action<Widget> a)
		{
			a(w);

			var children = w.GetRealChildren();

			if (children != null)
			{
				foreach (var child in children)
				{
					IterateWidget(child, a);
				}
			}
		}

		private void SetStylesheet(Stylesheet stylesheet)
		{
			if (Project.Root != null)
			{
				IterateWidget(Project.Root, w => w.ApplyStylesheet(stylesheet));
			}

			Project.Stylesheet = stylesheet;

			if (stylesheet != null && stylesheet.DesktopStyle != null)
			{
				_ui._projectHolder.Background = stylesheet.DesktopStyle.Background;
			}
			else
			{
				_ui._projectHolder.Background = null;
			}
		}

		private void LoadStylesheet(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
			{
				return;
			}

			try
			{
				if (!Path.IsPathRooted(filePath))
				{
					filePath = Path.Combine(Path.GetDirectoryName(FilePath), filePath);
				}

				var stylesheet = StylesheetFromFile(filePath);
				SetStylesheet(stylesheet);
			}
			catch (Exception ex)
			{
				var dialog = Dialog.CreateMessageBox("Error", ex.ToString());
				dialog.ShowModal(_desktop);
			}
		}

		private void OnMenuFileLoadStylesheet(object sender, EventArgs e)
		{
			var dlg = new FileDialog(FileDialogMode.OpenFile)
			{
				Filter = "*.json"
			};

			try
			{
				if (!string.IsNullOrEmpty(Project.StylesheetPath))
				{
					var stylesheetPath = Project.StylesheetPath;
					if (!Path.IsPathRooted(stylesheetPath))
					{
						// Prepend folder path
						stylesheetPath = Path.Combine(Path.GetDirectoryName(FilePath), stylesheetPath);
					}

					dlg.Folder = Path.GetDirectoryName(stylesheetPath);
				}
				else if (!string.IsNullOrEmpty(FilePath))
				{
					dlg.Folder = Path.GetDirectoryName(FilePath);
				}
			}
			catch (Exception)
			{
			}

			dlg.Closed += (s, a) =>
			{
				if (!dlg.Result)
				{
					return;
				}

				var filePath = dlg.FilePath;
				LoadStylesheet(filePath);

				// Try to make stylesheet path relative to project folder
				try
				{
					var fullPathUri = new Uri(filePath, UriKind.Absolute);

					var folderPath = Path.GetDirectoryName(FilePath);
					if (!folderPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
					{
						folderPath += Path.DirectorySeparatorChar;
					}
					var folderPathUri = new Uri(folderPath, UriKind.Absolute);

					filePath = folderPathUri.MakeRelativeUri(fullPathUri).ToString();
				}
				catch (Exception)
				{
				}

				Project.StylesheetPath = filePath;

				IsDirty = true;
			};

			dlg.ShowModal(_desktop);
		}

		private void OnMenuFileReloadStylesheet(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(Project.StylesheetPath))
			{
				return;
			}

			LoadStylesheet(Project.StylesheetPath);
		}

		private void DebugOptionsItemOnSelected(object sender1, EventArgs eventArgs)
		{
			var dlg = new DebugOptionsDialog();

			dlg.AddOption("Show debug info",
						() => { ShowDebugInfo = true; },
						() => { ShowDebugInfo = false; });

			dlg.ShowModal(_desktop);
		}

		private void ExportCsItemOnSelected(object sender1, EventArgs eventArgs)
		{
			var dlg = new ExportOptionsDialog();
			dlg.ShowModal(_desktop);
		}

		private void PropertyGridOnPropertyChanged(object sender, GenericEventArgs<string> eventArgs)
		{
			IsDirty = true;

			var xml = _project.SaveObjectToXml(_propertyGrid.Object);

			if (_needsCloseTag)
			{
				xml = xml.Replace("/>", ">");
			}

			if (_tagStart != null && _tagEnd != null)
			{
				var t = _ui._textSource.Text;

				_ui._textSource.Text = t.Substring(0, _tagStart.Value) + xml + t.Substring(_tagEnd.Value + 1);
				_tagEnd = _tagStart.Value + xml.Length - 1;
			}
		}

		private void QuitItemOnDown(object sender, EventArgs eventArgs)
		{
			var mb = Dialog.CreateMessageBox("Quit", "Are you sure?");

			mb.Closed += (o, args) =>
			{
				if (mb.Result)
				{
					Exit();
				}
			};

			mb.ShowModal(_desktop);
		}

		private void AboutItemOnClicked(object sender, EventArgs eventArgs)
		{
			var messageBox = Dialog.CreateMessageBox("About", "MyraPad " + MyraEnvironment.Version);
			messageBox.ShowModal(_desktop);
		}

		private void SaveAsItemOnClicked(object sender, EventArgs eventArgs)
		{
			Save(true);
		}

		private void SaveItemOnClicked(object sender, EventArgs eventArgs)
		{
			Save(false);
		}

		private void NewItemOnClicked(object sender, EventArgs eventArgs)
		{
			var dlg = new NewProjectWizard();

			dlg.Closed += (s, a) =>
			{
				if (!dlg.Result)
				{
					return;
				}

				var rootType = "Grid";

				if (dlg._radioButtonPanel.IsPressed)
				{
					rootType = "Panel";
				}
				else
				if (dlg._radioButtonScrollPane.IsPressed)
				{
					rootType = "ScrollPane";
				}
				else
				if (dlg._radioButtonHorizontalSplitPane.IsPressed)
				{
					rootType = "HorizontalSplitPane";
				}
				else
				if (dlg._radioButtonVerticalSplitPane.IsPressed)
				{
					rootType = "VerticalSplitPane";
				}
				else
				if (dlg._radioButtonWindow.IsPressed)
				{
					rootType = "Window";
				}
				else
				if (dlg._radioButtonDialog.IsPressed)
				{
					rootType = "Dialog";
				}

				New(rootType);
			};

			dlg.ShowModal(_desktop);
		}

		private void OpenItemOnClicked(object sender, EventArgs eventArgs)
		{
			var dlg = new FileDialog(FileDialogMode.OpenFile)
			{
				Filter = "*.xml"
			};

			if (!string.IsNullOrEmpty(FilePath))
			{
				dlg.Folder = Path.GetDirectoryName(FilePath);
			}
			else if (!string.IsNullOrEmpty(_lastFolder))
			{
				dlg.Folder = _lastFolder;
			}

			dlg.Closed += (s, a) =>
			{
				if (!dlg.Result)
				{
					return;
				}

				var filePath = dlg.FilePath;
				if (string.IsNullOrEmpty(filePath))
				{
					return;
				}

				Load(filePath);
			};

			dlg.ShowModal(_desktop);
		}

		protected override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			if (_refreshInitiated != null && (DateTime.Now - _refreshInitiated.Value).TotalSeconds >= 1)
			{
				QueueRefreshProject();
			}

			if (_newObject != null)
			{
				_propertyGrid.Object = _newObject;
				_newObject = null;
			}

			if (_newProject != null)
			{
				Project = _newProject;
				_newProject = null;
			}
		}

		protected override void Draw(GameTime gameTime)
		{
			base.Draw(gameTime);

			_gcMemoryLabel.Text = string.Format("GC Memory: {0} kb", GC.GetTotalMemory(false) / 1024);
			//			_fpsLabel.Text = string.Format("FPS: {0}", _fpsCounter.FramesPerSecond);
			_widgetsCountLabel.Text = string.Format("Visible Widgets: {0}", _desktop.CalculateTotalWidgets(true));

			GraphicsDevice.Clear(Color.Black);

			_desktop.Bounds = new Rectangle(0, 0,
				GraphicsDevice.PresentationParameters.BackBufferWidth,
				GraphicsDevice.PresentationParameters.BackBufferHeight);
			_desktop.Render();

#if !FNA
			_drawCallsLabel.Text = string.Format("Draw Calls: {0}", GraphicsDevice.Metrics.DrawCount);
#else
			_drawCallsLabel.Text = "Draw Calls: ?";
#endif

			//			_fpsCounter.Draw(gameTime);
		}

		protected override void EndRun()
		{
			base.EndRun();

			var state = new State
			{
				Size = new Point(GraphicsDevice.PresentationParameters.BackBufferWidth,
					GraphicsDevice.PresentationParameters.BackBufferHeight),
				TopSplitterPosition = _ui._topSplitPane.GetSplitterPosition(0),
				LeftSplitterPosition = _ui._leftSplitPane.GetSplitterPosition(0),
				EditedFile = FilePath,
				LastFolder = _lastFolder,
				UserColors = (from c in ColorPickerDialog.UserColors select c.PackedValue).ToArray()
			};

			state.Save();
		}

		private void New(string rootType)
		{
			var source = Resources.NewProjectTemplate.Replace("$containerType", rootType);

			_ui._textSource.Text = source;

			var newLineCount = 0;
			var pos = 0;
			while (pos < _ui._textSource.Text.Length && newLineCount < 3)
			{
				++pos;

				if (_ui._textSource.Text[pos] == '\n')
				{
					++newLineCount;
				}
			}

			_ui._textSource.CursorPosition = pos;
			_desktop.FocusedWidget = _ui._textSource;


			FilePath = string.Empty;
			IsDirty = false;
			_ui._projectHolder.Background = null;
		}

		private void ProcessSave(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
			{
				return;
			}

			File.WriteAllText(filePath, _ui._textSource.Text);

			FilePath = filePath;
			IsDirty = false;
		}

		private void Save(bool setFileName)
		{
			if (string.IsNullOrEmpty(FilePath) || setFileName)
			{
				var dlg = new FileDialog(FileDialogMode.SaveFile)
				{
					Filter = "*.xml"
				};

				if (!string.IsNullOrEmpty(FilePath))
				{
					dlg.FilePath = FilePath;
				}
				else if (!string.IsNullOrEmpty(_lastFolder))
				{
					dlg.Folder = _lastFolder;
				}

				dlg.ShowModal(_desktop);

				dlg.Closed += (s, a) =>
				{
					if (dlg.Result)
					{
						ProcessSave(dlg.FilePath);
					}
				};
			}
			else
			{
				ProcessSave(FilePath);
			}
		}

		private void Load(string filePath)
		{
			try
			{
				var data = File.ReadAllText(filePath);

				FilePath = filePath;

				_ui._textSource.Text = data;
				_ui._textSource.CursorPosition = 0;
				UpdateCursor();
				_desktop.FocusedWidget = _ui._textSource;

				IsDirty = false;
			}
			catch (Exception ex)
			{
				var dialog = Dialog.CreateMessageBox("Error", ex.ToString());
				dialog.ShowModal(_desktop);
			}
		}

		private void UpdateTitle()
		{
			var title = string.IsNullOrEmpty(_filePath) ? "MyraPad" : _filePath;

			if (_isDirty)
			{
				title += " *";
			}

			Window.Title = title;
		}

		private void UpdateMenuFile()
		{
			_ui._menuFileReload.Enabled = !string.IsNullOrEmpty(FilePath);
		}
	}
}