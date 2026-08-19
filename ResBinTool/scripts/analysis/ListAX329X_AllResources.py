import struct

destbin_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/DestBin.bin"
res_offset = 0x86A00

print("="*70)
print("AX329X Complete Resource Table")
print("="*70)
print()

with open(destbin_path, 'rb') as f:
    f.seek(res_offset)
    
    idx = 0
    font_entries = []
    
    while True:
        entry = f.read(8)
        if len(entry) < 8:
            break
        
        addr, length = struct.unpack_from('<II', entry, 0)
        
        if addr == 0 and length == 0:
            print(f"[{idx:2d}] END OF TABLE")
            break
        
        # Type hint based on size
        type_hint = ""
        if length >= 70000 and length <= 100000:
            type_hint = " [Font?]"
            font_entries.append(idx)
        elif length >= 900000 and length <= 1100000:
            type_hint = " [Large Font?]"
            font_entries.append(idx)
        elif length == 1024:
            type_hint = " [Palette]"
        elif length < 10000:
            type_hint = " [Small]"
        elif 10000 <= length < 100000:
            type_hint = " [Medium]"
        
        print(f"[{idx:2d}] Addr=0x{addr:08X}, Len={length:8d}{type_hint}")
        idx += 1
    
    print()
    print(f"Total entries: {idx}")
    print()
    
    if font_entries:
        print(f"Potential font resources at indices: {font_entries}")
        print()
        print("Detailed analysis of font candidates:")
        for font_idx in font_entries:
            # Seek to the resource data
            f.seek(res_offset + font_idx * 8)
            addr, length = struct.unpack_from('<II', f.read(8), 0)
            
            data_start = res_offset + addr
            f.seek(data_start)
            header = f.read(8)
            
            magic = struct.unpack_from('<H', header, 0)[0]
            char_count = struct.unpack_from('<I', header, 0)[0]
            
            print(f"\nEntry[{font_idx}]:")
            print(f"  Address: 0x{addr:08X}")
            print(f"  Length: {length} bytes ({length / 1024:.2f} KB)")
            print(f"  Magic: 0x{magic:04X} ({'Valid resfontidx' if magic == 0x584D else 'Not resfontidx'})")
            print(f"  Char count: {char_count} ({'Valid font' if 100 <= char_count <= 50000 else 'Invalid'})")
            print(f"  First 8 bytes: {' '.join(f'{b:02X}' for b in header)}")
