import struct
import os

destbin_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/DestBin.bin"
res_offset = 0x86A00  # AX329X 的 RES.BIN 偏移

print("="*70)
print("AX329X Font Resource Analysis")
print("="*70)

if not os.path.exists(destbin_path):
    print(f"File not found: {destbin_path}")
    exit(1)

with open(destbin_path, 'rb') as f:
    # 跳转到 RES.BIN 起始位置
    f.seek(res_offset)
    
    # 读取资源表，找到字体资源
    print("\nSearching for font resources in resource table...")
    print()
    
    font_resources = []
    entry_index = 0
    
    while True:
        entry_data = f.read(8)
        if len(entry_data) < 8:
            break
        
        addr, length = struct.unpack_from('<II', entry_data, 0)
        
        if addr == 0 and length == 0:
            print(f"[{entry_index}] END OF TABLE")
            break
        
        # 检查是否为字体资源（通过大小初步判断）
        # 字体文件通常较大：resfont.bin ~82KB, resfontidx.bin ~75KB
        is_potential_font = (
            (length >= 70000 and length <= 100000) or  # resfont/resfontidx 范围
            (length >= 900000 and length <= 1100000)    # MP3font 范围
        )
        
        if is_potential_font:
            # 读取数据头部进行验证
            current_pos = f.tell()
            data_start = res_offset + addr
            
            if data_start + 16 <= os.path.getsize(destbin_path):
                f.seek(data_start)
                header = f.read(16)
                
                # 检查魔数或字符数量
                is_font = False
                font_type = "Unknown"
                
                if len(header) >= 2:
                    magic = struct.unpack_from('<H', header, 0)[0]
                    if magic == 0x584D:
                        is_font = True
                        font_type = "resfontidx (Magic 0x584D)"
                
                if not is_font and len(header) >= 4:
                    char_count = struct.unpack_from('<I', header, 0)[0]
                    if 100 <= char_count <= 50000:
                        is_font = True
                        font_type = f"resfont/MP3font ({char_count} chars)"
                
                if is_font:
                    print(f"[{entry_index}] *** FONT RESOURCE FOUND ***")
                    print(f"      Address: 0x{addr:08X} (absolute: 0x{data_start:X})")
                    print(f"      Length: {length} bytes ({length / 1024:.2f} KB)")
                    print(f"      Type: {font_type}")
                    
                    # 显示前 32 字节
                    f.seek(data_start)
                    preview = f.read(32)
                    print(f"      Header (first 32 bytes):")
                    for i in range(0, min(32, len(preview)), 16):
                        hex_str = ' '.join(f'{b:02X}' for b in preview[i:i+16])
                        ascii_str = ''.join(chr(b) if 32 <= b < 127 else '.' for b in preview[i:i+16])
                        print(f"        {i:04X}: {hex_str:<48} {ascii_str}")
                    
                    font_resources.append({
                        'index': entry_index,
                        'addr': addr,
                        'length': length,
                        'type': font_type
                    })
                    print()
                
                f.seek(current_pos)
        
        entry_index += 1
    
    print(f"\nSummary:")
    print(f"  Total entries scanned: {entry_index}")
    print(f"  Font resources found: {len(font_resources)}")
    
    if font_resources:
        print(f"\nFont Resources List:")
        for fr in font_resources:
            print(f"  Entry[{fr['index']}]: {fr['type']}, Size={fr['length']} bytes")
        
        print(f"\nPotential Issues:")
        print(f"  1. Check if font resources are at expected indices")
        print(f"  2. Verify header format matches detection logic")
        print(f"  3. Confirm character count is in valid range (100-50000)")
    else:
        print(f"\n⚠ No font resources detected!")
        print(f"  Possible reasons:")
        print(f"    - Font files use different format")
        print(f"    - Character count outside expected range")
        print(f"    - Missing magic number")
        print(f"    - Resources at different indices than expected")
