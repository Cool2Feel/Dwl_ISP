import struct
import os

file_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/res.bin"

if not os.path.exists(file_path):
    print(f"File not found: {file_path}")
    exit(1)

file_size = os.path.getsize(file_path)
print(f"=== res.bin Structure Analysis ===")
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
    print("Resource Table Entries (first 10):")
    print(f"{'Index':<6} {'Address':<12} {'Length':<12} {'Type (predicted)':<20}")
    print("-" * 55)
    
    resources = []
    for i in range(10):
        offset = i * 8
        if offset + 8 > len(data):
            break
        
        addr, length = struct.unpack_from('<II', data, offset)
        
        if addr == 0 and length == 0:
            print(f"{i:<6} [END OF TABLE]")
            break
        
        # Predict type based on size and header
        predicted_type = "Unknown"
        if length > 0 and addr + 16 <= file_size:
            # Read data header
            f.seek(addr)
            data_hdr = f.read(16)
            
            if len(data_hdr) >= 3:
                if data_hdr[0] == 0xFF and data_hdr[1] == 0xD8 and data_hdr[2] == 0xFF:
                    predicted_type = "JPEG"
                elif data_hdr[0] == ord('B') and data_hdr[1] == ord('M'):
                    predicted_type = "Bitmap"
                elif len(data_hdr) >= 12 and data_hdr[0:4] == b'RIFF' and data_hdr[8:12] == b'WAVE':
                    predicted_type = "WAV"
                elif length == 1024:
                    predicted_type = "Palette"
                elif len(data_hdr) >= 4:
                    char_count = struct.unpack_from('<I', data_hdr, 0)[0]
                    if 100 <= char_count <= 50000:
                        predicted_type = "Font"
                    elif len(data_hdr) >= 2:
                        magic_font = struct.unpack_from('<H', data_hdr, 0)[0]
                        if magic_font == 0x584D:
                            predicted_type = "Font(idx)"
            
            if predicted_type == "Unknown":
                if 85000 <= length <= 90000:
                    predicted_type = "EncodingTable"
                elif 90000 <= length <= 100000:
                    predicted_type = "OsdSource"
                elif length < 10000:
                    predicted_type = "GameMap"
                elif 10000 <= length < 100000:
                    predicted_type = "IconSelection"
                else:
                    predicted_type = "Binary"
        
        print(f"{i:<6} 0x{addr:08X}   {length:<12} {predicted_type:<20}")
        resources.append((addr, length, predicted_type))
    
    print()
    print("Type Distribution:")
    type_counts = {}
    for _, _, rtype in resources:
        type_counts[rtype] = type_counts.get(rtype, 0) + 1
    
    for rtype, count in sorted(type_counts.items()):
        print(f"  {rtype}: {count}")
    
    print()
    print("Validation:")
    is_valid_table = True
    for i, (addr, length, _) in enumerate(resources):
        if addr == 0 and length == 0:
            break
        if addr >= file_size or length == 0:
            is_valid_table = False
            print(f"  ✗ Entry[{i}] invalid: addr=0x{addr:X}, len={length}")
    
    if is_valid_table:
        print(f"  ✓ Valid RES.BIN resource table detected")
        print()
        print("Conclusion:")
        print("  This is a standard RES.BIN file that can be loaded directly.")
        print("  Use ResBinParser in standalone mode (not DestBin mode).")
        print()
        print("Usage in ResBinManager:")
        print("  1. Open the file directly (not DestBin.bin)")
        print("  2. The tool will auto-detect it as RES.BIN format")
        print("  3. All resources will be parsed correctly")
    else:
        print(f"  ✗ Invalid resource table")
