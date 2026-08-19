import struct

# 读取 DestBin.bin 文件
file_path = r'D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin'

with open(file_path, 'rb') as f:
    data = f.read()

print(f'File size: {len(data)} bytes ({len(data)/1024/1024:.2f} MB)')
print()

# 尝试常见的偏移位置
candidate_offsets = [
    0x80000,   # 512 KB
    0x86A00,   # 539 KB (AX329X)
    0x90000,   # 576 KB
    0x9C000,   # 624 KB (JT529X)
    0x9DC00,   # 631 KB
    0xA0000,   # 640 KB
    0xB0000    # 704 KB
]

print('=== Checking Candidate Offsets ===')
for offset in candidate_offsets:
    if offset + 24 > len(data):
        continue
    
    addr1 = struct.unpack_from('<I', data, offset)[0]
    addr2 = struct.unpack_from('<I', data, offset + 4)[0]
    addr3 = struct.unpack_from('<I', data, offset + 8)[0]
    
    print(f'\nOffset 0x{offset:X} ({offset}):')
    print(f'  Entry 0: addr=0x{addr1:X}, len=0x{struct.unpack_from("<I", data, offset + 4)[0]:X}')
    print(f'  Entry 1: addr=0x{addr2:X}, len=0x{struct.unpack_from("<I", data, offset + 12)[0]:X}')
    print(f'  Entry 2: addr=0x{addr3:X}, len=0x{struct.unpack_from("<I", data, offset + 20)[0]:X}')
    
    # 检查第一个资源的实际数据
    if addr1 > 0 and addr1 < len(data):
        # 假设是相对偏移
        actual_addr = offset + addr1 if addr1 < 0x100000 else addr1
        if actual_addr < len(data):
            header = data[actual_addr:actual_addr+min(16, 100)]
            print(f'  First resource at 0x{actual_addr:X}:')
            print(f'    Header (first 16 bytes): {" ".join(f"{b:02X}" for b in header[:16])}')
            
            # 检测类型
            if len(header) >= 3 and header[0] == 0xFF and header[1] == 0xD8 and header[2] == 0xFF:
                print(f'    Type: JPEG ✓✓✓')
            elif len(header) >= 2 and header[0] == ord('B') and header[1] == ord('M'):
                print(f'    Type: BMP')
            elif len(header) >= 12 and header[0:4] == b'RIFF' and header[8:12] == b'WAVE':
                print(f'    Type: WAV')
            else:
                print(f'    Type: Unknown/Binary')

print('\n\n=== Scanning for Valid Resource Table ===')
# 扫描 0x80000 到 0x200000 范围
for offset in range(0x80000, min(0x200000, len(data) - 1024), 512):
    try:
        addr1 = struct.unpack_from('<I', data, offset)[0]
        addr2 = struct.unpack_from('<I', data, offset + 4)[0]
        addr3 = struct.unpack_from('<I', data, offset + 8)[0]
        
        # 严格验证：绝对地址，严格递增
        if addr1 > offset and addr2 > addr1 and addr3 > addr2 and addr3 < len(data):
            print(f'\n✓ Found valid table at offset 0x{offset:X} ({offset})')
            print(f'  Addresses: 0x{addr1:X}, 0x{addr2:X}, 0x{addr3:X}')
            
            # 检查第一个资源
            if addr1 < len(data):
                header = data[addr1:addr1+min(16, 100)]
                print(f'  First resource header: {" ".join(f"{b:02X}" for b in header[:16])}')
                
                if len(header) >= 3 and header[0] == 0xFF and header[1] == 0xD8 and header[2] == 0xFF:
                    print(f'  Type: JPEG ✓✓✓')
                elif len(header) >= 2 and header[0] == ord('B') and header[1] == ord('M'):
                    print(f'  Type: BMP')
                else:
                    print(f'  Type: Other')
            
            # 只显示前3个匹配
            break
            
    except:
        pass
