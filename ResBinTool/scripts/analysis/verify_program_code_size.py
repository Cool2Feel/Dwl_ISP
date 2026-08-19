import struct

# 读取原始 DestBin.bin 文件
original_path = r'D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin'

with open(original_path, 'rb') as f:
    original_data = f.read()

print(f'Original file size: {len(original_data)} bytes ({len(original_data)/1024:.2f} KB)')
print()

# 检查不同偏移位置的 RES.BIN 特征
test_offsets = [0x9DC00, 0x86A00, 0x80000]

for offset in test_offsets:
    if offset + 24 > len(original_data):
        continue
    
    addr1 = struct.unpack_from('<I', original_data, offset)[0]
    addr2 = struct.unpack_from('<I', original_data, offset + 4)[0]
    addr3 = struct.unpack_from('<I', original_data, offset + 8)[0]
    
    print(f'Offset 0x{offset:X}:')
    print(f'  Entry 0: addr=0x{addr1:X}, len=0x{struct.unpack_from("<I", original_data, offset + 4)[0]:X}')
    print(f'  Entry 1: addr=0x{addr2:X}, len=0x{struct.unpack_from("<I", original_data, offset + 12)[0]:X}')
    print(f'  Entry 2: addr=0x{addr3:X}, len=0x{struct.unpack_from("<I", original_data, offset + 20)[0]:X}')
    
    # 验证是否为有效的资源表
    if addr1 > offset and addr2 > addr1 and addr3 > addr2 and addr3 < len(original_data):
        print(f'  ✓ Valid resource table (absolute addresses)')
        
        # 检查第一个资源
        if addr1 < len(original_data):
            header = original_data[addr1:addr1+min(16, 100)]
            print(f'  First resource at 0x{addr1:X}:')
            print(f'    Header: {" ".join(f"{b:02X}" for b in header[:16])}')
            
            if len(header) >= 3 and header[0] == 0xFF and header[1] == 0xD8 and header[2] == 0xFF:
                print(f'    Type: JPEG ✓✓✓')
    else:
        print(f'  ✗ Not a valid resource table')
    
    print()

# 计算实际的程序代码段大小
print('=== Calculating Actual Program Code Size ===')
# 从 0x80000 开始扫描，找到第一个有效的资源表
for offset in range(0x80000, min(0x200000, len(original_data) - 1024), 512):
    try:
        addr1 = struct.unpack_from('<I', original_data, offset)[0]
        addr2 = struct.unpack_from('<I', original_data, offset + 4)[0]
        addr3 = struct.unpack_from('<I', original_data, offset + 8)[0]
        
        if addr1 > offset and addr2 > addr1 and addr3 > addr2 and addr3 < len(original_data):
            actual_program_size = offset
            print(f'Actual program code size: 0x{actual_program_size:X} ({actual_program_size} bytes, {actual_program_size/1024:.2f} KB)')
            print(f'Hardcoded PROGRAM_CODE_SIZE: 0x9DC00 (646,144 bytes, 631.00 KB)')
            print(f'Difference: {actual_program_size - 0x9DC00} bytes ({(actual_program_size - 0x9DC00)/1024:.2f} KB)')
            break
    except:
        pass
