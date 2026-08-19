namespace ResBinManager.ViewModels
{
    using ResBinManager.Core;
    using ResBinManager.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Windows;
    using DialogResult = System.Windows.Forms.DialogResult;
    using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
    using MessageBox = System.Windows.MessageBox;
    using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
    using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

    public partial class MainViewModel
    {
        #region RevertCommand

        private bool CanExecuteRevert(object? parameter)
        {
            return SelectedResource != null &&
                   SelectedResource.IsModified &&
                   SelectedResource.OriginalData != null;
        }

        private void ExecuteRevert(object? parameter)
        {
            if (SelectedResource == null || _parser == null || SelectedResource.OriginalData == null)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to revert '{SelectedResource.Name}' to its original state?\n\n" +
                $"This will undo the replacement and restore the original data.",
                "Confirm Revert",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = "Revert cancelled";
                return;
            }

            StatusMessage = $"Reverting {SelectedResource.Name}...";

            try
            {
                var resourceTable = _parser.GetResourceTable();
                var writer = new ResBinWriter(_currentFileData!, _currentTableOffset,
                                            resourceTable);

                if (writer.ReplaceResource(SelectedResource.Id, SelectedResource.OriginalData))
                {
                    _currentFileData = writer.GetData();

                    _parser.UpdateResourceTable(writer.GetResourceTable(), writer.GetData());

                    var syncError = SyncDestBinAfterReplace(_currentFileData);
                    if (syncError != null)
                    {
                        StatusMessage = $"✗ Revert succeeded but DestBin sync failed: {syncError}";
                    }
                    else
                    {
                        StatusMessage = $"✓ Reverted {SelectedResource.Name} to original";
                    }

                    var currentSelected = SelectedResource;
                    currentSelected.IsModified = false;
                    currentSelected.Size = currentSelected.OriginalSize;
                    currentSelected.Data = null;
                    currentSelected.OriginalData = null;
                    currentSelected.OriginalSize = 0;

                    (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RevertCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();

                    UpdateResourceOffsetsAfterReplace();

                    var index = Resources.IndexOf(currentSelected);
                    if (index >= 0)
                    {
                        var tempSelected = _selectedResource;
                        _selectedResource = null;

                        try
                        {
                            Resources.RemoveAt(index);
                            Resources.Insert(index, currentSelected);
                        }
                        finally
                        {
                            _selectedResource = tempSelected;
                            OnPropertyChanged(nameof(SelectedResource));
                        }
                    }

                    if (currentSelected.Type == ResourceType.Jpeg || currentSelected.Type == ResourceType.Bitmap)
                    {
                        PreviewRequested?.Invoke(this, currentSelected);
                    }
                    else if (currentSelected.Type == ResourceType.Wav)
                    {
                        LoadWavForPreview();
                    }
                    else if (currentSelected.Type == ResourceType.OsdSource)
                    {
                        LoadOsdForPreview();
                    }
                    else if (currentSelected.Type == ResourceType.Text)
                    {
                        LoadTextForPreview();
                    }
                    else if (currentSelected.Type == ResourceType.Palette)
                    {
                        LoadPaletteForPreview();
                    }
                    else if (IsFontResource(currentSelected))
                    {
                        LoadFontForPreview();
                    }

                    MessageBox.Show(
                        $"Resource reverted successfully!\n\n" +
                        $"'{currentSelected.Name}' has been restored to its original state.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Revert failed:\n{writer.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = "Revert failed";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\nType: {ex.GetType().Name}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Revert error occurred";
            }
        }

        #endregion

        #region ReplaceCommand

        private bool CanExecuteReplace(object? parameter) =>
            !IsLoading && SelectedResource != null && _parser != null && _currentFileData != null && SelectedResource.Size > 0 && !IsFontResource(SelectedResource);

        private void ExecuteReplace(object? parameter)
        {
            if (SelectedResource == null || _parser == null || _currentFileData == null)
                return;

            if (IsFontResource(SelectedResource))
            {
                MessageBox.Show(
                    $"Font resources (resfont.bin and resfontidx.bin) must be replaced together.\n\n" +
                    $"Please use the 'Replace Font' button in the Font preview panel instead.\n\n" +
                    $"Selected resource: {SelectedResource.Name}",
                    "Font Resource Replacement",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (SelectedResource.Size == 0)
            {
                MessageBox.Show(
                    $"Resource {SelectedResource.Id} ({SelectedResource.Name}) does not exist.\n\n" +
                    "This resource has zero length and cannot be replaced.\n" +
                    "It may have been removed or is not available in this platform.",
                    "Resource Not Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusMessage = $"Cannot replace: {SelectedResource.Name} does not exist";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = $"Replace Resource {SelectedResource.Id} ({SelectedResource.Name})",
                Filter = GetFilterByType(SelectedResource.Type)
            };

            if (dialog.ShowDialog() != true)
                return;

            StatusMessage = $"Replacing {SelectedResource.Name}...";

            try
            {
                var newData = File.ReadAllBytes(dialog.FileName);

                long sizeDiff = newData.Length - (long)SelectedResource.Size;
                double sizeDiffPercent = SelectedResource.Size > 0
                    ? (double)sizeDiff / SelectedResource.Size * 100
                    : 0;

                if (!ValidateAndConfirmResourceReplacement(newData, sizeDiff, sizeDiffPercent))
                    return;

                var resourceTable = _parser.GetResourceTable();
                var writer = new ResBinWriter(_currentFileData, _currentTableOffset, resourceTable);

                if (!writer.ReplaceResource(SelectedResource.Id, newData))
                {
                    MessageBox.Show($"Replace failed:\n{writer.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = "Replace failed";
                    return;
                }

                var syncError = SyncDestBinAfterReplace(writer.GetData());
                if (syncError != null)
                {
                    MessageBox.Show(
                        $"Failed to sync resource to DEST.BIN:\n\n{syncError}\n\n" +
                        "The replacement has been cancelled. Please try again.",
                        "DestBin Sync Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    StatusMessage = "Replace failed: DestBin sync error";
                    return;
                }

                var currentSelected = SelectedResource;
                FinalizeResourceReplace(currentSelected, writer.GetData(), writer.GetResourceTable(), newData);

                StatusMessage = $"✓ Replaced {currentSelected.Name}. Don't forget to save.";

                if (currentSelected.Type is ResourceType.Jpeg or ResourceType.Bitmap)
                    PreviewRequested?.Invoke(this, currentSelected);
                else if (currentSelected.Type == ResourceType.Wav)
                    LoadWavForPreview();
                else if (currentSelected.Type == ResourceType.OsdSource)
                {
                    LoadOsdForPreview();
                }
                else if (currentSelected.Type == ResourceType.Text)
                {
                    LoadTextForPreview();
                }
                else if (currentSelected.Type == ResourceType.Palette)
                {
                    LoadPaletteForPreview();
                }
                else if (IsFontResource(currentSelected))
                {
                    LoadFontForPreview();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\nType: {ex.GetType().Name}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Error occurred";
            }
        }

        #endregion

        #region ApplyTextEditCommand

        private bool CanExecuteApplyTextEdit(object? parameter) =>
            !IsLoading && SelectedResource != null && _parser != null && _currentFileData != null &&
            SelectedResource.Type == ResourceType.Text && !string.IsNullOrEmpty(TextContent);

        private void ExecuteApplyTextEdit(object? parameter)
        {
            if (SelectedResource == null || _parser == null || _currentFileData == null)
                return;

            if (SelectedResource.Type != ResourceType.Text)
                return;

            try
            {
                byte[] newData = System.Text.Encoding.UTF8.GetBytes(TextContent);

                long sizeDiff = newData.Length - (long)SelectedResource.Size;
                double sizeDiffPercent = SelectedResource.Size > 0
                    ? (double)sizeDiff / SelectedResource.Size * 100
                    : 0;

                if (!ValidateAndConfirmResourceReplacement(newData, sizeDiff, sizeDiffPercent))
                    return;

                var resourceTable = _parser.GetResourceTable();
                var writer = new ResBinWriter(_currentFileData, _currentTableOffset, resourceTable);

                if (!writer.ReplaceResource(SelectedResource.Id, newData))
                {
                    MessageBox.Show($"Text edit failed:\n{writer.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = "Text edit failed";
                    return;
                }

                var syncError = SyncDestBinAfterReplace(writer.GetData());
                if (syncError != null)
                {
                    MessageBox.Show(
                        $"Failed to sync resource to DEST.BIN:\n\n{syncError}\n\n" +
                        "The text edit has been cancelled. Please try again.",
                        "DestBin Sync Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    StatusMessage = "Text edit failed: DestBin sync error";
                    return;
                }

                var currentSelected = SelectedResource;
                FinalizeResourceReplace(currentSelected, writer.GetData(), writer.GetResourceTable(), newData);

                LoadTextForPreview();
                StatusMessage = $"✓ Text edited. Don't forget to save.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\nType: {ex.GetType().Name}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Error occurred";
            }
        }

        #endregion

        #region Resource Replacement Helpers

        /// <summary>
        /// 同步 DestBinParser（含事务保护），失败时自动回滚
        /// </summary>
        /// <param name="newFileData">替换后的文件数据</param>
        /// <returns>成功返回 null，失败返回错误消息</returns>
        private string? SyncDestBinAfterReplace(byte[] newFileData)
        {
            if (!IsDestBinMode || _destBinParser == null)
                return null;

            object? snapshot = _destBinParser.CreateSnapshot();
            bool configWasApplied = false;

            if (IsConfigModified)
            {
                ApplyConfigChangesToDestBin();
                configWasApplied = true;
            }

            if (!_destBinParser.ReplaceResBin(newFileData, keepSize: false))
            {
                _destBinParser.RestoreSnapshot(snapshot);
                if (configWasApplied)
                    IsConfigModified = true;
                return _destBinParser.ErrorMessage;
            }

            return null;
        }

        /// <summary>
        /// 完成资源替换后的最终刷新流程（保存原始数据、更新内存、刷新 UI）
        /// </summary>
        private void FinalizeResourceReplace(ResourceItem resource, byte[] newFileData,
            List<ResInfoEntry> newResourceTable, byte[] newData)
        {
            if (resource.OriginalData == null)
            {
                resource.OriginalData = new byte[resource.Size];
                Array.Copy(_currentFileData!, resource.Offset,
                          resource.OriginalData, 0, resource.Size);
                resource.OriginalSize = resource.Size;
            }

            _currentFileData = newFileData;
            _parser!.UpdateResourceTable(newResourceTable, newFileData);

            resource.IsModified = true;
            resource.Size = (uint)newData.Length;
            resource.Data = null;

            (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RevertCommand as RelayCommand)?.RaiseCanExecuteChanged();

            UpdateResourceOffsetsAfterReplace();

            var index = Resources.IndexOf(resource);
            if (index >= 0)
            {
                var tempSelected = _selectedResource;
                _selectedResource = null;
                try
                {
                    Resources.RemoveAt(index);
                    Resources.Insert(index, resource);
                }
                finally
                {
                    _selectedResource = tempSelected;
                    OnPropertyChanged(nameof(SelectedResource));
                }
            }

            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region Resource Replacement Validation

        private bool ValidateAndConfirmResourceReplacement(byte[] newData, long sizeDiff = 0, double sizeDiffPercent = 0)
        {
            if (SelectedResource == null)
                return false;

            ResourceValidationResult validationResult;

            if (IsImageResource(SelectedResource.Type))
            {
                validationResult = ResourceValidatorFactory.Validate(
                    SelectedResource.Type, newData, SelectedResource.Size,
                    SelectedResource.Width, SelectedResource.Height);
            }
            else
            {
                validationResult = ResourceValidatorFactory.Validate(SelectedResource.Type, newData, SelectedResource.Size);
            }

            bool IsImageResource(ResourceType type)
            {
                return type is ResourceType.Bitmap or ResourceType.Jpeg or ResourceType.Png;
            }

            if (!validationResult.IsValid)
            {
                MessageBox.Show(
                    $"Invalid resource file:\n\n{validationResult.ErrorMessage}",
                    "Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusMessage = $"{SelectedResource.Type} replacement cancelled";
                return false;
            }

            var message = new StringBuilder();
            message.AppendLine($"{SelectedResource.Type} Resource Replacement");
            message.AppendLine();
            message.AppendLine("New File Information:");
            message.AppendLine(validationResult.GetDisplayText());

            if (validationResult.Warnings.Count > 0)
            {
                message.AppendLine();
                foreach (var warning in validationResult.Warnings)
                {
                    message.AppendLine($"⚠ {warning}");
                }
            }

            if (sizeDiff != 0)
            {
                message.AppendLine();
                message.AppendLine("Size Change:");
                message.AppendLine($"  Original: {SelectedResource.Size:N0} bytes ({FormatFileSize(SelectedResource.Size)})");
                message.AppendLine($"  New:      {newData.Length:N0} bytes ({FormatFileSize((uint)newData.Length)})");
                if (sizeDiff > 0)
                {
                    message.AppendLine($"  Difference: +{sizeDiff:N0} bytes (+{sizeDiffPercent:F1}%)");
                    message.AppendLine();
                    message.AppendLine("⚠ This will shift all subsequent resources in the file.");
                }
                else
                {
                    message.AppendLine($"  Difference: {sizeDiff:N0} bytes ({sizeDiffPercent:F1}%)");
                    message.AppendLine();
                    message.AppendLine("✓ The remaining space will be filled with 0xFF padding.");
                }
            }

            message.AppendLine();
            message.AppendLine("Continue with replacement?");

            var result = MessageBox.Show(
                message.ToString(),
                $"Confirm {SelectedResource.Type} Replacement",
                MessageBoxButton.YesNo,
                validationResult.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = $"{SelectedResource.Type} replacement cancelled";
            }

            return result == MessageBoxResult.Yes;
        }

        private string FormatFileSize(uint bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F2} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            else
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private string GetFilterByType(ResourceType type)
        {
            return type switch
            {
                ResourceType.Jpeg => "JPEG files|*.jpg;*.jpeg|All files|*.*",
                ResourceType.Bitmap => "Bitmap files|*.bmp|All files|*.*",
                ResourceType.Png => "PNG files|*.png|All files|*.*",
                ResourceType.Wav => "WAV files|*.wav|All files|*.*",
                ResourceType.Mp3 => "MP3 files|*.mp3|All files|*.*",
                ResourceType.Palette => "Palette files|*.bin;|All files|*.*",
                ResourceType.OsdSource => "OSD resource files|*.bin|All files|*.*",
                ResourceType.Font => "Font files|*.bin|All files|*.*",
                _ => "Bin files|*.bin|All files|*.*"
            };
        }

        #endregion

        #region Export Resource

        private bool CanExecuteExport(object? parameter) => SelectedResource != null;

        private void ExecuteExport(object? parameter)
        {
            if (SelectedResource == null)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = $"{SelectedResource.Name}{GetExtension(SelectedResource.Type)}",
                Filter = "All files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                if (_parser!.ExportResource(SelectedResource.Id, dialog.FileName))
                {
                    StatusMessage = $"✓ Exported {SelectedResource.Name}";
                    MessageBox.Show("Resource exported successfully!", "Success",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Export failed:\n{_parser.ErrorMessage}",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private bool CanExecuteExportOsdIcons(object? parameter) => OsdInfo != null && OsdInfo.Icons.Count > 0;

        private void ExecuteExportOsdIcons(object? parameter)
        {
            if (OsdInfo == null || OsdInfo.Icons.Count == 0 || OsdData == null)
                return;

            var dialog = new FolderBrowserDialog
            {
                Description = "Select output folder for OSD icons"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                byte[]? paletteData = FindPaletteResourceData();
                OsdSourceParser.ExportOsdIcons(OsdData, paletteData ?? OsdData, dialog.SelectedPath, _osdOriginalIconDirectory);

                StatusMessage = $"✓ Exported {OsdInfo.Icons.Count} OSD icons";
                MessageBox.Show($"Successfully exported {OsdInfo.Icons.Count} icons!", "Success",
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region Replace OSD Icon

        private bool CanExecuteReplaceOsdIcon(object? parameter) => OsdInfo != null && OsdInfo.SelectedIcon != null;

        private void ExecuteReplaceOsdIcon(object? parameter)
        {
            if (OsdInfo == null || OsdInfo.SelectedIcon == null || OsdData == null || SelectedResource == null)
                return;

            var selectedIcon = OsdInfo.SelectedIcon;

            var dialog = new OpenFileDialog
            {
                Filter = "Bitmap files (*.bmp)|*.bmp|All files (*.*)|*.*",
                Title = $"Replace icon: {selectedIcon.Name} ({selectedIcon.Width} × {selectedIcon.Height})",
                FileName = $"{selectedIcon.Name}.bmp"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    byte[] newIconData = File.ReadAllBytes(dialog.FileName);

                    if (!OsdSourceParser.IsValidOsdBmp(newIconData, out string errorMsg))
                    {
                        MessageBox.Show($"Invalid OSD icon file:\n{errorMsg}", "Error",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    byte[]? paletteData = FindPaletteResourceData();
                    var palette = OsdSourceParser.ParsePalette(paletteData ?? OsdData);

                    byte[] newIndexData = OsdSourceParser.ConvertBmpToOsdIndexData(
                        newIconData, palette, selectedIcon.Width, selectedIcon.Height);

                    if (!SelectedResource.IsModified)
                    {
                        SelectedResource.OriginalData = new byte[SelectedResource.Size];
                        Array.Copy(_currentFileData!, SelectedResource.Offset,
                                  SelectedResource.OriginalData, 0, SelectedResource.Size);
                        SelectedResource.OriginalSize = SelectedResource.Size;
                    }

                    var icons = OsdSourceParser.ParseHeader(OsdData);
                    var iconInfo = icons.FirstOrDefault(i => i.Index == selectedIcon.Index);
                    if (iconInfo != null)
                    {
                        int pixelCount = selectedIcon.Width * selectedIcon.Height;
                        Array.Copy(newIndexData, 0, OsdData, (int)iconInfo.DataOffset, pixelCount);

                        selectedIcon.RawIndexData = newIndexData;

                        byte[] indexedBmpData = OsdSourceParser.ConvertRawIndexToIndexedBmp(
                            selectedIcon.Width, selectedIcon.Height, newIndexData, palette);
                        selectedIcon.IconData = indexedBmpData;

                        var writer = new ResBinWriter(_currentFileData!, _currentTableOffset, _parser.GetResourceTable());

                        if (writer.ReplaceResource(SelectedResource.Id, OsdData))
                        {
                            _currentFileData = writer.GetData();

                            _parser.UpdateResourceTable(writer.GetResourceTable(), writer.GetData());

                            SelectedResource.IsModified = true;
                            SelectedResource.Size = (uint)OsdData.Length;
                            selectedIcon.IsSelected = true;

                            UpdateResourceOffsetsAfterReplace();

                            bool destBinSyncFailed = false;
                            if (IsDestBinMode && _destBinParser != null)
                            {
                                var syncError = SyncDestBinAfterReplace(_currentFileData);
                                if (syncError != null)
                                {
                                    destBinSyncFailed = true;
                                    StatusMessage = $"✗ Failed to update DestBinParser: {syncError}";
                                    MessageBox.Show($"Icon replaced but DestBinParser update failed:\n{syncError}",
                                                  "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            }
                            LoadOsdForPreview();
                            if (!destBinSyncFailed)
                                StatusMessage = $"✓ Replaced icon: {selectedIcon.Name}";
                            MessageBox.Show($"Successfully replaced icon: {selectedIcon.Name}\n\nDon't forget to save the modified file.", "Success",
                                          MessageBoxButton.OK, MessageBoxImage.Information);

                            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        }
                        else
                        {
                            throw new InvalidOperationException(writer.ErrorMessage ?? "Failed to replace resource");
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"✗ Failed to replace icon: {ex.Message}";
                    MessageBox.Show($"Failed to replace icon:\n{ex.Message}", "Error",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Replace Resource

        private string GetExtension(ResourceType type)
        {
            return type switch
            {
                ResourceType.Jpeg => ".jpg",
                ResourceType.Bitmap => ".bmp",
                ResourceType.Wav => ".wav",
                ResourceType.Binary => ".bin",
                ResourceType.OsdSource => ".bin",
                ResourceType.Palette => ".bin",
                _ => ".dat"
            };
        }

        #endregion

        #region Font Replace

        private bool CanExecuteReplaceFont(object? parameter)
        {
            return SelectedResource != null && IsFontResource(SelectedResource);
        }

        private void ExecuteReplaceFont(object? parameter)
        {
            if (SelectedResource == null || _parser == null)
                return;

            if (!IsFontResource(SelectedResource))
            {
                MessageBox.Show("Please select a font resource first.",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var resfontResource = Resources.FirstOrDefault(r => r != null && IsFontDataResource(r));
            var resfontidxResource = Resources.FirstOrDefault(r => r != null && IsFontIndexResource(r));

            if (resfontResource == null || resfontidxResource == null)
            {
                MessageBox.Show("Font resources not found in resource list.",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            uint resfontId = resfontResource.Id;
            uint resfontidxId = resfontidxResource.Id;

            var dialog = new Views.FontReplaceDialog();
            dialog.SetCurrentFontInfo(FontData, FontIndex, FontInfo);
            dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() != true)
                return;

            var newFontData = dialog.NewFontData;
            var newFontIndex = dialog.NewFontIndex;

            if (newFontData == null || newFontIndex == null)
            {
                MessageBox.Show("Invalid font data.", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            StatusMessage = "Replacing font resources...";

            try
            {
                long resfontSizeDiff = newFontData.Length - (long)resfontResource.Size;
                long resfontidxSizeDiff = newFontIndex.Length - (long)resfontidxResource.Size;
                long totalSizeDiff = resfontSizeDiff + resfontidxSizeDiff;

                if (totalSizeDiff != 0)
                {
                    string message;
                    MessageBoxImage icon;

                    if (totalSizeDiff > 0)
                    {
                        message = $"New font files are LARGER than originals:\n\n" +
                                 $"resfont.bin: {resfontResource.Size:N0} → {newFontData.Length:N0} (+{resfontSizeDiff:N0})\n" +
                                 $"resfontidx.bin: {resfontidxResource.Size:N0} → {newFontIndex.Length:N0} (+{resfontidxSizeDiff:N0})\n" +
                                 $"Total: +{totalSizeDiff:N0} bytes\n\n" +
                                 $"⚠️ This will shift all subsequent resources in the file.\n" +
                                 $"The file size will increase accordingly.\n\n" +
                                 $"Continue with replacement?";
                        icon = MessageBoxImage.Warning;
                    }
                    else
                    {
                        message = $"New font files are SMALLER than originals:\n\n" +
                                 $"resfont.bin: {resfontResource.Size:N0} → {newFontData.Length:N0} ({resfontSizeDiff:N0})\n" +
                                 $"resfontidx.bin: {resfontidxResource.Size:N0} → {newFontIndex.Length:N0} ({resfontidxSizeDiff:N0})\n" +
                                 $"Total: {totalSizeDiff:N0} bytes\n\n" +
                                 $"✓ The remaining space will be filled with 0xFF padding.\n" +
                                 $"No other resources will be affected.\n\n" +
                                 $"Continue with replacement?";
                        icon = MessageBoxImage.Question;
                    }

                    var result = MessageBox.Show(message, "Confirm Replacement",
                                                 MessageBoxButton.YesNo, icon);
                    if (result != MessageBoxResult.Yes)
                    {
                        StatusMessage = "Replace cancelled by user";
                        return;
                    }
                }

                if (!resfontResource.IsModified)
                {
                    resfontResource.OriginalData = new byte[resfontResource.Size];
                    Array.Copy(_currentFileData!, resfontResource.Offset,
                              resfontResource.OriginalData, 0, resfontResource.Size);
                    resfontResource.OriginalSize = resfontResource.Size;
                }

                if (!resfontidxResource.IsModified)
                {
                    resfontidxResource.OriginalData = new byte[resfontidxResource.Size];
                    Array.Copy(_currentFileData!, resfontidxResource.Offset,
                              resfontidxResource.OriginalData, 0, resfontidxResource.Size);
                    resfontidxResource.OriginalSize = resfontidxResource.Size;
                }

                var writer = new ResBinWriter(_currentFileData!, _currentTableOffset,
                                            _parser.GetResourceTable());

                if (!writer.ReplaceResource(resfontId, newFontData))
                {
                    throw new Exception($"Failed to replace resfont.bin: {writer.ErrorMessage}");
                }

                if (!writer.ReplaceResource(resfontidxId, newFontIndex))
                {
                    throw new Exception($"Failed to replace resfontidx.bin: {writer.ErrorMessage}");
                }

                _currentFileData = writer.GetData();
                _parser.UpdateResourceTable(writer.GetResourceTable(), writer.GetData());

                FontData = newFontData;
                FontIndex = newFontIndex;
                FontBinData = null;

                resfontResource.IsModified = true;
                resfontResource.Size = (uint)newFontData.Length;
                resfontidxResource.IsModified = true;
                resfontidxResource.Size = (uint)newFontIndex.Length;

                if (IsDestBinMode && _destBinParser != null)
                {
                    if (IsConfigModified)
                    {
                        ApplyConfigChangesToDestBin();
                    }

                    if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
                    {
                        System.Diagnostics.Debug.WriteLine($"[FontReplace] DestBinParser updated successfully");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[FontReplace] Failed to update DestBinParser: {_destBinParser.ErrorMessage}");
                    }
                }

                UpdateResourceOffsetsAfterReplace();
                LoadFontForPreview();

                (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();

                StatusMessage = "✓ Font resources replaced successfully";

                string fontBinMessage = "Font resources replaced successfully!\n\n" +
                                        "Both resfont.bin and resfontidx.bin have been updated.\n\n";
                if (FontBinData != null)
                {
                    fontBinMessage += "⚠️ Notice: font.bin was also loaded for preview.\n" +
                                      "If you modified the font content, you should also update font.bin\n" +
                                      "to ensure string decoding works correctly in preview.\n\n";
                }
                fontBinMessage += "Don't forget to save the modified file.";

                MessageBox.Show(fontBinMessage, "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Font replacement failed";
            }
        }

        #endregion
    }
}
