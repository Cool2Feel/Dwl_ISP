import struct

# 读取原始 DestBin.bin 文件
original_path = r'D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin'

with open(original_path, 'rb') as f:
    original_data = f.read()

print(f'Original file size: {len(original_data)} bytes ({len(original_data)/1024:.2f} KB)')
print()

# 检查 0x9DC00 偏移（已知位置）
offset = 0x9DC00
print(f'=== Analyzing offset 0x{offset:X} ===')

addr1_rel = struct.unpack_from('<I', original_data, offset)[0]
addr2_rel = struct.unpack_from('<I', original_data, offset + 4)[0]
addr3_rel = struct.unpack_from('<I', original_data, offset + 8)[0]

print(f'Relative offsets:')
print(f'  Entry 0: rel=0x{addr1_rel:X}, len=0x{struct.unpack_from("<I", original_data, offset + 4)[0]:X}')
print(f'  Entry 1: rel=0x{addr2_rel:X}, len=0x{struct.unpack_from("<I", original_data, offset + 12)[0]:X}')
print(f'  Entry 2: rel=0x{addr3_rel:X}, len=0x{struct.unpack_from("<I", original_data, offset + 20)[0]:X}')

# 计算绝对地址
addr1_abs = offset + addr1_rel
addr2_abs = offset + addr2_rel
addr3_abs = offset + addr3_rel

print(f'\nAbsolute addresses (offset + relative):')
print(f'  Entry 0: abs=0x{addr1_abs:X}')
print(f'  Entry 1: abs=0x{addr2_abs:X}')
print(f'  Entry 2: abs=0x{addr3_abs:X}')

# 验证递增关系
if addr1_rel < addr2_rel < addr3_rel:
    print(f'\n✓ Valid resource table (relative offsets are increasing)')
    
    # 检查第一个资源的实际数据
    if addr1_abs < len(original_data):
        header = original_data[addr1_abs:addr1_abs+min(16, 100)]
        print(f'\nFirst resource at absolute address 0x{addr1_abs:X}:')
        print(f'  Header (first 16 bytes): {" ".join(f"{b:02X}" for b in header[:16])}')
        
        if len(header) >= 3 and header[0] == 0xFF and header[1] == 0xD8 and header[2] == 0xFF:
            print(f'  Type: JPEG ✓✓✓')
        elif len(header) >= 2 and header[0] == ord('B') and header[1] == ord('M'):
            print(f'  Type: BMP')
        else:
            print(f'  Type: Other')
else:
    print(f'\n✗ Not a valid resource table')

print('\n\n=== Problem Analysis ===')
print('The issue is that IsValidResBinStart() checks for ABSOLUTE addresses,')
print('but this DestBin.bin uses RELATIVE offsets in the resource table.')
print()
print('Current check in DestBinParser.cs line 320:')
print('  if (addr1 > offset && addr2 > addr1 && addr3 > addr2 && addr3 < _destBinData.Length)')
print()
print('This will FAIL because:')
print(f'  addr1 (0x{addr1_rel:X}) is NOT > offset (0x{offset:X})')
print()
print('Solution: Modify IsValidResBinStart() to also accept relative offsets.')
