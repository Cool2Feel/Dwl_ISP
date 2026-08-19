import struct

destbin_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/DestBin.bin"
res_offset = 0x86A00

print("Checking Entry[79] and Entry[80] in AX329X")
print()

with open(destbin_path, 'rb') as f:
    # Check Entry[79] (RES_RESFONT)
    f.seek(res_offset + 79 * 8)
    addr79, len79 = struct.unpack_from('<II', f.read(8), 0)
    
    # Check Entry[80] (RES_RESFONTIDX)
    f.seek(res_offset + 80 * 8)
    addr80, len80 = struct.unpack_from('<II', f.read(8), 0)
    
    print(f"Entry[79] (RES_RESFONT):")
    print(f"  Address: 0x{addr79:08X}")
    print(f"  Length: {len79} bytes")
    
    if len79 > 0:
        # Read header
        data_start = res_offset + addr79
        f.seek(data_start)
        header79 = f.read(8)
        magic79 = struct.unpack_from('<H', header79, 0)[0]
        charcount79 = struct.unpack_from('<I', header79, 0)[0]
        
        print(f"  Magic: 0x{magic79:04X} ({'Valid resfontidx' if magic79 == 0x584D else 'Invalid'})")
        print(f"  Char count: {charcount79} ({'Valid' if 100 <= charcount79 <= 50000 else 'Invalid'})")
        print(f"  First 8 bytes: {' '.join(f'{b:02X}' for b in header79)}")
    else:
        print("  ⚠ Resource does not exist (length = 0)")
    print()
    
    print(f"Entry[80] (RES_RESFONTIDX):")
    print(f"  Address: 0x{addr80:08X}")
    print(f"  Length: {len80} bytes")
    
    if len80 > 0:
        # Read header
        data_start = res_offset + addr80
        f.seek(data_start)
        header80 = f.read(8)
        magic80 = struct.unpack_from('<H', header80, 0)[0]
        charcount80 = struct.unpack_from('<I', header80, 0)[0]
        
        print(f"  Magic: 0x{magic80:04X} ({'Valid resfontidx' if magic80 == 0x584D else 'Invalid'})")
        print(f"  Char count: {charcount80} ({'Valid' if 100 <= charcount80 <= 50000 else 'Invalid'})")
        print(f"  First 8 bytes: {' '.join(f'{b:02X}' for b in header80)}")
    else:
        print("  ⚠ Resource does not exist (length = 0)")
    print()
    
    print("Analysis:")
    if len79 == 0 or len80 == 0:
        print("  One or both entries have zero length - resources may not exist!")
    elif charcount79 < 100 or charcount79 > 50000:
        print(f"  Entry[79] char count ({charcount79}) is outside valid range")
        print("  This resource will NOT be detected as Font type")
    elif charcount80 < 100 or charcount80 > 50000:
        print(f"  Entry[80] char count ({charcount80}) is outside valid range")
        print("  This resource will NOT be detected as Font type")
    else:
        print("  Both entries appear to be valid font files")
