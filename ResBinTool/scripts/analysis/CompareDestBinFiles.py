import struct
import os

def analyze_destbin(file_path, label):
    """分析 DestBin.bin 文件结构"""
    print(f"\n{'='*70}")
    print(f"Analysis: {label}")
    print(f"{'='*70}")
    
    if not os.path.exists(file_path):
        print(f"✗ File not found: {file_path}")
        return None
    
    file_size = os.path.getsize(file_path)
    print(f"File size: {file_size:,} bytes ({file_size / 1024:.2f} KB)")
    
    with open(file_path, 'rb') as f:
        # 读取头部 64 字节
        header = f.read(64)
        
        print(f"\nHeader (first 64 bytes):")
        for i in range(0, min(64, len(header)), 16):
            hex_str = ' '.join(f'{b:02X}' for b in header[i:i+16])
            ascii_str = ''.join(chr(b) if 32 <= b < 127 else '.' for b in header[i:i+16])
            print(f"  {i:04X}: {hex_str:<48} {ascii_str}")
        
        # Magic
        magic = struct.unpack_from('<I', header, 4)[0]
        magic_str = ''.join(chr((magic >> (i*8)) & 0xFF) for i in range(4))
        print(f"\nMagic: 0x{magic:08X} = '{magic_str}'")
        is_valid = (magic == 0x52444C42)
        print(f"  {'✓' if is_valid else '✗'} {'Valid BLDR' if is_valid else 'Invalid signature'}")
        
        # Version
        version = struct.unpack_from('<I', header, 8)[0]
        major = (version >> 16) & 0xFF
        minor = (version >> 8) & 0xFF
        patch = version & 0xFF
        print(f"Version: v{major}.{minor}.{patch} (0x{version:08X})")
        
        # Serial
        serial_bytes = header[16:24]
        serial = ''.join(chr(b) if 32 <= b < 127 else '?' for b in serial_bytes)
        print(f"Serial: '{serial}' ({serial_bytes.hex().upper()})")
        
        # 尝试检测 RES.BIN 位置
        print(f"\nRES.BIN Detection:")
        
        # 方法1: 标准偏移 0x9DC00
        STANDARD_OFFSET = 0x9DC00
        
        if file_size > STANDARD_OFFSET:
            print(f"  Checking standard offset 0x{STANDARD_OFFSET:X}...")
            f.seek(STANDARD_OFFSET)
            entry = f.read(8)
            addr1, len1 = struct.unpack_from('<II', entry, 0)
            
            print(f"    First entry: Address=0x{addr1:08X}, Length={len1}")
            
            if addr1 > STANDARD_OFFSET and len1 > 0 and len1 < 1000000:
                print(f"    ✓ Valid resource table detected")
                res_offset = STANDARD_OFFSET
            else:
                print(f"    ✗ Invalid at standard offset")
                res_offset = None
        else:
            print(f"  ✗ File too small for standard offset (need > {STANDARD_OFFSET})")
            res_offset = None
        
        # 方法2: 扫描常见偏移
        if res_offset is None:
            print(f"\n  Scanning for valid resource table...")
            for scan_offset in [0x80000, 0x90000, 0x9C000, 0xA0000, 0xB0000]:
                if scan_offset + 8 > file_size:
                    continue
                
                f.seek(scan_offset)
                entry = f.read(8)
                addr, length = struct.unpack_from('<II', entry, 0)
                
                if addr > scan_offset and addr < file_size and length > 0 and length < 1000000:
                    # 验证第二个条目
                    f.seek(scan_offset + 8)
                    entry2 = f.read(8)
                    addr2, len2 = struct.unpack_from('<II', entry2, 0)
                    
                    if addr2 > addr and len2 > 0:
                        print(f"    ✓ Found at 0x{scan_offset:X}")
                        res_offset = scan_offset
                        break
            
            if res_offset is None:
                print(f"    ✗ No valid resource table found in common offsets")
        
        # 如果找到 RES.BIN，分析资源表
        if res_offset is not None:
            res_size = file_size - res_offset
            print(f"\n  RES.BIN Info:")
            print(f"    Offset: 0x{res_offset:X} ({res_offset:,} bytes)")
            print(f"    Size: {res_size:,} bytes ({res_size / 1024:.2f} KB)")
            
            # 分析前 10 个资源
            print(f"\n  Resource Table (first 10 entries):")
            f.seek(res_offset)
            
            resources = []
            for i in range(10):
                entry = f.read(8)
                if len(entry) < 8:
                    print(f"    [{i}] [END - insufficient data]")
                    break
                
                addr, length = struct.unpack_from('<II', entry, 0)
                
                if addr == 0 and length == 0:
                    print(f"    [{i}] END OF TABLE")
                    break
                
                # 读取数据头部以检测类型
                if res_offset + addr + 16 <= file_size:
                    current_pos = f.tell()
                    f.seek(res_offset + addr)
                    data_hdr = f.read(16)
                    
                    # 类型检测逻辑（与 ResBinParser 相同）
                    rtype = "Unknown"
                    
                    # 魔数检测
                    if len(data_hdr) >= 3 and data_hdr[0] == 0xFF and data_hdr[1] == 0xD8 and data_hdr[2] == 0xFF:
                        rtype = "JPEG"
                    elif len(data_hdr) >= 2 and data_hdr[0] == ord('B') and data_hdr[1] == ord('M'):
                        rtype = "Bitmap"
                    elif len(data_hdr) >= 12 and data_hdr[0:4] == b'RIFF' and data_hdr[8:12] == b'WAVE':
                        rtype = "WAV"
                    elif length == 1024:
                        rtype = "Palette"
                    elif len(data_hdr) >= 2:
                        magic_font = struct.unpack_from('<H', data_hdr, 0)[0]
                        if magic_font == 0x584D:
                            rtype = "Font(idx)"
                        elif len(data_hdr) >= 4:
                            char_count = struct.unpack_from('<I', data_hdr, 0)[0]
                            if 100 <= char_count <= 50000:
                                rtype = "Font"
                    
                    # 大小范围检测
                    if rtype == "Unknown":
                        if 85000 <= length <= 90000:
                            rtype = "EncodingTable"
                        elif 90000 <= length <= 100000:
                            rtype = "OsdSource"
                        elif length < 10000:
                            rtype = "GameMap"
                        elif 10000 <= length < 100000:
                            rtype = "IconSelection"
                        else:
                            rtype = "Binary"
                    
                    print(f"    [{i:2d}] Addr=0x{addr:08X} Len={length:8d} Type={rtype:<15}")
                    resources.append((addr, length, rtype))
                    
                    f.seek(current_pos)
                else:
                    print(f"    [{i:2d}] Addr=0x{addr:08X} Len={length:8d} [OUT OF BOUNDS]")
            
            # 统计类型分布
            print(f"\n  Type Distribution:")
            type_counts = {}
            for _, _, rtype in resources:
                type_counts[rtype] = type_counts.get(rtype, 0) + 1
            
            for rtype, count in sorted(type_counts.items()):
                print(f"    {rtype}: {count}")
            
            return {
                'file_size': file_size,
                'magic': magic,
                'version': version,
                'res_offset': res_offset,
                'res_size': res_size,
                'resources': resources
            }
        else:
            print(f"\n  ✗ Cannot determine RES.BIN location")
            return None

