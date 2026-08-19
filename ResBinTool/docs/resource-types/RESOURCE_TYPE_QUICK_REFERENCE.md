# 资源类型快速参考指南

## 资源类型识别规则

| 资源类型 | 文件大小特征 | 文件示例 | 验证器 |
|---------|------------|---------|--------|
| **JPEG** | 以 `FF D8 FF` 开头 | power_on.jpg, main_bk.jpg | 魔数检测 |
| **Bitmap** | 以 `BM` 开头 | gamemenu_maze.bmp, playback_frame1_0.bmp | 魔数检测 |
| **WAV** | 以 `RIFF....WAVE` 开头 | music_power_on.wav, game_block_knock.wav | WavValidator |
| **Font** | resfont.bin: 前4字节为字符数 (100-50000)<br>resfontidx.bin: 前2字节为魔数 0x584D<br>**支持大型字体**: MP3font.bin (982KB, 20,998 chars) | resfont.bin (84KB)<br>resfontidx.bin (76KB)<br>MP3font.bin (982KB) | FontInfoParser |
| **Palette** | 固定 1024 字节 | palette.bin, palette_game.bin | PaletteValidator |
| **GameMap** | < 10KB | game_block_map.bin, game_maze_map.bin | GameMapValidator |
| **EncodingTable** | 80-90KB | oem2uni936.bin, uni2oem936.bin | EncodingTableValidator |
| **IconSelection** | 10-100KB | mainmenu_sel.bin, video_sel.bin | 大小检测 |
| **OsdSource** | 90-100KB | OSD_source.bin | 大小检测 |
| **Binary** | 其他 | str_version.bin | 默认类型 |

## 验证器使用示例

### Palette 验证
```csharp
var result = PaletteValidator.Validate(paletteData);
if (result.IsValid)
{
    Console.WriteLine(PaletteValidator.GetDisplayText(result));
    // 输出: ✓ Valid Palette
    //       Valid palette: 1024 bytes, 128 non-zero colors out of 256
}
```

### GameMap 验证
```csharp
var result = GameMapValidator.Validate(mapData);
if (result.IsValid)
{
    Console.WriteLine(GameMapValidator.GetDisplayText(result));
    // 输出: ✓ Valid Game Map
    //       Valid game map: 432 bytes, 200 zeros, 232 non-zeros
}
```

### EncodingTable 验证
```csharp
var result = EncodingTableValidator.Validate(encodingData);
if (result.IsValid)
{
    Console.WriteLine(EncodingTableValidator.GetDisplayText(result));
    // 输出: ✓ Valid Encoding Table
    //       Valid encoding table: 87172 bytes, ~21793 mappings, 95 non-zero in first 100
}
```

## 替换流程

1. **打开 RES.BIN** → 自动解析和类型识别
2. **选择资源** → 显示资源信息和类型
3. **点击 Replace** → 选择新文件
4. **自动验证** → 显示验证结果
5. **用户确认** → 执行替换
6. **保存文件** → 完成操作

## 注意事项

⚠️ **重要提示**:
- Palette 必须是精确的 1024 字节
- GameMap 不能是全零或全非零数据
- EncodingTable 必须在 80-90KB 范围内
- 所有替换操作都会检查文件大小差异并提示用户
- 建议先备份原始 RES.BIN 文件

## 常见问题

**Q: 为什么我的文件被识别为 Binary 而不是特定类型？**
A: 检查文件大小是否符合预期范围，或文件头是否正确。

**Q: 替换后固件无法启动？**
A: 确保新资源文件格式正确，大小合理，并经过验证器验证。

**Q: 如何添加新的资源类型支持？**
A: 
1. 在 `ResourceType` 枚举中添加新类型
2. 在 `DetectResourceType` 中添加检测逻辑
3. 创建对应的验证器类
4. 在 ViewModel 中添加验证方法
5. 在 UI 中添加预览支持
