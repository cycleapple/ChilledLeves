using ChilledLeves.Scheduler;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChilledLeves.Ui
{
    // Class to represent a saved worklist for export/import
    public class SavedWorklist
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<LeveEntry> Entries { get; set; } = new List<LeveEntry>();
    }

    internal class WorkListUi : Window
    {
        #region .json stuff (Ice don't touch this)

        // Fields for the WorkListUi class
        private string _worklistSearchFilter = "";

        // Flag to focus the search input when set to true
        private bool _shouldFocusSearch = false;

        // Directory where worklists will be saved
        private static readonly string WorklistDirectory = Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "worklists");
        
        // List of available saved worklists
        private List<SavedWorklist> savedWorklists = new List<SavedWorklist>();
        
        // Input field for naming the worklist to export
        private string exportWorklistName = "";
        
        // Whether the import/export section is expanded
        private bool showImportExport = false;
        
        // Selected worklist index for import
        private int selectedWorklistIndex = -1;
        
        // Import confirmation modal state
        private bool _importConfirmationOpen = false;
        
        // Temporary variable to store the worklist to be imported
        private SavedWorklist _worklistToImport;

        // Flag to indicate if we're creating a new list or editing an imported one
        private bool _isNewList = true;

        // Track the currently loaded worklist file (for save functionality)
        private string _currentWorklistFile = string.Empty;

        // Clipboard import confirmation state
        private bool _clipboardImportConfirmationOpen = false;

        // Temporary clipboard imported worklist
        private SavedWorklist _clipboardWorklist = null;

        #endregion

        public WorkListUi() : 
            base($"Worklist [ChilledLeves] {P.GetType().Assembly.GetName().Version} ###WorkListChilledLeves")
        {
            Flags = ImGuiWindowFlags.None;
            SizeConstraints = new()
            {
                MinimumSize = new Vector2(400, 400),
                MaximumSize = new Vector2(2000, 2000),
            };
            P.windowSystem.AddWindow(this);
            AllowPinning = false;
            
            // Create the worklist directory if it doesn't exist
            if (!Directory.Exists(WorklistDirectory))
                Directory.CreateDirectory(WorklistDirectory);
                
            // Load saved worklists on initialization
            LoadSavedWorklists();
        }

        public void Dispose() { }

        public override void Draw()
        {
            // Check for Ice theme
            bool usingIceTheme = C.UseIceTheme;
            
            // Begin theming with improved style boundary
            int styleCount = ThemeHelper.BeginTheming(usingIceTheme);
            
            // Call WorklistMode to render the UI
            WorklistMode(this);
            
            // End theming with proper style restoration
            ThemeHelper.EndTheming(usingIceTheme, styleCount);
        }

        #region JSON Operations

        // Load all saved worklists from the worklists directory
        private void LoadSavedWorklists()
        {
            savedWorklists.Clear();

            if (!Directory.Exists(WorklistDirectory))
            {
                Directory.CreateDirectory(WorklistDirectory);
                return;
            }

            var files = Directory.GetFiles(WorklistDirectory, "*.json");

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var worklist = JsonSerializer.Deserialize<SavedWorklist>(json, options);
                    if (worklist != null)
                    {
                        savedWorklists.Add(worklist);
                    }
                }
                catch (Exception ex)
                {
                    // Handle deserialization errors
                    Svc.Log.Error($"Error loading worklist: {ex.Message}");
                }
            }

            // Sort worklists by creation date (newest first)
            savedWorklists = savedWorklists.OrderByDescending(w => w.CreatedAt).ToList();
        }

        // Export the current worklist to a JSON file
        private void ExportWorklist(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (C.workList.Count == 0)
                return;

            var worklist = new SavedWorklist
            {
                Name = name,
                CreatedAt = DateTime.Now,
                Entries = C.workList.ToList() // Create a copy of the current worklist
            };

            try
            {
                // Serialize worklist
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(worklist, options);

                // If we're editing an existing worklist and the name hasn't changed, overwrite it
                if (!_isNewList && !string.IsNullOrEmpty(_currentWorklistFile) &&
                    Path.GetFileNameWithoutExtension(_currentWorklistFile).StartsWith(name))
                {
                    File.WriteAllText(_currentWorklistFile, json);
                }
                else // Otherwise create a new file
                {
                    string filename = $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    string filePath = Path.Combine(WorklistDirectory, filename);

                    File.WriteAllText(filePath, json);

                    // Update tracking for the current file
                    _currentWorklistFile = filePath;
                    _isNewList = false;
                }

                // Reload the worklists to include the newly saved one
                LoadSavedWorklists();

                // Reset the export name only if it's a new list
                if (_isNewList)
                {
                    exportWorklistName = "";
                }
            }
            catch (Exception ex)
            {
                // Handle serialization/file writing errors
                Svc.Log.Error($"Error exporting worklist: {ex.Message}");
            }
        }

        // Helper method to find a worklist file based on name and creation date
        private string FindWorklistFile(SavedWorklist worklist)
        {
            if (worklist == null) return string.Empty;

            var files = Directory.GetFiles(WorklistDirectory, $"{worklist.Name}_*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var fileWorklist = JsonSerializer.Deserialize<SavedWorklist>(json, options);

                    if (fileWorklist != null &&
                        fileWorklist.Name == worklist.Name &&
                        fileWorklist.CreatedAt == worklist.CreatedAt)
                    {
                        return file;
                    }
                }
                catch
                {
                    // If we can't deserialize, just continue
                    continue;
                }
            }
            return string.Empty;
        }

        // Import a worklist from the saved list
        private void ImportWorklist(SavedWorklist worklist)
        {
            if (worklist == null || worklist.Entries == null)
                return;

            // Store the worklist to be imported temporarily
            _worklistToImport = worklist;

            // Ask for confirmation if there are items in the current worklist
            if (C.workList.Count > 0)
            {
                _importConfirmationOpen = true;
                ImGui.OpenPopup("Import Confirmation");
            }
            else
            {
                // If worklist is empty, just import directly
                ApplyImport(worklist);

                // Track that we're now editing an imported list
                _isNewList = false;

                // Find and set the current file for later save operations
                _currentWorklistFile = FindWorklistFile(worklist);
                if (!string.IsNullOrEmpty(_currentWorklistFile))
                {
                    exportWorklistName = worklist.Name;
                }
            }
        }

        // Apply the import after confirmation
        private void ApplyImport(SavedWorklist worklist)
        {
            if (worklist == null || worklist.Entries == null)
                return;

            // Replace the current worklist with the imported one
            C.workList.Clear();

            foreach (var entry in worklist.Entries)
            {
                // Ensure the leve exists in the dictionary before adding it
                if (LeveDictionary.ContainsKey(entry.LeveID))
                {
                    C.workList.Add(entry);
                }
            }

            // If this is from the clipboard import, update the name field for saving
            if (worklist == _clipboardWorklist)
            {
                exportWorklistName = worklist.Name;
                _isNewList = true; // This is technically a new list since it's from clipboard
                _currentWorklistFile = string.Empty;
                _clipboardWorklist = null;
            }
            else
            {
                // For saved worklists, set up proper editing state
                _isNewList = false;
                exportWorklistName = worklist.Name;
                _currentWorklistFile = FindWorklistFile(worklist);
            }

            C.Save();
        }

        // Import from clipboard
        private void ImportFromClipboard()
        {
            try
            {
                string clipboardText = ImGui.GetClipboardText();
                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    return;
                }

                // Define deserialization options
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var worklist = JsonSerializer.Deserialize<SavedWorklist>(clipboardText, options);
                if (worklist == null || worklist.Entries == null)
                {
                    return;
                }

                // Store the clipboard worklist temporarily
                _clipboardWorklist = worklist;

                // Ask for confirmation if there are items in the current worklist
                if (C.workList.Count > 0)
                {
                    _clipboardImportConfirmationOpen = true;
                    ImGui.OpenPopup("Clipboard Import Confirmation");
                }
                else
                {
                    // If worklist is empty, just import directly
                    ApplyImport(worklist);
                    _isNewList = true;
                    exportWorklistName = worklist.Name;
                }
            }
            catch (Exception ex)
            {
                // Handle deserialization errors
                Svc.Log.Error($"Error importing worklist from clipboard: {ex.Message}");
            }
        }

        // Delete a saved worklist
        private void DeleteWorklist(int index)
        {
            if (index < 0 || index >= savedWorklists.Count)
                return;

            var worklist = savedWorklists[index];

            try
            {
                // Find the file associated with this worklist
                string fileToDelete = FindWorklistFile(worklist);

                if (!string.IsNullOrEmpty(fileToDelete))
                {
                    File.Delete(fileToDelete);

                    // If we're currently editing this worklist, reset to new list mode
                    if (!_isNewList && _currentWorklistFile == fileToDelete)
                    {
                        _isNewList = true;
                        _currentWorklistFile = string.Empty;
                        exportWorklistName = string.Empty;
                    }
                }

                // Remove from our list and refresh
                LoadSavedWorklists();

                // Reset selection if needed
                if (selectedWorklistIndex >= savedWorklists.Count)
                {
                    selectedWorklistIndex = -1;
                }
            }
            catch (Exception ex)
            {
                // Handle file deletion errors
                Svc.Log.Error($"Error deleting worklist: {ex.Message}");
            }
        }

        // Open the worklists folder in file explorer
        private void OpenWorklistFolder()
        {
            try
            {
                // Ensure the directory exists before trying to open it
                if (!Directory.Exists(WorklistDirectory))
                {
                    Directory.CreateDirectory(WorklistDirectory);
                }

                // Launch the file explorer with the worklists directory
                Process.Start("explorer.exe", WorklistDirectory);
            }
            catch (Exception ex)
            {
                // Handle any errors that might occur when trying to open the folder
                Svc.Log.Error($"Error opening worklists folder: {ex.Message}");
            }
        }

        // Get filtered worklists based on search term
        private List<SavedWorklist> GetFilteredWorklists()
        {
            if (string.IsNullOrWhiteSpace(_worklistSearchFilter))
            {
                return savedWorklists;
            }

            string searchTerm = _worklistSearchFilter.ToLower();

            return savedWorklists
                .Where(w => w.Name.ToLower().Contains(searchTerm) ||
                            w.CreatedAt.ToString("yyyy-MM-dd HH:mm").ToLower().Contains(searchTerm))
                .ToList();
        }

        // Method to save worklists to file
        private void SaveWorklistsToFile()
        {
            try
            {
                // Serialize worklist with nice formatting
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(savedWorklists, options);
                
                string filePath = Path.Combine(WorklistDirectory, "saved_worklists.json");
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                // Handle serialization/file writing errors
                Svc.Log.Error($"Failed to save worklists to file: {ex.Message}");
            }
        }
        
        // Method to copy the current worklist as formatted JSON to clipboard
        private void CopyWorklistAsJson()
        {
            if (C.workList.Count == 0)
                return;
                
            try
            {
                // Create a temporary worklist with current data and nice name
                var currentTime = DateTime.Now;
                string defaultName = $"Worklist_{currentTime:yyyyMMdd}";
                
                var worklist = new SavedWorklist
                {
                    Name = !string.IsNullOrWhiteSpace(exportWorklistName) ? exportWorklistName : defaultName,
                    CreatedAt = currentTime,
                    Entries = C.workList.ToList() // Create a copy of the current worklist
                };
                
                // Serialize
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(worklist, options);
                
                // Copy to clipboard
                ImGui.SetClipboardText(json);
                
                // Log success message
                Svc.Log.Info("Worklist copied to clipboard as JSON");
            }
            catch (Exception ex)
            {
                // Handle serialization errors
                Svc.Log.Error($"Failed to copy worklist as JSON: {ex.Message}");
            }
        }

        // Method to copy the worklist in a format compatible with Artisan
        private void CopyToArtisan()
        {
            if (C.workList.Count == 0)
                return;
                
            string importString = "Items : \n";

            foreach (var kdp in LeveDictionary)
            {
                var leveID = kdp.Key;
                var jobType = kdp.Value.JobAssignmentType;

                var itemId = CraftDictionary[leveID].ItemID;
                var itemName = CraftDictionary[leveID].ItemName;
                var itemAmount = CraftDictionary[leveID].TurninAmount;

                if (!C.workList.Any(x => x.LeveID == leveID))
                {
                    continue;
                }
                if (!CraftFisherJobs.Contains(jobType))
                {
                    continue;
                }

                var WorklistInput = C.workList.Where(x => x.LeveID == leveID).FirstOrDefault();
                var InputAmount = WorklistInput.InputValue;
                var AmountNeeded = InputAmount * itemAmount;

                string temp = $"{AmountNeeded}x {itemName}\n";

                importString += temp;
            }

            ImGui.SetClipboardText($"{importString}");
            
            // Log success message
            Svc.Log.Info("Worklist copied to clipboard for Artisan");
        }

        #endregion

        #region UI Components and Drawing

        // Used for the navmesh button
        private static string buttonText = "需要安裝 vnavmesh";
        private static float lastClickTime = 0f;
        private const float resetDelay = 2.0f; // 2 seconds delay

        public static void WorklistMode(WorkListUi currentInstance)
        {
            #region Main Menu Buttons

            // Get font scaling metrics
            float textLineHeight = ImGui.GetTextLineHeight();
            float fontScale = ImGui.GetIO().FontGlobalScale;
            float scaledSpacing = ImGui.GetStyle().ItemSpacing.Y * fontScale;

            // Get theme setting (but don't re-apply window styles - those are set by Draw method)
            bool usingIceTheme = C.UseIceTheme;
            
            // Add navigation buttons to other windows at the top
            if (usingIceTheme)
            {
                int navigationBtnStyleCount = ThemeHelper.PushButtonStyle();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                
                float navButtonHeight = textLineHeight * 1.5f;
                float btnPadding = 8 * fontScale;
                float mainBtnWidth = ImGui.CalcTextSize("Main Window").X + btnPadding * 2;
                float gatherBtnWidth = ImGui.CalcTextSize("Priority Leve Grind Window").X + btnPadding * 2;
                
                // Left-aligned buttons
                ImGui.BeginGroup();

                // Start and Stop buttons 
                using (ImRaii.Disabled(SchedulerMain.AreWeTicking))
                {
                    if (!P.navmesh.Ready)
                    {
                        // Check if button is clicked
                        if (ImGui.Button(buttonText, new Vector2(gatherBtnWidth / 2, navButtonHeight)))
                        {
                            ImGui.SetClipboardText("https://puni.sh/api/repository/veyn");
                            buttonText = "已複製到剪貼簿";
                            lastClickTime = (float)ImGui.GetTime(); // Store the current ImGui time
                        }

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("按下按鈕將插件倉庫網址複製到剪貼簿");
                            ImGui.EndTooltip();
                        }

                        // Check if the delay has passed and revert the text
                        if (buttonText == "已複製到剪貼簿" && (ImGui.GetTime() - lastClickTime) >= resetDelay)
                        {
                            buttonText = "需要安裝 vnavmesh";
                        }
                    }
                    else if (P.navmesh.Ready)
                    {
                        if (ImGui.Button("開始###Start", new Vector2(gatherBtnWidth / 2, navButtonHeight)))
                        {
                            SchedulerMain.WorkListMode = true;
                            SchedulerMain.EnablePlugin();
                        }
                    }
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!SchedulerMain.AreWeTicking))
                {
                    if (ImGui.Button("停止###Stop", new Vector2(gatherBtnWidth / 2, navButtonHeight)))
                    {
                        SchedulerMain.DisablePlugin();
                    }
                }

                if (ImGui.Button("主視窗###Main Window", new Vector2(mainBtnWidth, navButtonHeight)))
                {
                    P.mainWindow.IsOpen = true;
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button("優先理符執行###Priority Leve Grind Window", new Vector2(gatherBtnWidth, navButtonHeight)))
                {
                    P.gatherModeUi.IsOpen = true;
                }

                ImGui.EndGroup();
                
                // Right-aligned allowances info
                float windowWidth = ImGui.GetWindowWidth();
                string allowancesInfo = $"理符額度：{Allowances}/100｜下次恢復：{NextAllowances:hh\\:mm\\:ss}";
                float infoWidth = ImGui.CalcTextSize(allowancesInfo).X;
                
                // Increased padding to prevent text from being cut off
                float extraPadding = 20 * fontScale;
                ImGui.SameLine(windowWidth - infoWidth - btnPadding - extraPadding);
                
                // Add ice theme styling for the text
                int textStyleCount = ThemeHelper.PushHeadingTextStyle();
                ImGui.Text(allowancesInfo);
                ImGui.PopStyleColor(textStyleCount);
                
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(navigationBtnStyleCount);
            }
            else
            {
                float navButtonHeight = textLineHeight * 1.5f;
                float btnPadding = 8 * fontScale;
                float mainBtnWidth = ImGui.CalcTextSize("Main Window").X + btnPadding * 2;
                float gatherBtnWidth = ImGui.CalcTextSize("Priority Leve Grind Window").X + btnPadding * 2;

                // Left-aligned buttons

                ImGui.BeginGroup();
                // Start and Stop buttons 
                using (ImRaii.Disabled(SchedulerMain.AreWeTicking))
                {
                    if (!P.navmesh.Ready)
                    {
                        // Check if button is clicked
                        if (ImGui.Button(buttonText, new Vector2(gatherBtnWidth / 2, navButtonHeight)))
                        {
                            ImGui.SetClipboardText("https://puni.sh/api/repository/veyn");
                            buttonText = "已複製到剪貼簿";
                            lastClickTime = (float)ImGui.GetTime(); // Store the current ImGui time
                        }

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("按下按鈕將插件倉庫網址複製到剪貼簿");
                            ImGui.EndTooltip();
                        }

                        // Check if the delay has passed and revert the text
                        if (buttonText == "已複製到剪貼簿" && (ImGui.GetTime() - lastClickTime) >= resetDelay)
                        {
                            buttonText = "需要安裝 vnavmesh";
                        }
                    }
                    else if (P.navmesh.Ready)
                    {
                        if (ImGui.Button("開始###Start", new Vector2(gatherBtnWidth / 2, navButtonHeight)))
                        {
                            SchedulerMain.WorkListMode = true;
                            SchedulerMain.EnablePlugin();
                        }
                    }
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!SchedulerMain.AreWeTicking))
                {
                    if (ImGui.Button("停止###Stop", new Vector2(gatherBtnWidth / 2, navButtonHeight)))
                    {
                        SchedulerMain.DisablePlugin();
                    }
                }
                if (ImGui.Button("主視窗###Main Window", new Vector2(mainBtnWidth, navButtonHeight)))
                {
                    P.mainWindow.IsOpen = true;
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button("優先理符執行###Priority Leve Grind Window", new Vector2(gatherBtnWidth, navButtonHeight)))
                {
                    P.gatherModeUi.IsOpen = true;
                }

                ImGui.EndGroup();
                
                // Right-aligned allowances info
                float windowWidth = ImGui.GetWindowWidth();
                string allowancesInfo = $"理符額度：{Allowances}/100｜下次恢復：{NextAllowances:hh\\:mm\\:ss}";
                float infoWidth = ImGui.CalcTextSize(allowancesInfo).X;
                
                // Increased padding to prevent text from being cut off
                float extraPadding = 20 * fontScale;
                ImGui.SameLine(windowWidth - infoWidth - btnPadding - extraPadding);
                ImGui.Text(allowancesInfo);
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.Text($"已接受的理符數量：{GetNumAcceptedLeveQuests()}");

            #endregion

            // Apply control styling for checkboxes via scope to ensure cleanup
            if (usingIceTheme)
            {
                int styleCount = ThemeHelper.PushControlStyle();
                ImGui.Checkbox("###ChilledLevesKeepList", ref SchedulerMain.KeepLeves);
                ImGui.PopStyleColor(styleCount);
            }
            else
            {
            ImGui.Checkbox("###ChilledLevesKeepList", ref SchedulerMain.KeepLeves);
            }
            
            ImGui.SameLine();
            ImGui.Text("完成後保留清單？");
            
            if (usingIceTheme)
            {
                int styleCount = ThemeHelper.PushControlStyle();
                ImGui.Checkbox("###Delay grabbing leves", ref C.IncreaseDelay);
                ImGui.PopStyleColor(styleCount);
            }
            else
            {
            ImGui.Checkbox("###Delay grabbing leves", ref C.IncreaseDelay);
            }
            
            ImGui.SameLine();
            ImGui.Text("增加接受各理符之間的延遲");
            
            if (usingIceTheme)
            {
                int styleCount = ThemeHelper.PushControlStyle();
                ImGui.Checkbox("###GrabMultiLeve", ref C.GrabMulti);
                ImGui.PopStyleColor(styleCount);
            }
            else
            {
            ImGui.Checkbox("###GrabMultiLeve", ref C.GrabMulti);
            }
            
            ImGui.SameLine();
            ImGui.Text("一次從理符發行人接受多個理符");

            if (usingIceTheme)
            {
                int styleCount = ThemeHelper.PushControlStyle();
                ImGui.Checkbox("###RepeatLastLeve", ref C.RepeatLastLeve);
                ImGui.PopStyleColor(styleCount);
            }
            else
            {
                ImGui.Checkbox("###RepeatLastLeve", ref C.RepeatLastLeve);
            }

            ImGui.SameLine();
            ImGui.Text("重複上一個理符");

            // Create a row of buttons for different actions
            float buttonSpacing = 10 * fontScale;
            float availWidth = ImGui.GetContentRegionAvail().X;
            float buttonHeight = textLineHeight * 1.5f;

            // Artisan export button
            string artisanPreCraft = "匯出 Artisan 預製作";
            var artisanPCbuttonSize = ImGui.CalcTextSize(artisanPreCraft);
            float artisanPCBtnWidth = artisanPCbuttonSize.X + ImGui.GetStyle().FramePadding.X * 2 * fontScale;

            // Artisan export button
            string artisanFinal = "匯出 Artisan 最終成品";
            var buttonSize = ImGui.CalcTextSize(artisanFinal);
            float artisanFinalBtnWidth = buttonSize.X + ImGui.GetStyle().FramePadding.X * 2 * fontScale;

            // Teamcraft List button
            string teamcraftList = "Teamcraft 連結";
            var tcbuttonSize = ImGui.CalcTextSize(teamcraftList);
            float tcBtnWidth = tcbuttonSize.X + ImGui.GetStyle().FramePadding.X * 2 * fontScale;

            // Import/Export button
            string importExport = "匯入／匯出工作清單";
            var ieBtnSize = ImGui.CalcTextSize(importExport);
            float ieBtnWidth = ieBtnSize.X + ImGui.GetStyle().FramePadding.X * 2 * fontScale;
            
            // Calculate button widths to fit the row
            float totalBtnWidth = artisanPCBtnWidth + artisanFinalBtnWidth + tcBtnWidth + ieBtnWidth + buttonSpacing;
            float leftPadding = (availWidth - totalBtnWidth) / 2; 
            
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + leftPadding);

            void ExportButtons()
            {
                // Artisan Pre-Crafts button
                if (ImGui.Button(artisanPreCraft, new Vector2(artisanPCBtnWidth, buttonHeight)))
                {
                    AllItems.Clear();
                    ArtisanPreCrafts.Clear();
                    ArtisanFinalCrafts.Clear();

                    ExportPreCrafts();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("將所需物品匯出為「預製作物品」清單。\n" +
                               "不包含最終成品");
                    ImGui.EndTooltip();
                }

                ImGui.SameLine(0, buttonSpacing);

                // Copy to Artisan button
                if (ImGui.Button(artisanFinal, new Vector2(artisanFinalBtnWidth, buttonHeight)))
                {
                    currentInstance.CopyToArtisan();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("將所需物品匯出為「最終成品」清單。\n" +
                               "不包含預製作物品");
                    ImGui.EndTooltip();
                }

                ImGui.SameLine(0, buttonSpacing);

                if (ImGui.Button(teamcraftList, new Vector2(tcBtnWidth, buttonHeight)))
                {
                    ExportSelectedListToTC();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("複製 Teamcraft 連結");
                    ImGui.EndTooltip();
                }

                ImGui.SameLine(0, buttonSpacing);

                // Import/Export toggle button
                if (ImGui.Button(importExport, new Vector2(ieBtnWidth, buttonHeight)))
                {
                    currentInstance.showImportExport = !currentInstance.showImportExport;

                    // Load saved worklists when opening the section
                    if (currentInstance.showImportExport)
                    {
                        currentInstance.LoadSavedWorklists();
                    }
                }
            }
            
            if (usingIceTheme)
            {
                int btnStyleCount = ThemeHelper.PushButtonStyle();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);

                ExportButtons();
                
                // Pop button styling
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(btnStyleCount);
            }
            else
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);

                ExportButtons();
                
                // Pop button styling
                ImGui.PopStyleVar();
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("使用 JSON 檔案儲存及載入工作清單");
                ImGui.EndTooltip();
            }
            
            // Import/Export section
            if (currentInstance.showImportExport)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Create tabbed interface for import/export functionality
                if (usingIceTheme)
                {
                    int tabStyleCount = ThemeHelper.PushHeaderStyle();
                    
                    if (ImGui.BeginTabBar("ImportExportTabs", ImGuiTabBarFlags.None))
                    {
                        // Save tab
                        if (ImGui.BeginTabItem("目前工作清單###Active Worklist"))
                        {
                            DrawSaveWorklistSection(currentInstance, textLineHeight, usingIceTheme);
                            ImGui.EndTabItem();
                        }
                        
                        // Load tab
                        if (ImGui.BeginTabItem("已儲存清單###Saved Worklists"))
                        {
                            DrawSavedWorklistsSection(currentInstance, textLineHeight, usingIceTheme);
                            ImGui.EndTabItem();
                        }
                        
                        // Import tab
                        if (ImGui.BeginTabItem("匯入###Import"))
                        {
                            DrawImportSection(currentInstance, textLineHeight, usingIceTheme);
                            ImGui.EndTabItem();
                        }
                        
                        ImGui.EndTabBar();
                    }
                    
                    ImGui.PopStyleColor(tabStyleCount);
                }
                else
                {
                    if (ImGui.BeginTabBar("ImportExportTabs", ImGuiTabBarFlags.None))
                    {
                        // Save tab
                        if (ImGui.BeginTabItem("目前工作清單###Active Worklist"))
                        {
                            DrawSaveWorklistSection(currentInstance, textLineHeight, usingIceTheme);
                            ImGui.EndTabItem();
                        }
                        
                        // Load tab
                        if (ImGui.BeginTabItem("已儲存清單###Saved Worklists"))
                        {
                            DrawSavedWorklistsSection(currentInstance, textLineHeight, usingIceTheme);
                            ImGui.EndTabItem();
                        }
                        
                        // Import tab
                        if (ImGui.BeginTabItem("匯入###Import"))
                        {
                            DrawImportSection(currentInstance, textLineHeight, usingIceTheme);
                            ImGui.EndTabItem();
                        }
                        
                        ImGui.EndTabBar();
                    }
                }
                
                ImGui.Spacing();
                ImGui.Separator();
            }

            // Confirmation popup for regular import
            bool importConfirmOpen = currentInstance._importConfirmationOpen;
            if (ImGui.BeginPopupModal("Import Confirmation", ref importConfirmOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar))
            {
                currentInstance._importConfirmationOpen = importConfirmOpen;
                
                ImGui.Text("這將取代目前的工作清單，確定要繼續嗎？");
                if (currentInstance._worklistToImport != null)
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.3f, 1.0f), 
                        $"準備匯入：{currentInstance._worklistToImport.Name}（{currentInstance._worklistToImport.Entries.Count} 個項目）");
                }
                ImGui.Separator();
                
                ImGui.Spacing();
                
                float popupWidth = ImGui.GetWindowWidth();
                float buttonWidth = (popupWidth - 30) / 2;
                
                if (usingIceTheme)
                {
                    int btnPopupStyleCount = ThemeHelper.PushButtonStyle();
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                    
                    if (ImGui.Button("取消###Cancel", new Vector2(buttonWidth, 0)))
                    {
                        currentInstance._worklistToImport = null;
                        currentInstance._importConfirmationOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    
                    ImGui.SameLine();
                    
                    if (ImGui.Button("匯入###Import", new Vector2(buttonWidth, 0)))
                    {
                        if (currentInstance._worklistToImport != null)
                        {
                            currentInstance.ApplyImport(currentInstance._worklistToImport);
                            currentInstance._worklistToImport = null;
                        }
                        currentInstance._importConfirmationOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(btnPopupStyleCount);
                }
                else
                {
                    if (ImGui.Button("取消###Cancel", new Vector2(buttonWidth, 0)))
                    {
                        currentInstance._worklistToImport = null;
                        currentInstance._importConfirmationOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    
                    ImGui.SameLine();
                    
                    if (ImGui.Button("匯入###Import", new Vector2(buttonWidth, 0)))
                    {
                        if (currentInstance._worklistToImport != null)
                        {
                            currentInstance.ApplyImport(currentInstance._worklistToImport);
                            currentInstance._worklistToImport = null;
                        }
                        currentInstance._importConfirmationOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                }
                
                ImGui.EndPopup();
            }
            else if (currentInstance._importConfirmationOpen)
            {
                // If the popup should be open but isn't showing, open it
                ImGui.OpenPopup("Import Confirmation");
            }

            // Confirmation popup for clipboard import
            bool clipboardImportConfirmOpen = currentInstance._clipboardImportConfirmationOpen;
            if (ImGui.BeginPopupModal("Clipboard Import Confirmation", ref clipboardImportConfirmOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar))
            {
                currentInstance._clipboardImportConfirmationOpen = clipboardImportConfirmOpen;
                
                ImGui.Text("這將以剪貼簿中的資料取代目前工作清單，要繼續嗎？");
                if (currentInstance._clipboardWorklist != null)
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.3f, 1.0f),
                        $"準備匯入：{currentInstance._clipboardWorklist.Name}（{currentInstance._clipboardWorklist.Entries.Count} 個項目）");
                }
                ImGui.Separator();

                ImGui.Spacing();

                float popupWidth = ImGui.GetWindowWidth();
                float buttonWidth = (popupWidth - 30) / 2;

                if (usingIceTheme)
                {
                    int btnPopupStyleCount = ThemeHelper.PushButtonStyle();
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);

                    if (ImGui.Button("取消###Cancel", new Vector2(buttonWidth, 0)))
                    {
                        currentInstance._clipboardWorklist = null;
                        currentInstance._clipboardImportConfirmationOpen = false;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("匯入###Import", new Vector2(buttonWidth, 0)))
                    {
                        if (currentInstance._clipboardWorklist != null)
                        {
                            currentInstance.ApplyImport(currentInstance._clipboardWorklist);
                        }
                        currentInstance._clipboardImportConfirmationOpen = false;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(btnPopupStyleCount);
                }
                else
                {
                    if (ImGui.Button("取消###Cancel", new Vector2(buttonWidth, 0)))
                    {
                        currentInstance._clipboardWorklist = null;
                        currentInstance._clipboardImportConfirmationOpen = false;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("匯入###Import", new Vector2(buttonWidth, 0)))
                    {
                        if (currentInstance._clipboardWorklist != null)
                        {
                            currentInstance.ApplyImport(currentInstance._clipboardWorklist);
                        }
                        currentInstance._clipboardImportConfirmationOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndPopup();
            }
            else if (currentInstance._clipboardImportConfirmationOpen)
            {
                ImGui.OpenPopup("Clipboard Import Confirmation");
            }
            
            ImGui.Spacing();
            ImGui.Spacing();
            
            // Worklist table
            // Calculate column widths based on font metrics
            float baseColumnWidth = textLineHeight * 3;
            float col0Width = Math.Max(50, baseColumnWidth);  // Level column
            float col1Width = Math.Max(200, textLineHeight * 12);  // Leve name
            float col2Width = Math.Max(75, textLineHeight * 5);  // Run amount
            float col3Width = Math.Max(200, textLineHeight * 12);  // Item turnin
            float col4Width = Math.Max(50, baseColumnWidth);  // Need
            float col5Width = Math.Max(50, baseColumnWidth);  // Have
            float col6Width = Math.Max(70, textLineHeight * 4);  // Remove

            // Begin the worklist table with themed headers
            if (usingIceTheme)
            {
                // Apply child background
                int childStyleCount = ThemeHelper.PushChildStyle();
                
                // Apply table headers styling
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, ThemeHelper.DeepIceBlue);
                int headerStyleCount = ThemeHelper.PushHeaderStyle();

            if (ImGui.BeginTable($"Workshop List", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Reorderable))
            {
                ImGui.TableSetupColumn("等級###CrafterLevels", ImGuiTableColumnFlags.WidthFixed, col0Width);
                ImGui.TableSetupColumn("理符名稱###CrafterLeveNames", ImGuiTableColumnFlags.WidthFixed, col1Width);
                ImGui.TableSetupColumn("執行次數###CrafterRunAmounts", ImGuiTableColumnFlags.WidthFixed, col2Width);
                ImGui.TableSetupColumn("繳交物品###CrafterTurninItems", ImGuiTableColumnFlags.WidthFixed, col3Width);
                ImGui.TableSetupColumn("需要###CrafterAmountNecessary", ImGuiTableColumnFlags.WidthFixed, col4Width);
                    ImGui.TableSetupColumn("持有###CrafterCompleteCheck", ImGuiTableColumnFlags.WidthFixed, col5Width);
                    ImGui.TableSetupColumn("移除###CrafterLevesRemoveWorkList", ImGuiTableColumnFlags.WidthFixed, col6Width);

                ImGui.TableHeadersRow();

                    ImGui.PopStyleColor(headerStyleCount + 1);
                    
                    DrawWorklistRows(textLineHeight, fontScale, usingIceTheme);
                    
                    ImGui.EndTable();
                }
                
                ImGui.PopStyleColor(childStyleCount);
            }
            else
            {
                if (ImGui.BeginTable($"Workshop List", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Reorderable))
                {
                    ImGui.TableSetupColumn("等級###CrafterLevels", ImGuiTableColumnFlags.WidthFixed, col0Width);
                    ImGui.TableSetupColumn("理符名稱###CrafterLeveNames", ImGuiTableColumnFlags.WidthFixed, col1Width);
                    ImGui.TableSetupColumn("執行次數###CrafterRunAmounts", ImGuiTableColumnFlags.WidthFixed, col2Width);
                    ImGui.TableSetupColumn("繳交物品###CrafterTurninItems", ImGuiTableColumnFlags.WidthFixed, col3Width);
                    ImGui.TableSetupColumn("需要###CrafterAmountNecessary", ImGuiTableColumnFlags.WidthFixed, col4Width);
                    ImGui.TableSetupColumn("持有###CrafterCompleteCheck", ImGuiTableColumnFlags.WidthFixed, col5Width);
                    ImGui.TableSetupColumn("移除###CrafterLevesRemoveWorkList", ImGuiTableColumnFlags.WidthFixed, col6Width);
                    
                    ImGui.TableHeadersRow();
                    
                    DrawWorklistRows(textLineHeight, fontScale, usingIceTheme);
                    
                    ImGui.EndTable();
                }
            }
        }

        private static void DrawWorklistRows(float textLineHeight, float fontScale, bool usingIceTheme)
        {
            foreach (var kdp in LeveDictionary)
            {
                var leveID = kdp.Key;
                var leveLevel = kdp.Value.Level;
                var leveName = kdp.Value.LeveName;
                var jobAssignment = kdp.Value.JobAssignmentType;
                var jobIcon = LeveTypeDict[jobAssignment].AssignmentIcon;

                if (!C.workList.Any(x => x.LeveID == leveID))
                {
                    continue;
                }

                ImGui.TableNextRow();

                // Column 0 | Level Column
                ImGui.PushID((int)leveID);
                ImGui.TableSetColumnIndex(0);
                CenterText($"{leveLevel}");

                // Column 1 | JobIcon + Leve Name
                ImGui.TableNextColumn();
                // Scale icon size based on text line height
                float iconSize = Math.Max(25, textLineHeight * 1.5f);
                ImGui.Image(jobIcon.GetWrapOrEmpty().Handle, new Vector2(iconSize, iconSize));
                ImGui.SameLine(0, 5 * fontScale);
                ImGui.AlignTextToFramePadding();
                CenterTextInHeight($"{leveName}");

                // Column 2 | Amount to Run
                ImGui.TableNextColumn();
                var WorklistInput = C.workList.Where(x => x.LeveID == leveID).FirstOrDefault();
                var input = WorklistInput.InputValue;

                float availableWidth = ImGui.GetContentRegionAvail().X;

                // Apply slider styling for ice theme
                if (usingIceTheme)
                {
                    int sliderStyleCount = ThemeHelper.PushControlStyle();
                    ImGui.PushStyleColor(ImGuiCol.SliderGrab, ThemeHelper.IceBlue);
                    ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ThemeHelper.FrostWhite);

                    ImGui.SetNextItemWidth(availableWidth);
                    ImGui.SliderInt("###RunAmountSlider", ref input, 1, 100);

                    ImGui.PopStyleColor(sliderStyleCount + 2);
                }
                else
                {
                    ImGui.SetNextItemWidth(availableWidth);
                    ImGui.SliderInt("###RunAmountSlider", ref input, 1, 100);
                }

                if (input < 1)
                    input = 1;
                else if (input > 100)
                    input = 100;
                if (WorklistInput.InputValue != input)
                {
                    WorklistInput.InputValue = input;
                    C.Save();
                }

                if (CraftFisherJobs.Contains(jobAssignment))
                {
                    var ItemImage = CraftDictionary[leveID].ItemIcon.GetWrapOrEmpty();
                    var itemName = CraftDictionary[leveID].ItemName;
                    var itemNeed = CraftDictionary[leveID].TurninAmount;
                    var itemId = CraftDictionary[leveID].ItemID;

                    // Column 3 | Item Turnin
                    ImGui.TableNextColumn();
                    ImGui.Image(ItemImage.Handle, new Vector2(iconSize, iconSize));
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Image(ItemImage.Handle, new Vector2(iconSize * 2, iconSize * 2));
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine(0, 5 * fontScale);
                    ImGui.AlignTextToFramePadding();
                    CenterTextInHeight($"{itemName}");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    {
                        ImGui.SetClipboardText(itemName);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("按滑鼠左鍵將物品名稱複製到剪貼簿");
                        ImGui.EndTooltip();
                    }

                    // Column 4 | Need
                    ImGui.TableNextColumn();
                    int needAmount = WorklistInput.InputValue * itemNeed;
                    if (needAmount < 0)
                        needAmount = 0;
                    CenterText(needAmount.ToString());

                    // Column 5 | Have
                    ImGui.TableNextColumn();
                    var haveAmount = GetItemCount((int)itemId);
                    var isEnough = (haveAmount >= needAmount);

                    FancyCheckmark(isEnough);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"持有：{haveAmount}");
                        ImGui.EndTooltip();
                    }
                }

                // Column 6 | Remove Button
                ImGui.TableSetColumnIndex(6);

                if (usingIceTheme)
                {
                    int btnRemoveStyleCount = ThemeHelper.PushButtonStyle();
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);

                    if (ImGuiEx.IconButton(FontAwesomeIcon.Trash, "Remove From LeveList"))
                    {
                        C.workList.Remove(C.workList.Where(x => x.LeveID == leveID).FirstOrDefault());
                        C.Save();
                    }

                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(btnRemoveStyleCount);
                }
                else
                {
                    if (ImGuiEx.IconButton(FontAwesomeIcon.Trash, "Remove From LeveList"))
                    {
                        C.workList.Remove(C.workList.Where(x => x.LeveID == leveID).FirstOrDefault());
                        C.Save();
                    }
                }

                ImGui.PopID();
            }
        }

        private static void CenterText(string text)
        {
            float columnWidth = ImGui.GetColumnWidth(); 
            float textWidth = ImGui.CalcTextSize(text).X;

            float rowHeight = ImGui.GetTextLineHeightWithSpacing(); 
            float textHeight = ImGui.CalcTextSize(text).Y;

            float cursorX = ImGui.GetCursorPosX() + (columnWidth - textWidth) * 0.5f;
            float cursorY = ImGui.GetCursorPosY() + (rowHeight - textHeight) * 0.5f;

            cursorX = Math.Max(cursorX, ImGui.GetCursorPosX()); 
            cursorY = Math.Max(cursorY, ImGui.GetCursorPosY());

            ImGui.SetCursorPos(new Vector2(cursorX, cursorY));
            ImGui.Text(text);
        }

        private static void CenterTextInHeight(string text)
        {
            float rowHeight = ImGui.GetTextLineHeightWithSpacing(); 
            float textHeight = ImGui.CalcTextSize(text).Y;

            float cursorY = ImGui.GetCursorPosY() + (rowHeight - textHeight) * 0.5f;
            cursorY = Math.Max(cursorY, ImGui.GetCursorPosY()); 

            ImGui.SetCursorPosY(cursorY);
            ImGui.Text(text);
        }

        // Draw filtered worklist rows for the table
        private static void DrawFilteredWorklistTableRows(WorkListUi currentInstance, float textLineHeight, bool usingIceTheme)
        {
            var filteredList = currentInstance.GetFilteredWorklists();

            for (int i = 0; i < filteredList.Count; i++)
            {
                var worklist = filteredList[i];
                int originalIndex = currentInstance.savedWorklists.IndexOf(worklist);

                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                bool isSelected = currentInstance.selectedWorklistIndex == originalIndex;

                if (isSelected && usingIceTheme)
                {
                    int headerSelectStyleCount = ThemeHelper.PushHeaderStyle();
                    if (ImGui.Selectable($"{worklist.Name}###wl{originalIndex}", isSelected))
                    {
                        currentInstance.selectedWorklistIndex = originalIndex;
                    }
                    ImGui.PopStyleColor(headerSelectStyleCount);
                }
                else
                {
                    if (ImGui.Selectable($"{worklist.Name}###wl{originalIndex}", isSelected))
                    {
                        currentInstance.selectedWorklistIndex = originalIndex;
                    }
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.Text(worklist.CreatedAt.ToString("yyyy-MM-dd HH:mm"));

                ImGui.TableSetColumnIndex(2);
                ImGui.PushID(originalIndex);

                float columnWidth = ImGui.GetColumnWidth();
                float buttonSpacing = 8 * ImGui.GetIO().FontGlobalScale;
                float buttonWidth = (columnWidth - buttonSpacing) / 2 - 4;
                
                float minButtonWidth = Math.Max(60, ImGui.CalcTextSize("Import").X + 16);
                buttonWidth = Math.Max(buttonWidth, minButtonWidth);

                if (usingIceTheme)
                {
                    int btnImportStyleCount = ThemeHelper.PushButtonStyle();
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.5f, 0.7f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.6f, 0.8f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.7f, 0.9f, 1.0f));

                    if (ImGui.Button("匯入###Import", new Vector2(buttonWidth, textLineHeight * 1.2f)))
                    {
                        currentInstance.selectedWorklistIndex = originalIndex;
                        currentInstance.ImportWorklist(worklist);
                    }

                    ImGui.PopStyleColor(3);
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(btnImportStyleCount);

                    ImGui.SameLine(0, buttonSpacing);

                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.1f, 0.1f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));

                    if (ImGui.Button("刪除###Delete", new Vector2(buttonWidth, textLineHeight * 1.2f)))
                    {
                        currentInstance.DeleteWorklist(originalIndex);
                    }

                    ImGui.PopStyleColor(3);
                    ImGui.PopStyleVar();
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.5f, 0.7f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.6f, 0.8f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.7f, 0.9f, 1.0f));

                    if (ImGui.Button("匯入###Import", new Vector2(buttonWidth, textLineHeight * 1.2f)))
                    {
                        currentInstance.selectedWorklistIndex = originalIndex;
                        currentInstance.ImportWorklist(worklist);
                    }

                    ImGui.PopStyleColor(3);

                    ImGui.SameLine(0, buttonSpacing);

                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.1f, 0.1f, 0.8f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));

                    if (ImGui.Button("刪除###Delete", new Vector2(buttonWidth, textLineHeight * 1.2f)))
                    {
                        currentInstance.DeleteWorklist(originalIndex);
                    }

                    ImGui.PopStyleColor(3);
                }

                ImGui.PopID();
            }
        }

        // Helper method to draw the Save Worklist tab content
        private static void DrawSaveWorklistSection(WorkListUi currentInstance, float textLineHeight, bool usingIceTheme)
        {
            if (usingIceTheme)
            {
                int textStyleCount = ThemeHelper.PushHeadingTextStyle();
                if (currentInstance._isNewList)
                {
                    ImGui.Text("目前工作清單");
                }
                else
                {
                    ImGui.Text("編輯目前工作清單");
                }
                ImGui.PopStyleColor(textStyleCount);
            }
            else
            {
                if (currentInstance._isNewList)
                {
                    ImGui.Text("目前工作清單");
                }
                else
                {
                    ImGui.Text("編輯目前工作清單");
                }
            }
            
            ImGui.Separator();
            ImGui.Spacing();
            
            // Add indicator for current loaded worklist
            if (!currentInstance._isNewList && !string.IsNullOrEmpty(currentInstance._currentWorklistFile))
            {
                ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1.0f), 
                    $"使用中的工作清單：{currentInstance.exportWorklistName}");
                ImGui.Spacing();
            }
            
            ImGui.AlignTextToFramePadding();
            ImGui.Text("工作清單名稱：");
            ImGui.SameLine();
            
            float inputWidth = ImGui.GetContentRegionAvail().X;
            if (!currentInstance._isNewList)
            {
                inputWidth -= 60; 
            }
            
            if (usingIceTheme)
            {
                int controlStyleCount = ThemeHelper.PushControlStyle();
                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputText("###WorklistNameInput", ref currentInstance.exportWorklistName, 50);
                ImGui.PopStyleColor(controlStyleCount);
            }
            else
            {
                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputText("###WorklistNameInput", ref currentInstance.exportWorklistName, 50);
            }
            
            // Add clear button for imported worklists
            if (!currentInstance._isNewList)
            {
                ImGui.SameLine();
                if (ImGui.Button("清除###Clear", new Vector2(50, 0)))
                {
                    currentInstance._isNewList = true;
                    currentInstance._currentWorklistFile = string.Empty;
                    currentInstance.exportWorklistName = string.Empty;
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("清除目前工作清單並建立新清單");
                    ImGui.EndTooltip();
                }
            }
            
            ImGui.Spacing();
            
            bool canExport = !string.IsNullOrWhiteSpace(currentInstance.exportWorklistName) && C.workList.Count > 0;
            string buttonLabel = currentInstance._isNewList ? "儲存新工作清單###Save New Worklist" : "儲存變更###Save Changes";
            
            float buttonWidth = Math.Min(ImGui.GetContentRegionAvail().X * 0.7f, 250);
            float buttonHeight = textLineHeight * 1.2f;
            
            float availWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - buttonWidth) * 0.5f);
            
            if (usingIceTheme)
            {
                int btnExportStyleCount = ThemeHelper.PushButtonStyle();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                
                using (ImRaii.Disabled(!canExport))
                {
                    if (ImGui.Button(buttonLabel, new Vector2(buttonWidth, buttonHeight)))
                    {
                        currentInstance.ExportWorklist(currentInstance.exportWorklistName);
                    }
                }
                
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(btnExportStyleCount);
            }
            else
            {
                using (ImRaii.Disabled(!canExport))
                {
                    if (ImGui.Button(buttonLabel, new Vector2(buttonWidth, buttonHeight)))
                    {
                        currentInstance.ExportWorklist(currentInstance.exportWorklistName);
                    }
                }
            }
            
            if (ImGui.IsItemHovered() && !canExport)
            {
                ImGui.BeginTooltip();
                if (C.workList.Count == 0)
                {
                    ImGui.Text("工作清單是空的，請先加入項目再儲存。");
                }
                else
                {
                    ImGui.Text("請輸入要儲存的工作清單名稱");
                }
                ImGui.EndTooltip();
            }
            
            if (!currentInstance._isNewList)
            {
                ImGui.Spacing();
                ImGui.Spacing();
                
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - buttonWidth) * 0.5f);
                
                if (usingIceTheme)
                {
                    int btnNewListStyleCount = ThemeHelper.PushButtonStyle();
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                    
                    if (ImGui.Button("建立新清單###Create New List", new Vector2(buttonWidth, buttonHeight)))
                    {
                        currentInstance._isNewList = true;
                        currentInstance._currentWorklistFile = string.Empty;
                        currentInstance.exportWorklistName = string.Empty;
                    }
                    
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(btnNewListStyleCount);
                }
                else
                {
                    if (ImGui.Button("建立新清單###Create New List", new Vector2(buttonWidth, buttonHeight)))
                    {
                        currentInstance._isNewList = true;
                        currentInstance._currentWorklistFile = string.Empty;
                        currentInstance.exportWorklistName = string.Empty;
                    }
                }
            }
        }
        
        // Helper method to draw the Import tab content
        private static void DrawImportSection(WorkListUi currentInstance, float textLineHeight, bool usingIceTheme)
        {
            if (usingIceTheme)
            {
                int textStyleCount = ThemeHelper.PushHeadingTextStyle();
                ImGui.Text("從剪貼簿匯入工作清單");
                ImGui.PopStyleColor(textStyleCount);
            }
            else
            {
                ImGui.Text("從剪貼簿匯入工作清單");
            }
            
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.TextWrapped("將 JSON 格式的工作清單複製到剪貼簿，再點擊下方按鈕匯入。");
            ImGui.Spacing();
            
            float buttonWidth = Math.Min(ImGui.GetContentRegionAvail().X * 0.7f, 250);
            float buttonHeight = textLineHeight * 1.2f;
            
            float availWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - buttonWidth) * 0.5f);
            
            if (usingIceTheme)
            {
                int btnClipboardStyleCount = ThemeHelper.PushButtonStyle();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                
                if (ImGui.Button("從剪貼簿匯入###Import from Clipboard", new Vector2(buttonWidth, buttonHeight)))
                {
                    currentInstance.ImportFromClipboard();
                }
                
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(btnClipboardStyleCount);
            }
            else
            {
                if (ImGui.Button("從剪貼簿匯入###Import from Clipboard", new Vector2(buttonWidth, buttonHeight)))
                {
                    currentInstance.ImportFromClipboard();
                }
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("從剪貼簿匯入 JSON 格式的工作清單");
                ImGui.EndTooltip();
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            if (usingIceTheme)
            {
                int textStyleCount = ThemeHelper.PushHeadingTextStyle();
                ImGui.Text("將目前工作清單匯出至剪貼簿");
                ImGui.PopStyleColor(textStyleCount);
            }
            else
            {
                ImGui.Text("將目前工作清單匯出至剪貼簿");
            }
            
            ImGui.Spacing();
            
            bool hasItems = C.workList.Count > 0;
            
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - buttonWidth) * 0.5f);
            
            using (ImRaii.Disabled(!hasItems))
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.5f, 0.7f, hasItems ? 0.8f : 0.4f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.6f, 0.8f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.7f, 0.9f, 1.0f));
                
                if (ImGui.Button("將工作清單複製為 JSON###Copy Worklist as JSON", new Vector2(buttonWidth, buttonHeight)))
                {
                    if (hasItems)
                    {
                        currentInstance.CopyWorklistAsJson();
                    }
                }
                
                ImGui.PopStyleColor(3);
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (hasItems)
                {
                    ImGui.Text("複製為格式化 JSON，以便與其他人分享");
                }
                else
                {
                    ImGui.Text("請先將項目加入工作清單");
                }
                ImGui.EndTooltip();
            }
        }
        
        // Helper method to draw the Saved Worklists tab content
        private static void DrawSavedWorklistsSection(WorkListUi currentInstance, float textLineHeight, bool usingIceTheme)
        {
            if (usingIceTheme)
            {
                int textStyleCount = ThemeHelper.PushHeadingTextStyle();
                ImGui.Text("已儲存的工作清單");
                ImGui.PopStyleColor(textStyleCount);
            }
            else
            {
                ImGui.Text("已儲存的工作清單");
            }
            
            float folderButtonWidth = ImGui.CalcTextSize("Open Folder").X + 20;
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - folderButtonWidth);
            
            if (usingIceTheme)
            {
                int btnFolderStyleCount = ThemeHelper.PushButtonStyle();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
                
                if (ImGui.Button("開啟資料夾###Open Folder", new Vector2(folderButtonWidth, 0)))
                {
                    currentInstance.OpenWorklistFolder();
                }
                
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(btnFolderStyleCount);
            }
            else
            {
                if (ImGui.Button("開啟資料夾###Open Folder", new Vector2(folderButtonWidth, 0)))
                {
                    currentInstance.OpenWorklistFolder();
                }
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("在檔案總管中開啟工作清單資料夾");
                ImGui.EndTooltip();
            }
            
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("搜尋：");
            ImGui.SameLine();
            
            if (usingIceTheme)
            {
                int controlStyleCount = ThemeHelper.PushControlStyle();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("###WorklistSearchInput", ref currentInstance._worklistSearchFilter, 50);
                ImGui.PopStyleColor(controlStyleCount);
            }
            else
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("###WorklistSearchInput", ref currentInstance._worklistSearchFilter, 50);
            }
            
            ImGui.Spacing();
            
            int totalLists = currentInstance.savedWorklists.Count;
            int visibleLists = currentInstance.GetFilteredWorklists().Count;
            
            if (currentInstance.savedWorklists.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("找不到已儲存的工作清單");
            }
            else
            {
                ImGui.TextDisabled($"顯示 {visibleLists}/{totalLists} 個工作清單");
                ImGui.Spacing();
                
                float remainingSpace = ImGui.GetContentRegionAvail().Y * 0.95f;
                
                if (usingIceTheme)
                {
                    ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, ThemeHelper.DeepIceBlue);
                    int headerStyleCount = ThemeHelper.PushHeaderStyle();
                    
                    if (ImGui.BeginTable("###SavedWorklistsTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY))
                    {
                        float nameColWidth = Math.Max(140, textLineHeight * 10);
                        float dateColWidth = Math.Max(120, textLineHeight * 7);
                        float actionColWidth = Math.Max(170, textLineHeight * 10);
                        
                        ImGui.TableSetupColumn("名稱###Name", ImGuiTableColumnFlags.WidthFixed, nameColWidth);
                        ImGui.TableSetupColumn("建立時間###Created", ImGuiTableColumnFlags.WidthFixed, dateColWidth);
                        ImGui.TableSetupColumn("操作###Actions", ImGuiTableColumnFlags.WidthFixed, actionColWidth);
                        
                        ImGui.TableHeadersRow();
                        
                        ImGui.PopStyleColor(headerStyleCount + 1);
                        
                        DrawFilteredWorklistTableRows(currentInstance, textLineHeight, usingIceTheme);
                        
                        ImGui.EndTable();
                    }
                }
                else
                {
                    if (ImGui.BeginTable("###SavedWorklistsTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY))
                    {
                        float nameColWidth = Math.Max(140, textLineHeight * 10);
                        float dateColWidth = Math.Max(120, textLineHeight * 7);
                        float actionColWidth = Math.Max(170, textLineHeight * 10);
                        
                        ImGui.TableSetupColumn("名稱###Name", ImGuiTableColumnFlags.WidthFixed, nameColWidth);
                        ImGui.TableSetupColumn("建立時間###Created", ImGuiTableColumnFlags.WidthFixed, dateColWidth);
                        ImGui.TableSetupColumn("操作###Actions", ImGuiTableColumnFlags.WidthFixed, actionColWidth);
                        
                        ImGui.TableHeadersRow();
                        
                        DrawFilteredWorklistTableRows(currentInstance, textLineHeight, usingIceTheme);
                        
                        ImGui.EndTable();
                    }
                }
            }
        }

        #endregion
    }
}
