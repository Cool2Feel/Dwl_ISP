import struct
import shutil
import os

# 文件路径
original_path = r'D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin'
modified_path = r'D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin_Modified.bin'

print('=== Step 1: Analyze original DestBin.bin ===')
with open(original_path, 'rb') as f:
    original_data = f.read()

print(f'File size: {len(original_data)} bytes ({len(original_data)/1024:.2f} KB)')

# 找到 RES.BIN 偏移 (0x9DC00)
resbin_offset = 0x9DC00
print(f'RES.BIN offset: 0x{resbin_offset:X}')

# 读取资源表前3个条目
for i in range(3):
    addr_rel = struct.unpack_from('<I', original_data, resbin_offset + i*8)[0]
    length = struct.unpack_from('<I', original_data, resbin_offset + i*8 + 4)[0]
    addr_abs = resbin_offset + addr_rel
    print(f'Entry {i}: rel=0x{addr_rel:X}, abs=0x{addr_abs:X}, len={length} (0x{length:X})')

# 检查第一个资源
first_addr_rel = struct.unpack_from('<I', original_data, resbin_offset)[0]
first_addr_abs = resbin_offset + first_addr_rel
header = original_data[first_addr_abs:first_addr_abs+16]
print(f'\nFirst resource at 0x{first_addr_abs:X}:')
print(f'Header: {" ".join(f"{b:02X}" for b in header)}')
if header[0] == 0xFF and header[1] == 0xD8 and header[2] == 0xFF:
    print('Type: JPEG ✓✓✓')
else:
    print('Type: Other')

print('\n\n=== Step 2: Simulate save (copy file) ===')
shutil.copy2(original_path, modified_path)
print(f'Copied to: {modified_path}')

file_info = os.path.getsize(modified_path)
print(f'Modified file size: {file_info} bytes')

print('\n\n=== Step 3: Reload and verify ===')
with open(modified_path, 'rb') as f:
    modified_data = f.read()

print(f'File size: {len(modified_data)} bytes')

# 尝试不同的检测方法
print('\nMethod 1: Check fixed offset 0x9DC00')
addr1 = struct.unpack_from('<I', modified_data, 0x9DC00)[0]
addr2 = struct.unpack_from('<I', modified_data, 0x9DC00 + 4)[0]
addr3 = struct.unpack_from('<I', modified_data, 0x9DC00 + 8)[0]
print(f'  addr1=0x{addr1:X}, addr2=0x{addr2:X}, addr3=0x{addr3:X}')

# 检测是否为相对偏移
if addr1 < 0x100000 and addr2 < 0x100000 and addr3 < 0x100000:
    print('  Detected as RELATIVE offsets (< 1MB)')
    if addr2 > addr1 and addr3 > addr2:
        print('  ✓ Relative offsets are increasing')
        
        # 计算绝对地址
        first_abs = 0x9DC00 + addr1
        header2 = modified_data[first_abs:first_abs+16]
        print(f'\n  First resource at 0x{first_abs:X}:')
        print(f'  Header: {" ".join(f"{b:02X}" for b in header2)}')
        
        if header2[0] == 0xFF and header2[1] == 0xD8 and header2[2] == 0xFF:
            print('  Type: JPEG ✓✓✓')
        else:
            print('  Type: NOT JPEG ✗✗✗')
    else:
        print('  ✗ Relative offsets are NOT increasing')
else:
    print('  Detected as ABSOLUTE addresses')
    
    # 严格验证
    if addr1 > 0x9DC00 and addr2 > addr1 and addr3 > addr2:
        print('  ✓ Absolute addresses are valid')
        
        header2 = modified_data[addr1:addr1+16]
        print(f'\n  First resource at 0x{addr1:X}:')
        print(f'  Header: {" ".join(f"{b:02X}" for b in header2)}')
        
        if header2[0] == 0xFF and header2[1] == 0xD8 and header2[2] == 0xFF:
            print('  Type: JPEG ✓✓✓')
        else:
            print('  Type: NOT JPEG ✗✗✗')
    else:
        print('  ✗ Absolute addresses are NOT valid')

print('\n\n=== Conclusion ===')
print('The saved file should be identical to the original.')
print('If the first resource is not recognized as JPEG after reload,')
print('the problem is in the IsValidResBinStart() detection logic.')
