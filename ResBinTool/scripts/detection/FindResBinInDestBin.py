import struct
import os

destbin_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/DestBin.bin"
resbin_path = "D:/dwl/work/2026/JT/JX_SDK/AX329X/firmware/ax32_platform_demo/output/res.bin"

print("="*70)
print("Finding res.bin location in DestBin.bin")
print("="*70)

if not os.path.exists(destbin_path):
    print(f"DestBin.bin not found: {destbin_path}")
    exit(1)

if not os.path.exists(resbin_path):
    print(f"res.bin not found: {resbin_path}")
    exit(1)

destbin_size = os.path.getsize(destbin_path)
resbin_size = os.path.getsize(resbin_path)

print(f"DestBin.bin: {destbin_size} bytes (0x{destbin_size:X})")
print(f"res.bin: {resbin_size} bytes (0x{resbin_size:X})")
print()

# Read res.bin header (first 64 bytes)
with open(resbin_path, 'rb') as f:
    resbin_header = f.read(64)

print("res.bin header (first 64 bytes):")
for i in range(0, 64, 16):
    hex_str = ' '.join(f'{b:02X}' for b in resbin_header[i:i+16])
    ascii_str = ''.join(chr(b) if 32 <= b < 127 else '.' for b in resbin_header[i:i+16])
    print(f"  {i:04X}: {hex_str:<48} {ascii_str}")

print()
print("Searching for res.bin signature in DestBin.bin...")
print()

# Method 1: Search for the first resource table entry pattern
# res.bin starts with: 68 00 00 00 92 4C 00 00
signature = resbin_header[:8]  # First 8 bytes

with open(destbin_path, 'rb') as f:
    destbin_data = f.read()
    
    # Search for the signature
    found_offsets = []
    search_start = 0
    while True:
        pos = destbin_data.find(signature, search_start)
        if pos == -1:
            break
        
        # Verify this is really res.bin by checking more bytes
        if pos + 64 <= len(destbin_data):
            candidate = destbin_data[pos:pos+64]
            if candidate == resbin_header:
                found_offsets.append(pos)
                print(f"✓ Found exact match at offset 0x{pos:X} ({pos})")
        
        search_start = pos + 1
    
    if not found_offsets:
        print("✗ No exact match found")
        print()
        print("Trying partial match (first 4 bytes)...")
        
        signature_short = resbin_header[:4]
        search_start = 0
        partial_matches = []
        
        while True:
            pos = destbin_data.find(signature_short, search_start)
            if pos == -1:
                break
            
            # Check if it looks like a valid resource table
            if pos + 16 <= len(destbin_data):
                addr1, len1 = struct.unpack_from('<II', destbin_data, pos)
                addr2, len2 = struct.unpack_from('<II', destbin_data, pos + 8)
                
                # Validate
                if (addr1 > 0 and addr1 < resbin_size and 
                    len1 > 0 and len1 < 1000000 and
                    addr2 > addr1 and len2 > 0):
                    partial_matches.append(pos)
                    print(f"  Potential match at 0x{pos:X}: addr1=0x{addr1:X}, len1={len1}")
            
            search_start = pos + 1
        
        if partial_matches:
            print()
            print(f"Found {len(partial_matches)} potential matches")
            print("Verifying each one...")
            
            for offset in partial_matches:
                # Extract data and compare with res.bin
                if offset + resbin_size <= len(destbin_data):
                    extracted = destbin_data[offset:offset+resbin_size]
                    if extracted == resbin_header[:1024]:  # Compare first 1KB
                        print(f"\n✓✓✓ CONFIRMED: res.bin starts at offset 0x{offset:X} ({offset})")
                        print(f"    Program code size: {offset} bytes (0x{offset:X})")
                        print(f"    RES.BIN size: {resbin_size} bytes (0x{resbin_size:X})")
                        print(f"    Total: {offset + resbin_size} bytes")
                        
                        # Show resource table
                        print(f"\nResource Table (first 5 entries):")
                        for i in range(5):
                            entry_offset = offset + i * 8
                            addr, length = struct.unpack_from('<II', destbin_data, entry_offset)
                            if addr == 0 and length == 0:
                                print(f"  [{i}] END OF TABLE")
                                break
                            print(f"  [{i}] Addr=0x{addr:08X}, Len={length}")
                        
                        break
        else:
            print("No potential matches found")
    
    if not found_offsets and not partial_matches:
        print("\n✗ Could not find res.bin in DestBin.bin")
        print("\nPossible reasons:")
        print("  1. res.bin is not embedded in DestBin.bin")
        print("  2. res.bin is compressed or encrypted")
        print("  3. Different build configuration")
        print("  4. res.bin is loaded separately at runtime")
