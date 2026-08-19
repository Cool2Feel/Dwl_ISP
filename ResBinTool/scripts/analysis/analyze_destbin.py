import struct

# 读取 DestBin.bin 文件
file_path = r'D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin'

with open(file_path, 'rb') as f:
    data = f.read()

print(f'File size: {len(data)} bytes')
print()

# 尝试不同的资源表偏移位置
for table_offset in [0x200, 0x400, 0x800, 0x1000]:
    if table_offset + 8 > len(data):
        continue
    
    addr = struct.unpack_from('<I', data, table_offset)[0]
    length = struct.unpack_from('<I', data, table_offset + 4)[0]
    
    print(f'Table offset 0x{table_offset:X}:')
    print(f'  First resource: offset=0x{addr:X}, length={length}')
    
    if addr > 0 and addr + min(16, length) <= len(data):
        header = data[addr:addr+min(16, length)]
        print(f'  First 16 bytes: {" ".join(f"{b:02X}" for b in header)}')
        
        # 检查是否为 JPEG
        if length >= 3:
            is_jpeg = data[addr] == 0xFF and data[addr+1] == 0xD8 and data[addr+2] == 0xFF
            print(f'  Is JPEG? {is_jpeg}')
        
        # 检查是否为 BMP
        if length >= 2:
            is_bmp = data[addr] == ord('B') and data[addr+1] == ord('M')
            print(f'  Is BMP? {is_bmp}')
    
    print()
