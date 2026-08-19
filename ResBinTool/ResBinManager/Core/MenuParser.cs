using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ResBinManager.Core
{
    public class MenuParser
    {
        public class MenuOptionInfo
        {
            public string MenuName { get; set; } = string.Empty;
            public List<string> Options { get; set; } = new List<string>();
            public bool IsCommented { get; set; }
        }

        public class ParseResult
        {
            public List<MenuOptionInfo> MenuOptions { get; set; } = new List<MenuOptionInfo>();
            public Dictionary<string, bool> EnabledConfigIds { get; set; } = new Dictionary<string, bool>();
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
        }

        public ParseResult Parse(string menuFilePath)
        {
            var result = new ParseResult();

            try
            {
                if (!File.Exists(menuFilePath))
                {
                    result.ErrorMessage = $"文件不存在: {menuFilePath}";
                    return result;
                }

                string content = File.ReadAllText(menuFilePath);
                result.MenuOptions = ExtractMenuOptions(content);
                result.EnabledConfigIds = ExtractEnabledConfigIds(content);
                result.Success = result.MenuOptions.Count > 0 || result.EnabledConfigIds.Count > 0;

                if (!result.Success)
                {
                    result.ErrorMessage = "未找到任何菜单选项";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MenuParser] Parsed {result.MenuOptions.Count} menu options and {result.EnabledConfigIds.Count} config item statuses from {menuFilePath}");
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"解析失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[MenuParser] Error: {ex.Message}");
            }

            return result;
        }

        private List<MenuOptionInfo> ExtractMenuOptions(string content)
        {
            var menuOptions = new List<MenuOptionInfo>();

            var startPattern = new Regex(@"MENU_OPTION_START\((\w+)\)");
            var optionPattern = new Regex(@"MENU_OPTION_STR\((R_ID_STR_\w+)\)");
            var endPattern = new Regex(@"MENU_OPTION_END\(\)");

            var lines = content.Split('\n');

            MenuOptionInfo currentMenu = null;
            bool inCommentBlock = false;

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();

                int startComment = trimmedLine.IndexOf("/*");
                int endComment = trimmedLine.IndexOf("*/");
                
                if (startComment >= 0 && !inCommentBlock)
                    inCommentBlock = true;
                if (endComment >= 0 && inCommentBlock)
                    inCommentBlock = false;

                bool lineCommented = trimmedLine.StartsWith("//") || inCommentBlock;

                var startMatch = startPattern.Match(trimmedLine);
                if (startMatch.Success)
                {
                    currentMenu = new MenuOptionInfo
                    {
                        MenuName = startMatch.Groups[1].Value,
                        IsCommented = lineCommented
                    };
                    menuOptions.Add(currentMenu);
                    continue;
                }

                var optionMatch = optionPattern.Match(trimmedLine);
                if (optionMatch.Success && currentMenu != null)
                {
                    string option = optionMatch.Groups[1].Value;
                    bool isOptionCommented = trimmedLine.StartsWith("//") || inCommentBlock;
                    
                    if (!isOptionCommented)
                    {
                        currentMenu.Options.Add(option);
                    }
                    continue;
                }

                var endMatch = endPattern.Match(trimmedLine);
                if (endMatch.Success)
                {
                    currentMenu = null;
                    continue;
                }
            }

            return menuOptions;
        }

        private Dictionary<string, bool> ExtractEnabledConfigIds(string content)
        {
            var enabledConfigIds = new Dictionary<string, bool>();

            var menuStartPattern = new Regex(@"MENU_ITME_START\((\w+)\)");
            var menuEndPattern = new Regex(@"MENU_ITME_END\(\)");
            var menuItemOptionsPattern = new Regex(@"^\s*MENU_ITEM_OPTIONS\((\w+)\s*,\s*(CONFIG_ID_\w+)");

            var lines = content.Split('\n');

            bool inMenuBlock = false;
            bool inCommentBlock = false;

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();

                int startComment = trimmedLine.IndexOf("/*");
                int endComment = trimmedLine.IndexOf("*/");

                if (startComment >= 0 && !inCommentBlock)
                    inCommentBlock = true;
                if (endComment >= 0 && inCommentBlock)
                    inCommentBlock = false;

                bool isLineCommented = trimmedLine.StartsWith("//") || inCommentBlock;

                if (!inMenuBlock)
                {
                    var startMatch = menuStartPattern.Match(trimmedLine);
                    if (startMatch.Success)
                    {
                        inMenuBlock = true;
                    }
                    continue;
                }

                var endMatch = menuEndPattern.Match(trimmedLine);
                if (endMatch.Success)
                {
                    inMenuBlock = false;
                    continue;
                }

                var optionsMatch = menuItemOptionsPattern.Match(trimmedLine);
                if (optionsMatch.Success)
                {
                    string configId = optionsMatch.Groups[2].Value;
                    enabledConfigIds[configId] = !isLineCommented;
                }
            }

            return enabledConfigIds;
        }

        public static string? FindMenuFile(string projectPath)
        {
            string[] searchPaths = new[]
            {
                Path.Combine(projectPath, "menuMovieRec.c"),
                Path.Combine(projectPath, "menu_setting.c"),
                Path.Combine(projectPath, "src", "menuMovieRec.c"),
                Path.Combine(projectPath, "src", "menu_setting.c"),
                Path.Combine(projectPath, "firmware", "menuMovieRec.c"),
                Path.Combine(projectPath, "firmware", "menu_setting.c"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            try
            {
                var files = Directory.GetFiles(projectPath, "*menu*.c", SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MenuParser] Error searching menu file: {ex.Message}");
            }

            return null;
        }

        public Dictionary<string, List<uint>> GetMenuOptionValues(List<MenuOptionInfo> menuOptions)
        {
            var result = new Dictionary<string, List<uint>>();

            foreach (var menu in menuOptions)
            {
                var values = new List<uint>();
                foreach (var option in menu.Options)
                {
                    uint value = ResolveMenuOptionValue(option);
                    values.Add(value);
                }
                result[menu.MenuName] = values;
            }

            return result;
        }

        public uint ResolveMenuOptionValue(string optionName)
        {
            var field = typeof(FirmwareConstants).GetField(optionName);
            if (field != null)
            {
                var value = field.GetValue(null);
                if (value is uint uintValue)
                {
                    return uintValue;
                }
            }

            return ConfigSourceParser.ParseRIdStrConstantStatic(optionName);
        }
    }
}