# 分析两个文件
jt_path = "D:/dwl/work/2026/JT/JX_SDK/JT529X/firmware/ax32_platform_demo/output/DestBin.bin"
ax_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/DestBin.bin"

print("="*70)
print("DestBin.bin Comparative Analysis")
print("="*70)

jt_result = analyze_destbin(jt_path, "JT529X DestBin.bin")
ax_result = analyze_destbin(ax_path, "AX329X DestBin.bin")

# 对比总结
print(f"\n{'='*70}")
print("COMPARISON SUMMARY")
print(f"{'='*70}")

if jt_result and ax_result:
    print(f"\n{'Property':<25} {'JT529X':<20} {'AX329X':<20} {'Status':<10}")
    print("-" * 75)
    
    # 文件大小
    jt_size_kb = jt_result['file_size'] / 1024
    ax_size_kb = ax_result['file_size'] / 1024
    size_diff = abs(jt_size_kb - ax_size_kb)
    print(f"{'File Size':<25} {f'{jt_size_kb:.2f} KB':<20} {f'{ax_size_kb:.2f} KB':<20} {'DIFFERENT' if size_diff > 100 else 'SIMILAR':<10}")
    
    # Magic
    jt_magic = "BLDR" if jt_result['magic'] == 0x52444C42 else "INVALID"
    ax_magic = "BLDR" if ax_result['magic'] == 0x52444C42 else "INVALID"
    print(f"{'Magic':<25} {jt_magic:<20} {ax_magic:<20} {'✓ SAME' if jt_magic == ax_magic else '✗ DIFF':<10}")
    
    # Version
    jt_ver = f"v{(jt_result['version']>>16)&0xFF}.{(jt_result['version']>>8)&0xFF}.{jt_result['version']&0xFF}"
    ax_ver = f"v{(ax_result['version']>>16)&0xFF}.{(ax_result['version']>>8)&0xFF}.{ax_result['version']&0xFF}"
    print(f"{'Version':<25} {jt_ver:<20} {ax_ver:<20} {'✓ SAME' if jt_ver == ax_ver else '✗ DIFF':<10}")
    
    # RES.BIN Offset
    jt_offset_str = f"0x{jt_result['res_offset']:X}"
    ax_offset_str = f"0x{ax_result['res_offset']:X}"
    print(f"{'RES.BIN Offset':<25} {jt_offset_str:<20} {ax_offset_str:<20} {'✓ SAME' if jt_result['res_offset'] == ax_result['res_offset'] else '✗ DIFF':<10}")
    
    # RES.BIN Size
    jt_res_kb = jt_result['res_size'] / 1024
    ax_res_kb = ax_result['res_size'] / 1024
    print(f"{'RES.BIN Size':<25} {f'{jt_res_kb:.2f} KB':<20} {f'{ax_res_kb:.2f} KB':<20} {'DIFFERENT' if abs(jt_res_kb - ax_res_kb) > 10 else 'SIMILAR':<10}")
    
    # Resource count
    jt_count = len(jt_result['resources'])
    ax_count = len(ax_result['resources'])
    print(f"{'Resource Count':<25} {jt_count:<20} {ax_count:<20} {'DIFFERENT' if jt_count != ax_count else 'SAME':<10}")
    
    print(f"\nKey Findings:")
    if jt_result['res_offset'] != ax_result['res_offset']:
        print(f"  ⚠ RES.BIN offsets are different!")
        print(f"     This may cause parsing issues if using hardcoded offset")
    
    if abs(jt_size_kb - ax_size_kb) > 1000:
        print(f"  ⚠ File sizes differ significantly ({size_diff:.2f} KB)")
        print(f"     AX329X may have different program code size or structure")

else:
    print("\n✗ One or both files could not be analyzed")
