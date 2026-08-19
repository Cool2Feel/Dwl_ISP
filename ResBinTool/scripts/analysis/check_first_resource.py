import struct

# 读取修改后的 DestBin.bin
file_path = r'D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin_modified.bin'

with open(file_path, 'rb') as f:
    data = f.read()

print(f'File size: {len(data)} bytes')
print()

# RES.BIN 偏移
resbin_offset = 0x9DC00
print(f'RES.BIN offset in DestBin: 0x{resbin_offset:X}')

# 读取资源表第一个条目
first_addr_rel = struct.unpack_from('<I', data, resbin_offset)[0]
first_length = struct.unpack_from('<I', data, resbin_offset + 4)[0]
first_addr_abs = resbin_offset + first_addr_rel

print(f'\nFirst resource entry:')
print(f'  Relative offset: 0x{first_addr_rel:X} ({first_addr_rel})')
print(f'  Length: {first_length} (0x{first_length:X})')
print(f'  Absolute offset in DestBin: 0x{first_addr_abs:X}')
print()

# 检查绝对地址处的数据
print(f'Data at absolute offset 0x{first_addr_abs:X} in DestBin:')
header_abs = data[first_addr_abs:first_addr_abs+16]
print(f'  {" ".join(f"{b:02X}" for b in header_abs)}')
is_jpeg_abs = header_abs[0] == 0xFF and header_abs[1] == 0xD8 and header_abs[2] == 0xFF
print(f'  Is JPEG? {is_jpeg_abs}')
print()

# 提取 RES.BIN 数据
resbin_data = data[resbin_offset:]
print(f'RES.BIN extracted size: {len(resbin_data)} bytes')
print()

# 检查相对偏移处的数据（在提取的 RES.BIN 中）
print(f'Data at relative offset 0x{first_addr_rel:X} in RES.BIN:')
header_rel = resbin_data[first_addr_rel:first_addr_rel+16]
print(f'  {" ".join(f"{b:02X}" for b in header_rel)}')
is_jpeg_rel = header_rel[0] == 0xFF and header_rel[1] == 0xD8 and header_rel[2] == 0xFF
print(f'  Is JPEG? {is_jpeg_rel}')
print()

# 对比
print('=== Comparison ===')
print(f'Absolute header: {" ".join(f"{b:02X}" for b in header_abs)}')
print(f'Relative header: {" ".join(f"{b:02X}" for b in header_rel)}')
print(f'Match: {header_abs == header_rel}')
print()

# 如果不同，检查是否有偏移错误
if header_abs != header_rel:
    print('⚠️  WARNING: Headers do not match!')
    print()
    
    # 尝试其他可能的偏移
    for test_offset in [first_addr_rel - 2, first_addr_rel - 1, first_addr_rel + 1, first_addr_rel + 2]:
        if test_offset >= 0 and test_offset + 16 <= len(resbin_data):
            test_header = resbin_data[test_offset:test_offset+16]
            is_jpeg = test_header[0] == 0xFF and test_header[1] == 0xD8 and test_header[2] == 0xFF
            if is_jpeg:
                print(f'✓ Found JPEG at offset 0x{test_offset:X} (difference: {test_offset - first_addr_rel:+d})')
                print(f'  Header: {" ".join(f"{b:02X}" for b in test_header)}')
                break
