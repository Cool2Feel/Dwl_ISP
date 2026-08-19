# 资源类型检测速查表

## 🎯 检测方法优先级

| 优先级 | 检测方法 | 可靠性 | 适用类型 |
|--------|---------|--------|---------|
| 1️⃣ | **文件魔数** | ⭐⭐⭐⭐⭐ | JPEG, BMP, WAV, Font(idx) |
| 2️⃣ | **固定大小** | ⭐⭐⭐⭐ | Palette (1024 bytes) |
| 3️⃣ | **结构特征** | ⭐⭐⭐⭐ | Font(data) - 字符数量 |
| 4️⃣ | **大小范围** | ⭐⭐-⭐⭐⭐ | EncodingTable, OsdSource, GameMap, IconSelection |
| 5️⃣ | **默认类型** | - | Binary (兜底) |

---

## 📋 完整检测规则

### 🔴 魔数检测（立即识别）

```
FF D8 FF          → JPEG
BM                → Bitmap  
RIFF....WAVE      → WAV
0x584D            → Font (resfontidx.bin)
```

### 🟡 固定大小（精确匹配）

```
1024 bytes        → Palette
```

### 🔵 结构特征（语义分析）

```
前4字节 = 字符数量 (100-50,000)  → Font (resfont.bin, MP3font.bin)
```

### 🟢 大小范围（区间匹配）

```
85-90 KB          → EncodingTable
90-100 KB         → OsdSource
< 10 KB           → GameMap
10-100 KB         → IconSelection
其他              → Binary
```

---

## 📊 实际案例对照表

| 文件名 | 大小 | 检测结果 | 检测依据 |
|--------|------|---------|---------|
| power_on.jpg | 40.5 KB | JPEG | 魔数 FF D8 FF |
| gamemenu_maze.bmp | 42.2 KB | Bitmap | 魔数 BM |
| music_power_on.wav | 50.6 KB | WAV | 魔数 RIFF...WAVE |
| palette.bin | 1 KB | Palette | 固定大小 1024 |
| resfontidx.bin | 75 KB | Font | 魔数 0x584D |
| resfont.bin | 82.5 KB | Font | 字符数 899 |
| MP3font.bin | 982.8 KB | Font | 字符数 20,998 |
| oem2uni936.bin | 85 KB | EncodingTable | 大小范围 85-90 KB |
| OSD_source.bin | 94 KB | OsdSource | 大小范围 90-100 KB |
| game_block_map.bin | 432 B | GameMap | 大小 < 10 KB |
| mainmenu_sel.bin | 69 KB | IconSelection | 大小 10-100 KB |
| str_version.bin | 12 B | GameMap ⚠️ | 大小 < 10 KB (可能误判) |

---

## ⚠️ 常见误判场景

### 场景 1: 小文件被误判为 GameMap

**问题**: `str_version.bin` (12 bytes) → GameMap

**原因**: 所有 < 10 KB 的文件都被归类为 GameMap

**解决**: 
- 添加文件名检查: `if (name.Contains("_map"))`
- 或调整阈值: `< 5 KB` 更严格

### 场景 2: 中等文件类型混淆

**问题**: 10-100 KB 范围内的非 IconSelection 文件

**原因**: IconSelection 检测范围太宽泛

**解决**:
- 添加内容验证器
- 或使用资源名称辅助判断

### 场景 3: 新项目文件大小不同

**问题**: 其他项目的 EncodingTable 可能不在 85-90 KB 范围

**原因**: 大小范围基于当前项目硬编码

**解决**:
- 提供配置选项自定义范围
- 或增加内容特征检测

---

## 🔧 调试技巧

### 查看检测日志

在 Visual Studio 的输出窗口查看检测过程：

```
[IsFontFile] Detected font file by char count: 20998
[IsFontFile] Detected resfontidx.bin by magic: 0x584D
```

### 手动验证检测逻辑

使用 Python 脚本快速测试：

```python
import struct

data = open('MP3font.bin', 'rb').read(4)
charCount = struct.unpack_from('<I', data, 0)[0]
print(f"Char count: {charCount}")
print(f"Is Font: {100 <= charCount <= 50000}")
```

### 强制指定类型（临时方案）

如果自动检测不准确，可以在代码中手动指定：

```csharp
// 临时修复：根据资源ID强制指定类型
if (resourceId == 83) // RES_STR_VERSION
    resource.Type = ResourceType.Binary;
```

---

## 💡 最佳实践

### ✅ 推荐做法

1. **为新资源定义魔数**: 确保唯一性和准确性
2. **维护 RES.H 文件**: 资源名称可辅助判断
3. **实现验证器**: 对关键类型进行二次验证
4. **记录典型样本**: 建立各类型的文件大小参考
5. **多项目测试**: 确保检测逻辑的通用性

### ❌ 避免做法

1. **仅依赖大小范围**: 容易误判
2. **硬编码绝对路径**: 降低可移植性
3. **忽略边界情况**: 如空文件或超大文件
4. **不更新检测逻辑**: 新增资源类型后未同步更新
5. **缺少日志输出**: 难以排查检测问题

---

## 📈 检测性能

### 时间复杂度

- **最佳情况**: O(1) - 魔数匹配（JPEG/BMP/WAV）
- **平均情况**: O(1) - 3-5 次比较
- **最坏情况**: O(1) - 最多 10 次比较

### 内存占用

- **额外内存**: ~100 bytes（局部变量）
- **无需加载完整文件**: 仅需前几个字节

### 实测性能

处理 100 个资源的 RES.BIN 文件：
- **总耗时**: < 1 ms
- **平均每资源**: < 10 μs
- **瓶颈**: 文件 I/O，而非检测逻辑

---

## 🚀 快速诊断流程

当遇到检测问题时，按以下步骤排查：

```
1. 检查文件大小是否正确？
   ├─ 是 → 继续
   └─ 否 → 重新导出资源

2. 检查文件头是否有标准魔数？
   ├─ 是 → 应该被正确识别
   └─ 否 → 继续

3. 是否在已知的大小范围内？
   ├─ 是 → 可能被误判为其他类型
   └─ 否 → 归类为 Binary

4. 是否有对应的验证器？
   ├─ 是 → 运行验证器确认
   └─ 否 → 手动检查文件内容

5. 资源名称是否暗示类型？
   ├─ 是 → 考虑添加名称辅助检测
   └─ 否 → 接受当前分类或手动修正
```

---

## 📞 需要帮助？

参考文档：
- 📘 [完整检测逻辑分析](RESOURCE_DETECTION_LOGIC_ANALYSIS.md)
- 📗 [检测决策树](RESOURCE_DETECTION_DECISION_TREE.md)
- 📙 [MP3FONT 专项分析](MP3FONT_RESOURCE_ANALYSIS.md)
- 📕 [字体资源修复报告](FONT_RESOURCE_FIX_REPORT.md)

常见问题已在上文覆盖，如仍有疑问，请提供：
1. 文件名和大小
2. 文件头部 16 字节（十六进制）
3. 预期类型和实际检测结果
4. RES.H 中的资源名称
