import struct
import sys

# 检查命令行参数
if len(sys.argv) > 1:
    file_path = sys.argv[1]
else:
    file_path = r'D:\dwl\work\2026\JT\JX_SDK\JT529X\firmware\ax32_platform_demo\output\DestBin.bin'

print(f'Checking file: {file_path}')
print()

try:
    with open(file_path, 'rb') as f:
        data = f.read()
    
    print(f'File size: {len(data)} bytes ({len(data)/1024:.2f} KB)')
    print()
    
    # RES.BIN offset
    resbin_offset = 0x9DC00
    
    if resbin_offset + 8 > len(data):
        print(f'ERROR: File too small, no RES.BIN at 0x{resbin_offset:X}')
        sys.exit(1)
    
    # Read first resource entry
    first_addr_rel = struct.unpack_from('<I', data, resbin_offset)[0]
    first_length = struct.unpack_from('<I', data, resbin_offset + 4)[0]
    first_addr_abs = resbin_offset + first_addr_rel
    
    print(f'RES.BIN offset: 0x{resbin_offset:X}')
    print(f'First resource:')
    print(f'  Relative offset: 0x{first_addr_rel:X} ({first_addr_rel})')
    print(f'  Length: {first_length} (0x{first_length:X})')
    print(f'  Absolute offset: 0x{first_addr_abs:X}')
    print()
    
    # Check data at that position
    if first_addr_abs < len(data):
        header = data[first_addr_abs:first_addr_abs+16]
        print(f'Data at 0x{first_addr_abs:X}:')
        print(f'  {" ".join(f"{b:02X}" for b in header)}')
        
        is_jpeg = header[0] == 0xFF and header[1] == 0xD8 and header[2] == 0xFF
        print(f'  Is JPEG? {is_jpeg}')
        
        if is_jpeg:
            print(f'\n✓ File is GOOD - First resource is valid JPEG')
        else:
            print(f'\n✗ File is CORRUPTED - First resource is NOT JPEG')
            print(f'  Expected: FF D8 FF E0 ...')
            print(f'  Got:      {" ".join(f"{b:02X}" for b in header[:4])} ...')
    else:
        print(f'ERROR: Resource offset 0x{first_addr_abs:X} exceeds file size')
        
except FileNotFoundError:
    print(f'ERROR: File not found: {file_path}')
    sys.exit(1)
except Exception as e:
    print(f'ERROR: {e}')
    sys.exit(1)
