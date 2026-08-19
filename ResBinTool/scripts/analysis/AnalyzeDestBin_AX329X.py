import struct
import os

file_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/DestBin.bin"

if not os.path.exists(file_path):
    print(f"File not found: {file_path}")
    exit(1)

file_size = os.path.getsize(file_path)
print(f"=== DestBin.bin Structure Analysis ===")
print(f"File path: {file_path}")
print(f"File size: {file_size} bytes ({file_size / 1024:.2f} KB)")
print()

with open(file_path, 'rb') as f:
    data = f.read(min(512, file_size))
    
    print("First 64 bytes (hex dump):")
    for i in range(0, min(64, len(data)), 16):
        hex_str = ' '.join(f'{b:02X}' for b in data[i:i+16])
        ascii_str = ''.join(chr(b) if 32 <= b < 127 else '.' for b in data[i:i+16])
        print(f"  {i:04X}: {hex_str:<48}  {ascii_str}")
    
    print()
    print("Header fields analysis:")
    
    # Offset 0x04-0x07: Magic number (should be "BLDR" = 0x52444C42)
    magic = struct.unpack_from('<I', data, 4)[0]
    magic_str = ''.join(chr((magic >> (i*8)) & 0xFF) for i in range(4))
    print(f"  Offset 0x04-0x07 (Magic): 0x{magic:08X} = \"{magic_str}\"")
    if magic == 0x52444C42:
        print(f"    ✓ Valid BLDR signature detected")
    else:
        print(f"    ✗ Invalid magic (expected 0x52444C42)")
    
    # Offset 0x08-0x0B: Version
    version = struct.unpack_from('<I', data, 8)[0]
    major = (version >> 16) & 0xFF
    minor = (version >> 8) & 0xFF
    patch = version & 0xFF
    print(f"  Offset 0x08-0x0B (Version): 0x{version:08X}")
    if major > 0 or minor > 0 or patch > 0:
        print(f"    Parsed: v{major}.{minor}.{patch}")
    
    # Offset 0x10-0x17: Serial/Build ID (8 bytes ASCII)
    serial_bytes = data[16:24]
    serial = ''.join(chr(b) if 32 <= b < 127 else '?' for b in serial_bytes)
    print(f"  Offset 0x10-0x17 (Serial): {serial_bytes.hex().upper()}")
    print(f"    ASCII: \"{serial}\"")
    
    print()
    print("RES.BIN location detection:")
    
    # Standard program code size
    PROGRAM_CODE_SIZE = 0x9DC00  # 646,144 bytes
    
    print(f"  Expected RES.BIN offset: 0x{PROGRAM_CODE_SIZE:X} ({PROGRAM_CODE_SIZE} bytes)")
    
    if file_size > PROGRAM_CODE_SIZE:
        res_offset = PROGRAM_CODE_SIZE
        res_size = file_size - PROGRAM_CODE_SIZE
        
        print(f"  RES.BIN offset: 0x{res_offset:X}")
        print(f"  RES.BIN size: {res_size} bytes ({res_size / 1024:.2f} KB)")
        
        # Check if RES.BIN starts with valid resource table
        if res_offset + 8 <= len(data):
            addr1 = struct.unpack_from('<I', data, res_offset)[0]
            addr2 = struct.unpack_from('<I', data, res_offset + 4)[0]
            
            print(f"  First resource entry:")
            print(f"    Address: 0x{addr1:08X}")
            print(f"    Length: 0x{addr2:08X}")
            
            # Validate if addresses look reasonable
            if addr1 > res_offset and addr2 > 0:
                print(f"    ✓ Looks like valid resource table")
            else:
                print(f"    ⚠ May not be valid resource table")
        
        # Tail padding
        tail_padding = file_size % 4096
        if tail_padding > 0:
            tail_padding = 4096 - tail_padding
        print(f"  Tail padding: {tail_padding} bytes (to 4KB alignment)")
    else:
        print(f"  ✗ File too small to contain RES.BIN")
    
    print()
    print("Structure summary:")
    print(f"  Total file size: {file_size:,} bytes ({file_size / 1024:.2f} KB)")
    print(f"  Program code:    {PROGRAM_CODE_SIZE:,} bytes (0x000000 - 0x{PROGRAM_CODE_SIZE-1:06X})")
    if file_size > PROGRAM_CODE_SIZE:
        print(f"  RES.BIN:         {res_size:,} bytes (0x{PROGRAM_CODE_SIZE:06X} - 0x{file_size-1:06X})")
