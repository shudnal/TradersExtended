using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal sealed class ConfigEditor : IDisposable
    {
        private const int WindowId = 924617;
        private const int NewFileWindowId = 924618;
        private const int ItemPickerWindowId = 924619;
        private const int GlobalKeyWindowId = 924620;
        private const int ConfirmWindowId = 924621;
        private const float DefaultLeftPanelWidth = 382f;
        private const float MinimumLeftPanelWidth = 240f;
        private const float MaximumLeftPanelWidth = 800f;
        private const float ColumnSplitterWidth = 7f;
        private const float MinimumWidth = 1000f;
        private const float MinimumHeight = 600f;
        private const float TitleBarHeight = 22f;
        private const float ResizeEdgeHitSize = 4f;
        private const float ResizeHandleHitSize = 12f;
        private const float ResizeHandleVisualSize = 8f;
        private const float WindowBorderWidth = 1f;
        private const float LayoutSaveDelay = 0.5f;
        private const float CompactControlHeight = 20f;
        private const float CompactRowHeight = 22f;
        private const float ItemPickerControlHeight = 18f;
        private const float ItemPickerRowHeight = 20f;
        private const float ItemIconSize = 17f;
        private const float InlinePickerButtonWidth = 20f;
        private const float InlinePickerButtonHeight = 14f;
        private const float QuickDeleteButtonWidth = 18f;
        private const float ItemColumnSpacing = 3f;
        private const int ItemRowsPerPage = 100;
        private const float RequestTimeoutSeconds = 15f;

        private static readonly ItemSortColumn[] ItemColumnOrder =
        {
            ItemSortColumn.Prefab,
            ItemSortColumn.LocalizedName,
            ItemSortColumn.Stack,
            ItemSortColumn.Price,
            ItemSortColumn.Quality,
            ItemSortColumn.Currency,
            ItemSortColumn.RequiredGlobalKey,
            ItemSortColumn.BlockedGlobalKey,
            ItemSortColumn.RequiredPlayerKey,
            ItemSortColumn.BlockedPlayerKey
        };

        private readonly ConfigEditorCursor cursor = new ConfigEditorCursor();
        private readonly ConfigEditorGuiScale scale = new ConfigEditorGuiScale();
        private readonly ConfigEditorTheme theme = new ConfigEditorTheme();
        private readonly Dictionary<string, string> traderDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ItemOption> itemOptions = new List<ItemOption>();
        private readonly Dictionary<string, ItemOption> itemOptionsByPrefab = new Dictionary<string, ItemOption>(StringComparer.OrdinalIgnoreCase);

        private bool isOpen;
        private bool layoutLoaded;
        private bool resizing;
        private Vector2 resizeStartMouse;
        private Rect resizeStartRect;
        private bool resizeWidth;
        private bool resizeHeight;
        private bool layoutDirty;
        private bool splitterDragging;
        private Vector2 splitterDragStartMouse;
        private float splitterDragStartWidth;
        private float saveLayoutAfterRealtime;
        private Rect windowRect;

        private List<EditorFileInfo> files = new List<EditorFileInfo>();
        private EditorFileInfo activeFile;
        private ItemConfigDocument itemDocument;
        private TraderConfigDocument traderDocument;
        private string selectAfterList;
        private string fileSearch = string.Empty;
        private string itemSearch = string.Empty;
        private string statusText = string.Empty;
        private bool statusError;
        private bool requestInProgress;
        private float requestStartedAt;

        private Vector2 fileScroll;
        private Vector2 itemScroll;
        private int itemPage;
        private Vector2 settingsScroll;

        private bool showNewFile;
        private Rect newFileRect = new Rect(0f, 0f, 620f, 520f);
        private Vector2 newFileScroll;
        private EditorConfigKind newFileKind = EditorConfigKind.ItemList;
        private string newFileTrader = "common";
        private ItemsListType newFileListType = ItemsListType.Buy;
        private string newFileIdentifier = string.Empty;
        private string newFileExtension = "json";

        private bool showItemPicker;
        private Rect itemPickerRect = new Rect(0f, 0f, 572f, 650f);
        private string itemPickerSearch = string.Empty;
        private string itemPickerTitle = "Select item";
        private Vector2 itemPickerScroll;
        private int itemPickerPage;
        private ItemPickerSortColumn itemPickerSortColumn = ItemPickerSortColumn.Prefab;
        private bool itemPickerSortAscending = true;
        private Action<string> itemPickerCallback;
        private bool itemPickerMultiAdd;

        private bool showGlobalKeyPicker;
        private KeyPickerKind keyPickerKind = KeyPickerKind.Global;
        private string newPickerKey = string.Empty;
        private Rect globalKeyRect = new Rect(0f, 0f, 330f, 620f);
        private string globalKeySearch = string.Empty;
        private Vector2 globalKeyScroll;
        private HashSet<string> globalKeySelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Action<string> globalKeyCallback;

        private bool showConfirm;
        private Rect confirmRect = new Rect(0f, 0f, 360f, 112f);
        private string confirmMessage = string.Empty;
        private Action confirmAction;

        private GUISkin styleSourceSkin;
        private GUIStyle windowStyle;
        private GUIStyle panelStyle;
        private GUIStyle selectedFileStyle;
        private GUIStyle fileStyle;
        private GUIStyle headerStyle;
        private GUIStyle toolbarStyle;
        private GUIStyle rowStyle;
        private GUIStyle itemRowStyle;
        private GUIStyle mutedStyle;
        private GUIStyle rightMutedStyle;
        private GUIStyle errorStyle;
        private GUIStyle titleStyle;
        private GUIStyle smallButtonStyle;
        private GUIStyle centeredMutedStyle;
        private GUIStyle centeredMessageStyle;
        private GUIStyle singleLineMutedStyle;
        private GUIStyle itemPrefabTextFieldStyle;
        private GUIStyle invalidItemIconStyle;
        private GUIStyle fileNameStyle;
        private GUIStyle pickerPrefabStyle;
        private GUIStyle pickerNameStyle;
        private GUIStyle keyNameStyle;
        private GUIStyle keySelectedNameStyle;
        private GUIStyle selectedValueLabelStyle;
        private GUIStyle selectedValueStyle;
        private GUIStyle serverSourceStyle;
        private GUIStyle inlineButtonStyle;
        private GUIStyle splitterStyle;
        private bool stylesInitialized;
        private bool itemOptionsBuilt;
        private float itemPrefabColumnWidth = 150f;
        private bool itemPrefabColumnWidthDirty = true;
        private string lastGuiExceptionSignature = string.Empty;
        private bool focusPopupWindow;
        private readonly HashSet<ItemSortColumn> visibleItemColumns = new HashSet<ItemSortColumn>();
        private string visibleItemColumnsSource = string.Empty;

        private enum ItemSortColumn
        {
            Prefab,
            LocalizedName,
            Stack,
            Price,
            Quality,
            Currency,
            RequiredGlobalKey,
            BlockedGlobalKey,
            RequiredPlayerKey,
            BlockedPlayerKey
        }

        private enum ItemPickerSortColumn
        {
            Prefab,
            LocalizedName
        }

        private enum ItemRowAction
        {
            None,
            Delete,
            MoveUp,
            MoveDown
        }

        private enum KeyPickerKind
        {
            Global,
            Player
        }

        internal ConfigEditor()
        {
            ConfigEditorTransport.ResponseReceived += OnTransportResponse;
        }

        internal static bool IsOpenGlobal => configEditor != null && configEditor.IsOpen;

        internal bool IsOpen => isOpen;

        internal void MarkGameWindowReady() => scale.MarkGameWindowReady();

        internal void Toggle()
        {
            SetOpen(!isOpen);
        }

        internal void Update()
        {
            if (configEditorShortcut != null && configEditorShortcut.Value.IsDown())
            {
                Toggle();
                return;
            }

            if (isOpen && !AnyPopupOpen() && IsControlPressed() && UnityInput.Current.GetKeyDown(KeyCode.S))
            {
                if (activeFile != null && activeFile.Kind != EditorConfigKind.Unsupported && IsDirty() && !requestInProgress)
                    SaveActiveFile();
                return;
            }

            if (isOpen && UnityInput.Current.GetKeyDown(KeyCode.Escape))
            {
                if (showConfirm)
                    showConfirm = false;
                else if (showItemPicker)
                    CloseItemPicker();
                else if (showGlobalKeyPicker)
                    showGlobalKeyPicker = false;
                else if (showNewFile)
                    showNewFile = false;
                else
                    RequestClose();
            }

            if (requestInProgress && Time.realtimeSinceStartup - requestStartedAt > RequestTimeoutSeconds)
            {
                requestInProgress = false;
                selectAfterList = null;
                SetStatus("The configuration request timed out. Check the server connection and mod version.", true);
            }

            if (resizing && !UnityInput.Current.GetKey(KeyCode.Mouse0))
            {
                ClearResizeState();
                MarkLayoutDirty();
            }
            if (splitterDragging && !UnityInput.Current.GetKey(KeyCode.Mouse0))
                splitterDragging = false;

            if (layoutDirty && Time.realtimeSinceStartup >= saveLayoutAfterRealtime)
                SaveLayout();

            cursor.Update(isOpen);
        }


        private bool AnyPopupOpen() => showConfirm || showItemPicker || showGlobalKeyPicker || showNewFile;

        private static bool IsControlPressed()
        {
            return UnityInput.Current.GetKey(KeyCode.LeftControl) || UnityInput.Current.GetKey(KeyCode.RightControl);
        }
        internal void LateUpdate()
        {
            cursor.Update(isOpen);
        }

        internal void OnGUI()
        {
            if (!isOpen)
                return;

            Matrix4x4 oldMatrix = GUI.matrix;
            GUISkin oldSkin = GUI.skin;
            Color oldContentColor = GUI.contentColor;
            IsDrawing = true;
            try
            {
                GUI.matrix = scale.Matrix;
                GUI.skin = theme.Skin;
                GUI.contentColor = theme.TextColor;
                EnsureStyles();
                cursor.Update(true);
                ClampWindowToScreen();

                bool popupOpen = showConfirm || showItemPicker || showGlobalKeyPicker || showNewFile;
                bool previousEnabled = GUI.enabled;
                GUI.enabled = !popupOpen;
                Rect previousWindowRect = windowRect;
                windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Traders Extended configuration editor", windowStyle);
                GUI.enabled = previousEnabled;
                windowRect = ClampRect(windowRect);
                if (RectChanged(previousWindowRect, windowRect))
                    MarkLayoutDirty();

                // Custom modal behavior avoids Unity's global GUI.ModalWindow limitation while
                // keeping the editor window disabled and the active popup in the foreground.
                if (showConfirm)
                {
                    CenterModal(ref confirmRect);
                    confirmRect = GUI.Window(ConfirmWindowId, confirmRect, DrawConfirmWindow, "Confirm", windowStyle);
                    KeepPopupInFront(ConfirmWindowId);
                }
                else if (showItemPicker)
                {
                    CenterModal(ref itemPickerRect);
                    itemPickerRect = GUI.Window(ItemPickerWindowId, itemPickerRect, DrawItemPickerWindow, itemPickerTitle, windowStyle);
                    KeepPopupInFront(ItemPickerWindowId);
                }
                else if (showGlobalKeyPicker)
                {
                    CenterModal(ref globalKeyRect);
                    globalKeyRect = GUI.Window(GlobalKeyWindowId, globalKeyRect, DrawGlobalKeyWindow, keyPickerKind == KeyPickerKind.Global ? "Select global keys" : "Select player keys", windowStyle);
                    KeepPopupInFront(GlobalKeyWindowId);
                }
                else if (showNewFile)
                {
                    CenterModal(ref newFileRect);
                    newFileRect = GUI.Window(NewFileWindowId, newFileRect, DrawNewFileWindow, "Create configuration", windowStyle);
                    KeepPopupInFront(NewFileWindowId);
                }
                lastGuiExceptionSignature = string.Empty;
            }
            catch (Exception exception)
            {
                string signature = exception.GetType().FullName + ": " + exception.Message;
                if (!string.Equals(signature, lastGuiExceptionSignature, StringComparison.Ordinal))
                {
                    lastGuiExceptionSignature = signature;
                    logger?.LogError($"Configuration editor GUI error: {exception}");
                }
                SetStatus("Editor GUI error: " + exception.Message, true);
            }
            finally
            {
                GUI.contentColor = oldContentColor;
                GUI.skin = oldSkin;
                GUI.matrix = oldMatrix;
                IsDrawing = false;
            }
        }

        internal static bool IsDrawing { get; private set; }

        public void Dispose()
        {
            ConfigEditorTransport.ResponseReceived -= OnTransportResponse;
            cursor.Release();
            theme.Shutdown();
        }

        private void SetOpen(bool value)
        {
            if (isOpen == value)
                return;

            if (!value && IsDirty())
            {
                ShowConfirmation("Discard unsaved changes and close the editor?", () => SetOpenImmediate(false));
                return;
            }

            SetOpenImmediate(value);
        }

        private void SetOpenImmediate(bool value)
        {
            isOpen = value;
            if (value)
            {
                LoadLayout();
                traderDisplayNames.Clear();
                RefreshFiles();
                BuildItemOptions();
            }
            else
            {
                SaveLayout();
                showNewFile = false;
                showItemPicker = false;
                showGlobalKeyPicker = false;
                showConfirm = false;
                resizing = false;
                splitterDragging = false;
                itemOptions.Clear();
                itemOptionsByPrefab.Clear();
                itemOptionsBuilt = false;
                itemPrefabColumnWidthDirty = true;
                cursor.Release();
            }
        }

        private void RequestClose()
        {
            SetOpen(false);
        }

        private void DrawWindow(int windowId)
        {
            Event current = Event.current;
            Vector2 originalMousePosition = current.mousePosition;
            bool syntheticMousePosition = TryApplyRealtimeMousePosition(current);
            try
            {
                HandleResizeInput();

                GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
                DrawFileColumn();
                DrawColumnSplitter();
                DrawEditorColumn();
                GUILayout.EndHorizontal();

                if (requestInProgress || !string.IsNullOrWhiteSpace(statusText))
                {
                    GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(26f));
                    GUILayout.Label(statusText ?? string.Empty, statusError ? errorStyle : mutedStyle, GUILayout.ExpandWidth(true));
                    if (requestInProgress)
                        GUILayout.Label("Working...", mutedStyle, GUILayout.ExpandWidth(false));
                    GUILayout.EndHorizontal();
                }

                DrawWindowBorder();
                DrawResizeHandle();
                GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(0f, windowRect.width - 4f), TitleBarHeight));
            }
            finally
            {
                if (syntheticMousePosition)
                    current.mousePosition = originalMousePosition;
            }
        }


        private bool TryApplyRealtimeMousePosition(Event current)
        {
            if (current == null || current.type != EventType.Repaint && current.type != EventType.Layout)
                return false;
            Vector2 localMouse = scale.GetLogicalMousePosition() - windowRect.position;
            if (!IsFinite(localMouse.x) || !IsFinite(localMouse.y))
                return false;
            current.mousePosition = localMouse;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float GetHeaderToggleWidth(string text)
        {
            return Mathf.Ceil(GUI.skin.label.CalcSize(new GUIContent(text)).x + 22f);
        }

        private void DrawFileColumn()
        {
            GUILayout.BeginVertical(GUILayout.Width(GetLeftPanelWidth()), GUILayout.ExpandHeight(true));
            DrawFilePanel();
            GUILayout.EndVertical();
        }


        private void DrawEditorColumn()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawEditorPanel();
            GUILayout.EndVertical();
        }


        private float GetLeftPanelWidth()
        {
            float configured = configEditorFileListWidth?.Value ?? DefaultLeftPanelWidth;
            float maximum = Mathf.Min(MaximumLeftPanelWidth, Mathf.Max(MinimumLeftPanelWidth, windowRect.width * 0.65f));
            return Mathf.Clamp(configured, MinimumLeftPanelWidth, maximum);
        }

        private void DrawColumnSplitter()
        {
            Rect rect = GUILayoutUtility.GetRect(ColumnSplitterWidth, 1f, GUILayout.Width(ColumnSplitterWidth), GUILayout.ExpandHeight(true));
            GUI.Box(rect, GUIContent.none, splitterStyle);
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                splitterDragging = true;
                splitterDragStartMouse = current.mousePosition;
                splitterDragStartWidth = GetLeftPanelWidth();
                current.Use();
            }
            if (splitterDragging && current.type == EventType.MouseDrag && current.button == 0)
            {
                float maximum = Mathf.Min(MaximumLeftPanelWidth, Mathf.Max(MinimumLeftPanelWidth, windowRect.width * 0.65f));
                float width = Mathf.Clamp(splitterDragStartWidth + current.mousePosition.x - splitterDragStartMouse.x, MinimumLeftPanelWidth, maximum);
                if (configEditorFileListWidth != null)
                    configEditorFileListWidth.Value = width;
                current.Use();
            }
            if (splitterDragging && current.type == EventType.MouseUp && current.button == 0)
            {
                splitterDragging = false;
                current.Use();
            }
        }

        private void DrawFileActions()
        {
            GUI.enabled = !requestInProgress;
            if (GUILayout.Button("Refresh", smallButtonStyle, GUILayout.Width(62f), GUILayout.Height(CompactControlHeight)))
                RefreshFiles();
            if (GUILayout.Button("New", smallButtonStyle, GUILayout.Width(42f), GUILayout.Height(CompactControlHeight)))
                OpenNewFileWindow();
            GUI.enabled = activeFile != null && !requestInProgress;
            if (GUILayout.Button("Delete", smallButtonStyle, GUILayout.Width(54f), GUILayout.Height(CompactControlHeight)))
                DeleteActiveFile();
            GUI.enabled = true;
        }


        private void DrawEditorActions()
        {
            bool hasSupportedFile = activeFile != null && activeFile.Kind != EditorConfigKind.Unsupported;
            bool canApply = hasSupportedFile && IsDirty() && !requestInProgress;
            GUI.enabled = canApply;
            GUIStyle saveStyle = canApply ? theme.AccentButtonStyle : smallButtonStyle;
            if (GUILayout.Button("Save", saveStyle, GUILayout.Width(48f), GUILayout.Height(CompactControlHeight)))
                SaveActiveFile();
            if (GUILayout.Button("Cancel", smallButtonStyle, GUILayout.Width(56f), GUILayout.Height(CompactControlHeight)))
                RevertActiveFile();
            GUI.enabled = true;
        }


        private void DrawFilePanel()
        {
            GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUIStyle sourceStyle = ConfigEditorTransport.UsesRemoteServer ? serverSourceStyle : singleLineMutedStyle;
            string sourceText = ConfigEditorTransport.UsesRemoteServer ? "SERVER CONFIGURATION DIRECTORY" : "Local configuration directory";
            GUILayout.Label(sourceText, sourceStyle, GUILayout.ExpandWidth(true), GUILayout.Height(CompactControlHeight));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(50f), GUILayout.Height(CompactControlHeight));
            fileSearch = GUILayout.TextField(fileSearch ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(CompactControlHeight));
            if (GUILayout.Button("×", smallButtonStyle, GUILayout.Width(22f), GUILayout.Height(CompactControlHeight)))
                fileSearch = string.Empty;
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            fileScroll = GUILayout.BeginScrollView(fileScroll, GUILayout.ExpandHeight(true));
            List<EditorFileInfo> visibleFiles = files.Where(FileMatchesSearch).ToList();
            DrawFileGroup("Buy items", visibleFiles.Where(file => file.Kind == EditorConfigKind.ItemList && file.ListType == ItemsListType.Buy));
            DrawFileGroup("Sell items", visibleFiles.Where(file => file.Kind == EditorConfigKind.ItemList && file.ListType == ItemsListType.Sell));
            DrawFileGroup("Trader Settings", visibleFiles.Where(file => file.Kind == EditorConfigKind.TraderSettings));
            DrawFileGroup("Other files", visibleFiles.Where(file => file.Kind == EditorConfigKind.Unsupported));
            if (visibleFiles.Count == 0)
                GUILayout.Label("No matching files.", mutedStyle);
            GUILayout.EndScrollView();

            GUILayout.Space(2f);
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(24f));
            DrawFileActions();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }


        private void DrawFileGroup(string title, IEnumerable<EditorFileInfo> source)
        {
            List<EditorFileInfo> group = source
                .OrderBy(file => file.Trader, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => GetFileListDisplayName(file), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (group.Count == 0)
                return;

            GUILayout.Label(title, headerStyle, GUILayout.ExpandWidth(true), GUILayout.Height(CompactControlHeight));
            foreach (EditorFileInfo file in group)
                DrawFileEntry(file);
            GUILayout.Space(3f);
        }

        private void DrawFileEntry(EditorFileInfo file)
        {
            bool selected = activeFile != null && string.Equals(activeFile.Name, file.Name, StringComparison.OrdinalIgnoreCase);
            GUIStyle style = selected ? selectedFileStyle : fileStyle;
            string displayName = GetFileListDisplayName(file);
            float contentWidth = Mathf.Max(80f, GetLeftPanelWidth() - 28f);
            float textHeight = Mathf.Max(titleStyle.lineHeight + 2f, fileNameStyle.CalcHeight(new GUIContent(displayName), contentWidth));
            float entryHeight = Mathf.Max(30f, textHeight + 10f);
            Rect rect = GUILayoutUtility.GetRect(0f, entryHeight, GUILayout.ExpandWidth(true), GUILayout.Height(entryHeight));
            if (GUI.Button(rect, GUIContent.none, style))
                SelectFile(file);

            Rect nameRect = new Rect(rect.x + 8f, rect.y + 5f, Mathf.Max(0f, rect.width - 16f), textHeight);
            GUI.Label(nameRect, new GUIContent(displayName, file.Name), fileNameStyle);
        }

        private string GetFileListDisplayName(EditorFileInfo file)
        {
            if (file == null)
                return string.Empty;

            string trader = string.Equals(file.Trader, "common", StringComparison.OrdinalIgnoreCase)
                ? "Common"
                : file.Trader ?? string.Empty;
            string extension = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();

            if (file.Kind == EditorConfigKind.ItemList &&
                TryParseConfigFileName(file.Name, out _, out _, out string identifier))
            {
                return string.IsNullOrWhiteSpace(identifier)
                    ? $"{trader} · {extension}"
                    : $"{trader} · {identifier} · {extension}";
            }

            if (file.Kind == EditorConfigKind.TraderSettings)
                return $"{trader} · {extension}";

            return file.Name;
        }

        private void DrawEditorPanel()
        {
            GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawEditorHeader();

            if (activeFile == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Select a configuration file or create a new one.", titleStyle, GUILayout.ExpandWidth(true));
                GUILayout.FlexibleSpace();
            }
            else if (activeFile.Kind == EditorConfigKind.Unsupported)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("The file name does not match a supported Traders Extended configuration pattern.", errorStyle);
                GUILayout.FlexibleSpace();
            }
            else if (itemDocument != null)
            {
                DrawItemEditor();
            }
            else if (traderDocument != null)
            {
                DrawTraderEditor();
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(requestInProgress ? "Loading configuration..." : "Configuration is not loaded.", mutedStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndVertical();
        }

        private void DrawEditorHeader()
        {
            float headerControlHeight = Mathf.Max(CompactControlHeight, titleStyle.lineHeight + 3f);
            float headerHeight = Mathf.Max(26f, headerControlHeight + 6f);
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(headerHeight));

            string editorTitle = activeFile == null
                ? "No file selected"
                : itemDocument != null
                    ? $"{GetTraderDisplayName(activeFile.Trader)} — {activeFile.ListType}"
                    : traderDocument != null
                        ? $"{GetTraderDisplayName(traderDocument.Trader)} ({traderDocument.Trader})"
                        : "Configuration";
            float editorTitleWidth = Mathf.Ceil(titleStyle.CalcSize(new GUIContent(editorTitle)).x * 1.2f + 4f);
            GUILayout.Label(editorTitle, titleStyle, GUILayout.Width(editorTitleWidth), GUILayout.Height(headerControlHeight));
            GUILayout.Space(8f);

            DrawEditorActions();
            GUILayout.Space(8f);

            string activeTitle = activeFile == null ? string.Empty : activeFile.Name;
            GUILayout.Label(activeTitle, pickerNameStyle, GUILayout.ExpandWidth(false), GUILayout.Height(headerControlHeight));
            GUILayout.FlexibleSpace();

            bool blockInput = ConfigEditorGui.ToggleLayout(
                theme,
                configEditorBlockGameInput?.Value == true,
                new GUIContent("Prevent input", "Block all Valheim gameplay input while the configuration editor is open."),
                GetHeaderToggleWidth("Prevent input"));
            if (configEditorBlockGameInput != null && blockInput != configEditorBlockGameInput.Value)
                configEditorBlockGameInput.Value = blockInput;

            if (GUILayout.Button("Close", smallButtonStyle, GUILayout.Width(52f), GUILayout.Height(CompactControlHeight)))
                RequestClose();
            GUILayout.EndHorizontal();
        }

        private void DrawItemEditor()
        {
            EnsureItemOptions();
            EnsureVisibleItemColumns();
            List<EditorItemRow> filteredRows = itemDocument.Rows.Where(ItemMatchesSearch).ToList();
            int pageCount = Math.Max(1, (filteredRows.Count + ItemRowsPerPage - 1) / ItemRowsPerPage);
            itemPage = Mathf.Clamp(itemPage, 0, pageCount - 1);
            List<EditorItemRow> pageRows = filteredRows.Skip(itemPage * ItemRowsPerPage).Take(ItemRowsPerPage).ToList();

            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(28f));
            if (GUILayout.Button("Add item...", smallButtonStyle, GUILayout.Width(88f), GUILayout.Height(CompactControlHeight)))
                OpenItemPicker("Add item", prefab => AddItemRow(prefab), false);
            GUILayout.Space(2f);
            if (GUILayout.Button("Add items...", smallButtonStyle, GUILayout.Width(92f), GUILayout.Height(CompactControlHeight)))
                OpenItemPicker("Add items", prefab => AddItemRow(prefab), true);
            GUILayout.Space(2f);
            if (GUILayout.Button("Add blank", smallButtonStyle, GUILayout.Width(76f), GUILayout.Height(CompactControlHeight)))
            {
                itemDocument.Rows.Add(new EditorItemRow(new TradeableItem()));
                itemDocument.Dirty = true;
            }
            GUILayout.Space(2f);
            if (GUILayout.Button("Select filtered", smallButtonStyle, GUILayout.Width(100f), GUILayout.Height(CompactControlHeight)))
            {
                foreach (EditorItemRow row in filteredRows)
                    row.Selected = true;
            }
            GUILayout.Space(2f);
            if (GUILayout.Button("Clear selection", smallButtonStyle, GUILayout.Width(100f), GUILayout.Height(CompactControlHeight)))
            {
                foreach (EditorItemRow row in itemDocument.Rows)
                    row.Selected = false;
            }

            GUILayout.Space(2f);
            int selectedCount = itemDocument.Rows.Count(row => row.Selected);
            GUI.enabled = selectedCount > 0;
            if (GUILayout.Button($"Delete selected ({selectedCount})", smallButtonStyle, GUILayout.Width(140f), GUILayout.Height(CompactControlHeight)))
            {
                itemDocument.Rows.RemoveAll(row => row.Selected);
                itemDocument.Dirty = true;
            }
            GUI.enabled = true;

            GUILayout.Space(10f);
            GUILayout.Label("Search", GUILayout.Width(50f), GUILayout.Height(CompactControlHeight));
            string changedSearch = GUILayout.TextField(itemSearch ?? string.Empty, GUILayout.Width(150f), GUILayout.Height(CompactControlHeight));
            if (!string.Equals(changedSearch, itemSearch, StringComparison.Ordinal))
            {
                itemSearch = changedSearch;
                itemPage = 0;
            }
            if (GUILayout.Button("×", smallButtonStyle, GUILayout.Width(20f), GUILayout.Height(CompactControlHeight)))
            {
                itemSearch = string.Empty;
                itemPage = 0;
            }

            GUILayout.FlexibleSpace();
            GUI.enabled = itemPage > 0;
            if (GUILayout.Button("◀", smallButtonStyle, GUILayout.Width(28f), GUILayout.Height(CompactControlHeight)))
            {
                itemPage--;
                itemScroll = Vector2.zero;
            }
            GUI.enabled = true;
            GUILayout.Label($"Page {itemPage + 1}/{pageCount}", centeredMutedStyle, GUILayout.Width(75f), GUILayout.Height(CompactControlHeight));
            GUI.enabled = itemPage + 1 < pageCount;
            if (GUILayout.Button("▶", smallButtonStyle, GUILayout.Width(28f), GUILayout.Height(CompactControlHeight)))
            {
                itemPage++;
                itemScroll = Vector2.zero;
            }
            GUI.enabled = true;
            GUILayout.Label($"Rows: {itemDocument.Rows.Count}; filtered: {filteredRows.Count}", centeredMutedStyle, GUILayout.Width(180f), GUILayout.Height(CompactControlHeight));
            GUILayout.EndHorizontal();

            itemScroll = GUILayout.BeginScrollView(itemScroll, true, true, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawItemHeader();
            EditorItemRow pendingRow = null;
            ItemRowAction pendingAction = ItemRowAction.None;
            foreach (EditorItemRow row in pageRows)
            {
                int rowIndex = itemDocument.Rows.IndexOf(row);
                ItemRowAction action = DrawItemRow(row, rowIndex);
                if (action != ItemRowAction.None)
                {
                    pendingRow = row;
                    pendingAction = action;
                }
            }
            GUILayout.EndScrollView();

            if (pendingRow != null)
            {
                int rowIndex = itemDocument.Rows.IndexOf(pendingRow);
                if (pendingAction == ItemRowAction.Delete)
                    itemDocument.Rows.Remove(pendingRow);
                else if (pendingAction == ItemRowAction.MoveUp && rowIndex > 0)
                    MoveItemRow(rowIndex, rowIndex - 1);
                else if (pendingAction == ItemRowAction.MoveDown && rowIndex >= 0 && rowIndex + 1 < itemDocument.Rows.Count)
                    MoveItemRow(rowIndex, rowIndex + 1);

                itemDocument.Dirty = true;
            }

            DrawItemSortControls();
            DrawItemColumnVisibilityControls();
        }

        private void AddItemRow(string prefab)
        {
            itemDocument.Rows.Add(new EditorItemRow(new TradeableItem { prefab = prefab }));
            itemDocument.Dirty = true;
        }

        private void DrawItemHeader()
        {
            float prefabWidth = GetItemPrefabColumnWidth();
            GUILayout.BeginHorizontal(headerStyle, GUILayout.Height(CompactRowHeight));
            Header("", 22f); ItemColumnGap();
            Header("", ItemIconSize); ItemColumnGap();
            if (IsItemColumnVisible(ItemSortColumn.Prefab))
            {
                Header("Prefab", prefabWidth); ItemColumnGap();
            }
            Header("", QuickDeleteButtonWidth); ItemColumnGap();
            Header("", QuickDeleteButtonWidth); ItemColumnGap();
            Header("", QuickDeleteButtonWidth); ItemColumnGap();
            if (IsItemColumnVisible(ItemSortColumn.LocalizedName))
            {
                Header("Localized name", 170f); ItemColumnGap();
            }
            Header("", InlinePickerButtonWidth); ItemColumnGap();
            if (IsItemColumnVisible(ItemSortColumn.Stack))
            {
                Header("Stack", 55f); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.Price))
            {
                Header("Price", 60f); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.Quality))
            {
                Header("Quality", 55f); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.Currency))
            {
                Header("Currency", 140f); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.RequiredGlobalKey))
            {
                Header("Required global key", 155f); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.BlockedGlobalKey))
            {
                Header("Blocked global key", 155f); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.RequiredPlayerKey))
            {
                Header("Required player key", 150f); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.BlockedPlayerKey))
                Header("Blocked player key", 150f);
            GUILayout.EndHorizontal();
        }

        private ItemRowAction DrawItemRow(EditorItemRow row, int rowIndex)
        {
            ItemRowValidation validation = BuildItemRowValidation(row);
            row.ValidationError = validation.Combined;
            float prefabWidth = GetItemPrefabColumnWidth();
            ItemRowAction action = ItemRowAction.None;

            GUILayout.BeginHorizontal(itemRowStyle, GUILayout.Height(CompactRowHeight));
            bool selected = ConfigEditorGui.ToggleLayout(theme, row.Selected, GUIContent.none, 22f, mutedStyle, 0f);
            if (selected != row.Selected)
                row.Selected = selected;
            ItemColumnGap();

            if (!string.IsNullOrEmpty(validation.Prefab))
                GUILayout.Label(new GUIContent("!", validation.Prefab), invalidItemIconStyle, GUILayout.Width(ItemIconSize), GUILayout.Height(ItemIconSize));
            else
                DrawItemIcon(row.Item.prefab, ItemIconSize);
            ItemColumnGap();

            if (IsItemColumnVisible(ItemSortColumn.Prefab))
            {
                string prefab = DrawValidatedTextField(
                    row.Item.prefab ?? string.Empty,
                    itemPrefabTextFieldStyle,
                    string.Empty,
                    prefabWidth,
                    CompactControlHeight);
                if (!string.Equals(prefab, row.Item.prefab, StringComparison.Ordinal))
                {
                    row.Item.prefab = prefab;
                    itemDocument.Dirty = true;
                }
                ItemColumnGap();
            }

            if (DrawQuickActionButton("×", "Delete row"))
                action = ItemRowAction.Delete;
            ItemColumnGap();

            GUI.enabled = rowIndex > 0;
            if (DrawQuickActionButton("↑", "Move row up"))
                action = ItemRowAction.MoveUp;
            GUI.enabled = true;
            ItemColumnGap();

            GUI.enabled = rowIndex >= 0 && rowIndex + 1 < itemDocument.Rows.Count;
            if (DrawQuickActionButton("↓", "Move row down"))
                action = ItemRowAction.MoveDown;
            GUI.enabled = true;
            ItemColumnGap();

            if (IsItemColumnVisible(ItemSortColumn.LocalizedName))
            {
                string localizedName = GetItemDisplayName(row.Item.prefab);
                GUILayout.Label(new GUIContent(localizedName, localizedName), pickerNameStyle,
                    GUILayout.Width(170f), GUILayout.Height(CompactControlHeight));
                ItemColumnGap();
            }

            if (DrawInlinePickerButton())
            {
                OpenItemPicker("Select item prefab", value =>
                {
                    row.Item.prefab = value;
                    itemDocument.Dirty = true;
                }, false);
            }
            ItemColumnGap();

            if (IsItemColumnVisible(ItemSortColumn.Stack))
            {
                DrawIntegerField(ref row.StackText, value => row.Item.stack = value, 55f, validation.Stack); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.Price))
            {
                DrawIntegerField(ref row.PriceText, value => row.Item.price = value, 60f, validation.Price); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.Quality))
            {
                DrawIntegerField(ref row.QualityText, value => row.Item.quality = value, 55f, validation.Quality); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.Currency))
            {
                DrawPrefabField(row.Item.currency, value => row.Item.currency = value, 140f, "Select currency item", validation.Currency); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.RequiredGlobalKey))
            {
                DrawKeyField(row.Item.requiredGlobalKey, value => row.Item.requiredGlobalKey = value, 155f, KeyPickerKind.Global, validation.RequiredGlobalKey); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.BlockedGlobalKey))
            {
                DrawKeyField(row.Item.notRequiredGlobalKey, value => row.Item.notRequiredGlobalKey = value, 155f, KeyPickerKind.Global, validation.BlockedGlobalKey); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.RequiredPlayerKey))
            {
                DrawKeyField(row.Item.requiredPlayerKey, value => row.Item.requiredPlayerKey = value, 150f, KeyPickerKind.Player, validation.RequiredPlayerKey); ItemColumnGap();
            }
            if (IsItemColumnVisible(ItemSortColumn.BlockedPlayerKey))
                DrawKeyField(row.Item.notRequiredPlayerKey, value => row.Item.notRequiredPlayerKey = value, 150f, KeyPickerKind.Player, validation.BlockedPlayerKey);
            GUILayout.EndHorizontal();

            if (validation.HasErrors)
            {
                Rect rowRect = GUILayoutUtility.GetLastRect();
                DrawOutline(rowRect, new Color(0.9f, 0.2f, 0.18f, 1f), 1f);
                GUI.Label(rowRect, new GUIContent(string.Empty, validation.Combined), GUIStyle.none);
            }

            return action;
        }

        private void MoveItemRow(int sourceIndex, int destinationIndex)
        {
            if (sourceIndex < 0 || destinationIndex < 0 || sourceIndex >= itemDocument.Rows.Count || destinationIndex >= itemDocument.Rows.Count || sourceIndex == destinationIndex)
                return;

            EditorItemRow row = itemDocument.Rows[sourceIndex];
            itemDocument.Rows.RemoveAt(sourceIndex);
            itemDocument.Rows.Insert(destinationIndex, row);
        }

        private void DrawItemSortControls()
        {
            int selectedCount = itemDocument.Rows.Count(row => row.Selected);
            bool sortSelected = selectedCount > 1;
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(26f));
            GUILayout.Label(sortSelected ? "Sort selected by:" : "Sort by:", GUILayout.Width(sortSelected ? 102f : 48f), GUILayout.Height(CompactControlHeight));
            DrawItemSortCommand("Prefab", ItemSortColumn.Prefab, 48f, sortSelected);
            DrawItemSortCommand("Name", ItemSortColumn.LocalizedName, 40f, sortSelected);
            DrawItemSortCommand("Stack", ItemSortColumn.Stack, 40f, sortSelected);
            DrawItemSortCommand("Price", ItemSortColumn.Price, 36f, sortSelected);
            DrawItemSortCommand("Quality", ItemSortColumn.Quality, 46f, sortSelected);
            DrawItemSortCommand("Currency", ItemSortColumn.Currency, 58f, sortSelected);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawItemSortCommand(string label, ItemSortColumn column, float labelWidth, bool selectedOnly)
        {
            GUILayout.Label(label, GUILayout.Width(labelWidth), GUILayout.Height(CompactControlHeight));
            if (GUILayout.Button("↑", smallButtonStyle, GUILayout.Width(22f), GUILayout.Height(CompactControlHeight)))
                SortItemRows(column, true, selectedOnly);
            if (GUILayout.Button("↓", smallButtonStyle, GUILayout.Width(22f), GUILayout.Height(CompactControlHeight)))
                SortItemRows(column, false, selectedOnly);
            GUILayout.Space(5f);
        }

        private void DrawItemColumnVisibilityControls()
        {
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(29f));
            GUILayout.Label("Columns:", GUILayout.Width(58f), GUILayout.Height(CompactControlHeight));
            DrawItemColumnToggle(ItemSortColumn.Prefab, "Prefab", 72f);
            DrawItemColumnToggle(ItemSortColumn.LocalizedName, "Name", 62f);
            DrawItemColumnToggle(ItemSortColumn.Stack, "Stack", 62f);
            DrawItemColumnToggle(ItemSortColumn.Price, "Price", 58f);
            DrawItemColumnToggle(ItemSortColumn.Quality, "Quality", 70f);
            DrawItemColumnToggle(ItemSortColumn.Currency, "Currency", 82f);
            DrawItemColumnToggle(ItemSortColumn.RequiredGlobalKey, "Req global", 96f);
            DrawItemColumnToggle(ItemSortColumn.BlockedGlobalKey, "Block global", 104f);
            DrawItemColumnToggle(ItemSortColumn.RequiredPlayerKey, "Req player", 96f);
            DrawItemColumnToggle(ItemSortColumn.BlockedPlayerKey, "Block player", 104f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawItemColumnToggle(ItemSortColumn column, string label, float width)
        {
            bool visible = IsItemColumnVisible(column);
            bool changed = ConfigEditorGui.ToggleLayout(theme, visible, new GUIContent(label), width, singleLineMutedStyle, 0f, 22f);
            if (changed != visible)
                SetItemColumnVisible(column, changed);
        }

        private void DrawTraderEditor()
        {
            GUILayout.Label("Enable Override for a field to store a trader-specific value. Disabled fields inherit the synchronized BepInEx configuration.", mutedStyle);
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            foreach (IGrouping<string, TraderSettingState> section in traderDocument.Settings.GroupBy(state => state.Definition.Section))
            {
                GUILayout.Label($"— {section.Key} —", headerStyle, GUILayout.Height(CompactControlHeight));
                foreach (TraderSettingState state in section)
                    DrawTraderSetting(state);
            }
            GUILayout.EndScrollView();
        }

        private void DrawTraderSetting(TraderSettingState state)
        {
            GUILayout.BeginVertical(rowStyle);
            GUILayout.BeginHorizontal();
            bool hasOverride = ConfigEditorGui.ToggleLayout(theme, state.HasOverride, GUIContent.none, 22f, mutedStyle, 0f);
            if (hasOverride != state.HasOverride)
            {
                state.HasOverride = hasOverride;
                if (hasOverride)
                {
                    state.Value = state.Definition.Fallback(traderDocument.Trader);
                    state.EditText = TraderConfigSchema.FormatEditText(state.Definition.Type, state.Value);
                    state.ValidationError = string.Empty;
                    if (state.Definition.Type == TraderSettingType.Vector2)
                    {
                        Vector2 vector = state.Value is Vector2 parsed ? parsed : Vector2.zero;
                        state.VectorXText = vector.x.ToString("0.###", CultureInfo.InvariantCulture);
                        state.VectorYText = vector.y.ToString("0.###", CultureInfo.InvariantCulture);
                    }
                }
                traderDocument.Dirty = true;
            }

            GUILayout.Label(new GUIContent(state.Definition.Name, state.Definition.Description), GUILayout.Width(360f));
            GUI.enabled = state.HasOverride;
            DrawTraderValue(state);
            GUI.enabled = true;

            if (!state.HasOverride)
                GUILayout.Label("Inherited: " + FormatValue(state.Definition.Type, state.Definition.Fallback(traderDocument.Trader)), mutedStyle, GUILayout.Width(240f));
            GUILayout.EndHorizontal();
            if (state.HasOverride && !string.IsNullOrWhiteSpace(state.ValidationError))
                GUILayout.Label(state.ValidationError, errorStyle);
            GUILayout.EndVertical();
        }

        private void DrawTraderValue(TraderSettingState state)
        {
            switch (state.Definition.Type)
            {
                case TraderSettingType.Boolean:
                {
                    bool current = state.Value is bool boolean && boolean;
                    bool changed = ConfigEditorGui.ToggleLayout(theme, current, new GUIContent(current ? "Enabled" : "Disabled"), 150f);
                    if (changed != current)
                    {
                        state.Value = changed;
                        state.ValidationError = string.Empty;
                        traderDocument.Dirty = true;
                    }
                    break;
                }
                case TraderSettingType.Integer:
                {
                    string text = GUILayout.TextField(state.EditText ?? string.Empty, GUILayout.Width(150f));
                    if (!string.Equals(text, state.EditText, StringComparison.Ordinal))
                    {
                        state.EditText = text;
                        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        {
                            state.Value = parsed;
                            state.ValidationError = string.Empty;
                            traderDocument.Dirty = true;
                        }
                        else
                        {
                            state.ValidationError = "Enter a valid integer.";
                        }
                    }
                    break;
                }
                case TraderSettingType.Float:
                {
                    string text = GUILayout.TextField(state.EditText ?? string.Empty, GUILayout.Width(150f));
                    if (!string.Equals(text, state.EditText, StringComparison.Ordinal))
                    {
                        state.EditText = text;
                        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                        {
                            state.Value = parsed;
                            state.ValidationError = string.Empty;
                            traderDocument.Dirty = true;
                        }
                        else
                        {
                            state.ValidationError = "Enter a valid number using a dot as the decimal separator.";
                        }
                    }
                    break;
                }
                case TraderSettingType.String:
                {
                    string current = state.Value?.ToString() ?? string.Empty;
                    string changed = GUILayout.TextField(current, GUILayout.MinWidth(260f), GUILayout.ExpandWidth(true));
                    if (!string.Equals(current, changed, StringComparison.Ordinal))
                    {
                        state.Value = changed;
                        state.ValidationError = string.Empty;
                        traderDocument.Dirty = true;
                    }
                    break;
                }
                case TraderSettingType.ItemPrefab:
                {
                    string current = state.Value?.ToString() ?? string.Empty;
                    string changed = GUILayout.TextField(current, GUILayout.Width(125f), GUILayout.Height(CompactControlHeight));
                    if (!string.Equals(current, changed, StringComparison.Ordinal))
                    {
                        state.Value = changed;
                        state.ValidationError = ValidateItemPrefab(changed);
                        traderDocument.Dirty = true;
                    }
                    else
                    {
                        state.ValidationError = state.HasOverride ? ValidateItemPrefab(current) : string.Empty;
                    }
                    if (DrawInlinePickerButton())
                    {
                        OpenItemPicker("Select item prefab", prefab =>
                        {
                            state.Value = prefab;
                            state.EditText = prefab;
                            state.ValidationError = string.Empty;
                            traderDocument.Dirty = true;
                        }, false);
                    }
                    GUILayout.Label(GetItemDisplayName(state.Value?.ToString()), pickerNameStyle, GUILayout.Width(150f), GUILayout.Height(CompactControlHeight));
                    break;
                }
                case TraderSettingType.Vector2:
                {
                    GUILayout.Label("x", GUILayout.Width(12f));
                    string x = GUILayout.TextField(state.VectorXText ?? string.Empty, GUILayout.Width(85f));
                    GUILayout.Label("y", GUILayout.Width(12f));
                    string y = GUILayout.TextField(state.VectorYText ?? string.Empty, GUILayout.Width(85f));
                    if (!string.Equals(x, state.VectorXText, StringComparison.Ordinal) ||
                        !string.Equals(y, state.VectorYText, StringComparison.Ordinal))
                    {
                        state.VectorXText = x;
                        state.VectorYText = y;
                        if (float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedX) &&
                            float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedY))
                        {
                            state.Value = new Vector2(parsedX, parsedY);
                            state.ValidationError = string.Empty;
                            traderDocument.Dirty = true;
                        }
                        else
                        {
                            state.ValidationError = "Both x and y must be valid numbers using a dot as the decimal separator.";
                        }
                    }
                    break;
                }
            }
        }

        private void DrawNewFileWindow(int windowId)
        {
            GUILayout.BeginVertical();
            newFileScroll = GUILayout.BeginScrollView(newFileScroll, GUILayout.ExpandHeight(true));

            GUILayout.Label("Configuration type", headerStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Item list", newFileKind == EditorConfigKind.ItemList ? theme.AccentButtonStyle : smallButtonStyle, GUILayout.Height(28f)))
                newFileKind = EditorConfigKind.ItemList;
            if (GUILayout.Button("Trader settings", newFileKind == EditorConfigKind.TraderSettings ? theme.AccentButtonStyle : smallButtonStyle, GUILayout.Height(28f)))
            {
                newFileKind = EditorConfigKind.TraderSettings;
                if (string.Equals(newFileTrader, "common", StringComparison.OrdinalIgnoreCase))
                    newFileTrader = "Haldor";
                if (newFileExtension == "csv")
                    newFileExtension = "json";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Trader prefab", headerStyle);
            GUILayout.BeginHorizontal();
            newFileTrader = GUILayout.TextField(newFileTrader ?? string.Empty, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Select...", smallButtonStyle, GUILayout.Width(90f)))
                OpenTraderPicker();
            GUILayout.EndHorizontal();
            GUILayout.Label(GetTraderDisplayName(newFileTrader), mutedStyle);

            if (newFileKind == EditorConfigKind.ItemList)
            {
                GUILayout.Space(8f);
                GUILayout.Label("List type", headerStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Buy", newFileListType == ItemsListType.Buy ? theme.AccentButtonStyle : smallButtonStyle, GUILayout.Height(28f)))
                    newFileListType = ItemsListType.Buy;
                if (GUILayout.Button("Sell", newFileListType == ItemsListType.Sell ? theme.AccentButtonStyle : smallButtonStyle, GUILayout.Height(28f)))
                    newFileListType = ItemsListType.Sell;
                GUILayout.EndHorizontal();

                GUILayout.Label("Optional dot-separated identifier", headerStyle);
                newFileIdentifier = GUILayout.TextField(newFileIdentifier ?? string.Empty);
            }

            GUILayout.Space(8f);
            GUILayout.Label("File format", headerStyle);
            GUILayout.BeginHorizontal();
            DrawFormatToggle("json");
            DrawFormatToggle("yaml");
            DrawFormatToggle("yml");
            if (newFileKind == EditorConfigKind.ItemList)
                DrawFormatToggle("csv");
            GUILayout.EndHorizontal();

            string fileName = BuildNewFileName();
            GUILayout.Space(8f);
            GUILayout.Label("File name", headerStyle);
            GUILayout.Label(fileName, panelStyle);
            string error = ValidateNewFile(fileName);
            if (!string.IsNullOrEmpty(error))
                GUILayout.Label(error, errorStyle);

            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(34f));
            GUI.enabled = string.IsNullOrEmpty(error) && !requestInProgress;
            if (GUILayout.Button("Create", smallButtonStyle, GUILayout.Height(28f)))
                CreateNewFile(fileName);
            GUI.enabled = true;
            if (GUILayout.Button("Cancel", smallButtonStyle, GUILayout.Height(28f)))
                showNewFile = false;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            DrawModalBorder(newFileRect.width, newFileRect.height);
            GUI.DragWindow(new Rect(0f, 0f, newFileRect.width, TitleBarHeight));
        }

        private void DrawItemPickerWindow(int windowId)
        {
            bool traderPickerMode = temporaryTraderOptions != null;
            bool closeRequested = false;
            string selectedPrefab = null;
            string selectedTrader = null;

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal(GUILayout.Height(ItemPickerControlHeight));
            GUILayout.Label("Search", GUILayout.Width(49f), GUILayout.Height(ItemPickerControlHeight));
            string changedSearch = GUILayout.TextField(itemPickerSearch ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(ItemPickerControlHeight));
            if (!string.Equals(changedSearch, itemPickerSearch, StringComparison.Ordinal))
            {
                itemPickerSearch = changedSearch;
                itemPickerPage = 0;
                itemPickerScroll = Vector2.zero;
            }
            if (GUILayout.Button("×", smallButtonStyle, GUILayout.Width(20f), GUILayout.Height(ItemPickerControlHeight)))
            {
                itemPickerSearch = string.Empty;
                itemPickerPage = 0;
                itemPickerScroll = Vector2.zero;
            }

            if (!traderPickerMode)
            {
                GUILayout.Space(3f);
                GUILayout.Label("Sort", GUILayout.Width(29f), GUILayout.Height(ItemPickerControlHeight));
                DrawItemPickerSortButton("Prefab", ItemPickerSortColumn.Prefab, 70f);
                DrawItemPickerSortButton("Name", ItemPickerSortColumn.LocalizedName, 64f);
                GUILayout.Space(3f);
                bool showAll = ConfigEditorGui.ToggleLayout(theme, configEditorShowAllItems?.Value == true,
                    new GUIContent("Show all", "Include AI equipment and items without a normal user-facing name, description or icon."), 70f,
                    singleLineMutedStyle, 0f);
                if (configEditorShowAllItems != null && showAll != configEditorShowAllItems.Value)
                {
                    configEditorShowAllItems.Value = showAll;
                    itemPickerPage = 0;
                    itemPickerScroll = Vector2.zero;
                }
            }
            GUILayout.Space(3f);
            if (GUILayout.Button("Close", smallButtonStyle, GUILayout.Width(52f), GUILayout.Height(ItemPickerControlHeight)))
                closeRequested = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            int optionCount;
            int pageCount;
            itemPickerScroll = GUILayout.BeginScrollView(itemPickerScroll, GUILayout.ExpandHeight(true));
            if (traderPickerMode)
            {
                List<TraderOption> filtered = temporaryTraderOptions.Where(TraderOptionMatchesSearch).ToList();
                optionCount = filtered.Count;
                pageCount = Math.Max(1, (optionCount + ItemRowsPerPage - 1) / ItemRowsPerPage);
                itemPickerPage = Mathf.Clamp(itemPickerPage, 0, pageCount - 1);
                foreach (TraderOption option in filtered.Skip(itemPickerPage * ItemRowsPerPage).Take(ItemRowsPerPage))
                {
                    GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(ItemPickerRowHeight));
                    GUILayout.Label(option.DisplayName, pickerNameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(ItemPickerRowHeight));
                    GUILayout.Space(ItemColumnSpacing);
                    GUILayout.Label(option.Prefab, pickerPrefabStyle, GUILayout.Width(214f), GUILayout.Height(ItemPickerRowHeight));
                    GUILayout.Space(ItemColumnSpacing);
                    if (GUILayout.Button("Select", smallButtonStyle, GUILayout.Width(56f), GUILayout.Height(ItemIconSize)))
                        selectedTrader = option.Prefab;
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                bool showAll = configEditorShowAllItems?.Value == true;
                List<ItemOption> filtered = SortItemPickerOptions(itemOptions.Where(option => (showAll || option.VisibleByDefault) && ItemOptionMatchesSearch(option)));
                optionCount = filtered.Count;
                pageCount = Math.Max(1, (optionCount + ItemRowsPerPage - 1) / ItemRowsPerPage);
                itemPickerPage = Mathf.Clamp(itemPickerPage, 0, pageCount - 1);
                foreach (ItemOption option in filtered.Skip(itemPickerPage * ItemRowsPerPage).Take(ItemRowsPerPage))
                {
                    GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(ItemPickerRowHeight));
                    DrawSprite(option.Icon, ItemIconSize);
                    GUILayout.Space(ItemColumnSpacing);
                    GUILayout.Label(option.Prefab, pickerPrefabStyle, GUILayout.Width(214f), GUILayout.Height(ItemPickerRowHeight));
                    GUILayout.Space(ItemColumnSpacing);
                    GUILayout.Label(option.LocalizedName, pickerNameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(ItemPickerRowHeight));
                    GUILayout.Space(ItemColumnSpacing);
                    string actionText = itemPickerMultiAdd ? "Add item" : "Select";
                    float actionWidth = itemPickerMultiAdd ? 65f : 59f;
                    if (GUILayout.Button(actionText, smallButtonStyle, GUILayout.Width(actionWidth), GUILayout.Height(ItemIconSize)))
                    {
                        if (itemPickerMultiAdd)
                            itemPickerCallback?.Invoke(option.Prefab);
                        else
                            selectedPrefab = option.Prefab;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal(GUILayout.Height(ItemPickerControlHeight));
            GUI.enabled = itemPickerPage > 0;
            if (GUILayout.Button("Previous", smallButtonStyle, GUILayout.Width(72f), GUILayout.Height(ItemPickerControlHeight)))
            {
                itemPickerPage--;
                itemPickerScroll = Vector2.zero;
            }
            GUI.enabled = true;
            GUILayout.Label($"Page {itemPickerPage + 1}/{pageCount} — {optionCount} result(s)", centeredMutedStyle, GUILayout.ExpandWidth(true), GUILayout.Height(ItemPickerControlHeight));
            GUI.enabled = itemPickerPage + 1 < pageCount;
            if (GUILayout.Button("Next", smallButtonStyle, GUILayout.Width(60f), GUILayout.Height(ItemPickerControlHeight)))
            {
                itemPickerPage++;
                itemPickerScroll = Vector2.zero;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Close", smallButtonStyle, GUILayout.Width(58f), GUILayout.Height(ItemPickerControlHeight)))
                closeRequested = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.EndVertical();
            DrawModalBorder(itemPickerRect.width, itemPickerRect.height);
            GUI.DragWindow(new Rect(0f, 0f, itemPickerRect.width, 24f));

            if (!string.IsNullOrEmpty(selectedTrader))
            {
                newFileTrader = selectedTrader;
                CloseItemPicker();
            }
            else if (!string.IsNullOrEmpty(selectedPrefab))
            {
                Action<string> callback = itemPickerCallback;
                CloseItemPicker();
                callback?.Invoke(selectedPrefab);
            }
            else if (closeRequested)
            {
                CloseItemPicker();
            }
        }


        private void DrawGlobalKeyWindow(int windowId)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal(GUILayout.Height(ItemPickerControlHeight));
            GUILayout.Label("Search", GUILayout.Width(49f), GUILayout.Height(ItemPickerControlHeight));
            globalKeySearch = GUILayout.TextField(globalKeySearch ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(ItemPickerControlHeight));
            if (GUILayout.Button("×", smallButtonStyle, GUILayout.Width(20f), GUILayout.Height(ItemPickerControlHeight)))
                globalKeySearch = string.Empty;
            if (GUILayout.Button("Close", smallButtonStyle, GUILayout.Width(50f), GUILayout.Height(ItemPickerControlHeight)))
            {
                showGlobalKeyPicker = false;
                globalKeyCallback = null;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            HashSet<string> activeKeys = GetActiveKeys(keyPickerKind);
            List<string> options = GetKeyOptions(keyPickerKind);
            HashSet<string> defaultKeys = new HashSet<string>(GetDefaultKeyOptions(keyPickerKind), StringComparer.OrdinalIgnoreCase);
            globalKeyScroll = GUILayout.BeginScrollView(globalKeyScroll, GUILayout.ExpandHeight(true));
            foreach (string key in options.Where(GlobalKeyMatchesSearch).ToList())
            {
                Rect rowRect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true), GUILayout.Height(22f));
                GUI.Box(rowRect, GUIContent.none, rowStyle);

                float toggleSize = theme.CompactToggleSize;
                Rect toggleRect = new Rect(rowRect.x + 4f, rowRect.y + Mathf.Max(0f, (rowRect.height - toggleSize) * 0.5f), toggleSize, toggleSize);
                bool selected = globalKeySelection.Contains(key);
                bool changed = ConfigEditorGui.Toggle(theme, toggleRect, selected, GUIContent.none, keyNameStyle, 0f);
                if (changed && !selected)
                    globalKeySelection.Add(key);
                else if (!changed && selected)
                    globalKeySelection.Remove(key);

                bool isDefault = defaultKeys.Contains(key);
                float removeWidth = isDefault ? 0f : 16f;
                Rect labelRect = new Rect(toggleRect.xMax + 5f, rowRect.y, Mathf.Max(0f, rowRect.xMax - toggleRect.xMax - 9f - removeWidth), rowRect.height);
                string label = activeKeys.Contains(key) ? key + "  [active]" : key;
                GUI.Label(labelRect, new GUIContent(label, key), selected ? keySelectedNameStyle : keyNameStyle);

                if (!isDefault)
                {
                    Rect removeRect = new Rect(rowRect.xMax - 17f, rowRect.y + 3f, 14f, 14f);
                    if (GUI.Button(removeRect, "×", inlineButtonStyle))
                    {
                        options.RemoveAll(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase));
                        globalKeySelection.Remove(key);
                        SetKeyOptions(keyPickerKind, options);
                        break;
                    }
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal(toolbarStyle, GUILayout.Height(ItemPickerControlHeight));
            newPickerKey = GUILayout.TextField(newPickerKey ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(ItemPickerControlHeight));
            GUI.enabled = !string.IsNullOrWhiteSpace(newPickerKey);
            if (GUILayout.Button("Add key", smallButtonStyle, GUILayout.Width(58f), GUILayout.Height(ItemPickerControlHeight)))
            {
                string key = newPickerKey.Trim();
                if (!options.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    options.Add(key);
                    SetKeyOptions(keyPickerKind, options);
                }
                newPickerKey = string.Empty;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            string selectedValue = string.Join(",", globalKeySelection.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
            GUILayout.BeginHorizontal(GUILayout.Height(20f));
            GUILayout.Label("Selected value:", selectedValueLabelStyle, GUILayout.ExpandWidth(false), GUILayout.Height(20f));
            GUILayout.Label(new GUIContent(selectedValue, selectedValue), selectedValueStyle, GUILayout.ExpandWidth(true), GUILayout.Height(20f));
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.BeginHorizontal(GUILayout.Height(ItemPickerControlHeight));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Apply", smallButtonStyle, GUILayout.Width(50f), GUILayout.Height(ItemPickerControlHeight)))
            {
                Action<string> callback = globalKeyCallback;
                showGlobalKeyPicker = false;
                globalKeyCallback = null;
                callback?.Invoke(selectedValue);
            }
            if (GUILayout.Button("Close", smallButtonStyle, GUILayout.Width(50f), GUILayout.Height(ItemPickerControlHeight)))
            {
                showGlobalKeyPicker = false;
                globalKeyCallback = null;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
            GUILayout.EndVertical();
            DrawModalBorder(globalKeyRect.width, globalKeyRect.height);
            GUI.DragWindow(new Rect(0f, 0f, globalKeyRect.width, 24f));
        }


        private void DrawConfirmWindow(int windowId)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6f);
            GUILayout.Label(confirmMessage ?? string.Empty, centeredMessageStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Confirm", smallButtonStyle, GUILayout.Width(72f), GUILayout.Height(24f)))
            {
                Action action = confirmAction;
                showConfirm = false;
                confirmAction = null;
                action?.Invoke();
            }
            GUILayout.Space(6f);
            if (GUILayout.Button("Cancel", smallButtonStyle, GUILayout.Width(64f), GUILayout.Height(24f)))
            {
                showConfirm = false;
                confirmAction = null;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.EndVertical();
            DrawModalBorder(confirmRect.width, confirmRect.height);
            GUI.DragWindow(new Rect(0f, 0f, confirmRect.width, 24f));
        }

        private void RefreshFiles()
        {
            BeginRequest("Loading configuration files...");
            ConfigEditorTransport.RequestList();
        }

        private void SelectFile(EditorFileInfo file)
        {
            if (file == null)
                return;
            if (activeFile != null && string.Equals(activeFile.Name, file.Name, StringComparison.OrdinalIgnoreCase))
                return;

            if (IsDirty())
            {
                ShowConfirmation("Discard unsaved changes and open another file?", () => OpenFile(file));
                return;
            }

            OpenFile(file);
        }

        private void OpenFile(EditorFileInfo file)
        {
            activeFile = file;
            itemDocument = null;
            traderDocument = null;
            itemSearch = string.Empty;
            itemPage = 0;
            itemScroll = Vector2.zero;

            if (file.Kind == EditorConfigKind.Unsupported)
            {
                SetStatus("Unsupported configuration file name.", true);
                return;
            }

            BeginRequest("Loading " + file.Name + "...");
            ConfigEditorTransport.RequestRead(file.Name);
        }

        private void SaveActiveFile()
        {
            if (activeFile == null || requestInProgress)
                return;

            try
            {
                string extension = Path.GetExtension(activeFile.Name);
                string content;
                if (itemDocument != null)
                {
                    if (!itemDocument.Validate(out string validationError))
                        throw new InvalidDataException(validationError);
                    if (!ValidateItemRows(out validationError))
                        throw new InvalidDataException(validationError);
                    content = ConfigEditorSerialization.SerializeItems(itemDocument.GetItems(), extension);
                }
                else if (traderDocument != null)
                {
                    TraderSettingState invalid = traderDocument.Settings.FirstOrDefault(state => state.HasOverride && !string.IsNullOrWhiteSpace(state.ValidationError));
                    if (invalid != null)
                        throw new InvalidDataException($"Fix invalid value: {invalid.Definition.Section} / {invalid.Definition.Name}.");
                    content = ConfigEditorSerialization.SerializeTrader(traderDocument.BuildRoot(), extension);
                }
                else
                    return;

                BeginRequest("Saving " + activeFile.Name + "...");
                ConfigEditorTransport.RequestWrite(activeFile.Name, content);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, true);
            }
        }

        private void RevertActiveFile()
        {
            if (activeFile == null)
                return;
            if (IsDirty())
                ShowConfirmation("Discard unsaved changes and reload this file?", () => OpenFile(activeFile));
            else
                OpenFile(activeFile);
        }

        private void DeleteActiveFile()
        {
            if (activeFile == null || requestInProgress)
                return;
            string fileName = activeFile.Name;
            ShowConfirmation($"Delete '{fileName}' from {ConfigEditorTransport.TargetDescription}?", () =>
            {
                BeginRequest("Deleting " + fileName + "...");
                ConfigEditorTransport.RequestDelete(fileName);
            });
        }

        private void OpenNewFileWindow()
        {
            if (!ConfigEditorTransport.CanEditTarget)
            {
                SetStatus("Administrator access is required to create files on this server.", true);
                return;
            }

            if (IsDirty())
            {
                ShowConfirmation("Discard unsaved changes and create a new configuration file?", OpenNewFileWindowImmediate);
                return;
            }

            OpenNewFileWindowImmediate();
        }

        private void OpenNewFileWindowImmediate()
        {
            activeFile = null;
            itemDocument = null;
            traderDocument = null;
            showNewFile = true;
            newFileRect = new Rect(0f, 0f, 620f, 520f);
            newFileScroll = Vector2.zero;
            newFileKind = EditorConfigKind.ItemList;
            newFileTrader = "common";
            newFileListType = ItemsListType.Buy;
            newFileIdentifier = string.Empty;
            newFileExtension = "json";
            focusPopupWindow = true;
        }

        private void CreateNewFile(string fileName)
        {
            string content = newFileKind == EditorConfigKind.ItemList
                ? ConfigEditorSerialization.SerializeItems(Array.Empty<TradeableItem>(), Path.GetExtension(fileName))
                : ConfigEditorSerialization.SerializeTrader(new JObject(), Path.GetExtension(fileName));
            showNewFile = false;
            selectAfterList = fileName;
            BeginRequest("Creating " + fileName + "...");
            ConfigEditorTransport.RequestCreate(fileName, content);
        }

        private void OnTransportResponse(ConfigEditorOperation operation, bool success, string fileName, string message, string payload)
        {
            requestInProgress = false;
            SetStatus(success ? string.Empty : message, !success);
            if (!success)
            {
                if ((operation == ConfigEditorOperation.Write || operation == ConfigEditorOperation.Create) &&
                    string.Equals(selectAfterList, fileName, StringComparison.OrdinalIgnoreCase))
                    selectAfterList = null;
                return;
            }

            switch (operation)
            {
                case ConfigEditorOperation.List:
                    files = JsonConvert.DeserializeObject<List<EditorFileInfo>>(payload) ?? new List<EditorFileInfo>();
                    if (!string.IsNullOrWhiteSpace(selectAfterList))
                    {
                        string target = selectAfterList;
                        selectAfterList = null;
                        EditorFileInfo file = files.FirstOrDefault(entry => string.Equals(entry.Name, target, StringComparison.OrdinalIgnoreCase));
                        if (file != null)
                            OpenFile(file);
                    }
                    else if (activeFile != null)
                    {
                        activeFile = files.FirstOrDefault(entry => string.Equals(entry.Name, activeFile.Name, StringComparison.OrdinalIgnoreCase));
                        if (activeFile == null)
                        {
                            itemDocument = null;
                            traderDocument = null;
                        }
                    }
                    break;
                case ConfigEditorOperation.Read:
                    LoadDocument(fileName, payload);
                    break;
                case ConfigEditorOperation.Create:
                case ConfigEditorOperation.Write:
                    if (itemDocument != null && string.Equals(itemDocument.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                        itemDocument.Dirty = false;
                    if (traderDocument != null && string.Equals(traderDocument.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                        traderDocument.Dirty = false;
                    if (string.IsNullOrWhiteSpace(selectAfterList))
                        selectAfterList = fileName;
                    RefreshFiles();
                    break;
                case ConfigEditorOperation.Delete:
                    if (activeFile != null && string.Equals(activeFile.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        activeFile = null;
                        itemDocument = null;
                        traderDocument = null;
                    }
                    RefreshFiles();
                    break;
            }
        }

        private void LoadDocument(string fileName, string content)
        {
            EditorFileInfo file = files.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase)) ?? activeFile;
            if (file == null)
                return;
            activeFile = file;

            try
            {
                if (file.Kind == EditorConfigKind.ItemList)
                {
                    List<TradeableItem> items = DeserializeItems(content, fileName);
                    if (items == null)
                        throw new InvalidDataException("The item configuration could not be parsed.");
                    itemDocument = new ItemConfigDocument(fileName, items);
                    traderDocument = null;
                }
                else if (file.Kind == EditorConfigKind.TraderSettings)
                {
                    JObject root = TraderConfigManager.DeserializeTraderConfig(content, fileName);
                    if (root == null)
                        throw new InvalidDataException("The trader configuration could not be parsed.");
                    traderDocument = new TraderConfigDocument(fileName, file.Trader, root);
                    itemDocument = null;
                }
                SetStatus(string.Empty, false);
            }
            catch (Exception exception)
            {
                itemDocument = null;
                traderDocument = null;
                SetStatus(exception.Message, true);
            }
        }

        private void OpenItemPicker(string title, Action<string> callback, bool multiAdd)
        {
            if (showItemPicker)
            {
                focusPopupWindow = true;
                return;
            }

            temporaryTraderOptions = null;
            EnsureItemOptions();
            itemPickerTitle = title;
            itemPickerCallback = callback;
            itemPickerMultiAdd = multiAdd;
            itemPickerSearch = string.Empty;
            itemPickerPage = 0;
            itemPickerScroll = Vector2.zero;
            itemPickerRect.width = 572f;
            showItemPicker = true;
            focusPopupWindow = true;
        }

        private void CloseItemPicker()
        {
            showItemPicker = false;
            temporaryTraderOptions = null;
            itemPickerCallback = null;
            itemPickerMultiAdd = false;
        }


        private void OpenTraderPicker()
        {
            List<TraderOption> traders = GetTraderOptions();
            showItemPicker = true;
            focusPopupWindow = true;
            itemPickerTitle = "Select trader prefab";
            itemPickerSearch = string.Empty;
            itemPickerPage = 0;
            itemPickerScroll = Vector2.zero;
            itemPickerCallback = null;
            itemPickerMultiAdd = false;
            temporaryTraderOptions = traders;
        }

        private List<TraderOption> temporaryTraderOptions;

        private void OpenKeyPicker(string currentValue, KeyPickerKind kind, Action<string> callback)
        {
            keyPickerKind = kind;
            globalKeySelection = new HashSet<string>(SplitKeyValues(currentValue), StringComparer.OrdinalIgnoreCase);
            globalKeyCallback = callback;
            globalKeySearch = string.Empty;
            globalKeyScroll = Vector2.zero;
            newPickerKey = string.Empty;
            globalKeyRect.width = 330f;
            showGlobalKeyPicker = true;
            focusPopupWindow = true;
        }


        private void ShowConfirmation(string message, Action action)
        {
            confirmMessage = message ?? string.Empty;
            confirmAction = action;
            int lineCount = Math.Max(1, confirmMessage.Split(new[] { '\n' }, StringSplitOptions.None).Length);
            float width = Mathf.Clamp(120f + confirmMessage.Length * 5.4f, 300f, 620f);
            float height = 92f + Math.Max(0, lineCount - 1) * 18f;
            confirmRect = new Rect(0f, 0f, width, height);
            showConfirm = true;
            focusPopupWindow = true;
        }

        private void DrawIntegerField(ref string text, Action<int> setter, float width, string validationError)
        {
            string changed = DrawValidatedTextField(text ?? string.Empty, GUI.skin.textField, validationError, width, CompactControlHeight);
            if (!string.Equals(changed, text, StringComparison.Ordinal))
            {
                text = changed;
                if (int.TryParse(changed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    setter(parsed);
                itemDocument.Dirty = true;
            }
        }

        private string DrawValidatedTextField(string value, GUIStyle style, string validationError, float width, float height)
        {
            Color previousContentColor = GUI.contentColor;
            if (!string.IsNullOrEmpty(validationError))
                GUI.contentColor = new Color(1f, 0.35f, 0.3f, 1f);
            string changed = GUILayout.TextField(value ?? string.Empty, style, GUILayout.Width(width), GUILayout.Height(height));
            GUI.contentColor = previousContentColor;
            return changed;
        }

        private void DrawPrefabField(string value, Action<string> setter, float width, string title, string validationError)
        {
            DrawPickerField(value, setter, width, validationError, () =>
            {
                OpenItemPicker(title, prefab =>
                {
                    setter(prefab);
                    itemDocument.Dirty = true;
                }, false);
            });
        }

        private void DrawKeyField(string value, Action<string> setter, float width, KeyPickerKind kind, string validationError)
        {
            DrawPickerField(value, setter, width, validationError, () =>
            {
                OpenKeyPicker(value, kind, keys =>
                {
                    setter(keys);
                    itemDocument.Dirty = true;
                });
            });
        }

        private void DrawPickerField(string value, Action<string> setter, float width, string validationError, Action openPicker)
        {
            Rect slot = GUILayoutUtility.GetRect(width, CompactControlHeight, GUILayout.Width(width), GUILayout.Height(CompactControlHeight));
            float textWidth = Mathf.Max(30f, width - InlinePickerButtonWidth - ItemColumnSpacing);
            float fieldY = slot.y;
            Rect textRect = new Rect(slot.x, fieldY, textWidth, CompactControlHeight);
            Color previousContentColor = GUI.contentColor;
            if (!string.IsNullOrEmpty(validationError))
                GUI.contentColor = new Color(1f, 0.35f, 0.3f, 1f);
            string changed = GUI.TextField(textRect, value ?? string.Empty, GUI.skin.textField);
            GUI.contentColor = previousContentColor;
            if (!string.Equals(changed, value, StringComparison.Ordinal))
            {
                setter(changed);
                itemDocument.Dirty = true;
            }

            Rect buttonRect = new Rect(textRect.xMax + ItemColumnSpacing,
                fieldY + Mathf.Max(0f, (CompactControlHeight - InlinePickerButtonHeight) * 0.5f),
                InlinePickerButtonWidth, InlinePickerButtonHeight);
            if (GUI.Button(buttonRect, "...", inlineButtonStyle))
                openPicker?.Invoke();
        }


        private bool DrawInlinePickerButton()
        {
            Rect slot = GUILayoutUtility.GetRect(InlinePickerButtonWidth, CompactControlHeight,
                GUILayout.Width(InlinePickerButtonWidth), GUILayout.Height(CompactControlHeight));
            Rect buttonRect = new Rect(slot.x, slot.y + Mathf.Max(0f, (slot.height - InlinePickerButtonHeight) * 0.5f),
                InlinePickerButtonWidth, InlinePickerButtonHeight);
            return GUI.Button(buttonRect, "...", inlineButtonStyle);
        }

        private bool DrawQuickActionButton(string text, string tooltip)
        {
            Rect slot = GUILayoutUtility.GetRect(QuickDeleteButtonWidth, CompactControlHeight,
                GUILayout.Width(QuickDeleteButtonWidth), GUILayout.Height(CompactControlHeight));
            Rect buttonRect = new Rect(slot.x, slot.y + Mathf.Max(0f, (slot.height - InlinePickerButtonHeight) * 0.5f),
                QuickDeleteButtonWidth, InlinePickerButtonHeight);
            return GUI.Button(buttonRect, new GUIContent(text, tooltip), inlineButtonStyle);
        }

        private void KeepPopupInFront(int windowId)
        {
            GUI.BringWindowToFront(windowId);
            if (!focusPopupWindow)
                return;
            GUI.FocusWindow(windowId);
            focusPopupWindow = false;
        }

        private static void ItemColumnGap() => GUILayout.Space(ItemColumnSpacing);

        private float GetItemPrefabColumnWidth()
        {
            EnsureItemOptions();
            if (!itemPrefabColumnWidthDirty)
                return itemPrefabColumnWidth;

            GUIStyle style = GUI.skin?.textField ?? GUI.skin?.label;
            float width = 120f;
            if (style != null)
            {
                foreach (ItemOption option in itemOptions)
                    width = Mathf.Max(width, style.CalcSize(new GUIContent(option.Prefab ?? string.Empty)).x * 0.96f + 6f);
            }
            itemPrefabColumnWidth = Mathf.Ceil(width);
            itemPrefabColumnWidthDirty = false;
            return itemPrefabColumnWidth;
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            if (Event.current.type != EventType.Repaint || rect.width <= 0f || rect.height <= 0f)
                return;

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawItemIcon(string prefab, float size)
        {
            itemOptionsByPrefab.TryGetValue(prefab ?? string.Empty, out ItemOption option);
            DrawSprite(option?.Icon, size);
        }

        private void DrawSprite(Sprite sprite, float size)
        {
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (sprite == null || sprite.texture == null)
            {
                GUI.Box(rect, GUIContent.none);
                return;
            }

            Rect textureRect = sprite.textureRect;
            Rect uv = new Rect(
                textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
        }

        private void DrawItemPickerSortButton(string text, ItemPickerSortColumn column, float width)
        {
            bool selected = itemPickerSortColumn == column;
            string marker = selected ? (itemPickerSortAscending ? " ▲" : " ▼") : string.Empty;
            if (GUILayout.Button(text + marker, selected ? theme.AccentButtonStyle : smallButtonStyle, GUILayout.Width(width), GUILayout.Height(ItemPickerControlHeight)))
            {
                if (selected)
                    itemPickerSortAscending = !itemPickerSortAscending;
                else
                {
                    itemPickerSortColumn = column;
                    itemPickerSortAscending = true;
                }
                itemPickerPage = 0;
                itemPickerScroll = Vector2.zero;
            }
        }

        private List<ItemOption> SortItemPickerOptions(IEnumerable<ItemOption> options)
        {
            Func<ItemOption, string> selector = itemPickerSortColumn == ItemPickerSortColumn.LocalizedName
                ? option => option.LocalizedName ?? string.Empty
                : option => option.Prefab ?? string.Empty;
            IOrderedEnumerable<ItemOption> ordered = itemPickerSortAscending
                ? options.OrderBy(selector, StringComparer.CurrentCultureIgnoreCase)
                : options.OrderByDescending(selector, StringComparer.CurrentCultureIgnoreCase);
            return ordered.ThenBy(option => option.Prefab ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void SortItemRows(ItemSortColumn column, bool ascending, bool selectedOnly)
        {
            Func<EditorItemRow, object> selector = column switch
            {
                ItemSortColumn.LocalizedName => row => GetItemDisplayName(row.Item.prefab),
                ItemSortColumn.Stack => row => row.Item.stack,
                ItemSortColumn.Price => row => row.Item.price,
                ItemSortColumn.Quality => row => row.Item.quality,
                ItemSortColumn.Currency => row => row.Item.currency ?? string.Empty,
                ItemSortColumn.RequiredGlobalKey => row => row.Item.requiredGlobalKey ?? string.Empty,
                ItemSortColumn.BlockedGlobalKey => row => row.Item.notRequiredGlobalKey ?? string.Empty,
                ItemSortColumn.RequiredPlayerKey => row => row.Item.requiredPlayerKey ?? string.Empty,
                ItemSortColumn.BlockedPlayerKey => row => row.Item.notRequiredPlayerKey ?? string.Empty,
                _ => row => row.Item.prefab ?? string.Empty
            };

            List<EditorItemRow> source = selectedOnly
                ? itemDocument.Rows.Where(row => row.Selected).ToList()
                : itemDocument.Rows.ToList();
            if (source.Count < 2)
                return;

            List<EditorItemRow> sorted = (ascending
                    ? source.OrderBy(selector, ItemSortValueComparer.Instance)
                    : source.OrderByDescending(selector, ItemSortValueComparer.Instance))
                .ToList();

            if (selectedOnly)
            {
                int sortedIndex = 0;
                for (int index = 0; index < itemDocument.Rows.Count; index++)
                {
                    if (itemDocument.Rows[index].Selected)
                        itemDocument.Rows[index] = sorted[sortedIndex++];
                }
            }
            else
            {
                itemDocument.Rows.Clear();
                itemDocument.Rows.AddRange(sorted);
                itemPage = 0;
            }

            itemDocument.Dirty = true;
            itemScroll = Vector2.zero;
        }

        private void EnsureVisibleItemColumns()
        {
            string source = configEditorVisibleItemColumns?.Value ?? DefaultEditorVisibleItemColumns;
            if (string.Equals(source, visibleItemColumnsSource, StringComparison.Ordinal))
                return;

            visibleItemColumns.Clear();
            foreach (string token in (source ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseItemColumnToken(token.Trim(), out ItemSortColumn column))
                    visibleItemColumns.Add(column);
            }
            visibleItemColumnsSource = source ?? string.Empty;
        }

        private bool IsItemColumnVisible(ItemSortColumn column)
        {
            EnsureVisibleItemColumns();
            return visibleItemColumns.Contains(column);
        }

        private void SetItemColumnVisible(ItemSortColumn column, bool visible)
        {
            EnsureVisibleItemColumns();
            if (visible)
                visibleItemColumns.Add(column);
            else
                visibleItemColumns.Remove(column);

            string value = string.Join(",", ItemColumnOrder.Where(visibleItemColumns.Contains).Select(GetItemColumnToken));
            visibleItemColumnsSource = value;
            if (configEditorVisibleItemColumns != null)
                configEditorVisibleItemColumns.Value = value;
        }

        private static bool TryParseItemColumnToken(string token, out ItemSortColumn column)
        {
            switch (token?.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant())
            {
                case "prefab": column = ItemSortColumn.Prefab; return true;
                case "name":
                case "localizedname": column = ItemSortColumn.LocalizedName; return true;
                case "stack": column = ItemSortColumn.Stack; return true;
                case "price": column = ItemSortColumn.Price; return true;
                case "quality": column = ItemSortColumn.Quality; return true;
                case "currency": column = ItemSortColumn.Currency; return true;
                case "requiredglobalkey": column = ItemSortColumn.RequiredGlobalKey; return true;
                case "blockedglobalkey": column = ItemSortColumn.BlockedGlobalKey; return true;
                case "requiredplayerkey": column = ItemSortColumn.RequiredPlayerKey; return true;
                case "blockedplayerkey": column = ItemSortColumn.BlockedPlayerKey; return true;
                default:
                    column = default;
                    return false;
            }
        }

        private static string GetItemColumnToken(ItemSortColumn column)
        {
            return column switch
            {
                ItemSortColumn.LocalizedName => "Name",
                ItemSortColumn.RequiredGlobalKey => "RequiredGlobalKey",
                ItemSortColumn.BlockedGlobalKey => "BlockedGlobalKey",
                ItemSortColumn.RequiredPlayerKey => "RequiredPlayerKey",
                ItemSortColumn.BlockedPlayerKey => "BlockedPlayerKey",
                _ => column.ToString()
            };
        }

        private string GetItemRowValidation(EditorItemRow row)
        {
            return BuildItemRowValidation(row).Combined;
        }

        private ItemRowValidation BuildItemRowValidation(EditorItemRow row)
        {
            ItemRowValidation result = new ItemRowValidation();

            string rawPrefab = row.Item.prefab ?? string.Empty;
            string prefab = rawPrefab.Trim();
            if (string.IsNullOrEmpty(prefab))
                result.Prefab = "Item prefab is required.";
            else if (!string.Equals(rawPrefab, prefab, StringComparison.Ordinal))
                result.Prefab = "Item prefab must not contain leading or trailing whitespace.";
            else if (!itemOptionsByPrefab.ContainsKey(prefab))
                result.Prefab = $"Item prefab '{prefab}' was not found in ObjectDB.";

            if (!int.TryParse(row.StackText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                result.Stack = "Stack must be a valid integer.";
            if (!int.TryParse(row.PriceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                result.Price = "Price must be a valid integer.";
            if (!int.TryParse(row.QualityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                result.Quality = "Quality must be a valid integer.";

            string rawCurrency = row.Item.currency ?? string.Empty;
            string currency = rawCurrency.Trim();
            if (!string.Equals(rawCurrency, currency, StringComparison.Ordinal))
                result.Currency = "Currency prefab must not contain leading or trailing whitespace.";
            else if (!string.IsNullOrEmpty(currency) && !itemOptionsByPrefab.ContainsKey(currency))
                result.Currency = $"Currency prefab '{currency}' was not found in ObjectDB.";

            result.RequiredGlobalKey = GetKeyValidation(row.Item.requiredGlobalKey, KeyPickerKind.Global, "Required global key");
            result.BlockedGlobalKey = GetKeyValidation(row.Item.notRequiredGlobalKey, KeyPickerKind.Global, "Blocked global key");
            result.RequiredPlayerKey = GetKeyValidation(row.Item.requiredPlayerKey, KeyPickerKind.Player, "Required player key");
            result.BlockedPlayerKey = GetKeyValidation(row.Item.notRequiredPlayerKey, KeyPickerKind.Player, "Blocked player key");

            string globalIntersection = GetIntersectionValidation(row.Item.requiredGlobalKey, row.Item.notRequiredGlobalKey, "global");
            if (!string.IsNullOrEmpty(globalIntersection))
            {
                result.RequiredGlobalKey = AppendValidation(result.RequiredGlobalKey, globalIntersection);
                result.BlockedGlobalKey = AppendValidation(result.BlockedGlobalKey, globalIntersection);
            }

            string playerIntersection = GetIntersectionValidation(row.Item.requiredPlayerKey, row.Item.notRequiredPlayerKey, "player");
            if (!string.IsNullOrEmpty(playerIntersection))
            {
                result.RequiredPlayerKey = AppendValidation(result.RequiredPlayerKey, playerIntersection);
                result.BlockedPlayerKey = AppendValidation(result.BlockedPlayerKey, playerIntersection);
            }

            return result;
        }

        private string GetKeyValidation(string value, KeyPickerKind kind, string fieldName)
        {
            HashSet<string> allowed = new HashSet<string>(GetKeyOptions(kind), StringComparer.OrdinalIgnoreCase);
            List<string> errors = new List<string>();
            foreach (string key in SplitKeyValues(value))
            {
                if (!allowed.Contains(key))
                    errors.Add($"{fieldName} '{key}' is not present in the configured {(kind == KeyPickerKind.Global ? "global" : "player")} key list.");
            }
            return string.Join(" ", errors);
        }

        private static string GetIntersectionValidation(string required, string blocked, string type)
        {
            HashSet<string> requiredKeys = new HashSet<string>(SplitKeyValues(required), StringComparer.OrdinalIgnoreCase);
            requiredKeys.IntersectWith(SplitKeyValues(blocked));
            return requiredKeys.Count == 0
                ? string.Empty
                : $"The same {type} key cannot be both required and blocked: {string.Join(", ", requiredKeys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}.";
        }

        private static string AppendValidation(string current, string addition)
        {
            if (string.IsNullOrEmpty(current))
                return addition ?? string.Empty;
            if (string.IsNullOrEmpty(addition))
                return current;
            return current + " " + addition;
        }

        private bool ValidateItemRows(out string error)
        {
            error = string.Empty;
            EnsureItemOptions();
            for (int index = 0; index < itemDocument.Rows.Count; index++)
            {
                EditorItemRow row = itemDocument.Rows[index];
                row.ValidationError = GetItemRowValidation(row);
                if (!string.IsNullOrWhiteSpace(row.ValidationError))
                {
                    error = $"Row {index + 1}: {row.ValidationError}";
                    return false;
                }
            }
            return true;
        }

        private static IEnumerable<string> SplitKeyValues(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private void Header(string text, float width)
        {
            GUILayout.Label(text, headerStyle, GUILayout.Width(width));
        }

        private bool FileMatchesSearch(EditorFileInfo file)
        {
            if (string.IsNullOrWhiteSpace(fileSearch))
                return true;
            string search = fileSearch.Trim();
            return Contains(file.Name, search) || Contains(GetFileListDisplayName(file), search) ||
                   Contains(file.Trader, search) || Contains(GetTraderDisplayName(file.Trader), search);
        }

        private bool ItemMatchesSearch(EditorItemRow row)
        {
            if (string.IsNullOrWhiteSpace(itemSearch))
                return true;
            string search = itemSearch.Trim();
            TradeableItem item = row.Item;
            return Contains(item.prefab, search) || Contains(GetItemDisplayName(item.prefab), search) ||
                   Contains(item.currency, search) || Contains(item.requiredGlobalKey, search) ||
                   Contains(item.notRequiredGlobalKey, search) || Contains(item.requiredPlayerKey, search) ||
                   Contains(item.notRequiredPlayerKey, search);
        }



        private bool ItemOptionMatchesSearch(ItemOption option)
        {
            if (string.IsNullOrWhiteSpace(itemPickerSearch))
                return true;
            return Contains(option.Prefab, itemPickerSearch) || Contains(option.LocalizedName, itemPickerSearch);
        }

        private bool TraderOptionMatchesSearch(TraderOption option)
        {
            if (string.IsNullOrWhiteSpace(itemPickerSearch))
                return true;
            return Contains(option.Prefab, itemPickerSearch) || Contains(option.DisplayName, itemPickerSearch);
        }

        private bool GlobalKeyMatchesSearch(string key)
        {
            return string.IsNullOrWhiteSpace(globalKeySearch) || Contains(key, globalKeySearch);
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(search ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BuildItemOptions()
        {
            itemOptions.Clear();
            itemOptionsByPrefab.Clear();
            itemOptionsBuilt = false;
            itemPrefabColumnWidthDirty = true;
            if (ObjectDB.instance?.m_items == null)
                return;

            HashSet<string> aiItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Humanoid humanoid in Resources.FindObjectsOfTypeAll<Humanoid>())
            {
                if (humanoid == null || !humanoid.TryGetComponent<BaseAI>(out _) || humanoid.m_defaultItems == null)
                    continue;
                foreach (GameObject item in humanoid.m_defaultItems)
                {
                    if (item != null && item.TryGetComponent<ItemDrop>(out _))
                        aiItems.Add(item.name);
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                try
                {
                    if (prefab == null || string.IsNullOrWhiteSpace(prefab.name) || !seen.Add(prefab.name) || !prefab.TryGetComponent(out ItemDrop itemDrop))
                        continue;
                    ItemDrop.ItemData itemData = itemDrop.m_itemData;
                    if (itemData?.m_shared == null)
                        continue;
                    Sprite icon = GetSafeItemIcon(itemData);
                    string rawName = string.IsNullOrWhiteSpace(itemData.m_shared.m_name) ? prefab.name : itemData.m_shared.m_name;
                    string localizedName = Localization.instance != null ? Localization.instance.Localize(rawName) : rawName;
                    if (string.IsNullOrWhiteSpace(localizedName) || localizedName.StartsWith("$", StringComparison.Ordinal))
                        localizedName = string.Empty;
                    bool invalidGameItem = !(itemData.m_shared.m_name ?? string.Empty).StartsWith("$", StringComparison.Ordinal) ||
                        itemData.m_shared.m_description == null ||
                        itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.None ||
                        itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Customization;
                    bool visibleByDefault = icon != null && icon.texture != null &&
                        !aiItems.Contains(prefab.name) &&
                        !invalidGameItem;
                    ItemOption option = new ItemOption(prefab.name, localizedName, icon, visibleByDefault);
                    itemOptions.Add(option);
                    itemOptionsByPrefab[prefab.name] = option;
                }
                catch (Exception exception)
                {
                    LogWarning($"Configuration editor skipped item prefab '{prefab?.name ?? "<null>"}': {exception.Message}");
                }
            }

            itemOptions.Sort((left, right) =>
            {
                int byName = string.Compare(left.LocalizedName, right.LocalizedName, StringComparison.CurrentCultureIgnoreCase);
                return byName != 0 ? byName : string.Compare(left.Prefab, right.Prefab, StringComparison.OrdinalIgnoreCase);
            });
            itemOptionsBuilt = true;
        }

        private void EnsureItemOptions()
        {
            if (!itemOptionsBuilt)
                BuildItemOptions();
        }


        private static Sprite GetSafeItemIcon(ItemDrop.ItemData itemData)
        {
            Sprite[] icons = itemData?.m_shared?.m_icons;
            if (icons == null || icons.Length == 0)
                return null;
            int variant = itemData.m_variant;
            if (variant >= 0 && variant < icons.Length)
                return icons[variant];
            return icons[0];
        }

        private string ValidateItemPrefab(object value)
        {
            EnsureItemOptions();
            string rawPrefab = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            string prefab = rawPrefab.Trim();
            if (string.IsNullOrEmpty(prefab))
                return "Item prefab is required.";
            if (!string.Equals(rawPrefab, prefab, StringComparison.Ordinal))
                return "Item prefab must not contain leading or trailing whitespace.";
            return itemOptionsByPrefab.ContainsKey(prefab) ? string.Empty : $"Item prefab '{prefab}' was not found in ObjectDB.";
        }

        private string GetItemDisplayName(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
                return string.Empty;
            return itemOptionsByPrefab.TryGetValue(prefab, out ItemOption option) ? option.LocalizedName : string.Empty;
        }

        private string GetTraderDisplayName(string trader)
        {
            if (string.IsNullOrWhiteSpace(trader))
                return string.Empty;
            if (string.Equals(trader, "common", StringComparison.OrdinalIgnoreCase))
                return "Common";
            if (traderDisplayNames.TryGetValue(trader, out string cached))
                return cached;

            string normalized = TraderName(trader);
            Trader match = Resources.FindObjectsOfTypeAll<Trader>().FirstOrDefault(candidate =>
                candidate != null && string.Equals(TraderName(Utils.GetPrefabName(candidate.gameObject)), normalized, StringComparison.OrdinalIgnoreCase));
            string result = match != null && Localization.instance != null
                ? Localization.instance.Localize(match.m_name)
                : trader;
            if (string.IsNullOrWhiteSpace(result) || result.StartsWith("$", StringComparison.Ordinal))
                result = trader;
            traderDisplayNames[trader] = result;
            return result;
        }

        private List<TraderOption> GetTraderOptions()
        {
            Dictionary<string, TraderOption> result = new Dictionary<string, TraderOption>(StringComparer.OrdinalIgnoreCase);
            foreach (Trader trader in Resources.FindObjectsOfTypeAll<Trader>())
            {
                if (trader == null)
                    continue;
                string prefab = Utils.GetPrefabName(trader.gameObject);
                if (string.IsNullOrWhiteSpace(prefab))
                    continue;
                result[prefab] = new TraderOption(prefab, GetTraderDisplayName(prefab));
            }
            foreach (string trader in TraderConfigManager.GetKnownTraderNames())
            {
                if (!result.ContainsKey(trader))
                    result[trader] = new TraderOption(trader, GetTraderDisplayName(trader));
            }
            return result.Values.OrderBy(value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private HashSet<string> GetActiveKeys(KeyPickerKind kind)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (kind == KeyPickerKind.Player)
            {
                if (Player.m_localPlayer != null)
                {
                    foreach (string key in Player.m_localPlayer.GetUniqueKeys())
                    {
                        if (!string.IsNullOrWhiteSpace(key))
                            result.Add(key);
                    }
                }
                return result;
            }

            if (ZoneSystem.instance == null)
                return result;
            foreach (string raw in ZoneSystem.instance.GetGlobalKeys())
                result.Add(ZoneSystem.GetKeyValue(raw, out _, out _));
            return result;
        }

        private static IEnumerable<string> GetDefaultKeyOptions(KeyPickerKind kind)
        {
            string value = kind == KeyPickerKind.Global ? DefaultEditorGlobalKeys : DefaultEditorPlayerKeys;
            return SplitKeyValues(value);
        }

        private List<string> GetKeyOptions(KeyPickerKind kind)
        {
            string value = kind == KeyPickerKind.Global ? configEditorGlobalKeys?.Value : configEditorPlayerKeys?.Value;
            return SplitKeyValues(value).ToList();
        }

        private void SetKeyOptions(KeyPickerKind kind, IEnumerable<string> values)
        {
            string value = string.Join(",", (values ?? Enumerable.Empty<string>())
                .Select(key => key?.Trim())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (kind == KeyPickerKind.Global)
            {
                if (configEditorGlobalKeys != null)
                    configEditorGlobalKeys.Value = value;
            }
            else if (configEditorPlayerKeys != null)
            {
                configEditorPlayerKeys.Value = value;
            }
        }


        private string BuildNewFileName()
        {
            string trader = (newFileTrader ?? string.Empty).Trim();
            string extension = newFileExtension;
            if (newFileKind == EditorConfigKind.TraderSettings)
                return $"{trader}.config.{extension}";

            string list = newFileListType.ToString().ToLowerInvariant();
            string identifier = string.IsNullOrWhiteSpace(newFileIdentifier) ? string.Empty : "." + newFileIdentifier.Trim().Trim('.');
            return $"{trader}.{list}{identifier}.{extension}";
        }

        private string ValidateNewFile(string fileName)
        {
            if (!ConfigEditorTransport.CanEditTarget)
                return "Administrator access is required to create files on this server.";
            if (string.IsNullOrWhiteSpace(newFileTrader))
                return "Trader prefab is required. Use common for shared item lists.";
            if ((newFileTrader ?? string.Empty).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newFileTrader.Contains("/") || newFileTrader.Contains("\\"))
                return "Trader prefab contains invalid file-name characters.";
            if ((newFileIdentifier ?? string.Empty).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newFileIdentifier.Contains("/") || newFileIdentifier.Contains("\\"))
                return "Identifier contains invalid file-name characters.";
            if (files.Any(file => string.Equals(file.Name, fileName, StringComparison.OrdinalIgnoreCase)))
                return "A file with this name already exists.";
            if (newFileKind == EditorConfigKind.TraderSettings && files.Any(file =>
                    file.Kind == EditorConfigKind.TraderSettings && string.Equals(TraderName(file.Trader), TraderName(newFileTrader), StringComparison.OrdinalIgnoreCase)))
                return "A personal configuration for this trader already exists. Only one personal file per trader is used.";
            if (newFileKind == EditorConfigKind.ItemList && !TryParseConfigFileName(fileName, out _, out _))
                return "The generated item configuration file name is invalid.";
            if (newFileKind == EditorConfigKind.TraderSettings && !TryParseTraderConfigFileName(fileName, out _))
                return "The generated trader configuration file name is invalid.";
            return string.Empty;
        }

        private void DrawFormatToggle(string format)
        {
            bool selected = string.Equals(newFileExtension, format, StringComparison.OrdinalIgnoreCase);
            if (GUILayout.Button(format.ToUpperInvariant(), selected ? theme.AccentButtonStyle : smallButtonStyle, GUILayout.Height(28f)))
                newFileExtension = format;
        }

        private bool IsDirty()
        {
            return itemDocument?.Dirty == true || traderDocument?.Dirty == true;
        }

        private void BeginRequest(string message)
        {
            requestInProgress = true;
            requestStartedAt = Time.realtimeSinceStartup;
            SetStatus(message, false);
        }

        private void SetStatus(string message, bool error)
        {
            statusText = message ?? string.Empty;
            statusError = error;
        }

        private static string FormatValue(TraderSettingType type, object value)
        {
            if (value == null)
                return string.Empty;
            return type switch
            {
                TraderSettingType.Boolean => (bool)value ? "true" : "false",
                TraderSettingType.Float => Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture),
                TraderSettingType.Vector2 => value is Vector2 vector
                    ? $"{{ x: {vector.x.ToString("0.###", CultureInfo.InvariantCulture)}, y: {vector.y.ToString("0.###", CultureInfo.InvariantCulture)} }}"
                    : value.ToString(),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
            };
        }

        private void LoadLayout()
        {
            if (layoutLoaded)
                return;
            Vector2 size = configEditorWindowSize?.Value ?? new Vector2(1500f, 850f);
            size.x = Mathf.Clamp(size.x, MinimumWidth, Mathf.Max(MinimumWidth, scale.LogicalWidth));
            size.y = Mathf.Clamp(size.y, MinimumHeight, Mathf.Max(MinimumHeight, scale.LogicalHeight));
            Vector2 position = configEditorWindowPosition?.Value ?? new Vector2(-1f, -1f);
            if (position.x < 0f || position.y < 0f)
                position = new Vector2((scale.LogicalWidth - size.x) * 0.5f, (scale.LogicalHeight - size.y) * 0.5f);
            windowRect = ClampRect(new Rect(position, size));
            layoutLoaded = true;
        }

        private void SaveLayout()
        {
            if (!layoutLoaded)
                return;
            if (configEditorWindowPosition != null)
                configEditorWindowPosition.Value = windowRect.position;
            if (configEditorWindowSize != null)
                configEditorWindowSize.Value = windowRect.size;
            layoutDirty = false;
        }

        private void ClampWindowToScreen()
        {
            windowRect = ClampRect(windowRect);
        }

        private Rect ClampRect(Rect rect)
        {
            float maxWidth = Mathf.Max(MinimumWidth, scale.LogicalWidth);
            float maxHeight = Mathf.Max(MinimumHeight, scale.LogicalHeight);
            rect.width = Mathf.Clamp(rect.width, MinimumWidth, maxWidth);
            rect.height = Mathf.Clamp(rect.height, MinimumHeight, maxHeight);
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, scale.LogicalWidth - 40f));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, scale.LogicalHeight - TitleBarHeight));
            return rect;
        }

        private void HandleResizeInput()
        {
            Event current = Event.current;
            Vector2 localMouse = current.mousePosition;
            Rect handleRect = GetResizeHandleHitRect();
            bool overCorner = handleRect.Contains(localMouse);
            bool overRightEdge = !overCorner &&
                                 localMouse.x >= windowRect.width - ResizeEdgeHitSize &&
                                 localMouse.x <= windowRect.width &&
                                 localMouse.y >= TitleBarHeight &&
                                 localMouse.y < handleRect.yMin;
            bool overBottomEdge = !overCorner &&
                                  localMouse.y >= windowRect.height - ResizeEdgeHitSize &&
                                  localMouse.y <= windowRect.height &&
                                  localMouse.x >= 0f &&
                                  localMouse.x < handleRect.xMin;

            if (current.type == EventType.MouseDown && current.button == 0 && (overCorner || overRightEdge || overBottomEdge))
            {
                resizing = true;
                resizeStartMouse = scale.GetLogicalMousePosition();
                resizeStartRect = windowRect;
                resizeWidth = overCorner || overRightEdge;
                resizeHeight = overCorner || overBottomEdge;
                current.Use();
            }

            if (!resizing)
                return;

            if (UnityInput.Current.GetKey(KeyCode.Mouse0))
            {
                Vector2 delta = scale.GetLogicalMousePosition() - resizeStartMouse;
                Rect rect = resizeStartRect;
                if (resizeWidth)
                    rect.width = resizeStartRect.width + delta.x;
                if (resizeHeight)
                    rect.height = resizeStartRect.height + delta.y;
                windowRect = ClampRect(rect);
                MarkLayoutDirty();
                if (current.type == EventType.MouseDrag || current.type == EventType.MouseDown)
                    current.Use();
            }

            if (UnityInput.Current.GetKeyUp(KeyCode.Mouse0))
            {
                ClearResizeState();
                MarkLayoutDirty();
            }
        }

        private static bool RectChanged(Rect left, Rect right)
        {
            return Mathf.Abs(left.x - right.x) > 0.01f ||
                   Mathf.Abs(left.y - right.y) > 0.01f ||
                   Mathf.Abs(left.width - right.width) > 0.01f ||
                   Mathf.Abs(left.height - right.height) > 0.01f;
        }

        private void MarkLayoutDirty()
        {
            layoutDirty = true;
            saveLayoutAfterRealtime = Time.realtimeSinceStartup + LayoutSaveDelay;
        }

        private void DrawWindowBorder()
        {
            DrawModalBorder(windowRect.width, windowRect.height);
        }

        private void DrawModalBorder(float width, float height)
        {
            Texture2D texture = theme.BorderTexture;
            if (texture == null)
                return;
            width = Mathf.Max(1f, width);
            height = Mathf.Max(1f, height);
            GUI.DrawTexture(new Rect(0f, 0f, width, WindowBorderWidth), texture);
            GUI.DrawTexture(new Rect(0f, height - WindowBorderWidth, width, WindowBorderWidth), texture);
            GUI.DrawTexture(new Rect(0f, 0f, WindowBorderWidth, height), texture);
            GUI.DrawTexture(new Rect(width - WindowBorderWidth, 0f, WindowBorderWidth, height), texture);
        }

        private void DrawResizeHandle()
        {
            Rect hitRect = GetResizeHandleHitRect();
            if (theme.WindowTexture != null)
                GUI.DrawTexture(hitRect, theme.WindowTexture);
            Rect visualRect = new Rect(
                hitRect.xMax - ResizeHandleVisualSize,
                hitRect.yMax - ResizeHandleVisualSize,
                ResizeHandleVisualSize,
                ResizeHandleVisualSize);
            GUIContent content = new GUIContent(string.Empty, "Drag to resize");
            GUI.Label(visualRect, content, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                bool hover = visualRect.Contains(Event.current.mousePosition);
                theme.ResizeHandleStyle.Draw(visualRect, content, hover, resizing, false, false);
            }
            Texture2D line = theme.BorderTexture;
            if (line == null)
                return;
            float right = visualRect.xMax - 1f;
            float bottom = visualRect.yMax - 1f;
            GUI.DrawTexture(new Rect(right - 3f, bottom, 3f, 1f), line);
            GUI.DrawTexture(new Rect(right - 1f, bottom - 2f, 1f, 2f), line);
        }

        private Rect GetResizeHandleHitRect()
        {
            return new Rect(
                Mathf.Max(0f, windowRect.width - ResizeHandleHitSize - 2f),
                Mathf.Max(0f, windowRect.height - ResizeHandleHitSize - 2f),
                ResizeHandleHitSize,
                ResizeHandleHitSize);
        }

        private void ClearResizeState()
        {
            resizing = false;
            resizeWidth = false;
            resizeHeight = false;
        }

        private void CenterModal(ref Rect rect)
        {
            float maxWidth = Mathf.Max(320f, scale.LogicalWidth - 20f);
            float maxHeight = Mathf.Max(240f, scale.LogicalHeight - 20f);
            rect.width = Mathf.Min(rect.width, maxWidth);
            rect.height = Mathf.Min(rect.height, maxHeight);
            if (rect.x <= 0f && rect.y <= 0f)
                rect.position = new Vector2((scale.LogicalWidth - rect.width) * 0.5f, (scale.LogicalHeight - rect.height) * 0.5f);
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, scale.LogicalWidth - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, scale.LogicalHeight - rect.height));
        }

        private void EnsureStyles()
        {
            theme.EnsureStyles();
            if (stylesInitialized && styleSourceSkin == GUI.skin)
                return;
            stylesInitialized = true;
            styleSourceSkin = GUI.skin;
            itemPrefabColumnWidthDirty = true;

            windowStyle = new GUIStyle(GUI.skin.window);
            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(7, 7, 7, 7),
                margin = new RectOffset(3, 3, 3, 3)
            };
            SetStyleBackground(panelStyle, theme.WindowTexture);
            fileStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(7, 7, 5, 5),
                margin = new RectOffset(1, 1, 1, 1)
            };
            selectedFileStyle = new GUIStyle(theme.AccentButtonStyle)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(7, 7, 5, 5),
                margin = new RectOffset(1, 1, 1, 1)
            };
            headerStyle = new GUIStyle(GUI.skin.box)
            {
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(4, 4, 1, 1),
                alignment = TextAnchor.MiddleLeft
            };
            toolbarStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(4, 4, 2, 2),
                margin = new RectOffset(2, 2, 1, 1)
            };
            rowStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(2, 2, 1, 1),
                margin = new RectOffset(1, 1, 1, 1),
                alignment = TextAnchor.MiddleLeft
            };
            itemRowStyle = new GUIStyle(rowStyle);
            SetStyleBackground(itemRowStyle, theme.WindowTexture);
            mutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(9, (configEditorFontSize?.Value ?? 13) - 1),
                wordWrap = true
            };
            mutedStyle.normal.textColor = new Color(0.7f, 0.73f, 0.76f);
            rightMutedStyle = new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleRight };
            centeredMutedStyle = new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            centeredMessageStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(8, 8, 4, 4) };
            singleLineMutedStyle = new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            errorStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(5, 5, 3, 3),
                wordWrap = true
            };
            errorStyle.normal.textColor = new Color(1f, 0.72f, 0.72f);
            itemPrefabTextFieldStyle = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleLeft };
            invalidItemIconStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            SetStyleTextColor(invalidItemIconStyle, new Color(1f, 0.22f, 0.18f, 1f));
            pickerPrefabStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Bold
            };
            SetStyleTextColor(pickerPrefabStyle, new Color(0.94f, 0.94f, 0.94f, 1f));
            pickerNameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            SetStyleTextColor(pickerNameStyle, new Color(0.88f, 0.9f, 0.92f, 1f));
            keyNameStyle = new GUIStyle(pickerPrefabStyle) { fontStyle = FontStyle.Normal };
            keySelectedNameStyle = new GUIStyle(keyNameStyle) { fontStyle = FontStyle.Bold };
            selectedValueLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            SetStyleTextColor(selectedValueLabelStyle, Color.white);
            selectedValueStyle = new GUIStyle(selectedValueLabelStyle) { fontStyle = FontStyle.Bold };
            serverSourceStyle = new GUIStyle(pickerPrefabStyle) { fontStyle = FontStyle.Bold };

            splitterStyle = new GUIStyle(theme.ResizeHandleStyle)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(2, 2, 3, 3),
                border = new RectOffset(0, 0, 0, 0)
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            fileNameStyle = new GUIStyle(titleStyle)
            {
                wordWrap = true,
                clipping = TextClipping.Clip,
                alignment = TextAnchor.UpperLeft
            };
            smallButtonStyle = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(5, 5, 2, 2),
                margin = new RectOffset(1, 1, 1, 1),
                alignment = TextAnchor.MiddleCenter
            };
            inlineButtonStyle = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(0, 0, 0, 1),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(8, (configEditorFontSize?.Value ?? 13) - 2)
            };
        }

        private static void SetStyleBackground(GUIStyle style, Texture2D texture)
        {
            if (style == null)
                return;
            style.normal.background = texture;
            style.hover.background = texture;
            style.active.background = texture;
            style.focused.background = texture;
            style.onNormal.background = texture;
            style.onHover.background = texture;
            style.onActive.background = texture;
            style.onFocused.background = texture;
        }

        private static void SetStyleTextColor(GUIStyle style, Color color)
        {
            if (style == null)
                return;
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private sealed class ItemRowValidation
        {
            internal string Prefab = string.Empty;
            internal string Stack = string.Empty;
            internal string Price = string.Empty;
            internal string Quality = string.Empty;
            internal string Currency = string.Empty;
            internal string RequiredGlobalKey = string.Empty;
            internal string BlockedGlobalKey = string.Empty;
            internal string RequiredPlayerKey = string.Empty;
            internal string BlockedPlayerKey = string.Empty;

            internal bool HasErrors => !string.IsNullOrEmpty(Combined);

            internal string Combined => string.Join(" ", new[]
            {
                Prefab, Stack, Price, Quality, Currency,
                RequiredGlobalKey, BlockedGlobalKey, RequiredPlayerKey, BlockedPlayerKey
            }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));
        }

        private sealed class ItemSortValueComparer : IComparer<object>
        {
            internal static readonly ItemSortValueComparer Instance = new ItemSortValueComparer();

            public int Compare(object left, object right)
            {
                if (ReferenceEquals(left, right))
                    return 0;
                if (left == null)
                    return -1;
                if (right == null)
                    return 1;
                if (left is int leftInt && right is int rightInt)
                    return leftInt.CompareTo(rightInt);
                return string.Compare(Convert.ToString(left, CultureInfo.CurrentCulture), Convert.ToString(right, CultureInfo.CurrentCulture), StringComparison.CurrentCultureIgnoreCase);
            }
        }

        private sealed class ItemOption
        {
            internal ItemOption(string prefab, string localizedName, Sprite icon, bool visibleByDefault)
            {
                Prefab = prefab;
                LocalizedName = localizedName;
                Icon = icon;
                VisibleByDefault = visibleByDefault;
            }

            internal string Prefab { get; }
            internal string LocalizedName { get; }
            internal Sprite Icon { get; }
            internal bool VisibleByDefault { get; }
        }

        private sealed class TraderOption
        {
            internal TraderOption(string prefab, string displayName)
            {
                Prefab = prefab;
                DisplayName = displayName;
            }

            internal string Prefab { get; }
            internal string DisplayName { get; }
        }
    }
}
